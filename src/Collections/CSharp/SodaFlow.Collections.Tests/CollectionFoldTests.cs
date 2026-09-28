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
///     The fold over a collection. Each test reads the value of the fold after each edit. Thus, a
///     test fails where the fold and the items of the collection disagree.
/// </summary>
public sealed class CollectionFoldTests
{
    [Test]
    public async Task FoldOfNoItemsIsZero()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(edits);
        Cell<int> total = Transaction.Run(() => Total(collection));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(0);
    }

    [Test]
    public async Task FoldStartsFromTheItemsThatTheCollectionHolds()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial: [TestUtil.Item(number: 1, name: "a", score: 10), TestUtil.Item(number: 2, name: "b", score: 5)]);

        // The fold is built after the collection, thus it reads the store and does not wait for an
        // edit. Without that read, this value is 0 until something changes.
        Cell<int> total = Transaction.Run(() => Total(collection));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(15);
    }

    [Test]
    public async Task AnAddAnUpdateAndARemovalEachMoveTheFold()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(edits: edits, initial: [TestUtil.Item(number: 1, name: "a", score: 10)]);

        Cell<int> total = Transaction.Run(() => Total(collection));

        edits.Send(TestUtil.Add(TestUtil.Item(number: 2, name: "b", score: 5)));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(15)
            .Because("an add adds its own value");

        // An update must remove the previous value of the key. Without that, this value is 15 + 7.
        edits.Send(TestUtil.Score(key: 2, score: 7));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(17)
            .Because("an update replaces the value of its key");

        edits.Send(TestUtil.Remove(1));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(7)
            .Because("a removal removes the value of its key");

        edits.Send(TestUtil.Remove(2));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(0)
            .Because("an empty collection folds to zero");
    }

    [Test]
    public async Task AnEditOfSeveralKeysInOneTransactionMovesTheFoldOneTime()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial: [TestUtil.Item(number: 1, name: "a", score: 10), TestUtil.Item(number: 2, name: "b", score: 5)]);

        Cell<int> total = Transaction.Run(() => Total(collection));
        List<int> seen = [];
        IStrongListener l = Transaction.Run(() => total.Updates().ListenStrong(seen.Add));

        // One edit that adds a key, updates a key, and removes a key.
        edits.Send(
            TestUtil.Add(TestUtil.Item(number: 3, name: "c", score: 100))
                .CombineWith(TestUtil.Score(key: 1, score: 1))
                .CombineWith(TestUtil.Remove(2)));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(101);

        await Assert.That(seen)
            .IsEquivalentTo([101])
            .Because("one change of the collection gives one value, and not one for each key in it");

        l.Unlisten();
    }

    [Test]
    public async Task AFoldOfAViewFollowsThatViewAndNotTheStore()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial: [TestUtil.Item(number: 1, name: "a", score: 10), TestUtil.Item(number: 2, name: "b", score: 1)]);

        // The filter accepts a score of 5 and above.
        ReactiveCollection<int, ItemIdentity, ItemState> high =
            Transaction.Run(() => collection.Filter(static (_, state) => state.Score >= 5));

        Cell<int> total = Transaction.Run(() => Total(high));
        Cell<int> storeTotal = Transaction.Run(() => Total(collection));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(10)
            .Because("the view holds one item at the start");

        // An update of an item that the filter refuses, and that the filter refuses again. The fold
        // of the view must not move. A fold that reads each new state, with no test for the items
        // of the view, adds a value that the view does not hold.
        edits.Send(TestUtil.Score(key: 2, score: 2));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(10)
            .Because("an item outside the view adds nothing");

        await Assert.That(Transaction.Run(storeTotal.Sample)).IsEqualTo(12)
            .Because("the store holds it");

        // The same item enters the view.
        edits.Send(TestUtil.Score(key: 2, score: 6));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(16)
            .Because("an item that enters the view adds its value");

        // And leaves it again.
        edits.Send(TestUtil.Score(key: 2, score: 0));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(10)
            .Because("an item that leaves the view removes its value");
    }

    [Test]
    public async Task AFoldSurvivesAResetOfItsView()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial:
                [
                    TestUtil.Item(number: 1, name: "a", score: 10),
                    TestUtil.Item(number: 2, name: "b", score: 20),
                    TestUtil.Item(number: 3, name: "c", score: 30)
                ]);

        CellSink<int> limit = Cell.CreateSink(1);

        // A change of a limit builds the stage again and reports a reset, which carries no
        // operations. A fold that reads the operations of a change sees nothing here.
        ReactiveCollection<int, ItemIdentity, ItemState> window =
            Transaction.Run(() => collection.SortByKey().Take(limit.AsCell()));

        Cell<int> total = Transaction.Run(() => Total(window));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(10)
            .Because("the window holds the first item");

        limit.Send(2);

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(30)
            .Because("a reset adds the item that entered");

        limit.Send(3);
        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(60);

        limit.Send(1);

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(10)
            .Because("a reset removes the items that left");

        // An edit after a reset moves the fold by its own keys.
        edits.Send(TestUtil.Score(key: 1, score: 11));
        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(11);
    }

    [Test]
    public async Task AFoldCanCountTheItems()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(edits: edits, initial: [TestUtil.Item(number: 1, name: "a", score: 10)]);

        Cell<int> count =
            Transaction.Run(() =>
                collection.Fold(
                    select: static (_, _) => 1,
                    zero: 0,
                    add: static (a, b) => a + b,
                    subtract: static (a, b) => a - b));

        edits.Send(TestUtil.Add(TestUtil.Item(number: 2, name: "b", score: 5)));
        await Assert.That(Transaction.Run(count.Sample)).IsEqualTo(2);

        // An update names a key that the collection holds, thus the count does not change.
        edits.Send(TestUtil.Score(key: 2, score: 7));

        await Assert.That(Transaction.Run(count.Sample)).IsEqualTo(2)
            .Because("an update adds no item");

        edits.Send(TestUtil.Remove(1));
        await Assert.That(Transaction.Run(count.Sample)).IsEqualTo(1);
    }

    [Test]
    public async Task AFoldOfAViewRemovesTheValueOfAReplacedItemOneTime()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial: [TestUtil.Item(number: 1, name: "a", score: 10), TestUtil.Item(number: 2, name: "b", score: 20)]);

        ReactiveCollection<int, ItemIdentity, ItemState> high =
            Transaction.Run(() => collection.Filter(static (_, state) => state.Score >= 5));

        Cell<int> total = Transaction.Run(() => Total(high));

        // One edit removes key 2 and adds it again. The view names key 2 as a removal and as an
        // add. A fold that removes the previous value for each of the two gives 10 - 20 + 7.
        edits.Send(TestUtil.Remove(2).CombineWith(TestUtil.Add(TestUtil.Item(number: 2, name: "c", score: 7))));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(17);
    }

    [Test]
    public async Task AFoldOfAWindowFollowsTheKeysThatAnAddMovesInTheWindow()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial:
                [
                    TestUtil.Item(number: 1, name: "a", score: 10),
                    TestUtil.Item(number: 2, name: "b", score: 20),
                    TestUtil.Item(number: 3, name: "c", score: 30)
                ]);

        ReactiveCollection<int, ItemIdentity, ItemState> lowest =
            Transaction.Run(() => collection.SortBy(static (_, state) => state.Score).Take(2));

        Cell<int> total = Transaction.Run(() => Total(lowest));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(30);

        // The new item goes to the first position. The window removes each key after the common
        // part and inserts the keys again. Thus, key 1 is a removal and an add of one change.
        edits.Send(TestUtil.Add(TestUtil.Item(number: 4, name: "d", score: 1)));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(11)
            .Because("the window holds the scores 1 and 10");
    }

    [Test]
    public async Task TheSelectOfAFoldReadsTheIdentityAndTheState()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(edits: edits, initial: [TestUtil.Item(number: 1, name: "a", score: 10)]);

        Cell<int> weighted = Transaction.Run(() => Weighted(collection));

        await Assert.That(Transaction.Run(weighted.Sample)).IsEqualTo(110);

        edits.Send(TestUtil.Score(key: 1, score: 20));
        await Assert.That(Transaction.Run(weighted.Sample)).IsEqualTo(120);

        // The replacement changes the identity of key 1. The fold must remove the value of the
        // previous identity. A fold that reads the identity after the change for the two values
        // removes 1120 in place of 120. It gives 120 - 1120 + 1130 = 130.
        edits.Send(
            TestUtil.Remove(1)
                .CombineWith(
                    TestUtil.Add(
                        new Item<ItemIdentity, ItemState>(
                            identity: new ItemIdentity(Number: 1, Code: "R1"),
                            state: new ItemState(Name: "r", Score: 30)))));

        await Assert.That(Transaction.Run(weighted.Sample)).IsEqualTo(1130);
    }

    [Test]
    public async Task AFoldByIdentitySendsNoValueAtAnEditOfAState()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial: [TestUtil.Item(number: 1, name: "a", score: 10), TestUtil.Item(number: 2, name: "b", score: 5)]);

        Cell<int> count =
            Transaction.Run(() =>
                collection.FoldByIdentity(
                    select: static _ => 1,
                    zero: 0,
                    add: static (a, b) => a + b,
                    subtract: static (a, b) => a - b));

        List<int> seen = [];
        IStrongListener l = Transaction.Run(() => count.Updates().ListenStrong(seen.Add));

        await Assert.That(Transaction.Run(count.Sample)).IsEqualTo(2);

        // An edit of the states of the two keys. A fold that reads each change sends 2 again here.
        edits.Send(TestUtil.Score(key: 1, score: 11).CombineWith(TestUtil.Score(key: 2, score: 6)));

        await Assert.That(seen).IsEmpty()
            .Because("an edit of a state cannot change an identity");

        edits.Send(TestUtil.Add(TestUtil.Item(number: 3, name: "c", score: 1)));
        edits.Send(TestUtil.Remove(1));

        await Assert.That(seen).IsEquivalentTo(expected: [3, 2], ordering: CollectionOrdering.Matching);

        l.Unlisten();
    }

    [Test]
    public async Task AFoldByIdentityFollowsAReplacedIdentity()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(edits: edits, initial: [TestUtil.Item(number: 1, name: "a", score: 10)]);

        ReactiveCollection<int, ItemIdentity, ItemState> high =
            Transaction.Run(() => collection.Filter(static (_, state) => state.Score >= 5));

        Cell<int> root = Transaction.Run(() => WeightedByIdentity(collection));
        Cell<int> view = Transaction.Run(() => WeightedByIdentity(high));

        await Assert.That(Transaction.Run(root.Sample)).IsEqualTo(100);

        // The root names key 1 as an add, and the view names it as a removal and an add. Each
        // one must remove the previous identity one time and add the new one.
        edits.Send(
            TestUtil.Remove(1)
                .CombineWith(
                    TestUtil.Add(
                        new Item<ItemIdentity, ItemState>(
                            identity: new ItemIdentity(Number: 1, Code: "R1"),
                            state: new ItemState(Name: "r", Score: 30)))));

        await Assert.That(Transaction.Run(root.Sample)).IsEqualTo(1100);
        await Assert.That(Transaction.Run(view.Sample)).IsEqualTo(1100);
    }

    [Test]
    public async Task AFoldByIdentityReadsAReplacementThatAResetOfItsViewHides()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial:
                [
                    TestUtil.Item(number: 1, name: "a", score: 10),
                    TestUtil.Item(number: 2, name: "b", score: 20),
                    TestUtil.Item(number: 3, name: "c", score: 30)
                ]);

        CellSink<int> limit = Cell.CreateSink(5);

        ReactiveCollection<int, ItemIdentity, ItemState> window =
            Transaction.Run(() => collection.SortByKey().Take(limit.AsCell()));

        Cell<int> total = Transaction.Run(() => WeightedByIdentity(window));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(600);

        // The change of the limit builds the window again, and the window keeps the same keys.
        // The reset puts no key in Added or Removed, thus it does not name the replacement of
        // key 2 there.
        Transaction.RunVoid(() =>
        {
            edits.Send(
                TestUtil.Remove(2)
                    .CombineWith(
                        TestUtil.Add(
                            new Item<ItemIdentity, ItemState>(
                                identity: new ItemIdentity(Number: 2, Code: "R2"),
                                state: new ItemState(Name: "r", Score: 20)))));

            limit.Send(4);
        });

        await Assert.That(KeysOfWindow(window)).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(1600)
            .Because("the fold must read the new identity of key 2");
    }

    [Test]
    public async Task AFoldByIdentityOfAWindowKeepsItsValueWhenAnEditOfAStateReordersTheWindow()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial:
                [
                    TestUtil.Item(number: 1, name: "a", score: 10),
                    TestUtil.Item(number: 2, name: "b", score: 20),
                    TestUtil.Item(number: 3, name: "c", score: 30),
                    TestUtil.Item(number: 4, name: "d", score: 40)
                ]);

        ReactiveCollection<int, ItemIdentity, ItemState> lowest =
            Transaction.Run(() => collection.SortBy(static (_, state) => state.Score).Take(3));

        Cell<int> total = Transaction.Run(() => WeightedByIdentity(lowest));

        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(600);

        // Key 1 moves behind key 2 and stays in the window. The window removes and inserts keys
        // 1 and 2 again, thus the fold reads a change that holds the same identities.
        edits.Send(TestUtil.Score(key: 1, score: 25));

        await Assert.That(KeysOfWindow(lowest)).IsEquivalentTo(expected: [2, 1, 3], ordering: CollectionOrdering.Matching);
        await Assert.That(Transaction.Run(total.Sample)).IsEqualTo(600);
    }

    [Test]
    public async Task AFoldOfEachViewMatchesASumOfItsItemsAfterRandomEdits()
    {
        // The seed is fixed, thus a failure occurs again at each run.
        Random random = new(20260928);

        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<int> limit = Cell.CreateSink(3);
        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(edits);

        // Each view gives its changes from a different stage. The stages are the root, a filter on
        // the state, a filter on the identity, and a sort. The others are windows that an edit
        // moves, a filter below a window, and a window that a change of its limit builds again.
        (string Name, ReactiveCollection<int, ItemIdentity, ItemState> View)[] views =
            Transaction.Run(() =>
                new[]
                {
                    ("root", collection),
                    ("filter", collection.Filter(static (_, state) => state.Score >= 50)),
                    ("filter by identity", collection.FilterByIdentity(static identity => identity.Number % 2 == 1)),
                    ("sort", collection.SortBy(static (_, state) => state.Score)),
                    ("sort and take", collection.SortBy(static (_, state) => state.Score).Take(3)),
                    ("sort and slice", collection.SortBy(static (_, state) => state.Score).Slice(offset: 2, limit: 3)),
                    (
                        "filter below a window",
                        collection.SortBy(static (_, state) => state.Score)
                            .Take(5)
                            .Filter(static (_, state) => state.Score >= 30)),
                    ("take a limit", collection.SortByKey().Take(limit.AsCell()))
                });

        Cell<int>[] totals = Transaction.Run(() => views.Select(static view => Total(view.View)).ToArray());
        Cell<int>[] weighted = Transaction.Run(() => views.Select(static view => Weighted(view.View)).ToArray());

        Cell<int>[] byIdentity =
            Transaction.Run(() => views.Select(static view => WeightedByIdentity(view.View)).ToArray());

        HashSet<int> present = [];

        for (int step = 0; step < 400; step++)
        {
            List<CollectionEdit<int, ItemIdentity, ItemState>> parts = [];

            foreach (int key in Enumerable.Range(start: 1, count: 10).Where(_ => random.Next(4) == 0))
            {
                int score = random.Next(100);

                if (!present.Contains(key))
                {
                    parts.Add(TestUtil.Add(TestUtil.Item(number: key, name: "a", score: score)));
                    present.Add(key);

                    continue;
                }

                switch (random.Next(3))
                {
                    case 0:
                        parts.Add(TestUtil.Score(key: key, score: score));

                        break;

                    case 1:
                        parts.Add(TestUtil.Remove(key));
                        present.Remove(key);

                        break;

                    default:
                        parts.Add(
                            TestUtil.Remove(key)
                                .CombineWith(
                                    TestUtil.Add(
                                        new Item<ItemIdentity, ItemState>(
                                            identity: new ItemIdentity(Number: key, Code: $"R{step}"),
                                            state: new ItemState(Name: "r", Score: score)))));

                        break;
                }
            }

            int nextLimit = random.Next(6);

            Transaction.RunVoid(() =>
            {
                if (parts.Count > 0)
                {
                    edits.Send(parts.Aggregate(static (left, right) => left.CombineWith(right)));
                }

                limit.Send(nextLimit);
            });

            for (int index = 0; index < views.Length; index++)
            {
                ReactiveCollection<int, ItemIdentity, ItemState> view = views[index].View;

                CollectionSnapshot<int, ItemIdentity, ItemState> snapshot =
                    Transaction.Run(() => view.SnapshotCell.Sample());

                int expected = snapshot.States.Pairs.Sum(static pair => pair.Value.Score);

                int expectedWeighted =
                    snapshot.States.Pairs.Sum(pair =>
                        WeightOf(identity: snapshot.Identities[pair.Key], state: pair.Value));

                await Assert.That(Transaction.Run(totals[index].Sample)).IsEqualTo(expected)
                    .Because($"the fold of the view \"{views[index].Name}\" at step {step}");

                int expectedByIdentity =
                    snapshot.Identities.Sum(static pair => IdentityWeightOf(pair.Value));

                await Assert.That(Transaction.Run(weighted[index].Sample)).IsEqualTo(expectedWeighted)
                    .Because($"the fold of the identity and the state of \"{views[index].Name}\" at step {step}");

                await Assert.That(Transaction.Run(byIdentity[index].Sample)).IsEqualTo(expectedByIdentity)
                    .Because($"the fold of the identity of \"{views[index].Name}\" at step {step}");
            }
        }
    }

    private static ReactiveCollection<int, ItemIdentity, ItemState> Create(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        params Item<ItemIdentity, ItemState>[] initial) =>
        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: TestUtil.KeyOf,
            initialItems: initial,
            edits);

    private static Cell<int> Total(ReactiveCollection<int, ItemIdentity, ItemState> collection) =>
        collection.Fold(
            select: static (_, state) => state.Score,
            zero: 0,
            add: static (a, b) => a + b,
            subtract: static (a, b) => a - b);

    /// <summary>
    ///     A fold that reads the two parts of each item. Thus, a change of the identity moves it.
    /// </summary>
    private static Cell<int> Weighted(ReactiveCollection<int, ItemIdentity, ItemState> collection) =>
        collection.Fold(
            select: static (identity, state) => WeightOf(identity: identity, state: state),
            zero: 0,
            add: static (a, b) => a + b,
            subtract: static (a, b) => a - b);

    /// <summary>
    ///     A fold that reads the identity alone, with the weight that <see cref="Weighted" /> gives
    ///     it.
    /// </summary>
    private static Cell<int> WeightedByIdentity(ReactiveCollection<int, ItemIdentity, ItemState> collection) =>
        collection.FoldByIdentity(
            select: IdentityWeightOf,
            zero: 0,
            add: static (a, b) => a + b,
            subtract: static (a, b) => a - b);

    /// <summary>
    ///     The number of the key times 100, 1000 for an identity that a replacement made, and the
    ///     score.
    /// </summary>
    private static int WeightOf(ItemIdentity identity, ItemState state) => IdentityWeightOf(identity) + state.Score;

    /// <summary>The number of the key times 100, and 1000 for an identity that a replacement made.</summary>
    private static int IdentityWeightOf(ItemIdentity identity) =>
        identity.Number * 100 + (identity.Code[0] == 'R' ? 1000 : 0);

    private static List<int> KeysOfWindow(ReactiveCollection<int, ItemIdentity, ItemState> view) =>
        [.. Transaction.Run(() => view.KeysCell.Sample())];
}
