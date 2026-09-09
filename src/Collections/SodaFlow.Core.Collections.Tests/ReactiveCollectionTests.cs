using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

public sealed class ReactiveCollectionTests
{
    private static string NameOf(CollectionSnapshot<int, ItemId, ItemState> snapshot, int key) =>
        snapshot.TryGetEntry(key, out Entry<ItemId, ItemState>? entry) && entry is not null
            ? entry.State.Name
            : "?";

    private static string CodeOf(CollectionSnapshot<int, ItemId, ItemState> snapshot, int key) =>
        snapshot.TryGetEntry(key, out Entry<ItemId, ItemState>? entry) && entry is not null
            ? entry.Identity.Code
            : "?";

    private static int ScoreOf(CollectionSnapshot<int, ItemId, ItemState> snapshot, int key) =>
        snapshot.TryGetEntry(key, out Entry<ItemId, ItemState>? entry) && entry is not null
            ? entry.State.Score
            : -1;

    [Test]
    public async Task InitialEntriesAreInTheSnapshot()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = ReactiveCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10), TestUtil.Item(2, "two", 20)],
            edits);

        CollectionSnapshot<int, ItemId, ItemState> snapshot = collection.SnapshotCell.Sample();

        await Assert.That(snapshot.Count).IsEqualTo(2);
        await Assert.That(NameOf(snapshot, 1)).IsEqualTo("one");
        await Assert.That(CodeOf(snapshot, 1)).IsEqualTo("C1");
        await Assert.That(snapshot.ContainsKey(3)).IsFalse();
    }

    [Test]
    public async Task DuplicateKeyInTheInitialEntriesThrows()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        await Assert.That(
                () => ReactiveCollection<int, ItemId, ItemState>.Create(
                    TestUtil.KeyOf,
                    [TestUtil.Item(1, "one", 10), TestUtil.Item(1, "again", 20)],
                    edits))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AddAndRemoveMoveTheShapeCellAndAnUpdateDoesNot()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = ReactiveCollection<int, ItemId, ItemState>.Create(
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
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = ReactiveCollection<int, ItemId, ItemState>.Create(
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
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            edits);

        await Assert.That(() => edits.Send(TestUtil.Add(TestUtil.Item(1, "again", 20))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UpdatingAnAbsentKeyThrows()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            edits);

        await Assert.That(() => edits.Send(TestUtil.Score(99, 1))).Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task RemovingAndReAddingAKeyInOneTransactionIsARekey()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = ReactiveCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            edits);

        edits.Send(
            TestUtil.Remove(1).CombineWith(TestUtil.Add(TestUtil.Item(1, "replacement", 99))));

        CollectionSnapshot<int, ItemId, ItemState> snapshot = collection.SnapshotCell.Sample();

        await Assert.That(snapshot.Count).IsEqualTo(1);
        await Assert.That(NameOf(snapshot, 1)).IsEqualTo("replacement");
    }

    [Test]
    public async Task EditsFromSeparateStreamsInOneTransactionCombineIntoOneChange()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> adds =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();
        StreamSink<CollectionEdit<int, ItemId, ItemState>> updates =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = ReactiveCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            adds,
            updates);

        List<CollectionChange<int, ItemId, ItemState>> changes = [];
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
        StreamSink<CollectionEdit<int, ItemId, ItemState>> first =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();
        StreamSink<CollectionEdit<int, ItemId, ItemState>> second =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState>.Create(
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
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            edits);

        await Assert.That(() => edits.Send(TestUtil.Score(1, 11).CombineWith(TestUtil.Remove(1))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AChangeSeparatesMovedFromStillPresent()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = ReactiveCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10), TestUtil.Item(2, "two", 20)],
            edits);

        List<CollectionChange<int, ItemId, ItemState>> changes = [];
        IListener l = collection.ItemChangesStream.ListenStrong(changes.Add);

        edits.Send(TestUtil.Remove(1));

        l.Unlisten();

        CollectionChange<int, ItemId, ItemState> change = changes[0];

        // Removed: it moved, and it is not present afterwards. The two questions are separate
        // members here; the C# wrapper folds them back into one nested optional.
        await Assert.That(change.WasChanged(1)).IsTrue();
        await Assert.That(change.TryGetNewState(1, out ItemState _)).IsFalse();

        // Untouched: no event for an observer of this key at all.
        await Assert.That(change.WasChanged(2)).IsFalse();
    }
}
