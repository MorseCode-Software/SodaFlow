using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

public sealed class ReactiveCollectionTests
{
    private static string NameOf(CollectionSnapshot<int, ItemIdentity, ItemState> snapshot, int key) =>
        snapshot.TryGetItem(key, out Item<ItemIdentity, ItemState>? item) && item is not null
            ? item.State.Name
            : "?";

    private static string CodeOf(CollectionSnapshot<int, ItemIdentity, ItemState> snapshot, int key) =>
        snapshot.TryGetItem(key, out Item<ItemIdentity, ItemState>? item) && item is not null
            ? item.Identity.Code
            : "?";

    private static int ScoreOf(CollectionSnapshot<int, ItemIdentity, ItemState> snapshot, int key) =>
        snapshot.TryGetItem(key, out Item<ItemIdentity, ItemState>? item) && item is not null
            ? item.State.Score
            : -1;

    [Test]
    public async Task InitialEntriesAreInTheSnapshot()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10), TestUtil.Item(2, "two", 20)],
            edits);

        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot = collection.SnapshotCell.Sample();

        await Assert.That(snapshot.Count).IsEqualTo(2);
        await Assert.That(NameOf(snapshot, 1)).IsEqualTo("one");
        await Assert.That(CodeOf(snapshot, 1)).IsEqualTo("C1");
        await Assert.That(snapshot.ContainsKey(3)).IsFalse();
    }

    [Test]
    public async Task DuplicateKeyInTheInitialEntriesThrows()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        await Assert.That(
                () => ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    TestUtil.KeyOf,
                    [TestUtil.Item(1, "one", 10), TestUtil.Item(1, "again", 20)],
                    edits))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AddAndRemoveMoveTheShapeCellAndAnUpdateDoesNot()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            edits);

        List<int> shapes = [];
        List<int> snapshots = [];
        IListener shapeListener = collection.ShapeCell.Updates().ListenStrong(s => shapes.Add(s.Count));
        IListener snapshotListener = collection.SnapshotCell.Updates().ListenStrong(s => snapshots.Add(s.Count));

        edits.Send(TestUtil.Add(TestUtil.Item(2, "two", 20)));
        edits.Send(TestUtil.Score(1, 11));
        edits.Send(TestUtil.Remove(2));

        shapeListener.Unlisten();
        snapshotListener.Unlisten();

        // The update is not structural, so only the two structural edits reach the shape cell,
        // while all three reach the snapshot.
        await Assert.That(shapes).IsEquivalentTo([2, 1]);
        await Assert.That(snapshots).IsEquivalentTo([2, 2, 1]);
    }

    [Test]
    public async Task AnEditTouchingNothingFiresNothing()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            edits);

        List<int> fired = [];
        IListener l = collection.SnapshotCell.Updates().ListenStrong(s => fired.Add(s.Count));

        // Removing a key that is not there resolves to no change at all.
        edits.Send(TestUtil.Remove(99));

        l.Unlisten();

        await Assert.That(fired).IsEmpty();
    }

    [Test]
    public async Task AddingAnExistingKeyThrows()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            edits);

        await Assert.That(() => edits.Send(TestUtil.Add(TestUtil.Item(1, "again", 20))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UpdatingAnAbsentKeyThrows()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            edits);

        await Assert.That(() => edits.Send(TestUtil.Score(99, 1))).Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task RemovingAndReAddingAKeyInOneTransactionIsARekey()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            edits);

        edits.Send(
            TestUtil.Remove(1).CombineWith(TestUtil.Add(TestUtil.Item(1, "replacement", 99))));

        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot = collection.SnapshotCell.Sample();

        await Assert.That(snapshot.Count).IsEqualTo(1);
        await Assert.That(NameOf(snapshot, 1)).IsEqualTo("replacement");
    }

    [Test]
    public async Task EditsFromSeparateStreamsInOneTransactionCombineIntoOneChange()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> adds =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> updates =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            adds,
            updates);

        List<ItemChange<int, ItemIdentity, ItemState>> changes = [];
        IListener l = collection.ItemChangesStream.ListenStrong(changes.Add);

        Transaction.RunVoid(() =>
        {
            adds.Send(TestUtil.Add(TestUtil.Item(2, "two", 20)));
            updates.Send(TestUtil.Score(1, 11));
        });

        l.Unlisten();

        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].After.Count).IsEqualTo(2);
        await Assert.That(changes[0].Added).IsEquivalentTo([2]);
        await Assert.That(ScoreOf(changes[0].After, 1)).IsEqualTo(11);
    }

    [Test]
    public async Task TwoTransformsForOneKeyInOneTransactionThrow()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> first =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> second =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            first,
            second);

        // Merge order is arbitrary, so composing them has no defined result and is refused rather
        // than resolved.
        await Assert.That(
                () => Transaction.RunVoid(() =>
                {
                    first.Send(TestUtil.Score(1, 11));
                    second.Send(TestUtil.Score(1, 12));
                }))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UpdatingAndRemovingOneKeyInOneTransactionThrows()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            edits);

        await Assert.That(() => edits.Send(TestUtil.Score(1, 11).CombineWith(TestUtil.Remove(1))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AChangeSeparatesMovedFromStillPresent()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10), TestUtil.Item(2, "two", 20)],
            edits);

        List<ItemChange<int, ItemIdentity, ItemState>> changes = [];
        IListener l = collection.ItemChangesStream.ListenStrong(changes.Add);

        edits.Send(TestUtil.Remove(1));

        l.Unlisten();

        ItemChange<int, ItemIdentity, ItemState> change = changes[0];

        // Removed: it moved, and it is not present afterwards. The two questions are separate
        // members here; the C# wrapper folds them back into one nested optional.
        await Assert.That(change.WasChanged(1)).IsTrue();
        await Assert.That(change.TryGetNewState(1, out ItemState _)).IsFalse();

        // Untouched: no event for an observer of this key at all.
        await Assert.That(change.WasChanged(2)).IsFalse();
    }

    [Test]
    public async Task CreateTakesTheKeyFromASelfKeyedIdentity()
    {
        StreamSink<CollectionEdit<int, SelfKeyedItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, SelfKeyedItemIdentity, ItemState>>();

        // No key selector: the identity implements IIdentity<int>, and TKey is inferred from the
        // edit stream rather than from the constraint, which inference does not read.
        ReactiveCollection<int, SelfKeyedItemIdentity, ItemState> collection = ReactiveCollection.Create(
            [TestUtil.SelfKeyedItem(1, "one", 10), TestUtil.SelfKeyedItem(2, "two", 20)],
            edits);

        await Assert.That(TestUtil.Keys(collection.KeysCell.Sample())).IsEquivalentTo([1, 2]);

        // The derived selector is used for later adds too, not only the initial contents.
        edits.Send(
            CollectionEdit<int, SelfKeyedItemIdentity, ItemState>.Add(
                TestUtil.SelfKeyedItem(3, "three", 30)));

        await Assert.That(TestUtil.Keys(collection.KeysCell.Sample())).IsEquivalentTo([1, 2, 3]);
    }

    [Test]
    public async Task CreateFromASelfKeyedIdentityTakesAStateMapToo()
    {
        StreamSink<CollectionEdit<int, SelfKeyedItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, SelfKeyedItemIdentity, ItemState>>();

        ReactiveCollection<int, SelfKeyedItemIdentity, ItemState> collection = ReactiveCollection.Create(
            [TestUtil.SelfKeyedItem(1, "one", 10)],
            ImmutableStateMap<int, ItemState>.Empty,
            edits);

        await Assert.That(TestUtil.Keys(collection.KeysCell.Sample())).IsEquivalentTo([1]);
    }

    [Test]
    public async Task AChangeCarriesTheStoreOnBothSidesOfIt()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                TestUtil.KeyOf,
                [TestUtil.Item(1, "one", 10)],
                edits);

        List<ItemChange<int, ItemIdentity, ItemState>> changes = [];
        IListener l = collection.ItemChangesStream.ListenStrong(changes.Add);

        edits.Send(TestUtil.Score(1, 99));

        l.Unlisten();

        ItemChange<int, ItemIdentity, ItemState> change = changes[0];

        // The whole point of the pair: a delta needs no copy of the previous value kept alongside.
        await Assert.That(change.Before.States.TryGetState(1, out ItemState was)).IsTrue();
        await Assert.That(was.Score).IsEqualTo(10);

        await Assert.That(change.After.States.TryGetState(1, out ItemState now)).IsTrue();
        await Assert.That(now.Score).IsEqualTo(99);
    }

    [Test]
    public async Task OneChangeStartsWhereTheLastOneEnded()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                TestUtil.KeyOf,
                [TestUtil.Item(1, "one", 10)],
                edits);

        List<ItemChange<int, ItemIdentity, ItemState>> changes = [];
        IListener l = collection.ItemChangesStream.ListenStrong(changes.Add);

        edits.Send(TestUtil.Score(1, 20));
        edits.Send(TestUtil.Score(1, 30));

        l.Unlisten();

        await Assert.That(changes.Count).IsEqualTo(2);

        // Reference equality, not just equal contents: this is what makes holding a sequence of
        // changes cost no more than holding their After alone would.
        await Assert.That(ReferenceEquals(changes[1].Before, changes[0].After)).IsTrue();
    }
}
