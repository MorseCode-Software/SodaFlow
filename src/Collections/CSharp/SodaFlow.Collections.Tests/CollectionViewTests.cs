using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

public sealed class CollectionViewTests
{
    private static ReactiveCollection<int, ItemId, ItemState> Create(
        Stream<CollectionEdit<int, ItemId, ItemState>> edits,
        params Entry<ItemId, ItemState>[] initial) =>
        ReactiveCollection<int, ItemId, ItemState>.Create(TestUtil.KeyOf, initial, edits);

    private static List<int> KeysOf(IReactiveCollection<int, ItemId, ItemState> view) =>
        TestUtil.Keys(view.KeysCell.Sample());

    /// <summary>An operation as "kind:key", which is what these tests assert on.</summary>
    private static string Describe(ViewOperation<int> operation) =>
        $"{operation.GetType().Name.Replace("`1", string.Empty)}:{operation.Key}";

    [Test]
    public async Task TheRootIsOrderedByKey()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(3, "three", 30),
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20));

        await Assert.That(KeysOf(collection)).IsEquivalentTo([1, 2, 3]);

        edits.Send(TestUtil.Add(TestUtil.Item(0, "zero", 0)));

        await Assert.That(KeysOf(collection)).IsEquivalentTo([0, 1, 2, 3]);
    }

    [Test]
    public async Task SortByOrdersByTheProjectedValueAndReFilesOnUpdate()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(3, "three", 20));

        IReactiveCollection<int, ItemId, ItemState> byScore =
            collection.SortBy(static (_, state) => state.Score);

        await Assert.That(KeysOf(byScore)).IsEquivalentTo([2, 3, 1]);

        // Moving item 1 to the bottom of the range re-files it rather than rebuilding.
        edits.Send(TestUtil.Score(1, 5));

        await Assert.That(KeysOf(byScore)).IsEquivalentTo([1, 2, 3]);
    }

    [Test]
    public async Task SortByDescendingReversesTheOrder()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(3, "three", 20));

        IReactiveCollection<int, ItemId, ItemState> byScore =
            collection.SortByDescending(static (_, state) => state.Score);

        await Assert.That(KeysOf(byScore)).IsEquivalentTo([1, 3, 2]);
    }

    [Test]
    public async Task AReFilingUpdateReportsAMoveAndAnUpdate()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30));

        IReactiveCollection<int, ItemId, ItemState> byScore =
            collection.SortBy(static (_, state) => state.Score);

        List<string> operations = [];
        IListener l = byScore.ChangesStream.ListenStrong(
            change => operations.AddRange(change.Operations.Select(Describe)));

        edits.Send(TestUtil.Score(1, 99));

        l.Unlisten();

        await Assert.That(operations).IsEquivalentTo(["ViewMove:1", "ViewUpdate:1"]);
        await Assert.That(KeysOf(byScore)).IsEquivalentTo([2, 3, 1]);
    }

    [Test]
    public async Task AnUpdateThatDoesNotMoveAnythingIsStillReported()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20));

        List<string> operations = [];
        IListener l = collection.ChangesStream.ListenStrong(
            change => operations.AddRange(change.Operations.Select(Describe)));

        // The root orders by key, so this moves nothing - but a stage below might sort on exactly
        // the state that just changed, so it has to hear about it.
        edits.Send(TestUtil.Score(1, 99));

        l.Unlisten();

        await Assert.That(operations).IsEquivalentTo(["ViewUpdate:1"]);
    }

    [Test]
    public async Task FilterNarrowsAndPreservesTheUpstreamOrder()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(3, "three", 20),
            TestUtil.Item(4, "four", 40));

        IReactiveCollection<int, ItemId, ItemState> passing = collection
            .SortBy(static (_, state) => state.Score)
            .Filter(static (_, state) => state.Score >= 20);

        await Assert.That(KeysOf(passing)).IsEquivalentTo([3, 1, 4]);
    }

    [Test]
    public async Task AnUpdateCanMoveAnItemIntoAndOutOfAFilter()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 30));

        IReactiveCollection<int, ItemId, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 20);

        await Assert.That(KeysOf(passing)).IsEquivalentTo([2]);

        edits.Send(TestUtil.Score(1, 25));
        await Assert.That(KeysOf(passing)).IsEquivalentTo([1, 2]);

        edits.Send(TestUtil.Score(2, 5));
        await Assert.That(KeysOf(passing)).IsEquivalentTo([1]);
    }

    [Test]
    public async Task ChangingThePredicateRebuildsTheStageAndReportsAReset()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();
        CellSink<int> threshold = Cell.CreateSink(20);

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30));

        IReactiveCollection<int, ItemId, ItemState> passing = collection.Filter(
            threshold,
            static (limit, _, state) => state.Score >= limit);

        await Assert.That(KeysOf(passing)).IsEquivalentTo([2, 3]);

        List<bool> resets = [];
        IListener l = passing.ChangesStream.ListenStrong(change => resets.Add(change.IsReset));

        threshold.Send(5);

        l.Unlisten();

        await Assert.That(resets).IsEquivalentTo([true]);
        await Assert.That(KeysOf(passing)).IsEquivalentTo([1, 2, 3]);
    }

    [Test]
    public async Task TakeWindowsTheUpstream()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30),
            TestUtil.Item(4, "four", 40));

        IReactiveCollection<int, ItemId, ItemState> topTwo = collection
            .SortByDescending(static (_, state) => state.Score)
            .Take(2);

        await Assert.That(KeysOf(topTwo)).IsEquivalentTo([4, 3]);

        // A new item at the top pushes the last one out of the window.
        edits.Send(TestUtil.Add(TestUtil.Item(5, "five", 50)));

        await Assert.That(KeysOf(topTwo)).IsEquivalentTo([5, 4]);
    }

    [Test]
    public async Task TakeFollowsAChangingLimit()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();
        CellSink<int> limit = Cell.CreateSink(1);

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30));

        IReactiveCollection<int, ItemId, ItemState> window = collection.Take(limit);

        await Assert.That(KeysOf(window)).IsEquivalentTo([1]);

        limit.Send(2);

        await Assert.That(KeysOf(window)).IsEquivalentTo([1, 2]);
    }

    [Test]
    public async Task AChainRunsInTheOrderItIsWritten()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 50),
            TestUtil.Item(2, "two", 40),
            TestUtil.Item(3, "three", 30),
            TestUtil.Item(4, "four", 20),
            TestUtil.Item(5, "five", 10));

        IReactiveCollection<int, ItemId, ItemState> topTwoOfTheEvens = collection
            .SortByDescending(static (_, state) => state.Score)
            .Filter(static (identity, _) => identity.Number % 2 == 0)
            .Take(2);

        await Assert.That(KeysOf(topTwoOfTheEvens)).IsEquivalentTo([2, 4]);

        // The filter sits above the window, so an odd item scoring highest changes nothing here.
        edits.Send(TestUtil.Score(1, 99));

        await Assert.That(KeysOf(topTwoOfTheEvens)).IsEquivalentTo([2, 4]);
    }

    [Test]
    public async Task AViewSharesTheStoreWithItsRoot()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20));

        IReactiveCollection<int, ItemId, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 20);

        // The same cell, not an equal one: sharing is what falls out of a view never copying.
        await Assert.That(passing.StateCell(1)).IsSameReferenceAs(collection.StateCell(1));

        // And it answers for the store rather than for membership, so a key the view filtered out
        // still has its state.
        await Assert.That(passing.StateCell(1).Sample().Match(static s => s.Name, static () => "none"))
            .IsEqualTo("one");
        await Assert.That(passing.KeysCell.Sample().Contains(1)).IsFalse();
    }

    [Test]
    public async Task RemovingAnItemDropsItFromEveryStageOfTheChain()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30));

        IReactiveCollection<int, ItemId, ItemState> passing = collection
            .SortByDescending(static (_, state) => state.Score)
            .Filter(static (_, state) => state.Score >= 20);

        await Assert.That(KeysOf(passing)).IsEquivalentTo([3, 2]);
        await Assert.That(passing.IdentityCell(3).Sample().Match(static i => i.Code, static () => "gone"))
            .IsEqualTo("C3");

        edits.Send(TestUtil.Remove(3));

        await Assert.That(KeysOf(passing)).IsEquivalentTo([2]);
        await Assert.That(passing.IdentityCell(3).Sample().Match(static i => i.Code, static () => "gone"))
            .IsEqualTo("gone");
    }

    [Test]
    public async Task SwitchFollowsWhicheverViewTheCellHolds()
    {
        StreamSink<CollectionEdit<int, ItemId, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemId, ItemState>>();

        ReactiveCollection<int, ItemId, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(3, "three", 20));

        IReactiveCollection<int, ItemId, ItemState> byScore =
            collection.SortBy(static (_, state) => state.Score);
        IReactiveCollection<int, ItemId, ItemState> byName =
            collection.SortBy(static (_, state) => state.Name);

        CellSink<IReactiveCollection<int, ItemId, ItemState>> which = Cell.CreateSink(byScore);
        IReactiveCollection<int, ItemId, ItemState> switched = collection.Switch(which);

        await Assert.That(KeysOf(switched)).IsEquivalentTo([2, 3, 1]);

        List<bool> resets = [];
        IListener l = switched.ChangesStream.ListenStrong(change => resets.Add(change.IsReset));

        which.Send(byName);

        l.Unlisten();

        // Switching is itself a reset: every position potentially differs.
        await Assert.That(resets).IsEquivalentTo([true]);
        await Assert.That(KeysOf(switched)).IsEquivalentTo([1, 3, 2]);
    }
}
