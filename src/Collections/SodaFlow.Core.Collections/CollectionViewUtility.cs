using System;
using System.Collections.Generic;
using System.Linq;

namespace SodaFlow.Collections;

/// <summary>
///     The whole of the view chain, reached by the C# and F# wrappers through their own surfaces.
/// </summary>
/// <remarks>
///     Each stage keeps its own <see cref="OrderedKeys{TKey,TIdentity,TState}" /> and applies the
///     operations from above:
///     Filter tests on insert or remove, may enter, leave or move on update, and ignores an upstream
///     move; Sort adds or drops on insert or remove, re-files in O(log n) on update, and imposes its
///     own order regardless of an upstream move; Take re-windows on insert, remove or move, and
///     forwards an update inside the window.
/// </remarks>
internal static class CollectionViewUtility
{
    /// <summary>
    ///     Builds the root ordering for a collection: every key, in the order it arrived. Called lazily
    ///     by <see cref="ReactiveCollection{TKey,TIdentity,TState}" /> the first time anything asks it
    ///     for keys in order.
    /// </summary>
    internal static ReactiveCollection<TKey, TIdentity, TState> CreateRootImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> collection)
        where TKey : notnull
        where TIdentity : notnull
    {
        KeyOrder<TKey, TIdentity, TState> order = KeyOrder<TKey, TIdentity, TState>.ByArrival();

        return TransactionInternal.Apply<ReactiveCollection<TKey, TIdentity, TState>>((trans, _) =>
        {
            LoopedCell<OrderedKeys<TKey, TIdentity, TState>> stateLoopCell = new();

            Stream<StageResult<TKey, TIdentity, TState>> resultsStream =
                collection.ItemChangesStream
                    .SnapshotImpl(c: stateLoopCell, f: ProcessRoot);

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
                // The root's ordering holds every key, so scoping it would wrap the store in a
                // filter that admits all of it.
                snapshotCell: () => collection.SnapshotCell,
                keyChangesStream: ToChangesStream(resultsStream));
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

    /// <summary>Reorders by arrival - the collection's own order, available over any stage.</summary>
    internal static ReactiveCollection<TKey, TIdentity, TState> SortByArrivalImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> upstream)
        where TKey : notnull
        where TIdentity : notnull =>
        SortByImpl(
            upstream: upstream,
            orderCell: CellInternal.ConstantImpl(KeyOrder<TKey, TIdentity, TState>.ByArrival()));

    /// <summary>
    ///     Narrows the view, preserving the upstream order. The stage files its members into a set
    ///     built from the upstream's own order, so it does not need to know what that order sorts by
    ///     and it does not have to track positions within the upstream list.
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
            process: ProcessFilter);

    /// <summary>
    ///     Reorders the view. <typeparamref name="TSortKey" /> stays a real generic parameter all
    ///     the way down to the comparer, so sort values are stored and compared as themselves and
    ///     never boxed.
    /// </summary>
    internal static ReactiveCollection<TKey, TIdentity, TState> SortByImpl<TKey, TIdentity, TState, TSortKey>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TState, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool descending)
        where TKey : notnull
        where TIdentity : notnull =>
        SortByImpl(
            upstream: upstream,
            orderCell: CellInternal.ConstantImpl(
                KeyOrder<TKey, TIdentity, TState>.By(
                    selector: selector,
                    sortComparer: sortComparer,
                    keyComparer: keyComparer,
                    descending: descending)));

    /// <summary>
    ///     Narrows the view by a predicate over each item's immutable half alone, which a state
    ///     edit cannot change.
    /// </summary>
    /// <remarks>
    ///     The same membership <see cref="FilterImpl{TKey,TIdentity,TState}" /> would give for the same
    ///     answers, and cheaper to keep. A state edit cannot move a key into this filter or out of
    ///     it, so the stage neither re-tests the predicate nor asks whether the key was already in
    ///     - it forwards the update and is done. The predicate is not handed the state, which is
    ///     what makes that checkable rather than promised.
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
            process: ProcessFilterByIdentity);

    /// <summary>
    ///     Reorders the view by a value projected from each item's immutable half alone, which a
    ///     state edit cannot change.
    /// </summary>
    /// <remarks>
    ///     The same ordering <see cref="SortByImpl{TKey,TIdentity,TState,TSortKey}" /> would give for the
    ///     same values, and cheaper to keep: a stage under this order skips re-filing a key it is
    ///     told merely changed, and building one never reads the state map. The selector is not
    ///     handed the state, which is what makes the claim checkable rather than promised.
    /// </remarks>
    internal static ReactiveCollection<TKey, TIdentity, TState> SortByIdentityImpl<TKey, TIdentity, TState, TSortKey>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool descending)
        where TKey : notnull
        where TIdentity : notnull =>
        SortByImpl(
            upstream: upstream,
            orderCell: CellInternal.ConstantImpl(
                KeyOrder<TKey, TIdentity, TState>.ByIdentity(
                    selector: selector,
                    sortComparer: sortComparer,
                    keyComparer: keyComparer,
                    descending: descending)));

    /// <summary>Reorders the view by whichever order the cell currently holds.</summary>
    /// <remarks>
    ///     <para>
    ///         This is the stage every other sort is built from, those being sorts whose order never
    ///         changes. Holding the order as criteria rather than closing over it is what lets one
    ///         stage follow a clickable column header: an order carries its own sort key type
    ///         inside itself, so the cell's type does not mention that type and two orders held in
    ///         one cell need not agree on it.
    ///     </para>
    ///     <para>
    ///         A new order is an ordinary criteria change - it rebuilds this stage and reports a
    ///         reset - and a stage below re-files under the new order without being told anything,
    ///         because a filter builds from its upstream's own order whatever that has become.
    ///     </para>
    /// </remarks>
    internal static ReactiveCollection<TKey, TIdentity, TState> SortByImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<KeyOrder<TKey, TIdentity, TState>> orderCell)
        where TKey : notnull
        where TIdentity : notnull =>
        BuildStage(
            upstream: upstream,
            criteriaCell: orderCell,
            rebuild: static (order, upstreamKeys, snapshot) =>
                RebuildSort(order: order, upstreamKeys: upstreamKeys, snapshot: snapshot),
            process: static (_, keys, change) => ProcessSort(state: keys, change: change));

    /// <summary>
    ///     The first <c>limit</c> keys of the upstream — the top-n of whatever ordering and
    ///     filtering precedes it.
    /// </summary>
    /// <remarks>
    ///     This stage diffs its old and new windows rather than translating upstream operations,
    ///     which costs O(limit) per transaction and yields a coarser operation list: a reorder
    ///     inside the window is reported as removes and inserts from the first differing position
    ///     rather than as moves. For a top-n that is the cheap direction to be wrong in.
    /// </remarks>
    internal static ReactiveCollection<TKey, TIdentity, TState> TakeImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<int> limitCell)
        where TKey : notnull
        where TIdentity : notnull =>
        SliceImpl(upstream: upstream, offsetCell: CellInternal.ConstantImpl(0), limitCell: limitCell);

    /// <summary>
    ///     A window of <c>limit</c> keys starting at <c>offset</c> - the page of whatever ordering
    ///     and filtering precedes it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the stage <see cref="TakeImpl{TKey,TIdentity,TState}" /> is built from, a take
    ///         being a window whose offset is zero.
    ///     </para>
    ///     <para>
    ///         There is deliberately no skip. This stage diffs its old window against its new one
    ///         rather than translating operations, which is what keeps it at O(limit) per
    ///         transaction; a skip has no limit, so the same strategy would materialize the whole
    ///         remainder of the collection twice per edit. Translating operations instead would
    ///         bound it, at the cost of boundary logic - an insert above the window pushes one key
    ///         into it, a remove above it pops one out - and a skip on its own yields a view whose
    ///         size follows the collection, which is the property this design exists to avoid.
    ///         Paging wants both halves anyway, and that is this.
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
                new RangeKeys<TKey, TIdentity, TState>(
                    source: upstreamKeys,
                    offset: bounds.Offset,
                    limit: bounds.Limit),
            process: static (bounds, keys, change) => ProcessSlice(bounds: bounds, state: keys, change: change));

    private static ReactiveCollection<TKey, TIdentity, TState> BuildStage<TKey, TIdentity, TState, TCriteria>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<TCriteria> criteriaCell,
        Func<TCriteria, OrderedKeys<TKey, TIdentity, TState>, CollectionSnapshot<TKey, TIdentity, TState>,
            OrderedKeys<TKey, TIdentity, TState>> rebuild,
        Func<TCriteria, OrderedKeys<TKey, TIdentity, TState>, CollectionViewChange<TKey, TIdentity, TState>,
            StageOutcome<TKey, TIdentity, TState>> process)
        where TKey : notnull
        where TIdentity : notnull =>
        TransactionInternal.Apply<ReactiveCollection<TKey, TIdentity, TState>>((trans, _) =>
        {
            LoopedCell<OrderedKeys<TKey, TIdentity, TState>> stateLoopCell = new();

            // The store, not the stage above's view of it. Reading upstream.SnapshotCell here would
            // force the lazy scoped cell of every stage in the chain, which is the whole cost
            // deferring it was meant to avoid - and this needs no scoping, because what it feeds
            // is scoped explicitly below.
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
                        // Anything the input carries is newer than the context, which is still the
                        // pre-transaction sample.
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

                        bool mustRebuild =
                            input.Criteria.Match(onSome: static _ => true, onNone: static () => false) ||
                            input.Change.Match(onSome: static change => change.IsReset, onNone: static () => false);

                        if (mustRebuild)
                        {
                            OrderedKeys<TKey, TIdentity, TState> rebuilt =
                                rebuild(arg1: criteria, arg2: upstreamKeys, arg3: snapshot);

                            return new StageResult<TKey, TIdentity, TState>(
                                keys: rebuilt,
                                operations: Array.Empty<ViewOperation<TKey>>(),
                                isReset: true,
                                before: context.Snapshot.ScopedTo(state),
                                after: snapshot.ScopedTo(rebuilt));
                        }

                        return input.Change.Match(
                            onSome: change =>
                            {
                                StageOutcome<TKey, TIdentity, TState> outcome =
                                    process(arg1: criteria, arg2: state, arg3: change);

                                return new StageResult<TKey, TIdentity, TState>(
                                    keys: outcome.Keys,
                                    operations: outcome.Operations,
                                    isReset: false,
                                    before: context.Snapshot.ScopedTo(state),
                                    after: snapshot.ScopedTo(outcome.Keys));
                            },
                            onNone: () =>
                                new StageResult<TKey, TIdentity, TState>(
                                    keys: state,
                                    operations: Array.Empty<ViewOperation<TKey>>(),
                                    isReset: false,
                                    before: context.Snapshot.ScopedTo(state),
                                    after: snapshot.ScopedTo(state)));
                    });

            Cell<OrderedKeys<TKey, TIdentity, TState>> keysCell =
                resultsStream
                    .MapImpl(static result => result.Keys)
                    .HoldLazyImpl(
                        contextCell.SampleLazyImpl()
                            .MapImpl(context =>
                                rebuild(arg1: context.Criteria, arg2: context.UpstreamKeys, arg3: context.Snapshot)));

            stateLoopCell.Loop(trans: trans, c: keysCell);

            return new ViewStage<TKey, TIdentity, TState>(
                source: upstream,
                keysCell: PublishedKeys(resultsStream: resultsStream, stateKeysCell: keysCell),
                // The one above it behind this stage's keys, built only if something asks.
                snapshotCell: () =>
                    TransactionInternal.RunImpl(() =>
                        upstream.SnapshotCell.LiftImpl(
                            b2: keysCell,
                            f: static (snapshot, keys) => snapshot.ScopedTo(keys))),
                keyChangesStream: ToChangesStream(resultsStream));
        });

    private static Stream<CollectionViewChange<TKey, TIdentity, TState>> ToChangesStream<TKey, TIdentity, TState>(
        Stream<StageResult<TKey, TIdentity, TState>> resultsStream)
        where TKey : notnull
        where TIdentity : notnull =>
        resultsStream
            .MapImpl(static result =>
                new CollectionViewChange<TKey, TIdentity, TState>(
                    before: result.Before,
                    after: result.After,
                    keys: result.Keys,
                    operations: result.Operations,
                    isReset: result.IsReset))
            .FilterImpl(static change => change.IsReset || change.Operations.Count > 0);

    /// <summary>
    ///     The keys a stage shows the world, which move only when its membership or order does.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not the cell the stage loops its own state through. That one takes every result,
    ///         because a re-file that moves nothing still builds a new version of the keys carrying
    ///         the new sort value, and the next edit has to be filed against it.
    ///     </para>
    ///     <para>
    ///         This one skips a result that only updates. Its keys have the same members in the same
    ///         order as the version already held, so there is nothing for a consumer to react to -
    ///         and a projection over them, a bound list or a count, would otherwise be rebuilt for
    ///         every state edit that reached the stage. Starting from the loop cell's own value
    ///         means the stage's keys are still built once at construction, not twice.
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

    // --- root ---------------------------------------------------------------------------------

    private static OrderedKeys<TKey, TIdentity, TState> RebuildRoot<TKey, TIdentity, TState>(
        KeyOrder<TKey, TIdentity, TState> order,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        FileAll(order: order, keys: snapshot.Identities.Keys, snapshot: snapshot);

    private static StageResult<TKey, TIdentity, TState> ProcessRoot<TKey, TIdentity, TState>(
        ItemChange<TKey, TIdentity, TState> change,
        OrderedKeys<TKey, TIdentity, TState> state)
        where TKey : notnull
        where TIdentity : notnull
    {
        OrderedKeys<TKey, TIdentity, TState> keys = state;
        List<ViewOperation<TKey>> operations = new();

        foreach (TKey key in change.Removed)
        {
            int index = keys.IndexOfInternal(key);

            if (index >= 0)
            {
                operations.Add(new ViewRemove<TKey>(key: key, index: index));
                keys = keys.Remove(key);
            }
        }

        foreach (TKey key in change.NewStates.Keys)
        {
            if (change.WasAdded(key))
            {
                // A key added while it is still here is an item replaced within one edit - removed and
                // added back. It is a new arrival and goes to the end, so the place it leaves is reported
                // as a remove before its new place is reported as an insert; an insert alone would have a
                // list bound to this count the key twice.
                int replaced = keys.IndexOfInternal(key);

                if (replaced >= 0)
                {
                    operations.Add(new ViewRemove<TKey>(key: key, index: replaced));
                    keys = keys.Remove(key);
                }

                keys = keys.Add(key: key, snapshot: change.After);

                int index = keys.IndexOfInternal(key);

                if (index >= 0)
                {
                    operations.Add(new ViewInsert<TKey>(key: key, index: index));
                }
            }
            else
            {
                // The root orders by arrival, and an update is not an arrival, so it never moves
                // anything here. It still has to be reported: a stage further down may sort or
                // filter on the state that just changed.
                int index = keys.IndexOfInternal(key);

                if (index >= 0)
                {
                    operations.Add(new ViewUpdate<TKey>(key: key, index: index));
                }
            }
        }

        return new StageResult<TKey, TIdentity, TState>(
            keys: keys,
            operations: operations,
            isReset: false,
            before: change.Before,
            after: change.After);
    }

    // --- filter -------------------------------------------------------------------------------

    private static OrderedKeys<TKey, TIdentity, TState> RebuildFilter<TKey, TIdentity, TState>(
        Func<TIdentity, TState, bool> predicate,
        OrderedKeys<TKey, TIdentity, TState> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        // Built from the upstream's own order, so this stage sorts exactly as its upstream does
        // without knowing what that order is.
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

    /// <summary>
    ///     What an identity-only filter does with a change, which on an update is nothing but pass
    ///     it on.
    /// </summary>
    /// <remarks>
    ///     An update carries a new state and nothing else, so it cannot have moved a key into this
    ///     filter or out of it - the ordinary path's <c>Contains</c> and predicate test would both
    ///     be computing a foregone conclusion. What is left is the index, which has to be looked up
    ///     anyway to report the update, and which answers -1 for a key this stage does not hold, so
    ///     one lookup settles both questions.
    ///     Nothing guards that with a snapshot lookup, because an update names a key the snapshot
    ///     holds by construction: the root emits one only for a key in its change's new states, and
    ///     every stage below it only forwards.
    /// </remarks>
    private static StageOutcome<TKey, TIdentity, TState> ProcessFilterByIdentity<TKey, TIdentity, TState>(
        Func<TIdentity, bool> predicate,
        OrderedKeys<TKey, TIdentity, TState> state,
        CollectionViewChange<TKey, TIdentity, TState> change)
        where TKey : notnull
        where TIdentity : notnull
    {
        OrderedKeys<TKey, TIdentity, TState> keys = state;
        List<ViewOperation<TKey>> operations = new();

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

                    if (inserted >= 0)
                    {
                        operations.Add(new ViewInsert<TKey>(key: insert.Key, index: inserted));
                    }

                    break;
                }

                case ViewRemove<TKey> remove:
                {
                    int removed = keys.IndexOfInternal(remove.Key);

                    if (removed >= 0)
                    {
                        operations.Add(new ViewRemove<TKey>(key: remove.Key, index: removed));
                        keys = keys.Remove(remove.Key);
                    }

                    break;
                }

                case ViewUpdate<TKey> update:
                {
                    int updated = keys.IndexOfInternal(update.Key);

                    if (updated >= 0)
                    {
                        operations.Add(new ViewUpdate<TKey>(key: update.Key, index: updated));
                    }

                    break;
                }

                // A move upstream needs no action, for the reason it needs none in the ordinary
                // filter: this stage's order is the upstream's applied to its own members.
            }
        }

        return new StageOutcome<TKey, TIdentity, TState>(keys: keys, operations: operations);
    }

    private static StageOutcome<TKey, TIdentity, TState> ProcessFilter<TKey, TIdentity, TState>(
        Func<TIdentity, TState, bool> predicate,
        OrderedKeys<TKey, TIdentity, TState> state,
        CollectionViewChange<TKey, TIdentity, TState> change)
        where TKey : notnull
        where TIdentity : notnull
    {
        OrderedKeys<TKey, TIdentity, TState> keys = state;
        List<ViewOperation<TKey>> operations = new();

        foreach (ViewOperation<TKey> operation in change.Operations)
        {
            switch (operation)
            {
                case ViewInsert<TKey> insert:
                    Include(insert.Key);
                    break;

                case ViewRemove<TKey> remove:
                    Exclude(remove.Key);
                    break;

                case ViewUpdate<TKey> update:
                    Refresh(update.Key);
                    break;

                // A move upstream needs no action: this stage's order is the upstream's order
                // applied to its own members, and the only thing that can change a member's
                // position is a state change, which arrives as an update.
            }
        }

        return new StageOutcome<TKey, TIdentity, TState>(keys: keys, operations: operations);

        void Include(TKey key)
        {
            if (!Passes(key: key, predicate: predicate, snapshot: change.After))
            {
                return;
            }

            keys = keys.Add(key: key, snapshot: change.After);

            int index = keys.IndexOfInternal(key);

            if (index >= 0)
            {
                operations.Add(new ViewInsert<TKey>(key: key, index: index));
            }
        }

        void Exclude(TKey key)
        {
            int index = keys.IndexOfInternal(key);

            if (index >= 0)
            {
                operations.Add(new ViewRemove<TKey>(key: key, index: index));
                keys = keys.Remove(key);
            }
        }

        void Refresh(TKey key)
        {
            bool was = keys.Contains(key);
            bool now = Passes(key: key, predicate: predicate, snapshot: change.After);

            switch (was)
            {
                case true when now:
                    Refile(keys: ref keys, operations: operations, key: key, snapshot: change.After);
                    break;

                case true:
                    Exclude(key);
                    break;

                default:
                    if (now)
                    {
                        Include(key);
                    }

                    break;
            }
        }
    }

    // --- sort ---------------------------------------------------------------------------------

    private static OrderedKeys<TKey, TIdentity, TState> RebuildSort<TKey, TIdentity, TState>(
        KeyOrder<TKey, TIdentity, TState> order,
        IEnumerable<TKey> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        FileAll(order: order, keys: upstreamKeys, snapshot: snapshot);

    private static StageOutcome<TKey, TIdentity, TState> ProcessSort<TKey, TIdentity, TState>(
        OrderedKeys<TKey, TIdentity, TState> state,
        CollectionViewChange<TKey, TIdentity, TState> change)
        where TKey : notnull
        where TIdentity : notnull
    {
        OrderedKeys<TKey, TIdentity, TState> keys = state;
        List<ViewOperation<TKey>> operations = new();

        foreach (ViewOperation<TKey> operation in change.Operations)
        {
            switch (operation)
            {
                case ViewInsert<TKey> insert:
                {
                    keys = keys.Add(key: insert.Key, snapshot: change.After);

                    int index = keys.IndexOfInternal(insert.Key);

                    if (index >= 0)
                    {
                        operations.Add(new ViewInsert<TKey>(key: insert.Key, index: index));
                    }

                    break;
                }

                case ViewRemove<TKey> remove:
                {
                    int index = keys.IndexOfInternal(remove.Key);

                    if (index >= 0)
                    {
                        operations.Add(new ViewRemove<TKey>(key: remove.Key, index: index));
                        keys = keys.Remove(remove.Key);
                    }

                    break;
                }

                case ViewUpdate<TKey> update:
                    Refile(keys: ref keys, operations: operations, key: update.Key, snapshot: change.After);
                    break;

                // Upstream moves are irrelevant: this stage imposes its own order.
            }
        }

        return new StageOutcome<TKey, TIdentity, TState>(keys: keys, operations: operations);
    }

    // --- take ---------------------------------------------------------------------------------

    private static StageOutcome<TKey, TIdentity, TState> ProcessSlice<TKey, TIdentity, TState>(
        (int Offset, int Limit) bounds,
        IEnumerable<TKey> state,
        CollectionViewChange<TKey, TIdentity, TState> change)
        where TKey : notnull
        where TIdentity : notnull
    {
        OrderedKeys<TKey, TIdentity, TState> keys =
            new RangeKeys<TKey, TIdentity, TState>(source: change.Keys, offset: bounds.Offset, limit: bounds.Limit);

        List<TKey> before = new(state);
        List<TKey> after = new(keys);

        int common = 0;

        while (common < before.Count &&
               common < after.Count &&
               EqualityComparer<TKey>.Default.Equals(x: before[common], y: after[common]))
        {
            common++;
        }

        List<ViewOperation<TKey>> operations = new();

        for (int index = before.Count - 1; index >= common; index--)
        {
            operations.Add(new ViewRemove<TKey>(key: before[index], index: index));
        }

        for (int index = common; index < after.Count; index++)
        {
            operations.Add(new ViewInsert<TKey>(key: after[index], index: index));
        }

        // Keys that survived in place still need their updates forwarded, or a stage below this one
        // would never hear that their state changed.
        foreach (ViewOperation<TKey> operation in change.Operations)
        {
            if (operation is not ViewUpdate<TKey> update)
            {
                continue;
            }

            int index = after.IndexOf(update.Key);

            if (index >= 0 && index < common)
            {
                operations.Add(new ViewUpdate<TKey>(key: update.Key, index: index));
            }
        }

        return new StageOutcome<TKey, TIdentity, TState>(keys: keys, operations: operations);
    }

    /// <summary>
    ///     One object per key, in the collection's order, rebuilt only when the keys move.
    /// </summary>
    /// <remarks>
    ///     The projection runs once per key and the object is kept, so a collection whose items
    ///     changed but whose membership and order did not yields the same objects in the same
    ///     order - which is what keeps a bound list from rebuilding when one row's value moves.
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
            new(project: project, retainedBeyondTheView: retainedBeyondTheView, onEvicted: onEvicted);

        return new MappedItems<TResult>(
            items: collection.KeysCell.MapImpl(cache.Project),
            dispose: cache.ReleaseAll);
    }

    // --- shared -------------------------------------------------------------------------------

    /// <summary>Files every key into a new set under one order.</summary>
    /// <remarks>
    ///     In bulk, which is what keeps a rebuild from costing one persistent write per key. See
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
    ///     Whether an item passes a predicate that reads its identity and not its state, which is
    ///     one lookup rather than two.
    /// </summary>
    private static bool PassesByIdentity<TKey, TIdentity, TState>(
        TKey key,
        Func<TIdentity, bool> predicate,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        snapshot.TryGetIdentity(key: key, identity: out TIdentity identity) && predicate(identity);

    private static bool Passes<TKey, TIdentity, TState>(
        TKey key,
        Func<TIdentity, TState, bool> predicate,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        snapshot.TryGetHalves(key: key, identity: out TIdentity identity, state: out TState state) &&
        predicate(arg1: identity, arg2: state);

    /// <summary>
    ///     Removes and re-adds a key so it is filed under its new sort value, reporting a move if
    ///     that changed its position and an update either way.
    /// </summary>
    /// <remarks>
    ///     Unless the order cannot have moved it, in which case the removing and re-adding is four
    ///     tree operations that put the key back where it already was. Both callers reach this on
    ///     every state edit that touches a key they hold, so that is the incremental path, and an
    ///     order projecting its sort value from the key or the identity - the root's, and any
    ///     filter sitting directly on it - can never move a key on a state edit.
    ///     The snapshot is still consulted, because a key the snapshot has dropped does have to
    ///     leave the set, and one lookup is cheaper than the four operations it replaces.
    /// </remarks>
    private static void Refile<TKey, TIdentity, TState>(
        ref OrderedKeys<TKey, TIdentity, TState> keys,
        ICollection<ViewOperation<TKey>> operations,
        TKey key,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull
    {
        if (!keys.Order.DependsOnState && snapshot.ContainsKey(key))
        {
            int at = keys.IndexOfInternal(key);

            if (at >= 0)
            {
                operations.Add(new ViewUpdate<TKey>(key: key, index: at));
            }

            return;
        }

        int fromIndex = keys.IndexOfInternal(key);

        OrderedKeys<TKey, TIdentity, TState> updated = keys.Remove(key).Add(key: key, snapshot: snapshot);
        int toIndex = updated.IndexOfInternal(key);

        keys = updated;

        if (toIndex < 0)
        {
            // Gone from the set entirely, which a re-file can do when the snapshot no longer has
            // the item.
            if (fromIndex >= 0)
            {
                operations.Add(new ViewRemove<TKey>(key: key, index: fromIndex));
            }

            return;
        }

        if (fromIndex < 0)
        {
            operations.Add(new ViewInsert<TKey>(key: key, index: toIndex));

            return;
        }

        if (fromIndex != toIndex)
        {
            operations.Add(new ViewMove<TKey>(key: key, fromIndex: fromIndex, toIndex: toIndex));
        }

        operations.Add(new ViewUpdate<TKey>(key: key, index: toIndex));
    }
}

/// <summary>
///     What a stage's incremental step produced. A named type rather than the value tuple the
///     original used: this assembly compiles at C# 10 for netstandard2.0 and net472 as well, where a
///     tuple in a generic delegate's return position costs a System.ValueTuple reference for no
///     gain in readability here.
/// </summary>
internal sealed class StageOutcome<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    internal StageOutcome(
        OrderedKeys<TKey, TIdentity, TState> keys,
        IReadOnlyList<ViewOperation<TKey>> operations)
    {
        this.Keys = keys;
        this.Operations = operations;
    }

    internal OrderedKeys<TKey, TIdentity, TState> Keys { get; }

    internal IReadOnlyList<ViewOperation<TKey>> Operations { get; }
}
