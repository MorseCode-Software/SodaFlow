using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     Handing a sort stage a new order. Where the new order is the old one run the other way, the
///     stage can turn the list it already has around instead of filing every key again - a path
///     that was unreachable while its result was computed and dropped.
/// </summary>
public sealed class SortOrderChangeTests
{
    /// <summary>Shared instances, because reversing is only offered for orders built from the same ones.</summary>
    private static readonly IComparer<int> ScoreComparer = Comparer<int>.Default;

    private static readonly IComparer<int> KeyComparer = Comparer<int>.Default;

    private static ReactiveCollection<int, ItemIdentity, ItemState> Create(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        params Item<ItemIdentity, ItemState>[] initial) =>
        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: TestUtil.KeyOf,
            initialItems: initial,
            edits);

    private static List<int> KeysOf(ReactiveCollection<int, ItemIdentity, ItemState> view) =>
        TestUtil.Keys(view.KeysCell.Sample());

    private static KeyOrder<int, ItemIdentity, ItemState> ByScore(bool isDescending) =>
        KeyOrder<int, ItemIdentity, ItemState>.By(
            selector: static (_, state) => state.Score,
            sortComparer: ScoreComparer,
            keyComparer: KeyComparer,
            isDescending: isDescending);

    [Test]
    public async Task ReversingAnOrderReversesTheView()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 30),
                TestUtil.Item(number: 3, name: "three", score: 20));

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(ByScore(isDescending: false));

        ReactiveCollection<int, ItemIdentity, ItemState> sorted = collection.SortBy(order);

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [1, 3, 2], ordering: CollectionOrdering.Matching);

        order.Send(ByScore(isDescending: true));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);

        // And back, so the turned-around list is itself a sound thing to turn around again.
        order.Send(ByScore(isDescending: false));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [1, 3, 2], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A stage that turned its list around still has to carry the new order, not the one it was
    ///     built under. A filter below it builds from its upstream's order, so it is where keeping
    ///     the old one would show.
    /// </summary>
    [Test]
    public async Task AReversedStageCarriesTheNewOrderDownstream()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 30),
                TestUtil.Item(number: 3, name: "three", score: 20));

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(ByScore(isDescending: false));

        // Everything passes, so the filter holds the whole list in its upstream's order.
        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.SortBy(order).Filter(static (_, state) => state.Score > 0);

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [1, 3, 2], ordering: CollectionOrdering.Matching);

        order.Send(ByScore(isDescending: true));

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);

        // A later edit has to be filed under the new order too, not the one the list was built with.
        edits.Send(TestUtil.Add(TestUtil.Item(number: 4, name: "four", score: 25)));

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [2, 4, 3, 1], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A new order that runs the same way but reads a different value is not the old order, and
    ///     leaving the list alone would not sort it.
    /// </summary>
    /// <remarks>
    ///     Two orders are told apart by the selector the caller handed over as well as the comparers
    ///     and the direction. Comparing comparers and sort key types alone took orders that project
    ///     different values through the same comparers for one another, and the stage reported nothing.
    /// </remarks>
    [Test]
    public async Task AnIdenticallyDirectedOrderOverADifferentValueIsNotTreatedAsTheSameOrder()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 30),
                TestUtil.Item(number: 3, name: "three", score: 20));

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(ByScore(isDescending: false));

        ReactiveCollection<int, ItemIdentity, ItemState> sorted = collection.SortBy(order);

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [1, 3, 2], ordering: CollectionOrdering.Matching);

        // Lengths ascending are "one" (3) and "two" (3), 1 before 2 on the key tie-break, then
        // "three" (5).
        order.Send(
            KeyOrder<int, ItemIdentity, ItemState>.By(
                selector: static (_, state) => state.Name.Length,
                sortComparer: ScoreComparer,
                keyComparer: KeyComparer,
                isDescending: false));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A new order that runs the other way but reads a different value is not the old one
    ///     reversed, and turning the list around would not sort it.
    /// </summary>
    /// <remarks>
    ///     The reversal has the same test to pass as equivalence: the same selector, comparers and key
    ///     equality comparer, run the other way. Without the selector in it, the stage turned its list
    ///     around for an order that sorts by something else entirely.
    /// </remarks>
    [Test]
    public async Task AnOppositeOrderOverADifferentValueIsNotTreatedAsAReversal()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 30),
                TestUtil.Item(number: 3, name: "three", score: 20));

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(ByScore(isDescending: false));

        ReactiveCollection<int, ItemIdentity, ItemState> sorted = collection.SortBy(order);

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [1, 3, 2], ordering: CollectionOrdering.Matching);

        // Same sort key type and the same two comparers, opposite direction - but over the name's
        // length rather than the score. Scores ascending are 1, 3, 2; lengths descending are
        // "three" (5), "one" (3), "two" (3), which is 3, then 1 before 2 on the key tie-break.
        order.Send(
            KeyOrder<int, ItemIdentity, ItemState>.By(
                selector: static (_, state) => state.Name.Length,
                sortComparer: ScoreComparer,
                keyComparer: KeyComparer,
                isDescending: true));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [3, 1, 2], ordering: CollectionOrdering.Matching);
    }

    private static ReactiveCollection<int, ItemIdentity, ItemState> Scores(
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits) =>
        Create(
            edits: edits,
            TestUtil.Item(number: 1, name: "one", score: 10),
            TestUtil.Item(number: 2, name: "two", score: 40),
            TestUtil.Item(number: 3, name: "three", score: 30),
            TestUtil.Item(number: 4, name: "four", score: 20),
            TestUtil.Item(number: 5, name: "five", score: 50));

    private static KeyOrder<int, ItemIdentity, ItemState> ByName { get; } =
        KeyOrder<int, ItemIdentity, ItemState>.By(static (_, state) => state.Name);

    /// <summary>
    ///     A new order above a filter changes where its members sit and nothing about which items they
    ///     are, so the filter takes its members over to the new order rather than testing every item
    ///     above it against its predicate again.
    /// </summary>
    [Test]
    public async Task AFilterBelowANewOrderDoesNotTestItsPredicateAgain()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(ByScore(isDescending: false));

        int tests = 0;

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            Scores(edits)
                .SortBy(order)
                .Filter((_, state) =>
                {
                    tests++;

                    return state.Score >= 20;
                });

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [4, 3, 2, 5], ordering: CollectionOrdering.Matching);

        List<bool> resets = [];
        IListener l = passing.KeyChangesStream.ListenStrong(change => resets.Add(change.IsReset));

        tests = 0;

        // The same order reversed, which the sort answers by turning its list around.
        order.Send(ByScore(isDescending: true));

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [5, 2, 3, 4], ordering: CollectionOrdering.Matching);

        // A different order altogether, which the sort answers by filing everything again.
        order.Send(ByName);

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [5, 4, 3, 2], ordering: CollectionOrdering.Matching);

        l.Unlisten();

        await Assert.That(tests).IsEqualTo(0);

        // Still a reset: every position may differ, so a consumer reads the keys wholesale.
        await Assert.That(resets).IsEquivalentTo(expected: [true, true], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task AnIdentityFilterBelowANewOrderDoesNotTestItsPredicateAgain()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(ByScore(isDescending: false));

        int tests = 0;

        ReactiveCollection<int, ItemIdentity, ItemState> odds =
            Scores(edits)
                .SortBy(order)
                .FilterByIdentity(identity =>
                {
                    tests++;

                    return identity.Number % 2 == 1;
                });

        await Assert.That(KeysOf(odds)).IsEquivalentTo(expected: [1, 3, 5], ordering: CollectionOrdering.Matching);

        tests = 0;

        order.Send(ByScore(isDescending: true));
        order.Send(ByName);

        await Assert.That(tests).IsEqualTo(0);
        await Assert.That(KeysOf(odds)).IsEquivalentTo(expected: [5, 1, 3], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     Only a transaction that changed nothing but the order is a reorder. An edit landing in the
    ///     same transaction can move items into or out of the filter, so then it has to test again.
    /// </summary>
    [Test]
    public async Task AFilterBelowANewOrderStillTestsAnEditInTheSameTransaction()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(ByScore(isDescending: false));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            Scores(edits).SortBy(order).Filter(static (_, state) => state.Score >= 20);

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [4, 3, 2, 5], ordering: CollectionOrdering.Matching);

        Transaction.RunVoid(() =>
        {
            order.Send(ByScore(isDescending: true));

            // Key 1 scores into the filter and key 4 scores out of it.
            edits.Send(TestUtil.Score(key: 1, score: 35).CombineWith(TestUtil.Score(key: 4, score: 5)));
        });

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [5, 2, 1, 3], ordering: CollectionOrdering.Matching);
    }

    /// <summary>A filter whose own predicate changes in the same transaction as the order above it.</summary>
    [Test]
    public async Task AFilterBelowANewOrderAppliesItsOwnNewPredicateInTheSameTransaction()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(ByScore(isDescending: false));
        CellSink<int> threshold = Cell.CreateSink(20);

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            Scores(edits)
                .SortBy(order)
                .Filter(criteriaCell: threshold, predicate: static (limit, _, state) => state.Score >= limit);

        Transaction.RunVoid(() =>
        {
            order.Send(ByScore(isDescending: true));
            threshold.Send(40);
        });

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [5, 2], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A reorder carries on down the chain past every stage it does not change: a second filter
    ///     keeps its members too, and a sort with an order of its own keeps its list. A window is where
    ///     it stops, because reordering what is above a window changes what is in it.
    /// </summary>
    [Test]
    public async Task EveryStageBelowANewOrderHoldsWhatItShould()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(ByScore(isDescending: false));

        int tests = 0;

        ReactiveCollection<int, ItemIdentity, ItemState> first =
            Scores(edits).SortBy(order).Filter(static (_, state) => state.Score >= 20);

        ReactiveCollection<int, ItemIdentity, ItemState> second =
            first.Filter((identity, _) =>
            {
                tests++;

                return identity.Number != 3;
            });

        ReactiveCollection<int, ItemIdentity, ItemState> byNumber =
            second.SortByIdentity(static identity => identity.Number);

        ReactiveCollection<int, ItemIdentity, ItemState> firstTwo = second.Slice(offset: 0, limit: 2);

        ReactiveCollection<int, ItemIdentity, ItemState> belowTheWindow =
            firstTwo.Filter(static (_, state) => state.Score >= 0);

        await Assert.That(KeysOf(second)).IsEquivalentTo(expected: [4, 2, 5], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(byNumber)).IsEquivalentTo(expected: [2, 4, 5], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(belowTheWindow)).IsEquivalentTo(expected: [4, 2], ordering: CollectionOrdering.Matching);

        tests = 0;

        order.Send(ByScore(isDescending: true));

        await Assert.That(tests).IsEqualTo(0);
        await Assert.That(KeysOf(first)).IsEquivalentTo(expected: [5, 2, 3, 4], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(second)).IsEquivalentTo(expected: [5, 2, 4], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(byNumber)).IsEquivalentTo(expected: [2, 4, 5], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(firstTwo)).IsEquivalentTo(expected: [5, 2], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(belowTheWindow)).IsEquivalentTo(expected: [5, 2], ordering: CollectionOrdering.Matching);

        // After the reorder, edits are filed under the new order all the way down.
        edits.Send(TestUtil.Score(key: 4, score: 45));

        await Assert.That(KeysOf(second)).IsEquivalentTo(expected: [5, 4, 2], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(byNumber)).IsEquivalentTo(expected: [2, 4, 5], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(belowTheWindow)).IsEquivalentTo(expected: [5, 4], ordering: CollectionOrdering.Matching);
    }
}
