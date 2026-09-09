using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

public sealed class FrpCollectionTests
{
    [Test]
    public async Task InitialEntriesAreInTheSnapshot()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        FrpCollection<int, ItemId, ItemState> collection = FrpCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10), TestUtil.Item(2, "two", 20)],
            edits);

        CollectionSnapshot<int, ItemId, ItemState> snapshot = collection.SnapshotCell.Sample();

        await Assert.That(snapshot.Count).IsEqualTo(2);
        await Assert.That(snapshot.Lookup(1).Match(static e => e.State.Name, static () => "?")).IsEqualTo("one");
        await Assert.That(snapshot.ContainsKey(3)).IsFalse();
    }

    [Test]
    public async Task DuplicateKeyInTheInitialEntriesThrows()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        await Assert.That(
                () => FrpCollection<int, ItemId, ItemState>.Create(
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

        FrpCollection<int, ItemId, ItemState> collection = FrpCollection<int, ItemId, ItemState>.Create(
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

        FrpCollection<int, ItemId, ItemState> collection = FrpCollection<int, ItemId, ItemState>.Create(
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

        FrpCollection<int, ItemId, ItemState>.Create(
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

        FrpCollection<int, ItemId, ItemState>.Create(
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

        FrpCollection<int, ItemId, ItemState> collection = FrpCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            edits);

        edits.Send(
            TestUtil.Remove(1).CombineWith(TestUtil.Add(TestUtil.Item(1, "replacement", 99))));

        CollectionSnapshot<int, ItemId, ItemState> snapshot = collection.SnapshotCell.Sample();

        await Assert.That(snapshot.Count).IsEqualTo(1);
        await Assert.That(snapshot.Lookup(1).Match(static e => e.State.Name, static () => "?")).IsEqualTo("replacement");
    }

    [Test]
    public async Task EditsFromSeparateStreamsInOneTransactionCombineIntoOneChange()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> adds =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();
        StreamSink<CollectionEdit<int, ItemId, ItemState>> updates =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        FrpCollection<int, ItemId, ItemState> collection = FrpCollection<int, ItemId, ItemState>.Create(
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
        await Assert.That(changes[0].After.Lookup(1).Match(static e => e.State.Score, static () => -1)).IsEqualTo(11);
    }

    [Test]
    public async Task TwoTransformsForOneKeyInOneTransactionThrow()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> first =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();
        StreamSink<CollectionEdit<int, ItemId, ItemState>> second =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        FrpCollection<int, ItemId, ItemState>.Create(
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

        FrpCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            edits);

        await Assert.That(() => edits.Send(TestUtil.Score(1, 11).CombineWith(TestUtil.Remove(1))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ChangeForSeparatesMovedFromPresent()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        FrpCollection<int, ItemId, ItemState> collection = FrpCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10), TestUtil.Item(2, "two", 20)],
            edits);

        List<CollectionChange<int, ItemId, ItemState>> changes = [];
        IListener l = collection.ItemChangesStream.ListenStrong(changes.Add);

        edits.Send(TestUtil.Remove(1));

        l.Unlisten();

        CollectionChange<int, ItemId, ItemState> change = changes[0];

        // Removed: it moved, and it is not present afterwards.
        await Assert.That(change.ChangeFor(1).Match(
                static inner => inner.Match(static _ => "some", static () => "none"),
                static () => "no event"))
            .IsEqualTo("none");

        // Untouched: no event for an observer of this key at all.
        await Assert.That(change.ChangeFor(2).Match(static _ => "event", static () => "no event")).IsEqualTo("no event");
    }

    [Test]
    public async Task StateCellTracksOneKeyAcrossAddAndRemove()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        FrpCollection<int, ItemId, ItemState> collection = FrpCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [],
            edits);

        // Built before the key exists, which is the point: a bound view can outlive its item, and
        // can be created before it.
        Cell<Maybe<ItemState>> stateCell = collection.StateCell(7);

        List<string> seen = [];
        IListener l = stateCell.Updates().ListenStrong(
            state => seen.Add(state.Match(static s => s.Name, static () => "gone")));

        await Assert.That(stateCell.Sample().Match(static _ => "some", static () => "none")).IsEqualTo("none");

        edits.Send(TestUtil.Add(TestUtil.Item(7, "seven", 70)));
        edits.Send(TestUtil.Remove(7));
        edits.Send(TestUtil.Add(TestUtil.Item(7, "seven again", 71)));

        l.Unlisten();

        await Assert.That(seen).IsEquivalentTo(["seven", "gone", "seven again"]);
    }

    [Test]
    public async Task StateCellBuiltInTheTransactionThatAddsItsKeySeesTheNewValue()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        FrpCollection<int, ItemId, ItemState> collection = FrpCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [],
            edits);

        Cell<Maybe<ItemState>>? built = null;

        // A row constructed in response to the structural change that created its item. By the time
        // this runs the change stream has already fired, so the lazy seed is all the cell has - an
        // eager sample would read the pre-transaction snapshot and sit at no value.
        IListener l = collection.ShapeCell.Updates().ListenStrong(_ => built ??= collection.StateCell(5));

        edits.Send(TestUtil.Add(TestUtil.Item(5, "five", 50)));

        l.Unlisten();

        await Assert.That(built).IsNotNull();

        // ReSharper disable once NullableWarningSuppressionIsUsed - the assertion above is what
        // rules out null, and the compiler cannot see through it.
        await Assert.That(built!.Sample().Match(static s => s.Name, static () => "none")).IsEqualTo("five");
    }

    [Test]
    public async Task StateCellIsSharedPerKeyWhileSomethingHoldsIt()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        FrpCollection<int, ItemId, ItemState> collection = FrpCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            edits);

        Cell<Maybe<ItemState>> first = collection.StateCell(1);
        Cell<Maybe<ItemState>> second = collection.StateCell(1);

        await Assert.That(second).IsSameReferenceAs(first);
    }

    [Test]
    public async Task IdentityCellMovesOnlyOnStructuralChange()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        FrpCollection<int, ItemId, ItemState> collection = FrpCollection<int, ItemId, ItemState>.Create(
            TestUtil.KeyOf,
            [TestUtil.Item(1, "one", 10)],
            edits);

        List<string> seen = [];
        IListener l = collection.IdentityCell(1).Updates().ListenStrong(
            identity => seen.Add(identity.Match(static i => i.Code, static () => "gone")));

        edits.Send(TestUtil.Score(1, 11));
        edits.Send(TestUtil.Remove(1));

        l.Unlisten();

        await Assert.That(seen).IsEquivalentTo(["gone"]);
    }
}
