using System;
using System.Collections.Generic;
using System.Threading;

namespace SodaFlow.Collections;

/// <summary>
///     One stage of a chain: its own keys and order, and each value from them.
/// </summary>
/// <remarks>
///     A stage is a collection. Its content, its change, its items, and their identities answer
///     for this stage and not for the store below it. The only path to the store is
///     <see cref="ReactiveCollection{TKey,TIdentity,TState}.Root" />, which is internal and is here
///     for the caches of the cells for one item.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class ViewStage<TKey, TIdentity, TState> : ReactiveCollection<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly Lazy<Stream<ItemChange<TKey, TIdentity, TState>>> itemChangesStream;

    private readonly Lazy<Cell<IReadOnlyDictionary<TKey, TIdentity>>> shapeCell;

    /// <summary>This code builds it at its first use, thus a stage that no code reads has no cost
    /// for it.</summary>
    /// <remarks>
    ///     Each one of these is one node for each stage, and the graph calculates it again at each
    ///     transaction. A measurement of the scope on the snapshot, before this code made it lazy,
    ///     showed approximately one fifth of the cost of an edit across a chain. The same applies
    ///     to the two below it. Most consumers read the keys, the changes, and the cells for one
    ///     item, and never read the items of a view together.
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

        // These are the items of this stage as deltas with keys. A view change names the keys that
        // entered, the keys that left, and the keys that changed. Thus, this code changes the shape
        // of the data and does not calculate it again. A read of it also never makes the stage sort
        // itself, which is the contract of the item stream of the collection.
        this.itemChangesStream =
            new Lazy<Stream<ItemChange<TKey, TIdentity, TState>>>(
                valueFactory: () =>
                    TransactionInternal.RunImpl(() =>
                        this.KeyChangesStream.MapImpl(change => change.ToItemChange(source.Root.KeyEqualityComparer))),
                mode: LazyThreadSafetyMode.ExecutionAndPublication);

        // Only a change of the members changes this, thus it costs less to hold than the snapshot.
        // A state edit changes the order of a view and does not change its content, and this cell
        // sends no value at that change.
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
    internal override RootCollection<TKey, TIdentity, TState> Root => this.source.Root;

    /// <inheritdoc />
    /// <remarks>
    ///     This comes from the change stream of this stage, and that keeps its cost low. The
    ///     alternative is a lift of the cell of the collection against the keys of this view. That
    ///     alternative puts one cell node for each observer in the path of the view, and the graph
    ///     reads it at each change of the view, with a change of order that names no key of the
    ///     observer. A measurement showed two times the cost of a usual edit. This stream removes
    ///     itself, and it measures the same as an observer on the collection.
    /// </remarks>
    internal override Cell<TProjected> CreateStateCell<TProjected>(
        TKey key,
        Func<TState, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        // This uses the keys of this stage and the store, and not the snapshot of this stage. A
        // seed from SnapshotCell reads better, and it makes the lazy scoped cell of this stage and
        // of each stage above it. This code defers that node, because it costs approximately one
        // fifth of an edit.
        ReactiveCollection<TKey, TIdentity, TState> root = this.source.Root;

        return TransactionInternal.RunImpl(() =>
            this.KeyChangesStream
                .MapImpl(change => change.ProjectChangeFor(key: key, onPresent: onPresent, onAbsent: onAbsent))
                .FilterSomeInternal()
                .HoldLazyImpl(
                    this.KeysCell.SampleLazyImpl()
                        .MapImpl(keys =>
                            keys.Contains(key)
                            && root.SnapshotCell.SampleImpl().States.TryGetState(key: key, state: out TState? state)
                                ? onPresent(state)
                                : onAbsent())));
    }

    /// <inheritdoc />
    /// <remarks>
    ///     This comes from the change stream of this stage, as the state cell does, and it takes
    ///     the same operations. An update and a move thus send a value from one of these.
    /// </remarks>
    internal override Cell<TProjected> CreateItemCell<TProjected>(
        TKey key,
        Func<Item<TIdentity, TState>, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        ReactiveCollection<TKey, TIdentity, TState> root = this.source.Root;

        return TransactionInternal.RunImpl(() =>
            this.KeyChangesStream
                .MapImpl(change => change.ProjectItemChangeFor(key: key, onPresent: onPresent, onAbsent: onAbsent))
                .FilterSomeInternal()
                .HoldLazyImpl(
                    this.KeysCell.SampleLazyImpl()
                        .MapImpl(keys =>
                            keys.Contains(key)
                            && root.SnapshotCell.SampleImpl()
                                .TryGetItem(key: key, item: out Item<TIdentity, TState>? item)
                                ? onPresent(item)
                                : onAbsent())));
    }

    /// <inheritdoc />
    /// <remarks>
    ///     This comes from the change stream of this stage, as the state cell does. An update and a
    ///     move never come to it, thus an observer of the identity of one item through a view gets a
    ///     value only when that key enters the view or leaves it.
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
                            keys.Contains(key)
                            && root.SnapshotCell.SampleImpl()
                                .TryGetIdentity(key: key, identity: out TIdentity? identity)
                                ? onPresent(identity)
                                : onAbsent())));
    }
}

/// <summary>
///     The inputs from before the transaction. A stage reads them when the event in its current
///     step has no newer value for them.
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
///     The values that came to a stage in one transaction: a change from above, a new criteria, or
///     the two together.
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
        CollectionSnapshot<TKey, TIdentity, TState> after,
        bool movesKeys,
        bool changesMembership,
        bool reordersOnly)
    {
        this.Keys = keys;
        this.Operations = operations;
        this.IsReset = isReset;
        this.Before = before;
        this.After = after;
        this.MovesKeys = movesKeys;
        this.ChangesMembership = changesMembership;
        this.ReordersOnly = reordersOnly;
    }

    internal OrderedKeys<TKey, TIdentity, TState> Keys { get; }

    internal IReadOnlyList<ViewOperation<TKey>> Operations { get; }

    internal bool IsReset { get; }

    internal CollectionSnapshot<TKey, TIdentity, TState> Before { get; }

    internal CollectionSnapshot<TKey, TIdentity, TState> After { get; }

    /// <summary>Whether this result changes what the stage holds, or the order it holds it in.</summary>
    /// <remarks>
    ///     Each operation except an update changes this: an add, a removal, a move, or a reset. A
    ///     result with only updates keeps each key at its position, but its keys can be a new
    ///     version, because a sort that moved no key builds a version with the new sort value.
    /// </remarks>
    internal bool MovesKeys { get; }

    /// <summary>Whether this change alters what the view holds, rather than only where.</summary>
    /// <remarks>
    ///     A change of order is not a change of the members, thus a cell on the shape sends no
    ///     value at one.
    /// </remarks>
    internal bool ChangesMembership { get; }

    /// <summary>
    ///     True when this is a reset that changed only the order. The stage holds the keys that it
    ///     held, and no value of those keys changed.
    /// </summary>
    /// <remarks>See <see cref="CollectionViewChange{TKey,TIdentity,TState}.ReordersOnly" />.</remarks>
    internal bool ReordersOnly { get; }
}
