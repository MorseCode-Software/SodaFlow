using System.Collections.Generic;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     The optional-valued surface, which is the C# wrapper's rather than the core's: the core
///     answers in <c>TryGet</c>s so that F# can put <c>option</c> on top instead.
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

        // Built before the key exists, which is the point: a bound view can outlive its item, and
        // can be created before it.
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

        // A row constructed in response to the structural change that created its item. By the time
        // this runs the change stream has already fired, so the lazy seed is all the cell has - an
        // eager sample would read the pre-transaction snapshot and sit at no value.
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

        // Removed: it moved, and it is not present afterwards.
        await Assert.That(
                change.ChangeFor(1)
                    .Match(
                        onSome: static inner => inner.Match(onSome: static _ => "some", onNone: static () => "none"),
                        onNone: static () => "no event"))
            .IsEqualTo("none");

        // Untouched: no event for an observer of this key at all.
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
}
