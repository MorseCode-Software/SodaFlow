using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

public sealed class CollectionViewTests
{
    private static ReactiveCollection<int, ItemIdentity, ItemState> Create(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        params Item<ItemIdentity, ItemState>[] initial) =>
        ReactiveCollection<int, ItemIdentity, ItemState>.Create(TestUtil.KeyOf, initial, edits);

    private static List<int> KeysOf(IReactiveCollection<int, ItemIdentity, ItemState> view) =>
        TestUtil.Keys(view.KeysCell.Sample());

    /// <summary>An operation as "kind:key", which is what these tests assert on.</summary>
    private static string Describe(ViewOperation<int> operation) =>
        $"{operation.GetType().Name.Replace("`1", string.Empty)}:{operation.Key}";

    [Test]
    public async Task TheRootIsOrderedByKey()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
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
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(3, "three", 20));

        IReactiveCollection<int, ItemIdentity, ItemState> byScore =
            collection.SortBy(static (_, state) => state.Score);

        await Assert.That(KeysOf(byScore)).IsEquivalentTo([2, 3, 1]);

        // Moving item 1 to the bottom of the range re-files it rather than rebuilding.
        edits.Send(TestUtil.Score(1, 5));

        await Assert.That(KeysOf(byScore)).IsEquivalentTo([1, 2, 3]);
    }

    [Test]
    public async Task SortByDescendingReversesTheOrder()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(3, "three", 20));

        IReactiveCollection<int, ItemIdentity, ItemState> byScore =
            collection.SortByDescending(static (_, state) => state.Score);

        await Assert.That(KeysOf(byScore)).IsEquivalentTo([1, 3, 2]);
    }

    [Test]
    public async Task AReFilingUpdateReportsAMoveAndAnUpdate()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30));

        IReactiveCollection<int, ItemIdentity, ItemState> byScore =
            collection.SortBy(static (_, state) => state.Score);

        List<string> operations = [];
        IListener l = byScore.KeyChangesStream.ListenStrong(
            change => operations.AddRange(change.Operations.Select(Describe)));

        edits.Send(TestUtil.Score(1, 99));

        l.Unlisten();

        await Assert.That(operations).IsEquivalentTo(["ViewMove:1", "ViewUpdate:1"]);
        await Assert.That(KeysOf(byScore)).IsEquivalentTo([2, 3, 1]);
    }

    [Test]
    public async Task AnUpdateThatDoesNotMoveAnythingIsStillReported()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20));

        List<string> operations = [];
        IListener l = collection.KeyChangesStream.ListenStrong(
            change => operations.AddRange(change.Operations.Select(Describe)));

        // The root orders by key, so this moves nothing - but a stage below might sort on exactly
        // the state that just changed, so it has to hear about it.
        edits.Send(TestUtil.Score(1, 99));

        l.Unlisten();

        await Assert.That(operations).IsEquivalentTo(["ViewUpdate:1"]);
    }

    [Test]
    public async Task AnUpdateUnderAKeyOrderedStageReportsAnUpdateAndMovesNothing()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30));

        // Sitting directly on the collection, so this stage inherits the root's order, which
        // projects the key - and a key cannot change. A state edit therefore cannot move anything
        // here, which is the case Refile short-circuits rather than removing and re-adding.
        IReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 0);

        List<string> operations = [];
        IListener l = passing.KeyChangesStream.ListenStrong(
            change => operations.AddRange(change.Operations.Select(Describe)));

        edits.Send(TestUtil.Score(2, 99));

        l.Unlisten();

        // One update, no move, and the order untouched.
        await Assert.That(operations).IsEquivalentTo(["ViewUpdate:2"]);
        await Assert.That(KeysOf(passing)).IsEquivalentTo([1, 2, 3]);
    }

    [Test]
    public async Task FilterNarrowsAndPreservesTheUpstreamOrder()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(3, "three", 20),
            TestUtil.Item(4, "four", 40));

        IReactiveCollection<int, ItemIdentity, ItemState> passing = collection
            .SortBy(static (_, state) => state.Score)
            .Filter(static (_, state) => state.Score >= 20);

        await Assert.That(KeysOf(passing)).IsEquivalentTo([3, 1, 4]);
    }

    [Test]
    public async Task AnUpdateCanMoveAnItemIntoAndOutOfAFilter()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 30));

        IReactiveCollection<int, ItemIdentity, ItemState> passing =
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
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();
        CellSink<int> threshold = Cell.CreateSink(20);

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30));

        IReactiveCollection<int, ItemIdentity, ItemState> passing = collection.Filter(
            threshold,
            static (limit, _, state) => state.Score >= limit);

        await Assert.That(KeysOf(passing)).IsEquivalentTo([2, 3]);

        List<bool> resets = [];
        IListener l = passing.KeyChangesStream.ListenStrong(change => resets.Add(change.IsReset));

        threshold.Send(5);

        l.Unlisten();

        await Assert.That(resets).IsEquivalentTo([true]);
        await Assert.That(KeysOf(passing)).IsEquivalentTo([1, 2, 3]);
    }

    [Test]
    public async Task FilterByIdentityNarrowsAndDoesNotReTestOnAStateEdit()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30),
            TestUtil.Item(4, "four", 40));

        IReactiveCollection<int, ItemIdentity, ItemState> evens =
            collection.FilterByIdentity(static identity => identity.Number % 2 == 0);

        await Assert.That(KeysOf(evens)).IsEquivalentTo([2, 4]);

        List<string> operations = [];
        IListener l = evens.KeyChangesStream.ListenStrong(
            change => operations.AddRange(change.Operations.Select(Describe)));

        // In the view: a score change cannot move it out, so this reports the update and nothing
        // else - the membership was never in question.
        edits.Send(TestUtil.Score(2, -1));

        // Not in the view: an update for a key this filter does not hold reports nothing at all.
        edits.Send(TestUtil.Score(1, -1));

        l.Unlisten();

        await Assert.That(operations).IsEquivalentTo(["ViewUpdate:2"]);
        await Assert.That(KeysOf(evens)).IsEquivalentTo([2, 4]);
    }

    [Test]
    public async Task FilterByIdentityStillFollowsStructuralChange()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(edits, TestUtil.Item(2, "two", 20));

        IReactiveCollection<int, ItemIdentity, ItemState> evens =
            collection.FilterByIdentity(static identity => identity.Number % 2 == 0);

        await Assert.That(KeysOf(evens)).IsEquivalentTo([2]);

        // An identity arriving is the one thing that can change this membership, and it is tested.
        edits.Send(TestUtil.Add(TestUtil.Item(4, "four", 40)));
        await Assert.That(KeysOf(evens)).IsEquivalentTo([2, 4]);

        edits.Send(TestUtil.Add(TestUtil.Item(5, "five", 50)));
        await Assert.That(KeysOf(evens)).IsEquivalentTo([2, 4]);

        edits.Send(TestUtil.Remove(2));
        await Assert.That(KeysOf(evens)).IsEquivalentTo([4]);
    }

    [Test]
    public async Task FilterByIdentityComposesWithSortByIdentity()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30),
            TestUtil.Item(4, "four", 40));

        // Neither stage reads the state, so nothing a state edit does can reach either of them.
        IReactiveCollection<int, ItemIdentity, ItemState> view = collection
            .FilterByIdentity(static identity => identity.Number % 2 == 0)
            .SortByIdentityDescending(static identity => identity.Number);

        await Assert.That(KeysOf(view)).IsEquivalentTo([4, 2]);

        edits.Send(TestUtil.Score(4, -1000));

        await Assert.That(KeysOf(view)).IsEquivalentTo([4, 2]);
    }

    [Test]
    public async Task SortByIdentityOrdersByTheIdentityAndDoesNotReFileOnAStateEdit()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30));

        // Codes are "C1", "C2", "C3", so descending by code is descending by number here.
        IReactiveCollection<int, ItemIdentity, ItemState> byCode =
            collection.SortByIdentityDescending(static identity => identity.Code);

        await Assert.That(KeysOf(byCode)).IsEquivalentTo([3, 2, 1]);

        List<string> operations = [];
        IListener l = byCode.KeyChangesStream.ListenStrong(
            change => operations.AddRange(change.Operations.Select(Describe)));

        // A score change cannot touch a code, so this must report the update and move nothing -
        // which is the licence the identity-only selector buys.
        edits.Send(TestUtil.Score(3, -99));

        l.Unlisten();

        await Assert.That(operations).IsEquivalentTo(["ViewUpdate:3"]);
        await Assert.That(KeysOf(byCode)).IsEquivalentTo([3, 2, 1]);
    }

    [Test]
    public async Task SortByIdentityStillFollowsStructuralChange()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(3, "three", 30));

        IReactiveCollection<int, ItemIdentity, ItemState> byCode =
            collection.SortByIdentity(static identity => identity.Code);

        await Assert.That(KeysOf(byCode)).IsEquivalentTo([1, 3]);

        // An identity arriving or leaving is exactly what this order does follow.
        edits.Send(TestUtil.Add(TestUtil.Item(2, "two", 20)));
        await Assert.That(KeysOf(byCode)).IsEquivalentTo([1, 2, 3]);

        edits.Send(TestUtil.Remove(1));
        await Assert.That(KeysOf(byCode)).IsEquivalentTo([2, 3]);
    }

    [Test]
    public async Task TakeWindowsTheUpstream()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30),
            TestUtil.Item(4, "four", 40));

        IReactiveCollection<int, ItemIdentity, ItemState> topTwo = collection
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
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();
        CellSink<int> limit = Cell.CreateSink(1);

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30));

        IReactiveCollection<int, ItemIdentity, ItemState> window = collection.Take(limit);

        await Assert.That(KeysOf(window)).IsEquivalentTo([1]);

        limit.Send(2);

        await Assert.That(KeysOf(window)).IsEquivalentTo([1, 2]);
    }

    [Test]
    public async Task SliceWindowsTheMiddleOfTheUpstream()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30),
            TestUtil.Item(4, "four", 40),
            TestUtil.Item(5, "five", 50));

        IReactiveCollection<int, ItemIdentity, ItemState> page = collection.Slice(1, 2);

        await Assert.That(KeysOf(page)).IsEquivalentTo([2, 3]);

        // Asserted by position as well as by content, because which keys land in the window is the
        // whole of what an offset does and a set comparison would not see it move.
        await Assert.That(KeysOf(page)[0]).IsEqualTo(2);
        await Assert.That(KeysOf(page)[1]).IsEqualTo(3);

        // A new key below the window shifts everything down one, so the window holds different
        // items without its bounds having changed.
        edits.Send(TestUtil.Add(TestUtil.Item(0, "zero", 5)));

        await Assert.That(KeysOf(page)).IsEquivalentTo([1, 2]);
    }

    [Test]
    public async Task SliceFollowsAChangingOffset()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();
        CellSink<int> offset = Cell.CreateSink(0);

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30),
            TestUtil.Item(4, "four", 40),
            TestUtil.Item(5, "five", 50));

        IReactiveCollection<int, ItemIdentity, ItemState> page =
            collection.Slice(offset, Cell.Constant(2));

        await Assert.That(KeysOf(page)).IsEquivalentTo([1, 2]);

        // Turning the page is one send.
        offset.Send(2);

        await Assert.That(KeysOf(page)).IsEquivalentTo([3, 4]);

        // The last page is short rather than padded, and an offset past the end is empty rather
        // than an error.
        offset.Send(4);

        await Assert.That(KeysOf(page)).IsEquivalentTo([5]);

        offset.Send(99);

        await Assert.That(KeysOf(page)).IsEmpty();
    }

    [Test]
    public async Task SliceComposesWithASortAbove()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30),
            TestUtil.Item(4, "four", 40));

        // Scores descending are 4, 3, 2, 1 - so the second page of two is keys 2 and 1.
        IReactiveCollection<int, ItemIdentity, ItemState> page = collection
            .SortByDescending(static (_, state) => state.Score)
            .Slice(2, 2);

        await Assert.That(KeysOf(page)[0]).IsEqualTo(2);
        await Assert.That(KeysOf(page)[1]).IsEqualTo(1);
    }

    [Test]
    public async Task TakeIsASliceFromZero()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30));

        IReactiveCollection<int, ItemIdentity, ItemState> taken = collection.Take(2);
        IReactiveCollection<int, ItemIdentity, ItemState> sliced = collection.Slice(0, 2);

        await Assert.That(KeysOf(sliced)).IsEquivalentTo(KeysOf(taken));

        edits.Send(TestUtil.Add(TestUtil.Item(0, "zero", 5)));

        await Assert.That(KeysOf(sliced)).IsEquivalentTo(KeysOf(taken));
    }

    [Test]
    public async Task AChainRunsInTheOrderItIsWritten()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 50),
            TestUtil.Item(2, "two", 40),
            TestUtil.Item(3, "three", 30),
            TestUtil.Item(4, "four", 20),
            TestUtil.Item(5, "five", 10));

        IReactiveCollection<int, ItemIdentity, ItemState> topTwoOfTheEvens = collection
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
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20));

        IReactiveCollection<int, ItemIdentity, ItemState> passing =
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
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 10),
            TestUtil.Item(2, "two", 20),
            TestUtil.Item(3, "three", 30));

        IReactiveCollection<int, ItemIdentity, ItemState> passing = collection
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
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(
            edits,
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(3, "three", 20));

        IReactiveCollection<int, ItemIdentity, ItemState> byScore =
            collection.SortBy(static (_, state) => state.Score);
        IReactiveCollection<int, ItemIdentity, ItemState> byName =
            collection.SortBy(static (_, state) => state.Name);

        CellSink<IReactiveCollection<int, ItemIdentity, ItemState>> which = Cell.CreateSink(byScore);
        IReactiveCollection<int, ItemIdentity, ItemState> switched = collection.Switch(which);

        await Assert.That(KeysOf(switched)).IsEquivalentTo([2, 3, 1]);

        List<bool> resets = [];
        IListener l = switched.KeyChangesStream.ListenStrong(change => resets.Add(change.IsReset));

        which.Send(byName);

        l.Unlisten();

        // Switching is itself a reset: every position potentially differs.
        await Assert.That(resets).IsEquivalentTo([true]);
        await Assert.That(KeysOf(switched)).IsEquivalentTo([1, 3, 2]);
    }
}
