using System;
using System.Collections.Generic;
using System.Threading;

namespace SodaFlow.Collections;

/// <summary>
///     One stage of a chain: its own keys and order, and everything else derived from them.
/// </summary>
/// <remarks>
///     A stage is a collection in its own right. What it holds, how it changed, what its items are
///     and what their identities are all answer for this stage and not for the store beneath it -
///     the store is reached only through
///     <see cref="ReactiveCollection{TKey,TIdentity,TState}.Root" />, which is internal and exists
///     for the per-item caches.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class ViewStage<TKey, TIdentity, TState> : ReactiveCollection<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly Lazy<Stream<ItemChange<TKey, TIdentity, TState>>> itemChangesStream;

    private readonly Lazy<Cell<IReadOnlyDictionary<TKey, TIdentity>>> shapeCell;

    /// <summary>Built on first use, because a stage nobody asks should not pay for one.</summary>
    /// <remarks>
    ///     Each of these is a node per stage recomputed every transaction. Scoping the snapshot
    ///     measured about a fifth of an edit across a chain when it was eager, and the same applies
    ///     to the two below it: most consumers read the keys, the changes and the per-item cells,
    ///     and never ask a view for its items in bulk.
    /// </remarks>
    private readonly Lazy<Cell<CollectionSnapshot<TKey, TIdentity, TState>>> snapshotCell;

    /// <summary>The stage above this one, or the collection if this is the first.</summary>
    private readonly ReactiveCollection<TKey, TIdentity, TState> source;

    internal ViewStage(
        ReactiveCollection<TKey, TIdentity, TState> source,
        Cell<OrderedKeys<TKey, TIdentity, TState>> keysCell,
        Func<Cell<CollectionSnapshot<TKey, TIdentity, TState>>> snapshotCell,
        Stream<CollectionViewChange<TKey, TIdentity, TState>> keyChangesStream)
    {
        this.source = source;
        this.KeysCell = keysCell;
        this.KeyChangesStream = keyChangesStream;

        this.snapshotCell =
            new Lazy<Cell<CollectionSnapshot<TKey, TIdentity, TState>>>(
                valueFactory: snapshotCell,
                mode: LazyThreadSafetyMode.ExecutionAndPublication);

        // This stage's items as keyed deltas. A view change already says which keys entered, left
        // and changed, so this is a translation rather than a derivation - and reading it never
        // asks the stage to order itself, which is what the collection's own item stream promises.
        this.itemChangesStream =
            new Lazy<Stream<ItemChange<TKey, TIdentity, TState>>>(
                valueFactory: () =>
                    TransactionInternal.RunImpl(() =>
                        this.KeyChangesStream.MapImpl(static change => change.ToItemChange())),
                mode: LazyThreadSafetyMode.ExecutionAndPublication);

        // Only membership moves this, which is what makes it cheaper to hold than the snapshot: a
        // state edit reorders a view without changing what is in it, and this sleeps through that.
        this.shapeCell =
            new Lazy<Cell<IReadOnlyDictionary<TKey, TIdentity>>>(
                valueFactory: () =>
                    TransactionInternal.RunImpl(() =>
                        this.KeyChangesStream
                            .FilterImpl(static change => change.ChangesMembership)
                            .MapImpl(static change => change.After.Identities)
                            .HoldLazyImpl(
                                this.SnapshotCell.SampleLazyImpl().MapImpl(static snapshot => snapshot.Identities))),
                mode: LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public override Cell<OrderedKeys<TKey, TIdentity, TState>> KeysCell { get; }

    /// <inheritdoc />
    public override Stream<CollectionViewChange<TKey, TIdentity, TState>> KeyChangesStream { get; }

    /// <inheritdoc />
    public override Cell<CollectionSnapshot<TKey, TIdentity, TState>> SnapshotCell => this.snapshotCell.Value;

    /// <inheritdoc />
    public override Stream<ItemChange<TKey, TIdentity, TState>> ItemChangesStream => this.itemChangesStream.Value;

    /// <inheritdoc />
    public override Cell<IReadOnlyDictionary<TKey, TIdentity>> ShapeCell => this.shapeCell.Value;

    /// <inheritdoc />
    internal override ReactiveCollection<TKey, TIdentity, TState> Root => this.source.Root;

    /// <inheritdoc />
    /// <remarks>
    ///     Hung off this stage's own change stream, which is what makes it affordable. The
    ///     alternative - the collection's cell lifted against this view's keys - puts a cell node
    ///     per observer in the view's propagation path, walked whenever the view moves at all,
    ///     including a reorder that touched nobody's key. That measured twice an ordinary edit;
    ///     this filters itself out instead and measures the same as observing the collection.
    /// </remarks>
    internal override Cell<TProjected> CreateStateCell<TProjected>(
        TKey key,
        Func<TState, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        // This stage's keys and the store, rather than this stage's snapshot. Seeding from
        // SnapshotCell would read better and would force the lazy scoped cell of this stage and
        // every stage above it - the node deferred for costing about a fifth of an edit.
        ReactiveCollection<TKey, TIdentity, TState> root = this.source.Root;

        return TransactionInternal.RunImpl(() =>
            this.KeyChangesStream
                .MapImpl(change => change.ProjectChangeFor(key: key, onPresent: onPresent, onAbsent: onAbsent))
                .FilterSomeInternal()
                .HoldLazyImpl(
                    this.KeysCell.SampleLazyImpl()
                        .MapImpl(keys =>
                            keys.Contains(key) &&
                            root.SnapshotCell.SampleImpl().States.TryGetState(key: key, state: out TState state)
                                ? onPresent(state)
                                : onAbsent())));
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Hung off this stage's change stream as the state cell is. An update or a move never
    ///     reaches it, so an observer of one item's identity through a view wakes only when that key
    ///     enters or leaves the view.
    /// </remarks>
    internal override Cell<TProjected> CreateIdentityCell<TProjected>(
        TKey key,
        Func<TIdentity, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        ReactiveCollection<TKey, TIdentity, TState> root = this.source.Root;

        return TransactionInternal.RunImpl(() =>
            this.KeyChangesStream
                .MapImpl(change => change.ProjectIdentityChangeFor(key: key, onPresent: onPresent, onAbsent: onAbsent))
                .FilterSomeInternal()
                .HoldLazyImpl(
                    this.KeysCell.SampleLazyImpl()
                        .MapImpl(keys =>
                            keys.Contains(key) &&
                            root.SnapshotCell.SampleImpl().TryGetIdentity(key: key, identity: out TIdentity identity)
                                ? onPresent(identity)
                                : onAbsent())));
    }
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
        OrderedKeys<TKey, TIdentity, TState> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
    {
        this.Criteria = criteria;
        this.UpstreamKeys = upstreamKeys;
        this.Snapshot = snapshot;
    }

    internal TCriteria Criteria { get; }

    internal OrderedKeys<TKey, TIdentity, TState> UpstreamKeys { get; }

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
        OrderedKeys<TKey, TIdentity, TState> keys,
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

    internal OrderedKeys<TKey, TIdentity, TState> Keys { get; }

    internal IReadOnlyList<ViewOperation<TKey>> Operations { get; }

    internal bool IsReset { get; }

    internal CollectionSnapshot<TKey, TIdentity, TState> Before { get; }

    internal CollectionSnapshot<TKey, TIdentity, TState> After { get; }
}
