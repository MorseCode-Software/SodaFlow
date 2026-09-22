using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

public sealed class ReactiveCollectionTests
{
    private static string NameOf(CollectionSnapshot<int, ItemIdentity, ItemState> snapshot, int key) =>
        snapshot.TryGetItem(key: key, item: out Item<ItemIdentity, ItemState>? item)
            ? item.State.Name
            : "?";

    private static string CodeOf(CollectionSnapshot<int, ItemIdentity, ItemState> snapshot, int key) =>
        snapshot.TryGetItem(key: key, item: out Item<ItemIdentity, ItemState>? item)
            ? item.Identity.Code
            : "?";

    private static int ScoreOf(CollectionSnapshot<int, ItemIdentity, ItemState> snapshot, int key) =>
        snapshot.TryGetItem(key: key, item: out Item<ItemIdentity, ItemState>? item)
            ? item.State.Score
            : -1;

    [Test]
    public async Task InitialEntriesAreInTheSnapshot()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                keySelector: TestUtil.KeyOf,
                initialItems:
                [
                    TestUtil.Item(number: 1, name: "one", score: 10), TestUtil.Item(number: 2, name: "two", score: 20)
                ],
                edits);

        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot = collection.SnapshotCell.Sample();

        await Assert.That(snapshot.Count).IsEqualTo(2);
        await Assert.That(NameOf(snapshot: snapshot, key: 1)).IsEqualTo("one");
        await Assert.That(CodeOf(snapshot: snapshot, key: 1)).IsEqualTo("C1");
        await Assert.That(snapshot.ContainsKey(3)).IsFalse();
    }

    [Test]
    public async Task DuplicateKeyInTheInitialEntriesThrows()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        await Assert.That(() =>
                ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    keySelector: TestUtil.KeyOf,
                    initialItems:
                    [
                        TestUtil.Item(number: 1, name: "one", score: 10),
                        TestUtil.Item(number: 1, name: "again", score: 20)
                    ],
                    edits))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AddAndRemoveMoveTheShapeCellAndAnUpdateDoesNot()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                keySelector: TestUtil.KeyOf,
                initialItems: [TestUtil.Item(number: 1, name: "one", score: 10)],
                edits);

        List<int> shapes = [];
        List<int> snapshots = [];
        IListener shapeListener = collection.ShapeCell.Updates().ListenStrong(s => shapes.Add(s.Count));
        IListener snapshotListener = collection.SnapshotCell.Updates().ListenStrong(s => snapshots.Add(s.Count));

        edits.Send(TestUtil.Add(TestUtil.Item(number: 2, name: "two", score: 20)));
        edits.Send(TestUtil.Score(key: 1, score: 11));
        edits.Send(TestUtil.Remove(2));

        shapeListener.Unlisten();
        snapshotListener.Unlisten();

        // The update is not structural, thus only the two structural edits come to the shape cell,
        // and the three edits come to the snapshot.
        await Assert.That(shapes).IsEquivalentTo(expected: [2, 1], ordering: CollectionOrdering.Matching);
        await Assert.That(snapshots).IsEquivalentTo(expected: [2, 2, 1], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task AnEditTouchingNothingFiresNothing()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                keySelector: TestUtil.KeyOf,
                initialItems: [TestUtil.Item(number: 1, name: "one", score: 10)],
                edits);

        List<int> fired = [];
        IListener l = collection.SnapshotCell.Updates().ListenStrong(s => fired.Add(s.Count));

        // A removal of a key that the collection does not have resolves to no change.
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
            keySelector: TestUtil.KeyOf,
            initialItems: [TestUtil.Item(number: 1, name: "one", score: 10)],
            edits);

        await Assert.That(() => edits.Send(TestUtil.Add(TestUtil.Item(number: 1, name: "again", score: 20))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UpdatingAnAbsentKeyThrows()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: TestUtil.KeyOf,
            initialItems: [TestUtil.Item(number: 1, name: "one", score: 10)],
            edits);

        await Assert.That(() => edits.Send(TestUtil.Score(key: 99, score: 1))).Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task RemovingAndReAddingAKeyInOneTransactionIsARekey()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                keySelector: TestUtil.KeyOf,
                initialItems: [TestUtil.Item(number: 1, name: "one", score: 10)],
                edits);

        edits.Send(
            TestUtil.Remove(1).CombineWith(TestUtil.Add(TestUtil.Item(number: 1, name: "replacement", score: 99))));

        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot = collection.SnapshotCell.Sample();

        await Assert.That(snapshot.Count).IsEqualTo(1);
        await Assert.That(NameOf(snapshot: snapshot, key: 1)).IsEqualTo("replacement");
    }

    [Test]
    public async Task EditsFromSeparateStreamsInOneTransactionCombineIntoOneChange()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> adds =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> updates =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                keySelector: TestUtil.KeyOf,
                initialItems: [TestUtil.Item(number: 1, name: "one", score: 10)],
                adds,
                updates);

        List<ItemChange<int, ItemIdentity, ItemState>> changes = [];
        IListener l = collection.ItemChangesStream.ListenStrong(changes.Add);

        Transaction.RunVoid(() =>
        {
            adds.Send(TestUtil.Add(TestUtil.Item(number: 2, name: "two", score: 20)));
            updates.Send(TestUtil.Score(key: 1, score: 11));
        });

        l.Unlisten();

        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].After.Count).IsEqualTo(2);
        await Assert.That(changes[0].Added).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Any);
        await Assert.That(ScoreOf(snapshot: changes[0].After, key: 1)).IsEqualTo(11);
    }

    [Test]
    public async Task TwoTransformsForOneKeyInOneTransactionThrow()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> first =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> second =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: TestUtil.KeyOf,
            initialItems: [TestUtil.Item(number: 1, name: "one", score: 10)],
            first,
            second);

        // SodaFlow does not give the sequence of a merge, thus a composition of the two has no
        // result and this code refuses it and does not resolve it.
        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    first.Send(TestUtil.Score(key: 1, score: 11));
                    second.Send(TestUtil.Score(key: 1, score: 12));
                }))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UpdatingAndRemovingOneKeyInOneTransactionThrows()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: TestUtil.KeyOf,
            initialItems: [TestUtil.Item(number: 1, name: "one", score: 10)],
            edits);

        await Assert.That(() => edits.Send(TestUtil.Score(key: 1, score: 11).CombineWith(TestUtil.Remove(1))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AChangeSeparatesMovedFromStillPresent()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                keySelector: TestUtil.KeyOf,
                initialItems:
                [
                    TestUtil.Item(number: 1, name: "one", score: 10), TestUtil.Item(number: 2, name: "two", score: 20)
                ],
                edits);

        List<ItemChange<int, ItemIdentity, ItemState>> changes = [];
        IListener l = collection.ItemChangesStream.ListenStrong(changes.Add);

        edits.Send(TestUtil.Remove(1));

        l.Unlisten();

        ItemChange<int, ItemIdentity, ItemState> change = changes[0];

        // A removal: the change named the key, and the collection does not have it after the
        // change. The two questions are two members here, and the C# wrapper puts them in one
        // nested optional value.
        await Assert.That(change.WasChanged(1)).IsTrue();
        await Assert.That(change.TryGetNewState(key: 1, state: out _)).IsFalse();

        // No change: an observer of this key gets no event.
        await Assert.That(change.WasChanged(2)).IsFalse();
    }

    [Test]
    public async Task CreateTakesTheKeyFromASelfKeyedIdentity()
    {
        StreamSink<CollectionEdit<int, SelfKeyedItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, SelfKeyedItemIdentity, ItemState>>();

        // There is no key selector, because the identity is an IIdentity<int>. The compiler infers
        // TKey from the edit stream and not from the constraint, because type inference does not
        // read a constraint.
        ReactiveCollection<int, SelfKeyedItemIdentity, ItemState> collection =
            ReactiveCollection.Create(
                initialEntries:
                [
                    TestUtil.SelfKeyedItem(number: 1, name: "one", score: 10),
                    TestUtil.SelfKeyedItem(number: 2, name: "two", score: 20)
                ],
                edits);

        await Assert.That(TestUtil.Keys(collection.KeysCell.Sample()))
            .IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching);

        // A subsequent add also uses the derived selector, and not only the initial contents.
        edits.Send(
            CollectionEdit<int, SelfKeyedItemIdentity, ItemState>.Add(
                TestUtil.SelfKeyedItem(number: 3, name: "three", score: 30)));

        await Assert.That(TestUtil.Keys(collection.KeysCell.Sample()))
            .IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task AChangeCarriesTheStoreOnBothSidesOfIt()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                keySelector: TestUtil.KeyOf,
                initialItems: [TestUtil.Item(number: 1, name: "one", score: 10)],
                edits);

        List<ItemChange<int, ItemIdentity, ItemState>> changes = [];
        IListener l = collection.ItemChangesStream.ListenStrong(changes.Add);

        edits.Send(TestUtil.Score(key: 1, score: 99));

        l.Unlisten();

        ItemChange<int, ItemIdentity, ItemState> change = changes[0];

        // This is the purpose of the pair: a delta needs no copy of the previous value.
        await Assert.That(change.Before.States.TryGetState(key: 1, state: out ItemState? was)).IsTrue();
        await Assert.That(was).IsNotNull();
#pragma warning disable CS8602 // Dereference of a possibly null reference.
        await Assert.That(was.Score).IsEqualTo(10);
#pragma warning restore CS8602 // Dereference of a possibly null reference.

        await Assert.That(change.After.States.TryGetState(key: 1, state: out ItemState? now)).IsTrue();
        await Assert.That(now).IsNotNull();
#pragma warning disable CS8602 // Dereference of a possibly null reference.
        await Assert.That(now.Score).IsEqualTo(99);
#pragma warning restore CS8602 // Dereference of a possibly null reference.
    }

    [Test]
    public async Task OneChangeStartsWhereTheLastOneEnded()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                keySelector: TestUtil.KeyOf,
                initialItems: [TestUtil.Item(number: 1, name: "one", score: 10)],
                edits);

        List<ItemChange<int, ItemIdentity, ItemState>> changes = [];
        IListener l = collection.ItemChangesStream.ListenStrong(changes.Add);

        edits.Send(TestUtil.Score(key: 1, score: 20));
        edits.Send(TestUtil.Score(key: 1, score: 30));

        l.Unlisten();

        await Assert.That(changes.Count).IsEqualTo(2);

        // These are the same reference, and not two objects with equal contents. Thus, a hold on a
        // sequence of changes costs no more than a hold on their After alone.
        await Assert.That(ReferenceEquals(objA: changes[1].Before, objB: changes[0].After)).IsTrue();
    }
}
