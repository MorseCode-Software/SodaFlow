using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
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
                    select: static _ => 1,
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

    private static ReactiveCollection<int, ItemIdentity, ItemState> Create(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        params Item<ItemIdentity, ItemState>[] initial) =>
        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: TestUtil.KeyOf,
            initialItems: initial,
            edits);

    private static Cell<int> Total(ReactiveCollection<int, ItemIdentity, ItemState> collection) =>
        collection.Fold(
            select: static state => state.Score,
            zero: 0,
            add: static (a, b) => a + b,
            subtract: static (a, b) => a - b);
}
