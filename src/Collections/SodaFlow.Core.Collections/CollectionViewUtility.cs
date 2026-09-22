using System;
using System.Collections.Generic;
using System.Linq;

namespace SodaFlow.Collections;

/// <summary>
///     The full view chain. The C# wrapper and the F# wrapper use it through their own
///     surfaces.
/// </summary>
/// <remarks>
///     Each stage keeps its own <see cref="OrderedKeys{TKey,TIdentity,TState}" /> and applies the
///     operations from above. Filter tests a key at an add and at a removal. At an update, and at
///     a move above it, the key can enter the view, go out of the view, or move in it. Sort adds a
///     key or removes a key at an add and at a removal. At an update, and at a move above it, Sort
///     puts the key in its new position at a cost of <c>O(log n)</c>. Take makes its window again
///     at an add, a removal, or a move, and it sends an update for a key in the window. This code
///     sorts a move from above again and does not ignore it. A sort that moves a key reports only
///     the move, and that report is the only value with the new sort value of the key. Thus, a
///     stage below must sort against it, as it sorts against an update.
/// </remarks>
internal static class CollectionViewUtility
{
    /// <summary>
    ///     The quantity of work that a stage does on one change before it builds again.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This counts work and not messages. The work is an add, a removal, or a sort in an
    ///         order that reads the state, and each one writes paths through the trees of the
    ///         stage again. An update that the stage sends on, and that does not change a tree,
    ///         does not count. Such an update is for a key that the stage does not hold, which it
    ///         omits after one lookup, or it is in an order that a state edit cannot move, and the
    ///         stage then reports the key at its current position.
    ///     </para>
    ///     <para>
    ///         That difference is most important at the root, because a reset there makes each
    ///         stage in the chain build again. A count of each key in an edit gives an incorrect
    ///         result. A large update to items that a filter below does not show, such as a drain
    ///         of each frozen account, built the full chain again for rows that no user sees. A
    ///         list of those keys costs the filter one omission for each key. Each stage sets a
    ///         limit on its own work, thus an update that a stage must sort again counts at that
    ///         stage.
    ///     </para>
    /// </remarks>
    private static int GetMaxNumberOfOperations(int totalItems) => Math.Max(val1: 1000, val2: totalItems / 10);

    /// <summary>
    ///     Builds the root order of a collection, which is each key in the sequence of its
    ///     arrival. <see cref="ReactiveCollection{TKey,TIdentity,TState}" /> calls this at the
    ///     first read of the keys in order, and not before.
    /// </summary>
    internal static ReactiveCollection<TKey, TIdentity, TState> CreateRootImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> collection)
        where TKey : notnull
        where TIdentity : notnull
    {
        KeyOrder<TKey, TIdentity, TState> order =
            KeyOrder<TKey, TIdentity, TState>.ByArrival().With(collection.Root.KeyEqualityComparer);

        return TransactionInternal.Apply<ReactiveCollection<TKey, TIdentity, TState>>((trans, _) =>
        {
            LoopedCell<OrderedKeys<TKey, TIdentity, TState>> stateLoopCell = new();

            Stream<StageResult<TKey, TIdentity, TState>> resultsStream =
                collection.ItemChangesStream
                    .SnapshotImpl(
                        c: stateLoopCell,
                        f: (change, state) =>
                            ProcessRoot(change: change, state: state)
                                .Match(
                                    onSome: static result => result,
                                    onNone: () =>
                                    {
                                        OrderedKeys<TKey, TIdentity, TState> rebuilt =
                                            RebuildRoot(order: order, snapshot: change.After);

                                        return new StageResult<TKey, TIdentity, TState>(
                                            keys: rebuilt,
                                            operations: Array.Empty<ViewOperation<TKey>>(),
                                            isReset: true,
                                            before: change.Before,
                                            after: change.After,
                                            movesKeys: true,
                                            changesMembership: true,
                                            reordersOnly: false);
                                    }));

            Cell<OrderedKeys<TKey, TIdentity, TState>> keysCell =
                resultsStream
                    .MapImpl(static result => result.Keys)
                    .HoldLazyImpl(
                        collection.SnapshotCell.SampleLazyImpl()
                            .MapImpl(snapshot => RebuildRoot(order: order, snapshot: snapshot)));

            stateLoopCell.Loop(trans: trans, c: keysCell);

            return new ViewStage<TKey, TIdentity, TState>(
                source: collection,
                keysCell: PublishedKeys(resultsStream: resultsStream, stateKeysCell: keysCell),
                // The order of the root holds each key, thus a scope on it puts the store in a
                // filter that accepts each key.
                snapshotCell: () => collection.SnapshotCell,
                keyChangesStream: ToChangesStream(
                    resultsStream: resultsStream,
                    keyEqualityComparer: collection.Root.KeyEqualityComparer));
        });
    }

    /// <summary>Reorders by key, over any stage.</summary>
    internal static ReactiveCollection<TKey, TIdentity, TState> SortByKeyImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        IComparer<TKey> keyComparer)
        where TKey : notnull
        where TIdentity : notnull =>
        SortByImpl(
            upstream: upstream,
            orderCell: CellInternal.ConstantImpl(KeyOrder<TKey, TIdentity, TState>.ByKey(keyComparer)));

    /// <summary>Reorders by arrival, which is the order of the collection. It is available above
    /// each stage.</summary>
    internal static ReactiveCollection<TKey, TIdentity, TState> SortByArrivalImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> upstream)
        where TKey : notnull
        where TIdentity : notnull =>
        SortByImpl(
            upstream: upstream,
            orderCell: CellInternal.ConstantImpl(KeyOrder<TKey, TIdentity, TState>.ByArrival()));

    /// <summary>
    ///     Narrows the view and keeps the upstream order. The stage puts its members into a set
    ///     that comes from the order of the upstream. Thus, the stage does not know the sort value
    ///     of that order, and it does not monitor a position in the upstream list.
    /// </summary>
    internal static ReactiveCollection<TKey, TIdentity, TState> FilterImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<Func<TIdentity, TState, bool>> predicateCell)
        where TKey : notnull
        where TIdentity : notnull =>
        BuildStage(
            upstream: upstream,
            criteriaCell: predicateCell,
            rebuild: RebuildFilter,
            processNewCriteria: ProcessFilterNewCriteria,
            process: ProcessFilter,
            reorder: ReorderFilter);

    /// <summary>
    ///     Reorders the view. <typeparamref name="TSortKey" /> stays a generic parameter to the
    ///     comparer, thus this code keeps and compares each sort value as its own type and never
    ///     boxes it.
    /// </summary>
    internal static ReactiveCollection<TKey, TIdentity, TState> SortByImpl<TKey, TIdentity, TState, TSortKey>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TState, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool isDescending)
        where TKey : notnull
        where TIdentity : notnull =>
        SortByImpl(
            upstream: upstream,
            orderCell: CellInternal.ConstantImpl(
                KeyOrder<TKey, TIdentity, TState>.By(
                    selector: selector,
                    sortComparer: sortComparer,
                    keyComparer: keyComparer,
                    isDescending: isDescending)));

    /// <summary>
    ///     Narrows the view with a predicate on the immutable part of each item only. A state
    ///     edit cannot change that part.
    /// </summary>
    /// <remarks>
    ///     This gives the same members as <see cref="FilterImpl{TKey,TIdentity,TState}" /> for the
    ///     same answers, and it costs less to keep. A state edit cannot move a key into this filter
    ///     or out of it, thus the stage never tests the predicate on such a key again. The stage
    ///     sorts a key that it holds, and in an order that reads no state that operation only
    ///     reports the update. The predicate does not receive the state, thus a reader can test
    ///     this property and does not use a statement.
    /// </remarks>
    internal static ReactiveCollection<TKey, TIdentity, TState> FilterByIdentityImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, bool> predicate)
        where TKey : notnull
        where TIdentity : notnull =>
        BuildStage(
            upstream: upstream,
            criteriaCell: CellInternal.ConstantImpl(predicate),
            rebuild: RebuildFilterByIdentity,
            processNewCriteria: ProcessFilterByIdentityNewCriteria,
            process: ProcessFilterByIdentity,
            reorder: ReorderFilter);

    /// <summary>
    ///     Reorders the view by a value from the immutable part of each item only. A state edit
    ///     cannot change that part.
    /// </summary>
    /// <remarks>
    ///     This gives the same order as
    ///     <see cref="SortByImpl{TKey,TIdentity,TState,TSortKey}" /> for the same values, and it
    ///     costs less to keep. A stage in this order does not sort a key again when it hears only
    ///     that the key changed, and the construction of a stage never reads the state map. The
    ///     selector does not receive the state, thus a reader can test this property and does not
    ///     use a statement.
    /// </remarks>
    internal static ReactiveCollection<TKey, TIdentity, TState> SortByIdentityImpl<TKey, TIdentity, TState, TSortKey>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool isDescending)
        where TKey : notnull
        where TIdentity : notnull =>
        SortByImpl(
            upstream: upstream,
            orderCell: CellInternal.ConstantImpl(
                KeyOrder<TKey, TIdentity, TState>.ByIdentity(
                    selector: selector,
                    sortComparer: sortComparer,
                    keyComparer: keyComparer,
                    isDescending: isDescending)));

    /// <summary>Reorders the view by whichever order the cell currently holds.</summary>
    /// <remarks>
    ///     <para>
    ///         This stage is the base of each other sort, and the order of each one does not
    ///         change. This stage holds the order as criteria and does not capture it in a
    ///         closure. Thus, one stage can follow a column header that a user clicks. An order
    ///         holds its own sort key type, thus the type of the cell does not name that type, and
    ///         two orders in one cell can have different sort key types.
    ///     </para>
    ///     <para>
    ///         A new order is a usual change of criteria, and this stage always reports it as a
    ///         reset. The stage builds again in the new order. When the new order is the order that
    ///         the stage holds in the opposite direction, the stage sorts its keys again with the
    ///         sort values that it has. The report is a reset in the two conditions. The one order
    ///         with a different answer is an order equivalent to the order that the stage holds,
    ///         which is no change and reports nothing. A stage below sorts again in the new order
    ///         and no code tells it to, because a filter uses the current order of its upstream
    ///         collection.
    ///     </para>
    ///     <para>
    ///         The reset reports only a change of order, because no other change came to this
    ///         stage in the transaction. A filter below moves its members to the new order and does
    ///         not test its predicate again. A sort below keeps its list. The two stages send the
    ///         same report on. A window below builds again, because a change of order changes the
    ///         keys in the window. See
    ///         <see cref="CollectionViewChange{TKey,TIdentity,TState}.ReordersOnly" />.
    ///     </para>
    ///     <para>
    ///         Never report a change of order as moves, at each count of the keys that move. A
    ///         move means a change to the value of a key, and the stages below use that rule. A
    ///         filter sorts a key that moved in the order that it holds, and the key then stays in
    ///         the previous order. Each stage below, and each cell for one item, reads a move as a
    ///         change of value, thus each one sorts again or sends a value for no cause. See
    ///         <see cref="ViewMove{TKey}" />.
    ///     </para>
    /// </remarks>
    internal static ReactiveCollection<TKey, TIdentity, TState> SortByImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<KeyOrder<TKey, TIdentity, TState>> orderCell)
        where TKey : notnull
        where TIdentity : notnull =>
        BuildStage(
            upstream: upstream,
            criteriaCell: orderCell.MapImpl(order => order.With(upstream.Root.KeyEqualityComparer)),
            rebuild: static (order, upstreamKeys, snapshot) =>
                RebuildSort(order: order, upstreamKeys: upstreamKeys, snapshot: snapshot),
            processNewCriteria: ProcessSortNewCriteria,
            process: static (_, keys, change) => ProcessSort(state: keys, change: change),
            // The order of the stage and its members do not change, thus the list that it holds
            // is the list from a new build.
            reorder: static (state, _) => state);

    /// <summary>
    ///     The first <c>limit</c> keys of the upstream, which are the highest keys of the order
    ///     and the filter above this stage.
    /// </summary>
    /// <remarks>
    ///     This stage compares its previous window against its new window, and does not change
    ///     the operations from above. That costs <c>O(limit)</c> for each transaction and gives a
    ///     less accurate list of operations. It reports a change of order in the window as
    ///     removals and adds from the first different position, and not as moves. For a window of
    ///     the highest keys, that is the less expensive error.
    /// </remarks>
    internal static ReactiveCollection<TKey, TIdentity, TState> TakeImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<int> limitCell)
        where TKey : notnull
        where TIdentity : notnull =>
        SliceImpl(upstream: upstream, offsetCell: CellInternal.ConstantImpl(0), limitCell: limitCell);

    /// <summary>
    ///     A window of <c>limit</c> keys that starts at <c>offset</c>, which is a page of the
    ///     order and the filter above this stage.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This stage is the base of <see cref="TakeImpl{TKey,TIdentity,TState}" />, and a
    ///         <c>TakeImpl</c> is a window with an offset of zero.
    ///     </para>
    ///     <para>
    ///         There is no skip. This stage compares its previous window against its new window
    ///         and does not change the operations from above, and that keeps the cost at
    ///         <c>O(limit)</c> for each transaction. A skip has no limit, thus the same method
    ///         makes the full remainder of the collection two times for each edit. A change of the
    ///         operations gives a limit, at the cost of the code for the two edges: an add above
    ///         the window moves one key into it, and a removal above the window moves one key out
    ///         of it. A skip alone also gives a view whose size follows the collection, and this
    ///         rule prevents that. A page needs the two ends, and this stage gives them.
    ///     </para>
    /// </remarks>
    internal static ReactiveCollection<TKey, TIdentity, TState> SliceImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<int> offsetCell,
        Cell<int> limitCell)
        where TKey : notnull
        where TIdentity : notnull =>
        BuildStage(
            upstream: upstream,
            criteriaCell: offsetCell.LiftImpl(
                b2: limitCell,
                f: static (offset, limit) => (Offset: offset, Limit: limit)),
            rebuild: static (bounds, upstreamKeys, _) =>
                RebuildSlice(upstreamKeys: upstreamKeys, offset: bounds.Offset, limit: bounds.Limit),
            processNewCriteria: static (_, createResultFromRebuild, _, _, _, _, _) => createResultFromRebuild(),
            process: static (bounds, keys, change) => ProcessSlice(bounds: bounds, state: keys, change: change),
            // A change of the order above a window changes the keys in the window.
            reorder: null);

    private static ReactiveCollection<TKey, TIdentity, TState> BuildStage<TKey, TIdentity, TState, TCriteria>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<TCriteria> criteriaCell,
        Rebuild<TCriteria, TKey, TIdentity, TState> rebuild,
        ProcessNewCriteria<TCriteria, TKey, TIdentity, TState> processNewCriteria,
        Process<TCriteria, TKey, TIdentity, TState> process,
        Reorder<TKey, TIdentity, TState>? reorder)
        where TKey : notnull
        where TIdentity : notnull =>
        TransactionInternal.Apply<ReactiveCollection<TKey, TIdentity, TState>>((trans, _) =>
        {
            LoopedCell<OrderedKeys<TKey, TIdentity, TState>> stateLoopCell = new();

            // This is the store, and not the view of the store in the stage above. A read of
            // upstream.SnapshotCell here makes the lazy scoped cell of each stage in the chain,
            // which is the cost that the deferral prevents. This code needs no scope, because the
            // code below gives an explicit scope to the value that it receives.
            Cell<StageContext<TKey, TIdentity, TState, TCriteria>> contextCell =
                criteriaCell.LiftImpl(
                    b2: upstream.KeysCell,
                    b3: upstream.Root.SnapshotCell,
                    f: static (criteria, upstreamKeys, snapshot) =>
                        new StageContext<TKey, TIdentity, TState, TCriteria>(
                            criteria: criteria,
                            upstreamKeys: upstreamKeys,
                            snapshot: snapshot));

            Stream<StageInput<TKey, TIdentity, TState, TCriteria>> inputStream =
                upstream.KeyChangesStream
                    .MapImpl(static change =>
                        new StageInput<TKey, TIdentity, TState, TCriteria>(
                            change: MaybeInternal.Some(change),
                            criteria: MaybeInternal<TCriteria>.None))
                    .MergeImpl(
                        s: criteriaCell.UpdatesImpl.MapImpl(static criteria =>
                            new StageInput<TKey, TIdentity, TState, TCriteria>(
                                change: MaybeInternal<CollectionViewChange<TKey, TIdentity, TState>>.None,
                                criteria: MaybeInternal.Some(criteria))),
                        f: static (left, right) =>
                            new StageInput<TKey, TIdentity, TState, TCriteria>(
                                change: left.Change.Match(onSome: MaybeInternal.Some, onNone: () => right.Change),
                                criteria: left.Criteria.Match(
                                    onSome: MaybeInternal.Some,
                                    onNone: () => right.Criteria)));

            Stream<StageResult<TKey, TIdentity, TState>> resultsStream =
                inputStream.SnapshotImpl(
                    c1: stateLoopCell,
                    c2: contextCell,
                    f: (input, state, context) =>
                    {
                        // Each value in the input is newer than the context, which is the sample
                        // from before the transaction.
                        TCriteria criteria =
                            input.Criteria.Match(onSome: static c => c, onNone: () => context.Criteria);

                        CollectionSnapshot<TKey, TIdentity, TState> snapshot =
                            input.Change.Match(
                                onSome: static change => change.After,
                                onNone: () => context.Snapshot);

                        OrderedKeys<TKey, TIdentity, TState> upstreamKeys =
                            input.Change.Match(
                                onSome: static change => change.Keys,
                                onNone: () => context.UpstreamKeys);

                        bool hasCriteriaChange =
                            input.Criteria.Match(onSome: static _ => true, onNone: static () => false);

                        bool mustRebuild =
                            input.Change.Match(
                                onSome: change => hasCriteriaChange || change.IsReset,
                                onNone: static () => false);

                        if (mustRebuild)
                        {
                            // A change of order above, with no criteria of this stage to apply,
                            // does not change the members of this stage or their values.
                            return input.Change.Match(
                                onSome: change =>
                                    !hasCriteriaChange && change.ReordersOnly && reorder is not null
                                        ? CreateReorder(reorder(state: state, change: change))
                                        : Rebuild(),
                                onNone: Rebuild);
                        }

                        return input.Change.Match(
                            onSome: change =>
                                ConvertOutcomeMaybe(process(criteria: criteria, state: state, change: change)),
                            onNone: () =>
                            {
                                if (hasCriteriaChange)
                                {
                                    return processNewCriteria(
                                        createResultFromStageOutcome: ConvertOutcome,
                                        createResultFromRebuild: Rebuild,
                                        createResultForReorder: CreateReorder,
                                        criteria: criteria,
                                        upstreamKeys: upstreamKeys,
                                        snapshot: snapshot,
                                        state: state);
                                }

                                CollectionSnapshot<TKey, TIdentity, TState> beforeAndAfter = snapshot.ScopedTo(state);

                                return new StageResult<TKey, TIdentity, TState>(
                                    keys: state,
                                    operations: Array.Empty<ViewOperation<TKey>>(),
                                    isReset: false,
                                    before: beforeAndAfter,
                                    after: beforeAndAfter,
                                    movesKeys: false,
                                    changesMembership: false,
                                    reordersOnly: false);
                            });

                        StageResult<TKey, TIdentity, TState> CreateReset(
                            OrderedKeys<TKey, TIdentity, TState> keys,
                            bool reordersOnly) =>
                            new(
                                keys: keys,
                                operations: Array.Empty<ViewOperation<TKey>>(),
                                isReset: true,
                                before: context.Snapshot.ScopedTo(state),
                                after: snapshot.ScopedTo(keys),
                                movesKeys: true,
                                changesMembership: !reordersOnly,
                                reordersOnly: reordersOnly);

                        StageResult<TKey, TIdentity, TState> CreateReorder(OrderedKeys<TKey, TIdentity, TState> keys) =>
                            CreateReset(keys: keys, reordersOnly: true);

                        StageResult<TKey, TIdentity, TState> Rebuild() =>
                            CreateReset(
                                keys: rebuild(criteria: criteria, upstreamKeys: upstreamKeys, snapshot: snapshot),
                                reordersOnly: false);

                        StageResult<TKey, TIdentity, TState> ConvertOutcome(
                            StageOutcome<TKey, TIdentity, TState> outcome) =>
                            new(
                                keys: outcome.Keys,
                                operations: outcome.Operations,
                                isReset: false,
                                before: context.Snapshot.ScopedTo(state),
                                after: snapshot.ScopedTo(outcome.Keys),
                                movesKeys: outcome.MovesKeys,
                                changesMembership: outcome.ChangesMembership,
                                reordersOnly: false);

                        StageResult<TKey, TIdentity, TState> ConvertOutcomeMaybe(
                            MaybeInternal<StageOutcome<TKey, TIdentity, TState>> outcome) =>
                            outcome.Match(onSome: ConvertOutcome, onNone: Rebuild);
                    });

            Cell<OrderedKeys<TKey, TIdentity, TState>> keysCell =
                resultsStream
                    .MapImpl(static result => result.Keys)
                    .HoldLazyImpl(
                        contextCell.SampleLazyImpl()
                            .MapImpl(context =>
                                rebuild(
                                    criteria: context.Criteria,
                                    upstreamKeys: context.UpstreamKeys,
                                    snapshot: context.Snapshot)));

            stateLoopCell.Loop(trans: trans, c: keysCell);

            return new ViewStage<TKey, TIdentity, TState>(
                source: upstream,
                keysCell: PublishedKeys(resultsStream: resultsStream, stateKeysCell: keysCell),
                // The cell above it is behind the keys of this stage, and this code builds it
                // only when other code reads it.
                snapshotCell: () =>
                    TransactionInternal.RunImpl(() =>
                        upstream.SnapshotCell.LiftImpl(
                            b2: keysCell,
                            f: static (snapshot, keys) => snapshot.ScopedTo(keys))),
                keyChangesStream: ToChangesStream(
                    resultsStream: resultsStream,
                    keyEqualityComparer: upstream.Root.KeyEqualityComparer));
        });

    private static Stream<CollectionViewChange<TKey, TIdentity, TState>> ToChangesStream<TKey, TIdentity, TState>(
        Stream<StageResult<TKey, TIdentity, TState>> resultsStream,
        IEqualityComparer<TKey> keyEqualityComparer)
        where TKey : notnull
        where TIdentity : notnull =>
        resultsStream
            .MapImpl(result =>
                new CollectionViewChange<TKey, TIdentity, TState>(
                    before: result.Before,
                    after: result.After,
                    keys: result.Keys,
                    operations: result.Operations,
                    isReset: result.IsReset,
                    movesKeys: result.MovesKeys,
                    changesMembership: result.ChangesMembership,
                    reordersOnly: result.ReordersOnly,
                    keyEqualityComparer: keyEqualityComparer))
            .FilterImpl(static change => change.IsReset || change.Operations.Count > 0);

    /// <summary>
    ///     The keys that a stage gives to other code. They move only at a change of its members
    ///     or its order.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is not the cell that the stage loops its own state through. That cell takes
    ///         each result, because a sort that moves no key builds a new version of the keys with
    ///         the new sort value, and the next edit sorts against that version.
    ///     </para>
    ///     <para>
    ///         This cell omits a result that only updates. Its keys have the same members in the
    ///         same order as the version that it holds, thus a consumer has no change to react to.
    ///         Without this, each state edit that comes to the stage builds a projection of those
    ///         keys, a bound list, or a count again. This code starts from the value of the loop
    ///         cell, thus the construction builds the keys of the stage one time and not two
    ///         times.
    ///     </para>
    /// </remarks>
    private static Cell<OrderedKeys<TKey, TIdentity, TState>> PublishedKeys<TKey, TIdentity, TState>(
        Stream<StageResult<TKey, TIdentity, TState>> resultsStream,
        Cell<OrderedKeys<TKey, TIdentity, TState>> stateKeysCell)
        where TKey : notnull
        where TIdentity : notnull =>
        resultsStream
            .FilterImpl(static result => result.MovesKeys)
            .MapImpl(static result => result.Keys)
            .HoldLazyImpl(stateKeysCell.SampleLazyImpl());

    /// <summary>
    ///     Increases the operation counter. It returns <see langword="false" /> when the count is
    ///     above the maximum number of operations.
    /// </summary>
    /// <param name="numberOfOperations">The operation counter, passed by reference.</param>
    /// <param name="maxNumberOfOperations">The maximum number of operations allowed.</param>
    /// <returns>
    ///     <see langword="true" /> when the count is not above the maximum number of operations,
    ///     and <see langword="false" /> when the count is above the maximum.
    /// </returns>
    private static bool OperationAddedWasValid(ref int numberOfOperations, int maxNumberOfOperations)
    {
        if (numberOfOperations == maxNumberOfOperations)
        {
            return false;
        }

        numberOfOperations++;

        return true;
    }

    #region Map

    /// <summary>
    ///     One object for each key, in the order of the collection. This code builds the list
    ///     again only at a move of the keys.
    /// </summary>
    /// <remarks>
    ///     The projection runs one time for each key and this code keeps the object. Thus, a
    ///     collection whose items changed, and whose members and order did not change, gives the
    ///     same objects in the same sequence. That stops a new build of a bound list at a change
    ///     to the value of one row.
    /// </remarks>
    internal static MappedItems<TResult> MapImpl<TKey, TIdentity, TState, TResult>(
        ReactiveCollection<TKey, TIdentity, TState> collection,
        Func<TKey, TResult> project,
        int retainedBeyondTheView,
        Action<TResult>? onEvicted)
        where TKey : notnull
        where TIdentity : notnull
    {
        if (retainedBeyondTheView < 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName: nameof(retainedBeyondTheView),
                actualValue: retainedBeyondTheView,
                message: "A projection cannot retain a negative number of departed keys.");
        }

        MappedItemCache<TKey, TResult> cache =
            new(
                project: project,
                retainedBeyondTheView: retainedBeyondTheView,
                onEvicted: onEvicted,
                keyEqualityComparer: collection.Root.KeyEqualityComparer);

        return new MappedItems<TResult>(
            items: collection.KeysCell.MapImpl(cache.Project),
            dispose: cache.ReleaseAll);
    }

    #endregion

    private delegate OrderedKeys<TKey, TIdentity, TState> Rebuild<in TCriteria, TKey, TIdentity, TState>(
        TCriteria criteria,
        OrderedKeys<TKey, TIdentity, TState> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull;

    private delegate StageResult<TKey, TIdentity, TState> ProcessNewCriteria<in TCriteria, TKey, TIdentity, TState>(
        Func<StageOutcome<TKey, TIdentity, TState>, StageResult<TKey, TIdentity, TState>> createResultFromStageOutcome,
        Func<StageResult<TKey, TIdentity, TState>> createResultFromRebuild,
        Func<OrderedKeys<TKey, TIdentity, TState>, StageResult<TKey, TIdentity, TState>> createResultForReorder,
        TCriteria criteria,
        OrderedKeys<TKey, TIdentity, TState> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot,
        OrderedKeys<TKey, TIdentity, TState> state)
        where TKey : notnull
        where TIdentity : notnull;

    /// <summary>
    ///     The keys of a stage after the stage above it changed its order and nothing else
    ///     changed. This applies to a stage that can answer with no new build.
    /// </summary>
    /// <param name="state">The current content of the stage.</param>
    /// <param name="change">The change of order. Its keys are the keys of the stage above, in the new order.</param>
    private delegate OrderedKeys<TKey, TIdentity, TState> Reorder<TKey, TIdentity, TState>(
        OrderedKeys<TKey, TIdentity, TState> state,
        CollectionViewChange<TKey, TIdentity, TState> change)
        where TKey : notnull
        where TIdentity : notnull;

    private delegate MaybeInternal<StageOutcome<TKey, TIdentity, TState>>
        Process<in TCriteria, TKey, TIdentity, TState>(
            TCriteria criteria,
            OrderedKeys<TKey, TIdentity, TState> state,
            CollectionViewChange<TKey, TIdentity, TState> change)
        where TKey : notnull
        where TIdentity : notnull;

    #region Root

    private static OrderedKeys<TKey, TIdentity, TState> RebuildRoot<TKey, TIdentity, TState>(
        KeyOrder<TKey, TIdentity, TState> order,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        FileAll(order: order, keys: snapshot.Identities.Keys, snapshot: snapshot);

    private static MaybeInternal<StageResult<TKey, TIdentity, TState>> ProcessRoot<TKey, TIdentity, TState>(
        ItemChange<TKey, TIdentity, TState> change,
        OrderedKeys<TKey, TIdentity, TState> state)
        where TKey : notnull
        where TIdentity : notnull
    {
        int maxNumberOfOperations = GetMaxNumberOfOperations(state.Count);

        // Only an operation that adds a key to the order, or removes a key from it, counts. The
        // root uses the order of arrival, and no update can move a key in that order. Thus, an
        // update costs the root one lookup, and each stage below counts its own cost.
        if (change.Added.Count + change.Removed.Count > maxNumberOfOperations)
        {
            return MaybeInternal<StageResult<TKey, TIdentity, TState>>.None;
        }

        OrderedKeys<TKey, TIdentity, TState> keys = state;
        List<ViewOperation<TKey>> operations = new();
        bool movesKeys = false;
        bool changesMembership = false;

        foreach (TKey key in change.Removed)
        {
            int index = keys.IndexOfInternal(key);

            if (index >= 0)
            {
                operations.Add(new ViewRemove<TKey>(key: key, index: index));
                movesKeys = true;
                changesMembership = true;
                keys = keys.Remove(key);
            }
        }

        foreach (TKey key in change.NewStates.Keys)
        {
            if (change.WasAdded(key))
            {
                // An add of a key that is here now is a replacement of an item in one edit: a
                // removal and then an add. The key is a new arrival and goes to the end. Thus, this
                // code reports a removal at its previous position before it reports an add at its
                // new position. With only an add, a list that binds to this counts the key two
                // times.
                int replaced = keys.IndexOfInternal(key);

                if (replaced >= 0)
                {
                    operations.Add(new ViewRemove<TKey>(key: key, index: replaced));
                    keys = keys.Remove(key);
                }

                keys = keys.Add(key: key, snapshot: change.After);

                int index = keys.IndexOfInternal(key);

                operations.Add(new ViewInsert<TKey>(key: key, index: index));
                movesKeys = true;
                changesMembership = true;
            }
            else
            {
                // The root uses the order of arrival, and an update is not an arrival, thus an
                // update moves no key here. This code must report it, because a stage below can
                // sort or filter on the state that changed.
                int index = keys.IndexOfInternal(key);

                if (index >= 0)
                {
                    operations.Add(new ViewUpdate<TKey>(key: key, index: index));
                }
            }
        }

        return MaybeInternal<StageResult<TKey, TIdentity, TState>>.Some(
            new StageResult<TKey, TIdentity, TState>(
                keys: keys,
                operations: operations,
                isReset: false,
                before: change.Before,
                after: change.After,
                movesKeys: movesKeys,
                changesMembership: changesMembership,
                reordersOnly: false));
    }

    #endregion

    #region Filter

    private static OrderedKeys<TKey, TIdentity, TState> RebuildFilter<TKey, TIdentity, TState>(
        Func<TIdentity, TState, bool> predicate,
        OrderedKeys<TKey, TIdentity, TState> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        // This comes from the order of the upstream, thus this stage sorts as its upstream sorts
        // and does not know that order.
        FileAll(
            order: upstreamKeys.Order,
            keys: upstreamKeys.Where(key => Passes(key: key, predicate: predicate, snapshot: snapshot)),
            snapshot: snapshot);

    private static OrderedKeys<TKey, TIdentity, TState> RebuildFilterByIdentity<TKey, TIdentity, TState>(
        Func<TIdentity, bool> predicate,
        OrderedKeys<TKey, TIdentity, TState> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        FileAll(
            order: upstreamKeys.Order,
            keys: upstreamKeys.Where(key => PassesByIdentity(key: key, predicate: predicate, snapshot: snapshot)),
            snapshot: snapshot);

    /// <summary>The members of a filter, in the new order of its upstream.</summary>
    /// <remarks>
    ///     No value that the predicate reads changed, thus the members do not change. This code
    ///     puts them in the new order, and does not read the full upstream and test each item
    ///     again. When the order is the order that the stage holds, which occurs for a change of
    ///     order from above a stage that kept its own order, the members are in that order.
    /// </remarks>
    private static OrderedKeys<TKey, TIdentity, TState> ReorderFilter<TKey, TIdentity, TState>(
        OrderedKeys<TKey, TIdentity, TState> state,
        CollectionViewChange<TKey, TIdentity, TState> change)
        where TKey : notnull
        where TIdentity : notnull =>
        ReferenceEquals(objA: state.Order, objB: change.Keys.Order)
            ? state
            : FileAll(order: change.Keys.Order, keys: state, snapshot: change.After);

    private static StageResult<TKey, TIdentity, TState> ProcessFilterNewCriteria<TKey, TIdentity, TState>(
        Func<StageOutcome<TKey, TIdentity, TState>, StageResult<TKey, TIdentity, TState>> createResultFromStageOutcome,
        Func<StageResult<TKey, TIdentity, TState>> createResultFromRebuild,
        Func<OrderedKeys<TKey, TIdentity, TState>, StageResult<TKey, TIdentity, TState>> createResultForReorder,
        Func<TIdentity, TState, bool> predicate,
        OrderedKeys<TKey, TIdentity, TState> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot,
        OrderedKeys<TKey, TIdentity, TState> state)
        where TKey : notnull
        where TIdentity : notnull
    {
        int maxNumberOfOperations = GetMaxNumberOfOperations(state.Count);
        int numberOfOperations = 0;

        OrderedKeys<TKey, TIdentity, TState> keys = state;
        List<ViewOperation<TKey>> operations = new();
        bool movesKeys = false;
        bool changesMembership = false;

        foreach (TKey key in upstreamKeys)
        {
            bool passes = Passes(key: key, predicate: predicate, snapshot: snapshot);
            int index = keys.IndexOfInternal(key);

            if (passes)
            {
                if (index < 0)
                {
                    keys = keys.Add(key: key, snapshot: snapshot);

                    int newIndex = keys.IndexOfInternal(key);

                    operations.Add(new ViewInsert<TKey>(key: key, index: newIndex));
                    movesKeys = true;
                    changesMembership = true;

                    if (!OperationAddedWasValid(
                            numberOfOperations: ref numberOfOperations,
                            maxNumberOfOperations: maxNumberOfOperations))
                    {
                        return createResultFromRebuild();
                    }
                }
            }
            else
            {
                if (index >= 0)
                {
                    operations.Add(new ViewRemove<TKey>(key: key, index: index));
                    movesKeys = true;
                    changesMembership = true;
                    keys = keys.Remove(key);

                    if (!OperationAddedWasValid(
                            numberOfOperations: ref numberOfOperations,
                            maxNumberOfOperations: maxNumberOfOperations))
                    {
                        return createResultFromRebuild();
                    }
                }
            }
        }

        return createResultFromStageOutcome(
            new StageOutcome<TKey, TIdentity, TState>(
                keys: keys,
                operations: operations,
                movesKeys: movesKeys,
                changesMembership: changesMembership));
    }

    private static StageResult<TKey, TIdentity, TState> ProcessFilterByIdentityNewCriteria<TKey, TIdentity, TState>(
        Func<StageOutcome<TKey, TIdentity, TState>, StageResult<TKey, TIdentity, TState>> createResultFromStageOutcome,
        Func<StageResult<TKey, TIdentity, TState>> createResultFromRebuild,
        Func<OrderedKeys<TKey, TIdentity, TState>, StageResult<TKey, TIdentity, TState>> createResultForReorder,
        Func<TIdentity, bool> predicate,
        OrderedKeys<TKey, TIdentity, TState> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot,
        OrderedKeys<TKey, TIdentity, TState> state)
        where TKey : notnull
        where TIdentity : notnull
    {
        int maxNumberOfOperations = GetMaxNumberOfOperations(state.Count);
        int numberOfOperations = 0;

        OrderedKeys<TKey, TIdentity, TState> keys = state;
        List<ViewOperation<TKey>> operations = new();
        bool movesKeys = false;
        bool changesMembership = false;

        foreach (TKey key in upstreamKeys)
        {
            bool passes = PassesByIdentity(key: key, predicate: predicate, snapshot: snapshot);
            int index = keys.IndexOfInternal(key);

            if (passes)
            {
                if (index < 0)
                {
                    keys = keys.Add(key: key, snapshot: snapshot);

                    int newIndex = keys.IndexOfInternal(key);

                    operations.Add(new ViewInsert<TKey>(key: key, index: newIndex));
                    movesKeys = true;
                    changesMembership = true;

                    if (!OperationAddedWasValid(
                            numberOfOperations: ref numberOfOperations,
                            maxNumberOfOperations: maxNumberOfOperations))
                    {
                        return createResultFromRebuild();
                    }
                }
            }
            else
            {
                if (index >= 0)
                {
                    operations.Add(new ViewRemove<TKey>(key: key, index: index));
                    movesKeys = true;
                    changesMembership = true;
                    keys = keys.Remove(key);

                    if (!OperationAddedWasValid(
                            numberOfOperations: ref numberOfOperations,
                            maxNumberOfOperations: maxNumberOfOperations))
                    {
                        return createResultFromRebuild();
                    }
                }
            }
        }

        return createResultFromStageOutcome(
            new StageOutcome<TKey, TIdentity, TState>(
                keys: keys,
                operations: operations,
                movesKeys: movesKeys,
                changesMembership: changesMembership));
    }

    private static MaybeInternal<StageOutcome<TKey, TIdentity, TState>> ProcessFilter<TKey, TIdentity, TState>(
        Func<TIdentity, TState, bool> predicate,
        OrderedKeys<TKey, TIdentity, TState> state,
        CollectionViewChange<TKey, TIdentity, TState> change)
        where TKey : notnull
        where TIdentity : notnull
    {
        int maxNumberOfOperations = GetMaxNumberOfOperations(state.Count);
        int numberOfOperations = 0;

        // In an order that reads no state, a sort is one lookup and one update, and the limit does
        // not count those. See GetMaxNumberOfOperations.
        bool refilingCostsWork = state.Order.DependsOnState;

        OrderedKeys<TKey, TIdentity, TState> keys = state;
        List<ViewOperation<TKey>> operations = new();
        bool movesKeys = false;
        bool changesMembership = false;

        foreach (ViewOperation<TKey> operation in change.Operations)
        {
            switch (operation)
            {
                case ViewInsert<TKey> insert:
                    if (Include(key: insert.Key, alreadyPassed: false))
                    {
                        if (!OperationAddedWasValid(
                                numberOfOperations: ref numberOfOperations,
                                maxNumberOfOperations: maxNumberOfOperations))
                        {
                            return MaybeInternal<StageOutcome<TKey, TIdentity, TState>>.None;
                        }
                    }

                    break;

                case ViewRemove<TKey> remove:
                    if (Exclude(remove.Key))
                    {
                        if (!OperationAddedWasValid(
                                numberOfOperations: ref numberOfOperations,
                                maxNumberOfOperations: maxNumberOfOperations))
                        {
                            return MaybeInternal<StageOutcome<TKey, TIdentity, TState>>.None;
                        }
                    }

                    break;

                case ViewUpdate<TKey> update:
                    if (Refresh(update.Key))
                    {
                        if (!OperationAddedWasValid(
                                numberOfOperations: ref numberOfOperations,
                                maxNumberOfOperations: maxNumberOfOperations))
                        {
                            return MaybeInternal<StageOutcome<TKey, TIdentity, TState>>.None;
                        }
                    }

                    break;

                case ViewMove<TKey> move:
                    if (Refresh(move.Key))
                    {
                        if (!OperationAddedWasValid(
                                numberOfOperations: ref numberOfOperations,
                                maxNumberOfOperations: maxNumberOfOperations))
                        {
                            return MaybeInternal<StageOutcome<TKey, TIdentity, TState>>.None;
                        }
                    }

                    break;

                default:
                    throw new InvalidOperationException("Unhandled operation type: " + operation.GetType().FullName);
            }
        }

        return MaybeInternal.Some(
            new StageOutcome<TKey, TIdentity, TState>(
                keys: keys,
                operations: operations,
                movesKeys: movesKeys,
                changesMembership: changesMembership));

        bool Include(TKey key, bool alreadyPassed)
        {
            if (!alreadyPassed && !Passes(key: key, predicate: predicate, snapshot: change.After))
            {
                return false;
            }

            keys = keys.Add(key: key, snapshot: change.After);

            int index = keys.IndexOfInternal(key);

            operations.Add(new ViewInsert<TKey>(key: key, index: index));
            movesKeys = true;
            changesMembership = true;

            return true;
        }

        bool Exclude(TKey key)
        {
            int index = keys.IndexOfInternal(key);

            // A missing key is not an error here. A removal from above names each key that leaves
            // the stage above, with the keys that this filter never accepted.
            if (index >= 0)
            {
                operations.Add(new ViewRemove<TKey>(key: key, index: index));
                movesKeys = true;
                changesMembership = true;
                keys = keys.Remove(key);

                return true;
            }

            return false;
        }

        // True when the update or the move cost this stage work that its limit counts.
        bool Refresh(TKey key)
        {
            bool was = keys.Contains(key);
            bool now = Passes(key: key, predicate: predicate, snapshot: change.After);

            if (was)
            {
                if (now)
                {
                    if (Refile(keys: ref keys, operations: operations, key: key, snapshot: change.After))
                    {
                        movesKeys = true;
                    }

                    return refilingCostsWork;
                }

                if (Exclude(key))
                {
                    movesKeys = true;
                    changesMembership = true;

                    return true;
                }

                return false;
            }

            if (now)
            {
                if (Include(key: key, alreadyPassed: true)) // now being true means alreadyPassed can be true
                {
                    movesKeys = true;
                    changesMembership = true;

                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    ///     The operation of a filter on the identity only. At an update, and at a move, it sorts a
    ///     key that it holds again and never tests a key again.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An update and a move give a new state and nothing else, thus the two cannot move a
    ///         key into this filter or out of it. The predicate test on the usual path
    ///         calculates a known answer, and one lookup with no result answers for a key that
    ///         this stage does not hold.
    ///     </para>
    ///     <para>
    ///         An update can move a key in the filter. This stage keeps the order of its upstream
    ///         collection. When that order reads the state, a state edit changes the sort value of
    ///         a key that the stage holds, and the upstream can report that as a move or not. A
    ///         value can change and not move the key across a second key, and the next arrival
    ///         then sorts against an incorrect value. Thus, this code sorts a key that it holds
    ///         again, and reports the positions of this stage. In an order that reads no state,
    ///         that sort is one lookup and one update, which is the only operation here.
    ///     </para>
    /// </remarks>
    private static MaybeInternal<StageOutcome<TKey, TIdentity, TState>>
        ProcessFilterByIdentity<TKey, TIdentity, TState>(
            Func<TIdentity, bool> predicate,
            OrderedKeys<TKey, TIdentity, TState> state,
            CollectionViewChange<TKey, TIdentity, TState> change)
        where TKey : notnull
        where TIdentity : notnull
    {
        int maxNumberOfOperations = GetMaxNumberOfOperations(state.Count);
        int numberOfOperations = 0;

        // This is the same as the usual filter. A sort in an order that reads no state does not
        // count.
        bool refilingCostsWork = state.Order.DependsOnState;

        OrderedKeys<TKey, TIdentity, TState> keys = state;
        List<ViewOperation<TKey>> operations = new();
        bool movesKeys = false;
        bool changesMembership = false;

        foreach (ViewOperation<TKey> operation in change.Operations)
        {
            switch (operation)
            {
                case ViewInsert<TKey> insert:
                {
                    if (!PassesByIdentity(key: insert.Key, predicate: predicate, snapshot: change.After))
                    {
                        break;
                    }

                    keys = keys.Add(key: insert.Key, snapshot: change.After);

                    int inserted = keys.IndexOfInternal(insert.Key);

                    operations.Add(new ViewInsert<TKey>(key: insert.Key, index: inserted));
                    movesKeys = true;
                    changesMembership = true;

                    if (!OperationAddedWasValid(
                            numberOfOperations: ref numberOfOperations,
                            maxNumberOfOperations: maxNumberOfOperations))
                    {
                        return MaybeInternal<StageOutcome<TKey, TIdentity, TState>>.None;
                    }

                    break;
                }

                case ViewRemove<TKey> remove:
                {
                    int removed = keys.IndexOfInternal(remove.Key);

                    if (removed >= 0)
                    {
                        operations.Add(new ViewRemove<TKey>(key: remove.Key, index: removed));
                        movesKeys = true;
                        changesMembership = true;

                        if (!OperationAddedWasValid(
                                numberOfOperations: ref numberOfOperations,
                                maxNumberOfOperations: maxNumberOfOperations))
                        {
                            return MaybeInternal<StageOutcome<TKey, TIdentity, TState>>.None;
                        }

                        keys = keys.Remove(remove.Key);
                    }

                    break;
                }

                case ViewUpdate<TKey> update:
                {
                    if (Refresh(update.Key))
                    {
                        if (!OperationAddedWasValid(
                                numberOfOperations: ref numberOfOperations,
                                maxNumberOfOperations: maxNumberOfOperations))
                        {
                            return MaybeInternal<StageOutcome<TKey, TIdentity, TState>>.None;
                        }
                    }

                    break;
                }

                case ViewMove<TKey> move:
                {
                    if (Refresh(move.Key))
                    {
                        if (!OperationAddedWasValid(
                                numberOfOperations: ref numberOfOperations,
                                maxNumberOfOperations: maxNumberOfOperations))
                        {
                            return MaybeInternal<StageOutcome<TKey, TIdentity, TState>>.None;
                        }
                    }

                    break;
                }

                default:
                    throw new InvalidOperationException("Unhandled operation type: " + operation.GetType().FullName);
            }
        }

        return MaybeInternal.Some(
            new StageOutcome<TKey, TIdentity, TState>(
                keys: keys,
                operations: operations,
                movesKeys: movesKeys,
                changesMembership: changesMembership));

        // True when the update or the move cost this stage work that its limit counts.
        bool Refresh(TKey key)
        {
            if (!keys.Contains(key))
            {
                return false;
            }

            if (Refile(keys: ref keys, operations: operations, key: key, snapshot: change.After))
            {
                movesKeys = true;
            }

            return refilingCostsWork;
        }
    }

    #endregion

    #region Sort

    private static OrderedKeys<TKey, TIdentity, TState> RebuildSort<TKey, TIdentity, TState>(
        KeyOrder<TKey, TIdentity, TState> order,
        IEnumerable<TKey> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        FileAll(order: order, keys: upstreamKeys, snapshot: snapshot);

    /// <summary>The report of a sort stage at a new order, when nothing else changed.</summary>
    /// <remarks>
    ///     It is a reset, or nothing for an order equivalent to the order that the stage holds. It
    ///     is never a set of operations. A second use of the sort values that the stage holds, for
    ///     an order in the opposite direction, prevents a new sort value for each key. It does not
    ///     prevent the report of a reset. See
    ///     <see
    ///         cref="SortByImpl{TKey,TIdentity,TState}(ReactiveCollection{TKey,TIdentity,TState},Cell{KeyOrder{TKey,TIdentity,TState}})" />
    ///     for why a change of order cannot be reported as moves.
    /// </remarks>
    private static StageResult<TKey, TIdentity, TState> ProcessSortNewCriteria<TKey, TIdentity, TState>(
        Func<StageOutcome<TKey, TIdentity, TState>, StageResult<TKey, TIdentity, TState>> createResultFromStageOutcome,
        Func<StageResult<TKey, TIdentity, TState>> createResultFromRebuild,
        Func<OrderedKeys<TKey, TIdentity, TState>, StageResult<TKey, TIdentity, TState>> createResultForReorder,
        KeyOrder<TKey, TIdentity, TState> order,
        OrderedKeys<TKey, TIdentity, TState> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot,
        OrderedKeys<TKey, TIdentity, TState> state)
        where TKey : notnull
        where TIdentity : notnull
    {
        if (order.IsEquivalentTo(state.Order))
        {
            return new StageResult<TKey, TIdentity, TState>(
                keys: state,
                operations: Array.Empty<ViewOperation<TKey>>(),
                before: snapshot,
                after: snapshot,
                movesKeys: false,
                changesMembership: false,
                isReset: false,
                reordersOnly: false);
        }

        // This code runs only when the order is the one change that came to this stage in the
        // transaction. Thus, the stage holds the keys that it held, with their values, and each
        // answer is a change of order.
        return createResultForReorder(
            order.TryReverse(keys: state, reversedKeys: out OrderedKeys<TKey, TIdentity, TState>? reversedKeys)
                ? reversedKeys
                : RebuildSort(order: order, upstreamKeys: upstreamKeys, snapshot: snapshot));
    }

    private static MaybeInternal<StageOutcome<TKey, TIdentity, TState>> ProcessSort<TKey, TIdentity, TState>(
        OrderedKeys<TKey, TIdentity, TState> state,
        CollectionViewChange<TKey, TIdentity, TState> change)
        where TKey : notnull
        where TIdentity : notnull
    {
        OrderedKeys<TKey, TIdentity, TState> keys = state;
        List<ViewOperation<TKey>> operations = new();
        bool movesKeys = false;
        bool changesMembership = false;

        if (!state.Order.DependsOnState && change is { MovesKeys: false, ChangesMembership: false })
        {
            foreach (ViewOperation<TKey> operation in change.Operations)
            {
                if (operation is not ViewUpdate<TKey> update)
                {
                    throw new InvalidOperationException("Only updates should be handled here.");
                }

                int index = keys.IndexOfInternal(update.Key);

                // A sort holds each key of its upstream, thus an update for a key that it cannot
                // find is a defect above it, and not a key that the sort removed.
                if (index < 0)
                {
                    throw new InvalidOperationException("A sort can only hear an update for a key it holds.");
                }

                operations.Add(new ViewUpdate<TKey>(key: update.Key, index: index));
            }

            return MaybeInternal.Some(
                new StageOutcome<TKey, TIdentity, TState>(
                    keys: keys,
                    operations: operations,
                    movesKeys: movesKeys,
                    changesMembership: changesMembership));
        }

        int maxNumberOfOperations = GetMaxNumberOfOperations(state.Count);
        int numberOfOperations = 0;

        // A sort in an order that reads no state does not count. See GetMaxNumberOfOperations.
        bool refilingCostsWork = state.Order.DependsOnState;

        // A sort holds each key of the stage above it, thus each operation names a key that the
        // sort holds. In an order that reads the state, each one of those operations costs work.
        // Thus, this code knows before the work if the change is above the limit, and does not know
        // it after the work at the limit. A new build discards that work.
        if (refilingCostsWork && change.Operations.Count > maxNumberOfOperations)
        {
            return MaybeInternal<StageOutcome<TKey, TIdentity, TState>>.None;
        }

        foreach (ViewOperation<TKey> operation in change.Operations)
        {
            switch (operation)
            {
                case ViewInsert<TKey> insert:
                {
                    keys = keys.Add(key: insert.Key, snapshot: change.After);

                    int index = keys.IndexOfInternal(insert.Key);

                    operations.Add(new ViewInsert<TKey>(key: insert.Key, index: index));
                    movesKeys = true;
                    changesMembership = true;

                    if (!OperationAddedWasValid(
                            numberOfOperations: ref numberOfOperations,
                            maxNumberOfOperations: maxNumberOfOperations))
                    {
                        return MaybeInternal<StageOutcome<TKey, TIdentity, TState>>.None;
                    }

                    break;
                }

                case ViewRemove<TKey> remove:
                {
                    int index = keys.IndexOfInternal(remove.Key);

                    // This is the same as an update. A sort holds each key of its upstream, thus a
                    // key that it cannot find to remove is a defect above it.
                    if (index < 0)
                    {
                        throw new InvalidOperationException("A sort can only hear a remove for a key it holds.");
                    }

                    operations.Add(new ViewRemove<TKey>(key: remove.Key, index: index));
                    movesKeys = true;
                    changesMembership = true;

                    if (!OperationAddedWasValid(
                            numberOfOperations: ref numberOfOperations,
                            maxNumberOfOperations: maxNumberOfOperations))
                    {
                        return MaybeInternal<StageOutcome<TKey, TIdentity, TState>>.None;
                    }

                    keys = keys.Remove(remove.Key);

                    break;
                }

                case ViewUpdate<TKey> update:
                    if (Refile(keys: ref keys, operations: operations, key: update.Key, snapshot: change.After))
                    {
                        movesKeys = true;
                    }

                    if (refilingCostsWork
                        && !OperationAddedWasValid(
                            numberOfOperations: ref numberOfOperations,
                            maxNumberOfOperations: maxNumberOfOperations))
                    {
                        return MaybeInternal<StageOutcome<TKey, TIdentity, TState>>.None;
                    }

                    break;

                case ViewMove<TKey> move:
                    if (Refile(keys: ref keys, operations: operations, key: move.Key, snapshot: change.After))
                    {
                        movesKeys = true;
                    }

                    if (refilingCostsWork
                        && !OperationAddedWasValid(
                            numberOfOperations: ref numberOfOperations,
                            maxNumberOfOperations: maxNumberOfOperations))
                    {
                        return MaybeInternal<StageOutcome<TKey, TIdentity, TState>>.None;
                    }

                    break;

                default:
                    throw new InvalidOperationException("Unhandled operation type: " + operation.GetType().FullName);
            }
        }

        return MaybeInternal.Some(
            new StageOutcome<TKey, TIdentity, TState>(
                keys: keys,
                operations: operations,
                movesKeys: movesKeys,
                changesMembership: changesMembership));
    }

    #endregion

    #region Slice

    private static OrderedKeys<TKey, TIdentity, TState> RebuildSlice<TKey, TIdentity, TState>(
        OrderedKeys<TKey, TIdentity, TState> upstreamKeys,
        int offset,
        int limit)
        where TKey : notnull
        where TIdentity : notnull =>
        new RangeKeys<TKey, TIdentity, TState>(
            source: upstreamKeys,
            offset: offset,
            limit: limit);

    private static MaybeInternal<StageOutcome<TKey, TIdentity, TState>> ProcessSlice<TKey, TIdentity, TState>(
        (int Offset, int Limit) bounds,
        IEnumerable<TKey> state,
        CollectionViewChange<TKey, TIdentity, TState> change)
        where TKey : notnull
        where TIdentity : notnull
    {
        OrderedKeys<TKey, TIdentity, TState> keys =
            new RangeKeys<TKey, TIdentity, TState>(source: change.Keys, offset: bounds.Offset, limit: bounds.Limit);

        List<ViewOperation<TKey>> operations = new();
        bool changesMembership = false;

        int common = -1;

        if (change.MovesKeys || change.ChangesMembership)
        {
            List<TKey> before = new(state);
            List<TKey> after = new(keys);

            common = 0;

            while (common < before.Count
                   && common < after.Count
                   && change.KeyEqualityComparer.Equals(x: before[common], y: after[common]))
            {
                common++;
            }

            for (int index = before.Count - 1; index >= common; index--)
            {
                operations.Add(new ViewRemove<TKey>(key: before[index], index: index));
                changesMembership = true;
            }

            for (int index = common; index < after.Count; index++)
            {
                operations.Add(new ViewInsert<TKey>(key: after[index], index: index));
                changesMembership = true;
            }
        }

        // A key that stays at its position also needs its update, or a stage below this one does
        // not learn about the change to its state.
        foreach (ViewOperation<TKey> operation in change.Operations)
        {
            if (operation is not ViewUpdate<TKey> update)
            {
                continue;
            }

            int index = keys.IndexOfInternal(update.Key);

            if (index >= 0 && (common < 0 || index < common))
            {
                operations.Add(new ViewUpdate<TKey>(key: update.Key, index: index));
            }
        }

        return MaybeInternal.Some(
            new StageOutcome<TKey, TIdentity, TState>(
                keys: keys,
                operations: operations,
                movesKeys: changesMembership,
                changesMembership: changesMembership));
    }

    #endregion

    #region Shared

    /// <summary>Puts each key into a new set, in one order.</summary>
    /// <remarks>
    ///     This operates on all keys together, thus a new build does not cost one write for each
    ///     key. See
    ///     <see cref="KeyOrder{TKey,TIdentity,TState}.CreateFrom" />.
    /// </remarks>
    private static OrderedKeys<TKey, TIdentity, TState> FileAll<TKey, TIdentity, TState>(
        KeyOrder<TKey, TIdentity, TState> order,
        IEnumerable<TKey> keys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        order.CreateFrom(keys: keys, snapshot: snapshot);

    /// <summary>
    ///     True when an item agrees with a predicate that reads its identity and not its state.
    ///     That is one lookup and not two.
    /// </summary>
    private static bool PassesByIdentity<TKey, TIdentity, TState>(
        TKey key,
        Func<TIdentity, bool> predicate,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        snapshot.TryGetIdentity(key: key, identity: out TIdentity? identity) && predicate(identity);

    private static bool Passes<TKey, TIdentity, TState>(
        TKey key,
        Func<TIdentity, TState, bool> predicate,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        snapshot.TryGetHalves(key: key, identity: out TIdentity? identity, state: out TState? state)
        && predicate(arg1: identity, arg2: state);

    /// <summary>
    ///     Puts a key that the stage holds at its new sort value, and reports one operation. That
    ///     operation is a move when the position changed, and an update when it did not change.
    /// </summary>
    /// <returns>True when the key moved.</returns>
    /// <remarks>
    ///     <para>
    ///         This code reports a move alone, with no update. The move is the sort, and the new
    ///         value of the key moved it. Thus, a consumer, and a stage below, reads the move as an
    ///         update that also moved the key.
    ///     </para>
    ///     <para>
    ///         An order that cannot move a key at a state edit omits the removal and the add,
    ///         which are four tree operations to put the key at its current position. Each caller
    ///         comes here at each state edit to a key that it holds, thus this is the incremental
    ///         path. An order that makes its sort value from the key or from the identity can never
    ///         move a key at a state edit. The order of the root is such an order, and so is each
    ///         filter directly above it.
    ///     </para>
    ///     <para>
    ///         This code always sorts in the order of <paramref name="keys" />, and never in the
    ///         order of the upstream. A sort sets that order itself, thus this is always correct
    ///         for a sort. A filter keeps the order of its upstream collection, and this is correct
    ///         for a filter because a change of order is always a reset and never a move. A change
    ///         of value in an order that did not change causes each operation that comes here. For
    ///         the same cause, the short path is correct at a move and at an update. In an order
    ///         that reads no state, a change of value cannot move a key, at each operation that
    ///         reports it.
    ///     </para>
    ///     <para>
    ///         The key must be a key that the stage holds and a key that the snapshot has. Each
    ///         caller sorts only the keys that it holds, for operations that name keys in the
    ///         snapshot. Thus, a key that fails one of the two tests is a defect above this code.
    ///         This method throws an exception for a key that the stage does not hold, and a sort
    ///         of a key that the snapshot does not hold throws an exception in <c>Project</c>. It
    ///         does not report an operation with no position.
    ///     </para>
    /// </remarks>
    private static bool Refile<TKey, TIdentity, TState>(
        ref OrderedKeys<TKey, TIdentity, TState> keys,
        ICollection<ViewOperation<TKey>> operations,
        TKey key,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull
    {
        int fromIndex = keys.IndexOfInternal(key);

        if (fromIndex < 0)
        {
            throw new InvalidOperationException("A stage can only re-file a key it holds.");
        }

        if (!keys.Order.DependsOnState)
        {
            operations.Add(new ViewUpdate<TKey>(key: key, index: fromIndex));

            return false;
        }

        keys = keys.Remove(key).Add(key: key, snapshot: snapshot);
        int toIndex = keys.IndexOfInternal(key);

        if (fromIndex != toIndex)
        {
            operations.Add(new ViewMove<TKey>(key: key, fromIndex: fromIndex, toIndex: toIndex));

            return true;
        }

        operations.Add(new ViewUpdate<TKey>(key: key, index: toIndex));

        return false;
    }

    #endregion
}

/// <summary>
///     The result of the incremental step of a stage. This is a named type and not the value
///     tuple of the initial code. This assembly also compiles at C# 10 for netstandard2.0 and
///     net472, where a tuple in the return position of a generic delegate costs a
///     System.ValueTuple reference and gives no better readability here.
/// </summary>
internal sealed class StageOutcome<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    internal StageOutcome(
        OrderedKeys<TKey, TIdentity, TState> keys,
        IReadOnlyList<ViewOperation<TKey>> operations,
        bool movesKeys,
        bool changesMembership)
    {
        this.Keys = keys;
        this.Operations = operations;
        this.MovesKeys = movesKeys;
        this.ChangesMembership = changesMembership;
    }

    internal OrderedKeys<TKey, TIdentity, TState> Keys { get; }

    internal IReadOnlyList<ViewOperation<TKey>> Operations { get; }

    internal bool MovesKeys { get; }

    internal bool ChangesMembership { get; }
}
