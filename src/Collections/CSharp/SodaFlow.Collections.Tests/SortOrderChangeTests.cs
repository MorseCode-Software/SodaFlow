using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     A new order for a sort stage. Where the new order is the previous order in the opposite
///     direction, the stage can turn the list that it holds and does not sort each key again.
///     No code reached that path while this class calculated its result and then discarded it.
/// </summary>
public sealed class SortOrderChangeTests
{
    /// <summary>Shared instances, because reversing is only offered for orders built from the same ones.</summary>
    private static readonly IComparer<int> ScoreComparer = Comparer<int>.Default;

    private static readonly IComparer<int> KeyComparer = Comparer<int>.Default;

    private static KeyOrder<int, ItemIdentity, ItemState> ByName { get; } =
        KeyOrder<int, ItemIdentity, ItemState>.By(static (_, state) => state.Name);

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

        // This reverses the list again, thus a reversed list is also correct for a second
        // reversal.
        order.Send(ByScore(isDescending: false));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [1, 3, 2], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A stage that gets its order in the opposite direction keeps the items with equal sort
    ///     values in the order of their keys, as a new descending sort does. It does not turn the
    ///     list that it held, which also turns those items. An edit after the reversal uses
    ///     the same rule.
    /// </summary>
    [Test]
    public async Task ReversingAnOrderKeepsTiedItemsInKeyOrder()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 10),
                TestUtil.Item(number: 4, name: "four", score: 20),
                TestUtil.Item(number: 5, name: "five", score: 30),
                TestUtil.Item(number: 6, name: "six", score: 10));

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(ByScore(isDescending: false));

        ReactiveCollection<int, ItemIdentity, ItemState> sorted = collection.SortBy(order);

        await Assert.That(KeysOf(sorted))
            .IsEquivalentTo(expected: [1, 3, 6, 2, 4, 5], ordering: CollectionOrdering.Matching);

        order.Send(ByScore(isDescending: true));

        // This is not [5, 4, 2, 6, 3, 1], which is the list in the opposite direction.
        await Assert.That(KeysOf(sorted))
            .IsEquivalentTo(expected: [5, 2, 4, 1, 3, 6], ordering: CollectionOrdering.Matching);

        // A key with a sort value equal to a second key goes in the order of the two keys.
        edits.Send(TestUtil.Add(TestUtil.Item(number: 0, name: "zero", score: 20)));

        await Assert.That(KeysOf(sorted))
            .IsEquivalentTo(expected: [5, 0, 2, 4, 1, 3, 6], ordering: CollectionOrdering.Matching);

        // This reverses the order again.
        order.Send(ByScore(isDescending: false));

        await Assert.That(KeysOf(sorted))
            .IsEquivalentTo(expected: [1, 3, 6, 0, 2, 4, 5], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A stage that reversed its list must hold the new order, and not the order of its
    ///     construction. A filter below it builds from the order of its upstream collection, thus
    ///     that filter shows the error when the stage keeps the previous order.
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

        // Each key agrees with the predicate, thus the filter holds the full list in the order of
        // its upstream collection.
        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.SortBy(order).Filter(static (_, state) => state.Score > 0);

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [1, 3, 2], ordering: CollectionOrdering.Matching);

        order.Send(ByScore(isDescending: true));

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);

        // A subsequent edit also sorts in the new order, and not in the order of the construction
        // of the list.
        edits.Send(TestUtil.Add(TestUtil.Item(number: 4, name: "four", score: 25)));

        await Assert.That(KeysOf(passing))
            .IsEquivalentTo(expected: [2, 4, 3, 1], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A new order with the same direction that reads a different value is not the previous
    ///     order, and the list that the stage holds is not in that new order.
    /// </summary>
    /// <remarks>
    ///     This code compares two orders with the selector from the caller, and also with the
    ///     comparers and the direction. A test of the comparers and the sort key types alone
    ///     reads two orders with different values through the same comparers as one order, and the
    ///     stage then reported nothing.
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

        // The ascending lengths are "one" at 3 and "two" at 3, with key 1 before key 2 because the
        // key is the last level, and then "three" at 5.
        order.Send(
            KeyOrder<int, ItemIdentity, ItemState>.By(
                selector: static (_, state) => state.Name.Length,
                sortComparer: ScoreComparer,
                keyComparer: KeyComparer,
                isDescending: false));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A new order in the opposite direction that reads a different value is not the previous
    ///     order in the opposite direction, and a reversal of the list does not put it in that new
    ///     order.
    /// </summary>
    /// <remarks>
    ///     The reversal has the test of an equivalence: the same selector, the same comparers, the
    ///     same key equality comparer, and the opposite direction. Without the selector in that
    ///     test, the stage reversed its list for an order on a different value.
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

        // This has the same sort key type, the same two comparers, and the opposite direction, and
        // it reads the length of the name and not the score. The ascending scores are 1, 3, and 2.
        // The descending lengths are "three" at 5, "one" at 3, and "two" at 3, which is key 3, and
        // then key 1 before key 2 because the key is the last level.
        order.Send(
            KeyOrder<int, ItemIdentity, ItemState>.By(
                selector: static (_, state) => state.Name.Length,
                sortComparer: ScoreComparer,
                keyComparer: KeyComparer,
                isDescending: true));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [3, 1, 2], ordering: CollectionOrdering.Matching);
    }

    private static ReactiveCollection<int, ItemIdentity, ItemState> Scores(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits) =>
        Create(
            edits: edits,
            TestUtil.Item(number: 1, name: "one", score: 10),
            TestUtil.Item(number: 2, name: "two", score: 40),
            TestUtil.Item(number: 3, name: "three", score: 30),
            TestUtil.Item(number: 4, name: "four", score: 20),
            TestUtil.Item(number: 5, name: "five", score: 50));

    /// <summary>
    ///     A new order above a filter changes the positions of its members and does not change the
    ///     members. Thus, the filter moves its members to the new order and does not test each item
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
                    // ReSharper disable once AccessToModifiedClosure
                    tests++;

                    return state.Score >= 20;
                });

        await Assert.That(KeysOf(passing))
            .IsEquivalentTo(expected: [4, 3, 2, 5], ordering: CollectionOrdering.Matching);

        List<bool> resets = [];
        IListener l = passing.KeyChangesStream.ListenStrong(change => resets.Add(change.IsReset));

        tests = 0;

        // This is the same order in the opposite direction, and the sort answers it with a
        // reversal of its list.
        order.Send(ByScore(isDescending: true));

        await Assert.That(KeysOf(passing))
            .IsEquivalentTo(expected: [5, 2, 3, 4], ordering: CollectionOrdering.Matching);

        // This is a different order, and the sort answers it with a new sort of each key.
        order.Send(ByName);

        await Assert.That(KeysOf(passing))
            .IsEquivalentTo(expected: [5, 4, 3, 2], ordering: CollectionOrdering.Matching);

        l.Unlisten();

        await Assert.That(tests).IsEqualTo(0);

        // This is also a reset, because each position can be different, thus a consumer reads all
        // of the keys.
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
                    // ReSharper disable once AccessToModifiedClosure
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
    ///     Only a transaction that changed the order and nothing else is a change of order. An edit
    ///     in the same transaction can move items into the filter or out of it, thus the filter must
    ///     then test again.
    /// </summary>
    [Test]
    public async Task AFilterBelowANewOrderStillTestsAnEditInTheSameTransaction()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(ByScore(isDescending: false));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            Scores(edits).SortBy(order).Filter(static (_, state) => state.Score >= 20);

        await Assert.That(KeysOf(passing))
            .IsEquivalentTo(expected: [4, 3, 2, 5], ordering: CollectionOrdering.Matching);

        Transaction.RunVoid(() =>
        {
            order.Send(ByScore(isDescending: true));

            // A new score moves key 1 into the filter and moves key 4 out of it.
            edits.Send(TestUtil.Score(key: 1, score: 35).CombineWith(TestUtil.Score(key: 4, score: 5)));
        });

        await Assert.That(KeysOf(passing))
            .IsEquivalentTo(expected: [5, 2, 1, 3], ordering: CollectionOrdering.Matching);
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
    ///     A change of order continues down the chain, through each stage that it does not change.
    ///     A second filter also keeps its members, and a sort with its own order keeps its list. It
    ///     stops at a window, because a change of the order above a window changes the keys in the
    ///     window.
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
                // ReSharper disable once AccessToModifiedClosure
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

        await Assert.That(KeysOf(belowTheWindow))
            .IsEquivalentTo(expected: [4, 2], ordering: CollectionOrdering.Matching);

        tests = 0;

        order.Send(ByScore(isDescending: true));

        await Assert.That(tests).IsEqualTo(0);
        await Assert.That(KeysOf(first)).IsEquivalentTo(expected: [5, 2, 3, 4], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(second)).IsEquivalentTo(expected: [5, 2, 4], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(byNumber)).IsEquivalentTo(expected: [2, 4, 5], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(firstTwo)).IsEquivalentTo(expected: [5, 2], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(belowTheWindow))
            .IsEquivalentTo(expected: [5, 2], ordering: CollectionOrdering.Matching);

        // After the change of order, each stage sorts an edit in the new order.
        edits.Send(TestUtil.Score(key: 4, score: 45));

        await Assert.That(KeysOf(second)).IsEquivalentTo(expected: [5, 4, 2], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(byNumber)).IsEquivalentTo(expected: [2, 4, 5], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(belowTheWindow))
            .IsEquivalentTo(expected: [5, 4], ordering: CollectionOrdering.Matching);
    }
}
