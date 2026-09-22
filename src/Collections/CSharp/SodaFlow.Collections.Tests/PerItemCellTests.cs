using System.Collections.Generic;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     The surface with optional values, which is in the C# wrapper and not in the core. The core
///     answers with a <c>TryGet</c>, thus F# can add <c>option</c> above it.
/// </summary>
public sealed class PerItemCellTests
{
    private static ReactiveCollection<int, ItemIdentity, ItemState> Create(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        params Item<ItemIdentity, ItemState>[] initial) =>
        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: TestUtil.KeyOf,
            initialItems: initial,
            edits);

    [Test]
    public async Task StateCellTracksOneKeyAcrossAddAndRemove()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(edits);

        // This code builds the cell before the key is in the collection, and that is the purpose.
        // A bound view can continue after its item, and code can make one before the item.
        Cell<Maybe<ItemState>> stateCell = collection.StateCell(7);

        List<string> seen = [];

        IListener l =
            stateCell.Updates()
                .ListenStrong(state => seen.Add(state.Match(onSome: static s => s.Name, onNone: static () => "gone")));

        await Assert.That(stateCell.Sample().Match(onSome: static _ => "some", onNone: static () => "none"))
            .IsEqualTo("none");

        edits.Send(TestUtil.Add(TestUtil.Item(number: 7, name: "seven", score: 70)));
        edits.Send(TestUtil.Remove(7));
        edits.Send(TestUtil.Add(TestUtil.Item(number: 7, name: "seven again", score: 71)));

        l.Unlisten();

        await Assert.That(seen)
            .IsEquivalentTo(expected: ["seven", "gone", "seven again"], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task StateCellBuiltInTheTransactionThatAddsItsKeySeesTheNewValue()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(edits);

        Cell<Maybe<ItemState>>? built = null;

        // This is a row from the structural change that made its item. The change stream sent its
        // value before this code runs, thus the lazy seed is the only source for the cell. A sample
        // before the lazy step reads the snapshot from before the transaction, and the cell then
        // has no value.
        IListener l = collection.ShapeCell.Updates().ListenStrong(_ => built ??= collection.StateCell(5));

        edits.Send(TestUtil.Add(TestUtil.Item(number: 5, name: "five", score: 50)));

        l.Unlisten();

        await Assert.That(built).IsNotNull();

        // ReSharper disable once NullableWarningSuppressionIsUsed - the assertion above is what
        // rules out null, and the compiler cannot see through it.
        await Assert.That(built!.Sample().Match(onSome: static s => s.Name, onNone: static () => "none"))
            .IsEqualTo("five");
    }

    [Test]
    public async Task StateCellIsSharedPerKeyWhileSomethingHoldsIt()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(edits: edits, TestUtil.Item(number: 1, name: "one", score: 10));

        Cell<Maybe<ItemState>> first = collection.StateCell(1);
        Cell<Maybe<ItemState>> second = collection.StateCell(1);

        await Assert.That(second).IsSameReferenceAs(first);
    }

    [Test]
    public async Task IdentityCellMovesOnlyOnStructuralChange()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(edits: edits, TestUtil.Item(number: 1, name: "one", score: 10));

        List<string> seen = [];

        IListener l =
            collection.IdentityCell(1)
                .Updates()
                .ListenStrong(identity =>
                    seen.Add(identity.Match(onSome: static i => i.Code, onNone: static () => "gone")));

        edits.Send(TestUtil.Score(key: 1, score: 11));
        edits.Send(TestUtil.Remove(1));

        l.Unlisten();

        await Assert.That(seen).IsEquivalentTo(expected: ["gone"], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task ChangeForNestsMovedInsideStillPresent()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        List<ItemChange<int, ItemIdentity, ItemState>> changes = [];
        IListener l = collection.ItemChangesStream.ListenStrong(changes.Add);

        edits.Send(TestUtil.Remove(1));

        l.Unlisten();

        ItemChange<int, ItemIdentity, ItemState> change = changes[0];

        // A removal: the change named the key, and the collection does not have it after the
        // change.
        await Assert.That(
                change.ChangeFor(1)
                    .Match(
                        onSome: static inner => inner.Match(onSome: static _ => "some", onNone: static () => "none"),
                        onNone: static () => "no event"))
            .IsEqualTo("none");

        // No change: an observer of this key gets no event.
        await Assert.That(change.ChangeFor(2).Match(onSome: static _ => "event", onNone: static () => "no event"))
            .IsEqualTo("no event");
    }

    [Test]
    public async Task LookupAndIndexOfAnswerWithMaybe()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot = collection.SnapshotCell.Sample();

        await Assert.That(snapshot.Lookup(1).Match(onSome: static e => e.State.Name, onNone: static () => "?"))
            .IsEqualTo("one");

        await Assert.That(snapshot.Lookup(9).Match(onSome: static _ => "some", onNone: static () => "none"))
            .IsEqualTo("none");

        await Assert.That(snapshot.States.Lookup(2).Match(onSome: static s => s.Score, onNone: static () => -1))
            .IsEqualTo(20);

        OrderedKeys<int, ItemIdentity, ItemState> keys = collection.KeysCell.Sample();

        await Assert.That(keys.IndexOf(2).Match(onSome: static i => i, onNone: static () => -1)).IsEqualTo(1);

        await Assert.That(keys.IndexOf(9).Match(onSome: static _ => "some", onNone: static () => "none"))
            .IsEqualTo("none");
    }

    /// <summary>
    ///     A state edit that also sorts the row again. The stage reports that sort as a move alone.
    ///     Thus a cell for one item that omits a move never gets the new value, and the row moves to
    ///     its new position and shows the previous value.
    /// </summary>
    [Test]
    public async Task StateCellOnASortedViewSeesAnUpdateThatMovesItsRow()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30));

        ReactiveCollection<int, ItemIdentity, ItemState> byScore =
            collection.SortBy(static (_, state) => state.Score);

        List<int> scores = [];

        IListener l =
            byScore.StateCell(1)
                .ListenStrong(state => scores.Add(state.Match(onSome: static s => s.Score, onNone: static () => -1)));

        // A change from 10 to 99 moves key 1 from the front of the view to the end.
        edits.Send(TestUtil.Score(key: 1, score: 99));

        l.Unlisten();

        await Assert.That(TestUtil.Keys(byScore.KeysCell.Sample()))
            .IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);

        await Assert.That(scores).IsEquivalentTo(expected: [10, 99], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     The same test, one stage below. The filter gets the move from the sort above it and
    ///     sorts in the same order, thus its own change is also a move alone.
    /// </summary>
    [Test]
    public async Task StateCellOnAFilterOverASortedViewSeesAnUpdateThatMovesItsRow()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30));

        // Each item agrees with the predicate, thus the filter holds the full list of the sort and
        // has the same moves.
        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection
                .SortBy(static (_, state) => state.Score)
                .Filter(static (_, state) => state.Score > 0);

        List<int> scores = [];

        IListener l =
            passing.StateCell(1)
                .ListenStrong(state => scores.Add(state.Match(onSome: static s => s.Score, onNone: static () => -1)));

        edits.Send(TestUtil.Score(key: 1, score: 99));

        l.Unlisten();

        await Assert.That(TestUtil.Keys(passing.KeysCell.Sample()))
            .IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);

        await Assert.That(scores).IsEquivalentTo(expected: [10, 99], ordering: CollectionOrdering.Matching);
    }
}
