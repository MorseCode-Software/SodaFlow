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
///     What a stage promises about the change it reports, checked over a run of edits rather than
///     one at a time.
/// </summary>
/// <remarks>
///     <para>
///         Every operation names a position in the list a consumer is applying them to, so a
///         position that was never found is not something to report - it is a row index, and
///         nothing downstream can do anything sensible with -1.
///     </para>
///     <para>
///         The two flags a change carries have to agree with the operations in it. A stage
///         publishes its key list only when it says its keys moved, and a sort below it takes a
///         change that says neither flag for one that can only hold updates - so a flag that
///         understates what happened leaves a stale list published, or throws one stage down.
///     </para>
///     <para>
///         A re-file throws on the two cases that would break either promise - a key the stage does
///         not hold, and a key the snapshot has dropped - rather than reporting an operation at no
///         position. Neither is reachable through the public surface today, since a stage only
///         re-files keys it holds and only for operations naming keys the snapshot still has, so
///         these run every shape of stage through a mix of edits to keep it that way: an edit that
///         reached either would throw out of <c>Send</c> and fail them.
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
                    predicateCell: Cell.Constant<Func<ItemIdentity, ItemState, bool>>(
                        static (_, state) => state.Score >= 20))),
            ("filterOverSort",
                CollectionViewUtility.FilterImpl(
                    upstream: byScore,
                    predicateCell: Cell.Constant<Func<ItemIdentity, ItemState, bool>>(
                        static (_, state) => state.Score >= 20))),
            ("sliceOverSort",
                CollectionViewUtility.SliceImpl(
                    upstream: byScore,
                    offsetCell: Cell.Constant(0),
                    limitCell: Cell.Constant(3)))
        ];
    }

    /// <summary>A run of edits that has every stage inserting, removing, updating and re-filing.</summary>
    private static void SendEdits(StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits)
    {
        // Re-files in the score order, and moves rows into and out of the filter and the slice.
        edits.Send(TestUtil.Score(key: 1, score: 99));
        edits.Send(TestUtil.Score(key: 5, score: 1));
        edits.Send(TestUtil.Score(key: 3, score: 3));

        // Leaves every sort value alone, so it can only be an update.
        edits.Send(
            CollectionEdit<int, ItemIdentity, ItemState>.Update(
                key: 2,
                transform: static state => state with { Name = "renamed" }));

        edits.Send(TestUtil.Add(TestUtil.Item(number: 6, name: "six", score: 15)));
        edits.Send(TestUtil.Remove(4));

        // An add and a remove in one transaction.
        edits.Send(
            TestUtil.Add(TestUtil.Item(number: 7, name: "seven", score: 5))
                .CombineWith(TestUtil.Remove(2)));

        // Big enough to be answered with a reset rather than operations.
        edits.Send(TestUtil.Remove([.. Enumerable.Range(1, 7)]));
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
                [.. Enumerable.Range(1, 5).Select(n => TestUtil.Item(number: n, name: $"n{n}", score: n * 10))]);

        List<string> bad = [];

        List<IListener> listeners =
            [
                .. Stages(collection).Select(stage =>
                    stage.View.KeyChangesStream.ListenStrong(change =>
                    {
                        foreach (ViewOperation<int> operation in change.Operations)
                        {
                            int index = operation switch
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
                [.. Enumerable.Range(1, 5).Select(n => TestUtil.Item(number: n, name: $"n{n}", score: n * 10))]);

        List<string> bad = [];

        List<IListener> listeners =
            [
                .. Stages(collection).Select(stage =>
                    stage.View.KeyChangesStream.ListenStrong(change =>
                    {
                        bool moved =
                            change.Operations.Any(static operation =>
                                operation is ViewInsert<int> or ViewRemove<int> or ViewMove<int>);

                        bool membership =
                            change.Operations.Any(static operation => operation is ViewInsert<int> or ViewRemove<int>);

                        // A reset says both and lists nothing, which is the one case they part.
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
                            bad.Add($"{stage.Name}: ChangesMembership is {change.ChangesMembership} for {Listed(change)}");
                        }
                    }))
            ];

        SendEdits(edits);

        foreach (IListener listener in listeners)
        {
            listener.Unlisten();
        }

        await Assert.That(bad).IsEmpty();

        static string Listed(CollectionViewChange<int, ItemIdentity, ItemState> change) =>
            string.Join(separator: ", ", values: change.Operations.Select(Describe));
    }

    /// <summary>
    ///     What understating those flags costs. A stage publishes its keys only when it says they
    ///     moved, so the list the world reads has to match the one the stage is working from after
    ///     every edit, whatever the flags said.
    /// </summary>
    [Test]
    public async Task EveryStagePublishesTheKeysItsOwnOperationsAddUpTo()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                [.. Enumerable.Range(1, 5).Select(n => TestUtil.Item(number: n, name: $"n{n}", score: n * 10))]);

        List<(string Name, ReactiveCollection<int, ItemIdentity, ItemState> View)> stages = Stages(collection);

        // The keys each change reports, which the published cell has to agree with.
        Dictionary<string, List<int>> reported =
            stages.ToDictionary(stage => stage.Name, stage => TestUtil.Keys(stage.View.KeysCell.Sample()));

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
}
