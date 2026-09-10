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
    ///     Builds the root ordering for a collection: every key, ordered by key. Called lazily by
    ///     <see cref="ReactiveCollection{TKey,TIdentity,TState}" /> the first time anything asks it for keys in
    ///     order.
    /// </summary>
    internal static ReactiveCollection<TKey, TIdentity, TState> CreateRootImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> collection,
        IComparer<TKey> keyComparer)
        where TKey : notnull
        where TIdentity : notnull
    {
        SortKeyOrder<TKey, TIdentity, TState, TKey> order = new(
            static (key, _) => key,
            keyComparer,
            keyComparer,
            descending: false);

        return TransactionInternal.Apply<ReactiveCollection<TKey, TIdentity, TState>>((trans, _) =>
        {
            LoopedCell<OrderedKeys<TKey, TIdentity, TState>> stateLoopCell = new();

            Stream<StageResult<TKey, TIdentity, TState>> resultsStream = collection.ItemChangesStream
                .SnapshotImpl(stateLoopCell, ProcessRoot);

            Cell<OrderedKeys<TKey, TIdentity, TState>> keysCell = resultsStream
                .MapImpl(static result => result.Keys)
                .HoldLazyImpl(collection.SnapshotCell.SampleLazyImpl().MapImpl(
                    snapshot => RebuildRoot(order, snapshot)));

            stateLoopCell.Loop(trans, keysCell);

            return new ViewStage<TKey, TIdentity, TState>(
                collection,
                keysCell,
                // The root's ordering holds every key, so scoping it would wrap the store in a
                // filter that admits all of it.
                () => collection.SnapshotCell,
                ToChangesStream(resultsStream));
        });
    }

    /// <summary>Reorders by key — the root's own order, available over any stage.</summary>
    internal static ReactiveCollection<TKey, TIdentity, TState> SortByKeyImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> upstream,
        IComparer<TKey> keyComparer)
        where TKey : notnull
        where TIdentity : notnull
    {
        SortKeyOrder<TKey, TIdentity, TState, TKey> order = new(
            static (key, _) => key,
            keyComparer,
            keyComparer,
            descending: false);

        return BuildStage(
            upstream,
            CellInternal.ConstantImpl(UnitInternal.Value),
            (_, upstreamKeys, snapshot) => RebuildSort(order, upstreamKeys, snapshot),
            static (_, keys, change) => ProcessSort(keys, change));
    }

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
            upstream,
            predicateCell,
            RebuildFilter,
            ProcessFilter);

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
        where TIdentity : notnull
    {
        SortKeyOrder<TKey, TIdentity, TState, TSortKey> order = new(
            (_, identity, state) => selector(identity, state),
            sortComparer,
            keyComparer,
            descending);

        return BuildStage(
            upstream,
            CellInternal.ConstantImpl(UnitInternal.Value),
            (_, upstreamKeys, snapshot) => RebuildSort(order, upstreamKeys, snapshot),
            static (_, keys, change) => ProcessSort(keys, change));
    }

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
            upstream,
            CellInternal.ConstantImpl(predicate),
            RebuildFilterByIdentity,
            ProcessFilterByIdentity);

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
        where TIdentity : notnull
    {
        SortKeyOrder<TKey, TIdentity, TState, TSortKey> order = new(
            (_, identity) => selector(identity),
            sortComparer,
            keyComparer,
            descending);

        return BuildStage(
            upstream,
            CellInternal.ConstantImpl(UnitInternal.Value),
            (_, upstreamKeys, snapshot) => RebuildSort(order, upstreamKeys, snapshot),
            static (_, keys, change) => ProcessSort(keys, change));
    }

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
        SliceImpl(upstream, CellInternal.ConstantImpl(0), limitCell);

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
            upstream,
            offsetCell.LiftImpl(
                limitCell,
                static (offset, limit) => (Offset: offset, Limit: limit)),
            static (bounds, upstreamKeys, _) =>
                new RangeKeys<TKey, TIdentity, TState>(upstreamKeys, bounds.Offset, bounds.Limit),
            static (bounds, keys, change) => ProcessSlice(bounds, keys, change));

    /// <summary>
    ///     Follows whichever view the cell currently holds — the way to switch between sorts whose
    ///     sort keys are different types, as clickable column headers need.
    /// </summary>
    internal static ReactiveCollection<TKey, TIdentity, TState> SwitchImpl<TKey, TIdentity, TState>(
        ReactiveCollection<TKey, TIdentity, TState> source,
        Cell<ReactiveCollection<TKey, TIdentity, TState>> viewCell)
        where TKey : notnull
        where TIdentity : notnull =>
        TransactionInternal.RunImpl<ReactiveCollection<TKey, TIdentity, TState>>(() =>
        {
            Stream<CollectionViewChange<TKey, TIdentity, TState>> switchedChangesStream = viewCell
                .MapImpl(static view => view.KeyChangesStream)
                .SwitchSImpl<CollectionViewChange<TKey, TIdentity, TState>,
                    Stream<CollectionViewChange<TKey, TIdentity, TState>>>();

            // Switching is itself a reset: every position potentially differs. The new view already
            // exists and did not change in this transaction, so sampling its keys here gives the
            // right answer.
            Stream<CollectionViewChange<TKey, TIdentity, TState>> switchResetsStream = viewCell
                .UpdatesImpl
                .SnapshotImpl(
                    source.SnapshotCell,
                    static (view, snapshot) =>
                    {
                        OrderedKeys<TKey, TIdentity, TState> keys = view.KeysCell.SampleImpl();
                        CollectionSnapshot<TKey, TIdentity, TState> scoped = snapshot.ScopedTo(keys);

                        // Both sides are the newly followed view. A switch reports a reset, which
                        // says to recompute rather than to apply a delta, so there is no before for
                        // a delta to be taken against - and scoping one to the view just abandoned
                        // would suggest otherwise.
                        return new CollectionViewChange<TKey, TIdentity, TState>(
                            scoped,
                            scoped,
                            keys,
                            Array.Empty<ViewOperation<TKey>>(),
                            true);
                    });

            Cell<OrderedKeys<TKey, TIdentity, TState>> switchedKeysCell = viewCell
                .MapImpl(static view => view.KeysCell)
                .SwitchCImpl<OrderedKeys<TKey, TIdentity, TState>, Cell<OrderedKeys<TKey, TIdentity, TState>>>();

            return new ViewStage<TKey, TIdentity, TState>(
                source,
                switchedKeysCell,
                () => TransactionInternal.RunImpl(() => source.SnapshotCell.LiftImpl(
                    switchedKeysCell,
                    static (snapshot, keys) => snapshot.ScopedTo(keys))),
                switchResetsStream.OrElseImpl(switchedChangesStream));
        });

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
            Cell<StageContext<TKey, TIdentity, TState, TCriteria>> contextCell = criteriaCell.LiftImpl(
                upstream.KeysCell,
                upstream.Root.SnapshotCell,
                static (criteria, upstreamKeys, snapshot) =>
                    new StageContext<TKey, TIdentity, TState, TCriteria>(criteria, upstreamKeys, snapshot));

            Stream<StageInput<TKey, TIdentity, TState, TCriteria>> inputStream = upstream.KeyChangesStream
                .MapImpl(static change => new StageInput<TKey, TIdentity, TState, TCriteria>(
                    MaybeInternal.Some(change),
                    MaybeInternal<TCriteria>.None))
                .MergeImpl(
                    s: criteriaCell.UpdatesImpl.MapImpl(
                        static criteria => new StageInput<TKey, TIdentity, TState, TCriteria>(
                            MaybeInternal<CollectionViewChange<TKey, TIdentity, TState>>.None,
                            MaybeInternal.Some(criteria))),
                    f: static (left, right) => new StageInput<TKey, TIdentity, TState, TCriteria>(
                        left.Change.Match(MaybeInternal.Some, () => right.Change),
                        left.Criteria.Match(MaybeInternal.Some, () => right.Criteria)));

            Stream<StageResult<TKey, TIdentity, TState>> resultsStream = inputStream.SnapshotImpl(
                stateLoopCell,
                contextCell,
                (input, state, context) =>
                {
                    // Anything the input carries is newer than the context, which is still the
                    // pre-transaction sample.
                    TCriteria criteria = input.Criteria.Match(static c => c, () => context.Criteria);

                    CollectionSnapshot<TKey, TIdentity, TState> snapshot = input.Change.Match(
                        static change => change.After,
                        () => context.Snapshot);

                    OrderedKeys<TKey, TIdentity, TState> upstreamKeys = input.Change.Match(
                        static change => change.Keys,
                        () => context.UpstreamKeys);

                    bool mustRebuild =
                        input.Criteria.Match(static _ => true, static () => false) ||
                        input.Change.Match(static change => change.IsReset, static () => false);

                    if (mustRebuild)
                    {
                        OrderedKeys<TKey, TIdentity, TState> rebuilt =
                            rebuild(criteria, upstreamKeys, snapshot);

                        return new StageResult<TKey, TIdentity, TState>(
                            rebuilt,
                            Array.Empty<ViewOperation<TKey>>(),
                            true,
                            context.Snapshot.ScopedTo(state),
                            snapshot.ScopedTo(rebuilt));
                    }

                    return input.Change.Match(
                        change =>
                        {
                            StageOutcome<TKey, TIdentity, TState> outcome = process(criteria, state, change);

                            return new StageResult<TKey, TIdentity, TState>(
                                outcome.Keys,
                                outcome.Operations,
                                false,
                                context.Snapshot.ScopedTo(state),
                                snapshot.ScopedTo(outcome.Keys));
                        },
                        () => new StageResult<TKey, TIdentity, TState>(
                            state,
                            Array.Empty<ViewOperation<TKey>>(),
                            false,
                            context.Snapshot.ScopedTo(state),
                            snapshot.ScopedTo(state)));
                });

            Cell<OrderedKeys<TKey, TIdentity, TState>> keysCell = resultsStream
                .MapImpl(static result => result.Keys)
                .HoldLazyImpl(contextCell.SampleLazyImpl().MapImpl(
                    context => rebuild(context.Criteria, context.UpstreamKeys, context.Snapshot)));

            stateLoopCell.Loop(trans, keysCell);

            return new ViewStage<TKey, TIdentity, TState>(
                upstream,
                keysCell,
                // The one above it behind this stage's keys, built only if something asks.
                () => TransactionInternal.RunImpl(() => upstream.SnapshotCell.LiftImpl(
                    keysCell,
                    static (snapshot, keys) => snapshot.ScopedTo(keys))),
                ToChangesStream(resultsStream));
        });

    private static Stream<CollectionViewChange<TKey, TIdentity, TState>> ToChangesStream<TKey, TIdentity, TState>(
        Stream<StageResult<TKey, TIdentity, TState>> resultsStream)
        where TKey : notnull
        where TIdentity : notnull =>
        resultsStream
            .MapImpl(static result => new CollectionViewChange<TKey, TIdentity, TState>(
                result.Before,
                result.After,
                result.Keys,
                result.Operations,
                result.IsReset))
            .FilterImpl(static change => change.IsReset || change.Operations.Count > 0);

    // --- root ---------------------------------------------------------------------------------

    private static OrderedKeys<TKey, TIdentity, TState> RebuildRoot<TKey, TIdentity, TState>(
        IKeyOrder<TKey, TIdentity, TState> order,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        FileAll(order, snapshot.Identities.Keys, snapshot);

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
            int index = keys.IndexOf(key);

            if (index >= 0)
            {
                operations.Add(new ViewRemove<TKey>(key, index));
                keys = keys.Remove(key);
            }
        }

        foreach (TKey key in change.NewStates.Keys)
        {
            if (change.WasAdded(key))
            {
                keys = keys.Add(key, change.After);

                int index = keys.IndexOf(key);

                if (index >= 0)
                {
                    operations.Add(new ViewInsert<TKey>(key, index));
                }
            }
            else
            {
                // The root orders by key, and a key cannot change, so an update never moves
                // anything here. It still has to be reported: a stage further down may sort or
                // filter on the state that just changed.
                int index = keys.IndexOf(key);

                if (index >= 0)
                {
                    operations.Add(new ViewUpdate<TKey>(key, index));
                }
            }
        }

        return new StageResult<TKey, TIdentity, TState>(
            keys,
            operations,
            false,
            change.Before,
            change.After);
    }

    // --- filter -------------------------------------------------------------------------------

    private static OrderedKeys<TKey, TIdentity, TState> RebuildFilter<TKey, TIdentity, TState>(
        Func<TIdentity, TState, bool> predicate,
        OrderedKeys<TKey, TIdentity, TState> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull
    {
        // Built from the upstream's own order, so this stage sorts exactly as its upstream does
        // without knowing what that order is.
        return FileAll(
            upstreamKeys.Order,
            upstreamKeys.Where(key => Passes(key, predicate, snapshot)),
            snapshot);
    }

    private static OrderedKeys<TKey, TIdentity, TState> RebuildFilterByIdentity<TKey, TIdentity, TState>(
        Func<TIdentity, bool> predicate,
        OrderedKeys<TKey, TIdentity, TState> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        FileAll(
            upstreamKeys.Order,
            upstreamKeys.Where(key => PassesByIdentity(key, predicate, snapshot)),
            snapshot);

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
                    if (!PassesByIdentity(insert.Key, predicate, change.After))
                    {
                        break;
                    }

                    keys = keys.Add(insert.Key, change.After);

                    int inserted = keys.IndexOf(insert.Key);

                    if (inserted >= 0)
                    {
                        operations.Add(new ViewInsert<TKey>(insert.Key, inserted));
                    }

                    break;
                }

                case ViewRemove<TKey> remove:
                {
                    int removed = keys.IndexOf(remove.Key);

                    if (removed >= 0)
                    {
                        operations.Add(new ViewRemove<TKey>(remove.Key, removed));
                        keys = keys.Remove(remove.Key);
                    }

                    break;
                }

                case ViewUpdate<TKey> update:
                {
                    int updated = keys.IndexOf(update.Key);

                    if (updated >= 0)
                    {
                        operations.Add(new ViewUpdate<TKey>(update.Key, updated));
                    }

                    break;
                }

                // A move upstream needs no action, for the reason it needs none in the ordinary
                // filter: this stage's order is the upstream's applied to its own members.
            }
        }

        return new StageOutcome<TKey, TIdentity, TState>(keys, operations);
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

        return new StageOutcome<TKey, TIdentity, TState>(keys, operations);

        void Include(TKey key)
        {
            if (!Passes(key, predicate, change.After))
            {
                return;
            }

            keys = keys.Add(key, change.After);

            int index = keys.IndexOf(key);

            if (index >= 0)
            {
                operations.Add(new ViewInsert<TKey>(key, index));
            }
        }

        void Exclude(TKey key)
        {
            int index = keys.IndexOf(key);

            if (index >= 0)
            {
                operations.Add(new ViewRemove<TKey>(key, index));
                keys = keys.Remove(key);
            }
        }

        void Refresh(TKey key)
        {
            bool was = keys.Contains(key);
            bool now = Passes(key, predicate, change.After);

            switch (was)
            {
                case true when now:
                    Refile(ref keys, operations, key, change.After);
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
        IKeyOrder<TKey, TIdentity, TState> order,
        IEnumerable<TKey> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        FileAll(order, upstreamKeys, snapshot);

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
                    keys = keys.Add(insert.Key, change.After);

                    int index = keys.IndexOf(insert.Key);

                    if (index >= 0)
                    {
                        operations.Add(new ViewInsert<TKey>(insert.Key, index));
                    }

                    break;
                }

                case ViewRemove<TKey> remove:
                {
                    int index = keys.IndexOf(remove.Key);

                    if (index >= 0)
                    {
                        operations.Add(new ViewRemove<TKey>(remove.Key, index));
                        keys = keys.Remove(remove.Key);
                    }

                    break;
                }

                case ViewUpdate<TKey> update:
                    Refile(ref keys, operations, update.Key, change.After);
                    break;

                // Upstream moves are irrelevant: this stage imposes its own order.
            }
        }

        return new StageOutcome<TKey, TIdentity, TState>(keys, operations);
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
            new RangeKeys<TKey, TIdentity, TState>(change.Keys, bounds.Offset, bounds.Limit);

        List<TKey> before = new(state);
        List<TKey> after = new(keys);

        int common = 0;

        while (common < before.Count &&
               common < after.Count &&
               EqualityComparer<TKey>.Default.Equals(before[common], after[common]))
        {
            common++;
        }

        List<ViewOperation<TKey>> operations = new();

        for (int index = before.Count - 1; index >= common; index--)
        {
            operations.Add(new ViewRemove<TKey>(before[index], index));
        }

        for (int index = common; index < after.Count; index++)
        {
            operations.Add(new ViewInsert<TKey>(after[index], index));
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
                operations.Add(new ViewUpdate<TKey>(update.Key, index));
            }
        }

        return new StageOutcome<TKey, TIdentity, TState>(keys, operations);
    }

    // --- shared -------------------------------------------------------------------------------

    /// <summary>Files every key into a new set under one order.</summary>
    /// <remarks>
    ///     In bulk, which is what keeps a rebuild from costing one persistent write per key. See
    ///     <see cref="IKeyOrder{TKey,TIdentity,TState}.CreateFrom" />.
    /// </remarks>
    private static OrderedKeys<TKey, TIdentity, TState> FileAll<TKey, TIdentity, TState>(
        IKeyOrder<TKey, TIdentity, TState> order,
        IEnumerable<TKey> keys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        order.CreateFrom(keys, snapshot);

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
        snapshot.TryGetIdentity(key, out TIdentity identity) && predicate(identity);

    private static bool Passes<TKey, TIdentity, TState>(
        TKey key,
        Func<TIdentity, TState, bool> predicate,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
        where TKey : notnull
        where TIdentity : notnull =>
        snapshot.TryGetHalves(key, out TIdentity identity, out TState state) &&
        predicate(identity, state);

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
            int at = keys.IndexOf(key);

            if (at >= 0)
            {
                operations.Add(new ViewUpdate<TKey>(key, at));
            }

            return;
        }

        int fromIndex = keys.IndexOf(key);

        OrderedKeys<TKey, TIdentity, TState> updated = keys.Remove(key).Add(key, snapshot);
        int toIndex = updated.IndexOf(key);

        keys = updated;

        if (toIndex < 0)
        {
            // Gone from the set entirely, which a re-file can do when the snapshot no longer has
            // the item.
            if (fromIndex >= 0)
            {
                operations.Add(new ViewRemove<TKey>(key, fromIndex));
            }

            return;
        }

        if (fromIndex < 0)
        {
            operations.Add(new ViewInsert<TKey>(key, toIndex));

            return;
        }

        if (fromIndex != toIndex)
        {
            operations.Add(new ViewMove<TKey>(key, fromIndex, toIndex));
        }

        operations.Add(new ViewUpdate<TKey>(key, toIndex));
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
