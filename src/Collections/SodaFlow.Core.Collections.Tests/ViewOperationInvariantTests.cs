using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     The rules of a stage for the change that it reports, with a test across a sequence of edits
///     and not one edit at a time.
/// </summary>
/// <remarks>
///     <para>
///         Each operation names a position in the list of a consumer, thus this code does not
///         report a position with no value. The position is a row index, and no code below can use
///         a value of -1.
///     </para>
///     <para>
///         The two flags in a change must agree with its operations. A stage publishes its key
///         list only when it reports a change to its keys. A sort below it reads a change with no
///         flag as a change with only updates. Thus a flag that is below the true change leaves an
///         previous list in the public cell, or throws an exception one stage below.
///     </para>
///     <para>
///         A sort throws an exception for the two conditions that break one of those rules, and
///         does not report an operation with no position. Those conditions are a key that the stage
///         does not hold and a key that the snapshot removed. No code can get to one of the two
///         through the public surface now, because a stage sorts only the keys that it holds and
///         only for operations that name keys in the snapshot. These tests run each shape of stage
///         through a set of edits to keep that rule. An edit that reaches one of the two conditions
///         throws an exception out of <c>Send</c> and fails these tests.
///     </para>
/// </remarks>
public sealed class ViewOperationInvariantTests
{
    private static ReactiveCollection<int, ItemIdentity, ItemState> Create(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        params Item<ItemIdentity, ItemState>[] initial) =>
        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: TestUtil.KeyOf,
            initialItems: initial,
            edits);

    /// <summary>Every shape of stage, over the same collection, so one run of edits covers them all.</summary>
    private static List<(string Name, ReactiveCollection<int, ItemIdentity, ItemState> View)> Stages(
        ReactiveCollection<int, ItemIdentity, ItemState> collection)
    {
        ReactiveCollection<int, ItemIdentity, ItemState> byScore =
            CollectionViewUtility.SortByImpl(
                upstream: collection,
                selector: static (_, state) => state.Score,
                sortComparer: Comparer<int>.Default,
                keyComparer: Comparer<int>.Default,
                isDescending: false);

        return
        [
            ("root", collection),
            ("byScore", byScore),
            ("byKey",
                CollectionViewUtility.SortByImpl(
                    upstream: collection,
                    orderCell: Cell.Constant(KeyOrder<int, ItemIdentity, ItemState>.ByKey(Comparer<int>.Default)))),
            ("filterOverRoot",
                CollectionViewUtility.FilterImpl(
                    upstream: collection,
                    predicateCell: Cell.Constant<Func<ItemIdentity, ItemState, bool>>(static (_, state) =>
                        state.Score >= 20))),
            ("filterOverSort",
                CollectionViewUtility.FilterImpl(
                    upstream: byScore,
                    predicateCell: Cell.Constant<Func<ItemIdentity, ItemState, bool>>(static (_, state) =>
                        state.Score >= 20))),
            ("sliceOverSort",
                CollectionViewUtility.SliceImpl(
                    upstream: byScore,
                    offsetCell: Cell.Constant(0),
                    limitCell: Cell.Constant(3)))
        ];
    }

    /// <summary>A sequence of edits that makes each stage add a key, remove a key, update a key, and
/// sort a key again.</summary>
    private static void SendEdits(StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits)
    {
        // This sorts again in the score order, and moves rows into the filter and the slice and out
        // of them.
        edits.Send(TestUtil.Score(key: 1, score: 99));
        edits.Send(TestUtil.Score(key: 5, score: 1));
        edits.Send(TestUtil.Score(key: 3, score: 3));

        // This changes no sort value, thus it can be only an update.
        edits.Send(
            CollectionEdit<int, ItemIdentity, ItemState>.Update(
                key: 2,
                transform: static state => state with { Name = "renamed" }));

        edits.Send(TestUtil.Add(TestUtil.Item(number: 6, name: "six", score: 15)));
        edits.Send(TestUtil.Remove(4));

        // This is an add and a removal in one transaction.
        edits.Send(
            TestUtil.Add(TestUtil.Item(number: 7, name: "seven", score: 5))
                .CombineWith(TestUtil.Remove(2)));

        // This is sufficiently large for an answer of a reset and not a list of operations.
        edits.Send(TestUtil.Remove([.. Enumerable.Range(start: 1, count: 7)]));
    }

    private static string Describe(ViewOperation<int> operation) =>
        $"{operation.GetType().Name.Replace(oldValue: "`1", newValue: string.Empty)}:{operation.Key}";

    [Test]
    public async Task NoOperationCarriesAPositionThatWasNeverFound()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial:
                [
                    .. Enumerable.Range(start: 1, count: 5)
                        .Select(static n => TestUtil.Item(number: n, name: $"n{n}", score: n * 10))
                ]);

        List<string> bad = [];

        List<IListener> listeners =
        [
            .. Stages(collection)
                .Select(stage =>
                    stage.View.KeyChangesStream.ListenStrong(change =>
                    {
                        foreach (ViewOperation<int> operation in change.Operations)
                        {
                            int index =
                                operation switch
                                {
                                    ViewInsert<int> insert => insert.Index,
                                    ViewRemove<int> remove => remove.Index,
                                    ViewUpdate<int> update => update.Index,
                                    ViewMove<int> move => Math.Min(val1: move.FromIndex, val2: move.ToIndex),
                                    _ => 0
                                };

                            if (index < 0)
                            {
                                bad.Add($"{stage.Name}: {Describe(operation)} at {index}");
                            }
                        }
                    }))
        ];

        SendEdits(edits);

        foreach (IListener listener in listeners)
        {
            listener.Unlisten();
        }

        await Assert.That(bad).IsEmpty();
    }

    [Test]
    public async Task TheFlagsAChangeCarriesAgreeWithItsOperations()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial:
                [
                    .. Enumerable.Range(start: 1, count: 5)
                        .Select(static n => TestUtil.Item(number: n, name: $"n{n}", score: n * 10))
                ]);

        List<string> bad = [];

        List<IListener> listeners =
        [
            .. Stages(collection)
                .Select(stage =>
                    stage.View.KeyChangesStream.ListenStrong(change =>
                    {
                        bool moved =
                            change.Operations.Any(static operation =>
                                operation is ViewInsert<int> or ViewRemove<int> or ViewMove<int>);

                        bool membership =
                            change.Operations.Any(static operation => operation is ViewInsert<int> or ViewRemove<int>);

                        // A reset gives the two flags and lists no operation, and that is the one
                        // condition where the two are different.
                        if (change.IsReset)
                        {
                            return;
                        }

                        if (change.MovesKeys != moved)
                        {
                            bad.Add($"{stage.Name}: MovesKeys is {change.MovesKeys} for {Listed(change)}");
                        }

                        if (change.ChangesMembership != membership)
                        {
                            bad.Add(
                                $"{stage.Name}: ChangesMembership is {change.ChangesMembership} for {Listed(change)}");
                        }
                    }))
        ];

        SendEdits(edits);

        foreach (IListener listener in listeners)
        {
            listener.Unlisten();
        }

        await Assert.That(bad).IsEmpty();

        return;

        static string Listed(CollectionViewChange<int, ItemIdentity, ItemState> change) =>
            string.Join(separator: ", ", values: change.Operations.Select(Describe));
    }

    /// <summary>
    ///     The cost of a flag below the true change. A stage publishes its keys only when it
    ///     reports a change to them. Thus the list in the public cell must be the list of the stage
    ///     after each edit, at each value of the flags.
    /// </summary>
    [Test]
    public async Task EveryStagePublishesTheKeysItsOwnOperationsAddUpTo()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial:
                [
                    .. Enumerable.Range(start: 1, count: 5)
                        .Select(static n => TestUtil.Item(number: n, name: $"n{n}", score: n * 10))
                ]);

        List<(string Name, ReactiveCollection<int, ItemIdentity, ItemState> View)> stages = Stages(collection);

        // These are the keys from each change, and the public cell must agree with them.
        Dictionary<string, List<int>> reported =
            stages.ToDictionary(
                keySelector: static stage => stage.Name,
                elementSelector: static stage => TestUtil.Keys(stage.View.KeysCell.Sample()));

        List<IListener> listeners =
        [
            .. stages.Select(stage =>
                stage.View.KeyChangesStream.ListenStrong(change => reported[stage.Name] = TestUtil.Keys(change.Keys)))
        ];

        SendEdits(edits);

        foreach (IListener listener in listeners)
        {
            listener.Unlisten();
        }

        foreach ((string name, ReactiveCollection<int, ItemIdentity, ItemState> view) in stages)
        {
            await Assert.That(TestUtil.Keys(view.KeysCell.Sample()))
                .IsEquivalentTo(expected: reported[name], ordering: CollectionOrdering.Matching);
        }
    }

    /// <summary>
    ///     A new order comes to each stage below it as a reset that reports only a change of order,
    ///     thus it does not change the shape. It continues to a window, which cannot report that
    ///     and gives a usual reset. A transaction that also holds an edit is never a change of order
    ///     alone, at each stage.
    /// </summary>
    [Test]
    public async Task ANewOrderIsReportedAsAReorderDownToAWindow()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial:
                [
                    .. Enumerable.Range(start: 1, count: 6)
                        .Select(static n => TestUtil.Item(number: n, name: $"n{n}", score: n * 10))
                ]);

        KeyOrder<int, ItemIdentity, ItemState> ascending =
            KeyOrder<int, ItemIdentity, ItemState>.By(static (_, state) => state.Score);

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(ascending);

        ReactiveCollection<int, ItemIdentity, ItemState> sorted =
            CollectionViewUtility.SortByImpl(upstream: collection, orderCell: order);

        ReactiveCollection<int, ItemIdentity, ItemState> filtered =
            CollectionViewUtility.FilterImpl(
                upstream: sorted,
                predicateCell:
                Cell.Constant<Func<ItemIdentity, ItemState, bool>>(static (_, state) => state.Score >= 20));

        ReactiveCollection<int, ItemIdentity, ItemState> byIdentity =
            CollectionViewUtility.FilterByIdentityImpl(
                upstream: filtered,
                predicate: static identity => identity.Number != 4);

        ReactiveCollection<int, ItemIdentity, ItemState> resorted =
            CollectionViewUtility.SortByImpl(
                upstream: byIdentity,
                orderCell: Cell.Constant(KeyOrder<int, ItemIdentity, ItemState>.ByKey(Comparer<int>.Default)));

        ReactiveCollection<int, ItemIdentity, ItemState> window =
            CollectionViewUtility.SliceImpl(
                upstream: byIdentity,
                offsetCell: Cell.Constant(0),
                limitCell: Cell.Constant(2));

        ReactiveCollection<int, ItemIdentity, ItemState> belowWindow =
            CollectionViewUtility.FilterImpl(
                upstream: window,
                predicateCell: Cell.Constant<Func<ItemIdentity, ItemState, bool>>(static (_, _) => true));

        List<(string Name, ReactiveCollection<int, ItemIdentity, ItemState> View)> stages =
        [
            ("sorted", sorted),
            ("filtered", filtered),
            ("byIdentity", byIdentity),
            ("resorted", resorted),
            ("window", window),
            ("belowWindow", belowWindow)
        ];

        List<string> seen = [];

        List<IListener> listeners =
        [
            .. stages.Select(stage =>
                stage.View.KeyChangesStream.ListenStrong(change =>
                    seen.Add(
                        $"{stage.Name}: reset={change.IsReset} reordersOnly={change.ReordersOnly} "
                        + $"changesMembership={change.ChangesMembership}")))
        ];

        order.Send(KeyOrder<int, ItemIdentity, ItemState>.By(static (_, state) => -state.Score));

        List<string> reorder = [.. seen];
        seen.Clear();

        Transaction.RunVoid(() =>
        {
            order.Send(ascending);
            edits.Send(TestUtil.Score(key: 2, score: 25));
        });

        List<string> reorderWithAnEdit = [.. seen];

        foreach (IListener listener in listeners)
        {
            listener.Unlisten();
        }

        await Assert.That(reorder)
            .IsEquivalentTo(
                expected:
                [
                    "sorted: reset=True reordersOnly=True changesMembership=False",
                    "filtered: reset=True reordersOnly=True changesMembership=False",
                    "byIdentity: reset=True reordersOnly=True changesMembership=False",
                    "resorted: reset=True reordersOnly=True changesMembership=False",
                    "window: reset=True reordersOnly=False changesMembership=True",
                    "belowWindow: reset=True reordersOnly=False changesMembership=True"
                ],
                ordering: CollectionOrdering.Any);

        await Assert.That(reorderWithAnEdit.Where(static line => line.Contains("reordersOnly=True"))).IsEmpty();
        await Assert.That(reorderWithAnEdit.Count).IsEqualTo(stages.Count);

        // Each stage also holds the correct content: the key order above the window, and the
        // window.
        await Assert.That(TestUtil.Keys(byIdentity.KeysCell.Sample()))
            .IsEquivalentTo(expected: [2, 3, 5, 6], ordering: CollectionOrdering.Matching);

        await Assert.That(TestUtil.Keys(resorted.KeysCell.Sample()))
            .IsEquivalentTo(expected: [2, 3, 5, 6], ordering: CollectionOrdering.Matching);

        await Assert.That(TestUtil.Keys(belowWindow.KeysCell.Sample()))
            .IsEquivalentTo(expected: [2, 3], ordering: CollectionOrdering.Matching);
    }
}
