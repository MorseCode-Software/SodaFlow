using System;
using System.Collections.Generic;
using System.Threading;

namespace SodaFlow.Collections;

/// <summary>
///     One stage of a chain: its own keys and order, everything else delegated to the store it
///     shares with every other stage over the same root.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class CollectionViewStage<TKey, TIdentity, TState>
    : IReactiveCollectionInternal<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly IReactiveCollection<TKey, TIdentity, TState> source;

    /// <summary>One per-key cell cache per projected type, as the root keeps.</summary>
    private readonly Dictionary<Type, object> projectedCaches = new();

    /// <summary>A plain object, for the reason the root's gate is one.</summary>
    private readonly object cacheGate = new();

    /// <summary>
    ///     Built on first use, because a stage that nobody asks for a snapshot should not pay for
    ///     one.
    /// </summary>
    /// <remarks>
    ///     Scoping the snapshot means a cell per stage, recomputed every transaction, and that
    ///     measured about a fifth of what an edit costs across the whole chain - against consumers
    ///     who overwhelmingly read <see cref="KeysCell" />, the per-item cells and
    ///     <see cref="KeyChangesStream" /> and never ask a view what it holds in bulk. Deferring it
    ///     is the same bargain the root's ordering already makes.
    /// </remarks>
    private readonly Lazy<Cell<CollectionSnapshot<TKey, TIdentity, TState>>> snapshotCell;

    internal CollectionViewStage(
        IReactiveCollection<TKey, TIdentity, TState> source,
        Cell<IOrderedKeys<TKey, TIdentity, TState>> keysCell,
        Func<Cell<CollectionSnapshot<TKey, TIdentity, TState>>> snapshotCell,
        Stream<CollectionViewChange<TKey, TIdentity, TState>> keyChangesStream)
    {
        this.source = source;
        this.KeysCell = keysCell;
        this.snapshotCell = new Lazy<Cell<CollectionSnapshot<TKey, TIdentity, TState>>>(
            snapshotCell,
            LazyThreadSafetyMode.ExecutionAndPublication);
        this.KeyChangesStream = keyChangesStream;
    }

    public Cell<IOrderedKeys<TKey, TIdentity, TState>> KeysCell { get; }

    public Stream<CollectionViewChange<TKey, TIdentity, TState>> KeyChangesStream { get; }

    public Cell<CollectionSnapshot<TKey, TIdentity, TState>> SnapshotCell => this.snapshotCell.Value;

    /// <inheritdoc />
    /// <remarks>
    ///     <para>
    ///         Hung off this stage's own change stream, which is what makes it affordable. The
    ///         alternative - the collection's cell lifted against this view's keys - puts a cell node
    ///         per observer in the view's propagation path, and every one of them is walked whenever
    ///         the view moves at all, including a reorder that touched nobody's key. Measured at
    ///         twenty observers that cost about four times an ordinary edit, and roughly three times
    ///         even with the membership held per key and calmed. This filters itself out instead: a
    ///         change that named no operation for this key yields nothing and propagates no further.
    ///     </para>
    ///     <para>
    ///         Cached weakly per key and per projected type, as the root's are, so observers of one
    ///         key through one view share a node - and two views of the same key are two cells,
    ///         because they are two answers.
    ///     </para>
    /// </remarks>
    Cell<TProjected> IReactiveCollectionInternal<TKey, TIdentity, TState>.StateCellImpl<TProjected>(
        TKey key,
        Func<TState, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        lock (this.cacheGate)
        {
            ProjectedCellCache<TKey, TProjected> cache = this.CacheFor<TProjected>();

            return cache.Get(key) ?? this.CreateStateCell(cache, key, onPresent, onAbsent);
        }
    }

    /// <summary>The cache for one projected type, created the first time that type is asked for.</summary>
    /// <remarks>The cast is sound for the reason it is sound on the root: see its <c>CacheFor</c>.</remarks>
    private ProjectedCellCache<TKey, TProjected> CacheFor<TProjected>()
    {
        if (this.projectedCaches.TryGetValue(typeof(TProjected), out object? existing))
        {
            return (ProjectedCellCache<TKey, TProjected>)existing;
        }

        ProjectedCellCache<TKey, TProjected> created = new();
        this.projectedCaches[typeof(TProjected)] = created;

        return created;
    }

    private Cell<TProjected> CreateStateCell<TProjected>(
        ProjectedCellCache<TKey, TProjected> cache,
        TKey key,
        Func<TState, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        // This stage's keys and the store, rather than this stage's snapshot. Seeding from
        // SnapshotCell would read better and would force the lazy scoped cell of this stage and
        // every stage above it - the node that measured about a fifth of an edit and was deferred
        // for it. Membership comes from the keys, the value from the store, and neither is new.
        ReactiveCollection<TKey, TIdentity, TState> root = this.source.RootOf();

        Cell<TProjected> stateCell = TransactionInternal.RunImpl(() =>
            this.KeyChangesStream
                .MapImpl(change => change.ProjectChangeFor(key, onPresent, onAbsent))
                .FilterSomeInternal()
                .HoldLazyImpl(this.KeysCell.SampleLazyImpl().MapImpl(
                    keys => keys.Contains(key) &&
                        root.SnapshotCell.SampleImpl().States.TryGetState(key, out TState state)
                            ? onPresent(state)
                            : onAbsent())));

        cache.Set(key, stateCell);

        return stateCell;
    }

    ReactiveCollection<TKey, TIdentity, TState>
        IReactiveCollectionInternal<TKey, TIdentity, TState>.Root => this.source.RootOf();
}

/// <summary>
///     The pre-transaction inputs a stage reads when the event it is processing does not carry a
///     newer value for them.
/// </summary>
internal sealed class StageContext<TKey, TIdentity, TState, TCriteria>
    where TKey : notnull
    where TIdentity : notnull
{
    internal StageContext(
        TCriteria criteria,
        IOrderedKeys<TKey, TIdentity, TState> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
    {
        this.Criteria = criteria;
        this.UpstreamKeys = upstreamKeys;
        this.Snapshot = snapshot;
    }

    internal TCriteria Criteria { get; }

    internal IOrderedKeys<TKey, TIdentity, TState> UpstreamKeys { get; }

    internal CollectionSnapshot<TKey, TIdentity, TState> Snapshot { get; }
}

/// <summary>
///     What reached a stage in one transaction: an upstream change, a new criteria, or both.
/// </summary>
internal sealed class StageInput<TKey, TIdentity, TState, TCriteria>
    where TKey : notnull
    where TIdentity : notnull
{
    internal StageInput(
        MaybeInternal<CollectionViewChange<TKey, TIdentity, TState>> change,
        MaybeInternal<TCriteria> criteria)
    {
        this.Change = change;
        this.Criteria = criteria;
    }

    internal MaybeInternal<CollectionViewChange<TKey, TIdentity, TState>> Change { get; }

    internal MaybeInternal<TCriteria> Criteria { get; }
}

/// <summary>What a stage produced in one transaction, before it becomes a change event.</summary>
internal sealed class StageResult<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    internal StageResult(
        IOrderedKeys<TKey, TIdentity, TState> keys,
        IReadOnlyList<ViewOperation<TKey>> operations,
        bool isReset,
        CollectionSnapshot<TKey, TIdentity, TState> before,
        CollectionSnapshot<TKey, TIdentity, TState> after)
    {
        this.Keys = keys;
        this.Operations = operations;
        this.IsReset = isReset;
        this.Before = before;
        this.After = after;
    }

    internal IOrderedKeys<TKey, TIdentity, TState> Keys { get; }

    internal IReadOnlyList<ViewOperation<TKey>> Operations { get; }

    internal bool IsReset { get; }

    internal CollectionSnapshot<TKey, TIdentity, TState> Before { get; }

    internal CollectionSnapshot<TKey, TIdentity, TState> After { get; }
}
