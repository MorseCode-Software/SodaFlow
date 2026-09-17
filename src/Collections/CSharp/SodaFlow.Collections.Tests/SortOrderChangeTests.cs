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
}
