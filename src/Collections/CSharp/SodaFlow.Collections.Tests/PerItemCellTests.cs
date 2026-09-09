using System.Collections.Generic;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     The optional-valued surface, which is the C# wrapper's rather than the core's: the core
///     answers in <c>TryGet</c>s so that F# can put <c>option</c> on top instead.
/// </summary>
public sealed class PerItemCellTests
{
    private static ReactiveCollection<int, ItemId, ItemState> Create(
        Stream<CollectionEdit<int, ItemId, ItemState>> edits,
        params Entry<ItemId, ItemState>[] initial) =>
        ReactiveCollection<int, ItemId, ItemState>.Create(TestUtil.KeyOf, initial, edits);

    [Test]
    public async Task StateCellTracksOneKeyAcrossAddAndRemove()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(edits);

        // Built before the key exists, which is the point: a bound view can outlive its item, and
        // can be created before it.
        Cell<Maybe<ItemState>> stateCell = collection.StateCell(7);

        List<string> seen = [];
        IListener l = stateCell.Updates().ListenStrong(
            state => seen.Add(state.Match(static s => s.Name, static () => "gone")));

        await Assert.That(stateCell.Sample().Match(static _ => "some", static () => "none"))
            .IsEqualTo("none");

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

        ReactiveCollection<int, ItemId, ItemState> collection = Create(edits);

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
        await Assert.That(built!.Sample().Match(static s => s.Name, static () => "none"))
            .IsEqualTo("five");
    }

    [Test]
    public async Task StateCellIsSharedPerKeyWhileSomethingHoldsIt()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection =
            Create(edits, TestUtil.Item(1, "one", 10));

        Cell<Maybe<ItemState>> first = collection.StateCell(1);
        Cell<Maybe<ItemState>> second = collection.StateCell(1);

        await Assert.That(second).IsSameReferenceAs(first);
    }

    [Test]
    public async Task IdentityCellMovesOnlyOnStructuralChange()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection =
            Create(edits, TestUtil.Item(1, "one", 10));

        List<string> seen = [];
        IListener l = collection.IdentityCell(1).Updates().ListenStrong(
            identity => seen.Add(identity.Match(static i => i.Code, static () => "gone")));

        edits.Send(TestUtil.Score(1, 11));
        edits.Send(TestUtil.Remove(1));

        l.Unlisten();

        await Assert.That(seen).IsEquivalentTo(["gone"]);
    }

    [Test]
    public async Task ChangeForNestsMovedInsideStillPresent()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20));

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
        await Assert.That(change.ChangeFor(2).Match(static _ => "event", static () => "no event"))
            .IsEqualTo("no event");
    }

    [Test]
    public async Task LookupAndIndexOfAnswerWithMaybe()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20));

        CollectionSnapshot<int, ItemId, ItemState> snapshot = collection.SnapshotCell.Sample();

        await Assert.That(snapshot.Lookup(1).Match(static e => e.State.Name, static () => "?"))
            .IsEqualTo("one");
        await Assert.That(snapshot.Lookup(9).Match(static _ => "some", static () => "none"))
            .IsEqualTo("none");
        await Assert.That(snapshot.States.Lookup(2).Match(static s => s.Score, static () => -1))
            .IsEqualTo(20);

        IOrderedKeys<int, ItemId, ItemState> keys = collection.KeysCell.Sample();

        await Assert.That(keys.IndexOfMaybe(2).Match(static i => i, static () => -1)).IsEqualTo(1);
        await Assert.That(keys.IndexOfMaybe(9).Match(static _ => "some", static () => "none"))
            .IsEqualTo("none");
    }
}
