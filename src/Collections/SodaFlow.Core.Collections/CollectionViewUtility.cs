using System;
using System.Collections.Generic;
using System.Linq;

namespace SodaFlow.Collections;

/// <summary>
///     The whole of the view chain, reached by the C# and F# wrappers through their own surfaces.
/// </summary>
/// <remarks>
///     Each stage keeps its own <see cref="IOrderedKeys{TKey,TId,TState}" /> and applies the
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
    ///     <see cref="ReactiveCollection{TKey,TId,TState}" /> the first time anything asks it for keys in
    ///     order.
    /// </summary>
    internal static IReactiveCollection<TKey, TId, TState> CreateRootImpl<TKey, TId, TState>(
        ReactiveCollection<TKey, TId, TState> collection,
        IComparer<TKey> keyComparer)
        where TKey : notnull
        where TId : notnull
    {
        SortKeyOrder<TKey, TId, TState, TKey> order = new(
            static (key, _, _) => key,
            keyComparer,
            keyComparer,
            false);

        return TransactionInternal.Apply<IReactiveCollection<TKey, TId, TState>>((trans, _) =>
        {
            LoopedCell<IOrderedKeys<TKey, TId, TState>> stateLoopCell = new();

            Stream<StageResult<TKey, TId, TState>> resultsStream = collection.ItemChangesStream
                .SnapshotImpl(stateLoopCell, ProcessRoot);

            Cell<IOrderedKeys<TKey, TId, TState>> keysCell = resultsStream
                .MapImpl(static result => result.Keys)
                .HoldLazyImpl(collection.SnapshotCell.SampleLazyImpl().MapImpl(
                    snapshot => RebuildRoot(order, snapshot)));

            stateLoopCell.Loop(trans, keysCell);

            return new CollectionViewStage<TKey, TId, TState>(
                collection,
                keysCell,
                ToChangesStream(resultsStream));
        });
    }

    /// <summary>Reorders by key — the root's own order, available over any stage.</summary>
    internal static IReactiveCollection<TKey, TId, TState> SortByKeyImpl<TKey, TId, TState>(
        IReactiveCollection<TKey, TId, TState> upstream,
        IComparer<TKey> keyComparer)
        where TKey : notnull
        where TId : notnull
    {
        SortKeyOrder<TKey, TId, TState, TKey> order = new(
            static (key, _, _) => key,
            keyComparer,
            keyComparer,
            false);

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
    internal static IReactiveCollection<TKey, TId, TState> FilterImpl<TKey, TId, TState>(
        IReactiveCollection<TKey, TId, TState> upstream,
        Cell<Func<TId, TState, bool>> predicateCell)
        where TKey : notnull
        where TId : notnull =>
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
    internal static IReactiveCollection<TKey, TId, TState> SortByImpl<TKey, TId, TState, TSortKey>(
        IReactiveCollection<TKey, TId, TState> upstream,
        Func<TId, TState, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool descending)
        where TKey : notnull
        where TId : notnull
    {
        SortKeyOrder<TKey, TId, TState, TSortKey> order = new(
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
    ///     The first <c>limit</c> keys of the upstream — the top-n of whatever ordering and
    ///     filtering precedes it.
    /// </summary>
    /// <remarks>
    ///     This stage diffs its old and new windows rather than translating upstream operations,
    ///     which costs O(limit) per transaction and yields a coarser operation list: a reorder
    ///     inside the window is reported as removes and inserts from the first differing position
    ///     rather than as moves. For a top-n that is the cheap direction to be wrong in.
    /// </remarks>
    internal static IReactiveCollection<TKey, TId, TState> TakeImpl<TKey, TId, TState>(
        IReactiveCollection<TKey, TId, TState> upstream,
        Cell<int> limitCell)
        where TKey : notnull
        where TId : notnull =>
        BuildStage(
            upstream,
            limitCell,
            static (limit, upstreamKeys, _) => new PrefixKeys<TKey, TId, TState>(upstreamKeys, limit),
            static (limit, keys, change) => ProcessTake(limit, keys, change));

    /// <summary>
    ///     Follows whichever view the cell currently holds — the way to switch between sorts whose
    ///     sort keys are different types, as clickable column headers need.
    /// </summary>
    internal static IReactiveCollection<TKey, TId, TState> SwitchImpl<TKey, TId, TState>(
        IReactiveCollection<TKey, TId, TState> source,
        Cell<IReactiveCollection<TKey, TId, TState>> viewCell)
        where TKey : notnull
        where TId : notnull =>
        TransactionInternal.RunImpl<IReactiveCollection<TKey, TId, TState>>(() =>
        {
            Stream<CollectionViewChange<TKey, TId, TState>> switchedChangesStream = viewCell
                .MapImpl(static view => view.ChangesStream)
                .SwitchSImpl<CollectionViewChange<TKey, TId, TState>,
                    Stream<CollectionViewChange<TKey, TId, TState>>>();

            // Switching is itself a reset: every position potentially differs. The new view already
            // exists and did not change in this transaction, so sampling its keys here gives the
            // right answer.
            Stream<CollectionViewChange<TKey, TId, TState>> switchResetsStream = viewCell
                .UpdatesImpl
                .SnapshotImpl(
                    source.SnapshotCell,
                    static (view, snapshot) => new CollectionViewChange<TKey, TId, TState>(
                        snapshot,
                        view.KeysCell.SampleImpl(),
                        Array.Empty<ViewOperation<TKey>>(),
                        true));

            return new CollectionViewStage<TKey, TId, TState>(
                source,
                viewCell
                    .MapImpl(static view => view.KeysCell)
                    .SwitchCImpl<IOrderedKeys<TKey, TId, TState>, Cell<IOrderedKeys<TKey, TId, TState>>>(),
                switchResetsStream.OrElseImpl(switchedChangesStream));
        });

    private static IReactiveCollection<TKey, TId, TState> BuildStage<TKey, TId, TState, TCriteria>(
        IReactiveCollection<TKey, TId, TState> upstream,
        Cell<TCriteria> criteriaCell,
        Func<TCriteria, IOrderedKeys<TKey, TId, TState>, CollectionSnapshot<TKey, TId, TState>,
            IOrderedKeys<TKey, TId, TState>> rebuild,
        Func<TCriteria, IOrderedKeys<TKey, TId, TState>, CollectionViewChange<TKey, TId, TState>,
            StageOutcome<TKey, TId, TState>> process)
        where TKey : notnull
        where TId : notnull =>
        TransactionInternal.Apply<IReactiveCollection<TKey, TId, TState>>((trans, _) =>
        {
            LoopedCell<IOrderedKeys<TKey, TId, TState>> stateLoopCell = new();

            Cell<StageContext<TKey, TId, TState, TCriteria>> contextCell = criteriaCell.LiftImpl(
                upstream.KeysCell,
                upstream.SnapshotCell,
                static (criteria, upstreamKeys, snapshot) =>
                    new StageContext<TKey, TId, TState, TCriteria>(criteria, upstreamKeys, snapshot));

            Stream<StageInput<TKey, TId, TState, TCriteria>> inputStream = upstream.ChangesStream
                .MapImpl(static change => new StageInput<TKey, TId, TState, TCriteria>(
                    MaybeInternal.Some(change),
                    MaybeInternal<TCriteria>.None))
                .MergeImpl(
                    s: criteriaCell.UpdatesImpl.MapImpl(
                        static criteria => new StageInput<TKey, TId, TState, TCriteria>(
                            MaybeInternal<CollectionViewChange<TKey, TId, TState>>.None,
                            MaybeInternal.Some(criteria))),
                    f: static (left, right) => new StageInput<TKey, TId, TState, TCriteria>(
                        left.Change.Match(MaybeInternal.Some, () => right.Change),
                        left.Criteria.Match(MaybeInternal.Some, () => right.Criteria)));

            Stream<StageResult<TKey, TId, TState>> resultsStream = inputStream.SnapshotImpl(
                stateLoopCell,
                contextCell,
                (input, state, context) =>
                {
                    // Anything the input carries is newer than the context, which is still the
                    // pre-transaction sample.
                    TCriteria criteria = input.Criteria.Match(static c => c, () => context.Criteria);

                    CollectionSnapshot<TKey, TId, TState> snapshot = input.Change.Match(
                        static change => change.Snapshot,
                        () => context.Snapshot);

                    IOrderedKeys<TKey, TId, TState> upstreamKeys = input.Change.Match(
                        static change => change.Keys,
                        () => context.UpstreamKeys);

                    bool mustRebuild =
                        input.Criteria.Match(static _ => true, static () => false) ||
                        input.Change.Match(static change => change.IsReset, static () => false);

                    if (mustRebuild)
                    {
                        return new StageResult<TKey, TId, TState>(
                            rebuild(criteria, upstreamKeys, snapshot),
                            Array.Empty<ViewOperation<TKey>>(),
                            true,
                            snapshot);
                    }

                    return input.Change.Match(
                        change =>
                        {
                            StageOutcome<TKey, TId, TState> outcome = process(criteria, state, change);

                            return new StageResult<TKey, TId, TState>(
                                outcome.Keys,
                                outcome.Operations,
                                false,
                                snapshot);
                        },
                        () => new StageResult<TKey, TId, TState>(
                            state,
                            Array.Empty<ViewOperation<TKey>>(),
                            false,
                            snapshot));
                });

            Cell<IOrderedKeys<TKey, TId, TState>> keysCell = resultsStream
                .MapImpl(static result => result.Keys)
                .HoldLazyImpl(contextCell.SampleLazyImpl().MapImpl(
                    context => rebuild(context.Criteria, context.UpstreamKeys, context.Snapshot)));

            stateLoopCell.Loop(trans, keysCell);

            return new CollectionViewStage<TKey, TId, TState>(
                upstream,
                keysCell,
                ToChangesStream(resultsStream));
        });

    private static Stream<CollectionViewChange<TKey, TId, TState>> ToChangesStream<TKey, TId, TState>(
        Stream<StageResult<TKey, TId, TState>> resultsStream)
        where TKey : notnull
        where TId : notnull =>
        resultsStream
            .MapImpl(static result => new CollectionViewChange<TKey, TId, TState>(
                result.Snapshot,
                result.Keys,
                result.Operations,
                result.IsReset))
            .FilterImpl(static change => change.IsReset || change.Operations.Count > 0);

    // --- root ---------------------------------------------------------------------------------

    private static IOrderedKeys<TKey, TId, TState> RebuildRoot<TKey, TId, TState>(
        IKeyOrder<TKey, TId, TState> order,
        CollectionSnapshot<TKey, TId, TState> snapshot)
        where TKey : notnull
        where TId : notnull =>
        FileAll(order, snapshot.Identities.Keys, snapshot);

    private static StageResult<TKey, TId, TState> ProcessRoot<TKey, TId, TState>(
        CollectionChange<TKey, TId, TState> change,
        IOrderedKeys<TKey, TId, TState> state)
        where TKey : notnull
        where TId : notnull
    {
        IOrderedKeys<TKey, TId, TState> keys = state;
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

        return new StageResult<TKey, TId, TState>(keys, operations, false, change.After);
    }

    // --- filter -------------------------------------------------------------------------------

    private static IOrderedKeys<TKey, TId, TState> RebuildFilter<TKey, TId, TState>(
        Func<TId, TState, bool> predicate,
        IOrderedKeys<TKey, TId, TState> upstreamKeys,
        CollectionSnapshot<TKey, TId, TState> snapshot)
        where TKey : notnull
        where TId : notnull
    {
        // Built from the upstream's own order, so this stage sorts exactly as its upstream does
        // without knowing what that order is.
        return FileAll(
            upstreamKeys.Order,
            upstreamKeys.Where(key => Passes(key, predicate, snapshot)),
            snapshot);
    }

    private static StageOutcome<TKey, TId, TState> ProcessFilter<TKey, TId, TState>(
        Func<TId, TState, bool> predicate,
        IOrderedKeys<TKey, TId, TState> state,
        CollectionViewChange<TKey, TId, TState> change)
        where TKey : notnull
        where TId : notnull
    {
        IOrderedKeys<TKey, TId, TState> keys = state;
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

        return new StageOutcome<TKey, TId, TState>(keys, operations);

        void Include(TKey key)
        {
            if (!Passes(key, predicate, change.Snapshot))
            {
                return;
            }

            keys = keys.Add(key, change.Snapshot);

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
            bool now = Passes(key, predicate, change.Snapshot);

            switch (was)
            {
                case true when now:
                    Refile(ref keys, operations, key, change.Snapshot);
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

    private static IOrderedKeys<TKey, TId, TState> RebuildSort<TKey, TId, TState>(
        IKeyOrder<TKey, TId, TState> order,
        IEnumerable<TKey> upstreamKeys,
        CollectionSnapshot<TKey, TId, TState> snapshot)
        where TKey : notnull
        where TId : notnull =>
        FileAll(order, upstreamKeys, snapshot);

    private static StageOutcome<TKey, TId, TState> ProcessSort<TKey, TId, TState>(
        IOrderedKeys<TKey, TId, TState> state,
        CollectionViewChange<TKey, TId, TState> change)
        where TKey : notnull
        where TId : notnull
    {
        IOrderedKeys<TKey, TId, TState> keys = state;
        List<ViewOperation<TKey>> operations = new();

        foreach (ViewOperation<TKey> operation in change.Operations)
        {
            switch (operation)
            {
                case ViewInsert<TKey> insert:
                {
                    keys = keys.Add(insert.Key, change.Snapshot);

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
                    Refile(ref keys, operations, update.Key, change.Snapshot);
                    break;

                // Upstream moves are irrelevant: this stage imposes its own order.
            }
        }

        return new StageOutcome<TKey, TId, TState>(keys, operations);
    }

    // --- take ---------------------------------------------------------------------------------

    private static StageOutcome<TKey, TId, TState> ProcessTake<TKey, TId, TState>(
        int limit,
        IEnumerable<TKey> state,
        CollectionViewChange<TKey, TId, TState> change)
        where TKey : notnull
        where TId : notnull
    {
        IOrderedKeys<TKey, TId, TState> keys =
            new PrefixKeys<TKey, TId, TState>(change.Keys, limit);

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

        return new StageOutcome<TKey, TId, TState>(keys, operations);
    }

    // --- shared -------------------------------------------------------------------------------

    /// <summary>Files every key into an empty set under one order.</summary>
    private static IOrderedKeys<TKey, TId, TState> FileAll<TKey, TId, TState>(
        IKeyOrder<TKey, TId, TState> order,
        IEnumerable<TKey> keys,
        CollectionSnapshot<TKey, TId, TState> snapshot)
        where TKey : notnull
        where TId : notnull =>
        keys.Aggregate(order.CreateEmpty(), (filed, key) => filed.Add(key, snapshot));

    private static bool Passes<TKey, TId, TState>(
        TKey key,
        Func<TId, TState, bool> predicate,
        CollectionSnapshot<TKey, TId, TState> snapshot)
        where TKey : notnull
        where TId : notnull =>
        snapshot.LookupInternal(key).Match(
            entry => predicate(entry.Identity, entry.State),
            static () => false);

    /// <summary>
    ///     Removes and re-adds a key so it is filed under its new sort value, reporting a move if
    ///     that changed its position and an update either way.
    /// </summary>
    private static void Refile<TKey, TId, TState>(
        ref IOrderedKeys<TKey, TId, TState> keys,
        ICollection<ViewOperation<TKey>> operations,
        TKey key,
        CollectionSnapshot<TKey, TId, TState> snapshot)
        where TKey : notnull
        where TId : notnull
    {
        int fromIndex = keys.IndexOf(key);

        IOrderedKeys<TKey, TId, TState> updated = keys.Remove(key).Add(key, snapshot);
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
internal sealed class StageOutcome<TKey, TId, TState>
    where TKey : notnull
    where TId : notnull
{
    internal StageOutcome(
        IOrderedKeys<TKey, TId, TState> keys,
        IReadOnlyList<ViewOperation<TKey>> operations)
    {
        this.Keys = keys;
        this.Operations = operations;
    }

    internal IOrderedKeys<TKey, TId, TState> Keys { get; }

    internal IReadOnlyList<ViewOperation<TKey>> Operations { get; }
}
