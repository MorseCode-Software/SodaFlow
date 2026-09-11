using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

public sealed class CollectionViewTests
{
    private static ReactiveCollection<int, ItemIdentity, ItemState> Create(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        params Item<ItemIdentity, ItemState>[] initial) =>
        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: TestUtil.KeyOf,
            initialItems: initial,
            edits);

    private static List<int> KeysOf(ReactiveCollection<int, ItemIdentity, ItemState> view) =>
        TestUtil.Keys(view.KeysCell.Sample());

    /// <summary>An operation as "kind:key", which is what these tests assert on.</summary>
    private static string Describe(ViewOperation<int> operation) =>
        $"{operation.GetType().Name.Replace(oldValue: "`1", newValue: string.Empty)}:{operation.Key}";

    [Test]
    public async Task TheRootIsOrderedByKey()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        await Assert.That(KeysOf(collection)).IsEquivalentTo([1, 2, 3]);

        edits.Send(TestUtil.Add(TestUtil.Item(number: 0, name: "zero", score: 0)));

        await Assert.That(KeysOf(collection)).IsEquivalentTo([0, 1, 2, 3]);
    }

    [Test]
    public async Task SortByOrdersByTheProjectedValueAndReFilesOnUpdate()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 30),
                TestUtil.Item(number: 2, name: "two", score: 10),
                TestUtil.Item(number: 3, name: "three", score: 20));

        ReactiveCollection<int, ItemIdentity, ItemState> byScore =
            collection.SortBy(static (_, state) => state.Score);

        await Assert.That(KeysOf(byScore)).IsEquivalentTo([2, 3, 1]);

        // Moving item 1 to the bottom of the range re-files it rather than rebuilding.
        edits.Send(TestUtil.Score(key: 1, score: 5));

        await Assert.That(KeysOf(byScore)).IsEquivalentTo([1, 2, 3]);
    }

    [Test]
    public async Task SortByDescendingReversesTheOrder()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 30),
                TestUtil.Item(number: 2, name: "two", score: 10),
                TestUtil.Item(number: 3, name: "three", score: 20));

        ReactiveCollection<int, ItemIdentity, ItemState> byScore =
            collection.SortByDescending(static (_, state) => state.Score);

        await Assert.That(KeysOf(byScore)).IsEquivalentTo([1, 3, 2]);
    }

    [Test]
    public async Task AReFilingUpdateReportsAMoveAndAnUpdate()
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

        List<string> operations = [];

        IListener l =
            byScore.KeyChangesStream.ListenStrong(change => operations.AddRange(change.Operations.Select(Describe)));

        edits.Send(TestUtil.Score(key: 1, score: 99));

        l.Unlisten();

        await Assert.That(operations).IsEquivalentTo(["ViewMove:1", "ViewUpdate:1"]);
        await Assert.That(KeysOf(byScore)).IsEquivalentTo([2, 3, 1]);
    }

    [Test]
    public async Task AnUpdateThatDoesNotMoveAnythingIsStillReported()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        List<string> operations = [];

        IListener l =
            collection.KeyChangesStream.ListenStrong(change => operations.AddRange(change.Operations.Select(Describe)));

        // The root orders by key, so this moves nothing - but a stage below might sort on exactly
        // the state that just changed, so it has to hear about it.
        edits.Send(TestUtil.Score(key: 1, score: 99));

        l.Unlisten();

        await Assert.That(operations).IsEquivalentTo(["ViewUpdate:1"]);
    }

    [Test]
    public async Task AnUpdateUnderAKeyOrderedStageReportsAnUpdateAndMovesNothing()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30));

        // Sitting directly on the collection, so this stage inherits the root's order, which
        // projects the key - and a key cannot change. A state edit therefore cannot move anything
        // here, which is the case Refile short-circuits rather than removing and re-adding.
        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 0);

        List<string> operations = [];

        IListener l =
            passing.KeyChangesStream.ListenStrong(change => operations.AddRange(change.Operations.Select(Describe)));

        edits.Send(TestUtil.Score(key: 2, score: 99));

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

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 30),
                TestUtil.Item(number: 2, name: "two", score: 10),
                TestUtil.Item(number: 3, name: "three", score: 20),
                TestUtil.Item(number: 4, name: "four", score: 40));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection
                .SortBy(static (_, state) => state.Score)
                .Filter(static (_, state) => state.Score >= 20);

        await Assert.That(KeysOf(passing)).IsEquivalentTo([3, 1, 4]);
    }

    [Test]
    public async Task AnUpdateCanMoveAnItemIntoAndOutOfAFilter()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 30));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 20);

        await Assert.That(KeysOf(passing)).IsEquivalentTo([2]);

        edits.Send(TestUtil.Score(key: 1, score: 25));
        await Assert.That(KeysOf(passing)).IsEquivalentTo([1, 2]);

        edits.Send(TestUtil.Score(key: 2, score: 5));
        await Assert.That(KeysOf(passing)).IsEquivalentTo([1]);
    }

    [Test]
    public async Task ChangingThePredicateRebuildsTheStageAndReportsAReset()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<int> threshold = Cell.CreateSink(20);

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.Filter(
                criteriaCell: threshold,
                predicate: static (limit, _, state) => state.Score >= limit);

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

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 4, name: "four", score: 40));

        ReactiveCollection<int, ItemIdentity, ItemState> evens =
            collection.FilterByIdentity(static identity => identity.Number % 2 == 0);

        await Assert.That(KeysOf(evens)).IsEquivalentTo([2, 4]);

        List<string> operations = [];

        IListener l =
            evens.KeyChangesStream.ListenStrong(change => operations.AddRange(change.Operations.Select(Describe)));

        // In the view: a score change cannot move it out, so this reports the update and nothing
        // else - the membership was never in question.
        edits.Send(TestUtil.Score(key: 2, score: -1));

        // Not in the view: an update for a key this filter does not hold reports nothing at all.
        edits.Send(TestUtil.Score(key: 1, score: -1));

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
            Create(edits: edits, TestUtil.Item(number: 2, name: "two", score: 20));

        ReactiveCollection<int, ItemIdentity, ItemState> evens =
            collection.FilterByIdentity(static identity => identity.Number % 2 == 0);

        await Assert.That(KeysOf(evens)).IsEquivalentTo([2]);

        // An identity arriving is the one thing that can change this membership, and it is tested.
        edits.Send(TestUtil.Add(TestUtil.Item(number: 4, name: "four", score: 40)));
        await Assert.That(KeysOf(evens)).IsEquivalentTo([2, 4]);

        edits.Send(TestUtil.Add(TestUtil.Item(number: 5, name: "five", score: 50)));
        await Assert.That(KeysOf(evens)).IsEquivalentTo([2, 4]);

        edits.Send(TestUtil.Remove(2));
        await Assert.That(KeysOf(evens)).IsEquivalentTo([4]);
    }

    [Test]
    public async Task FilterByIdentityComposesWithSortByIdentity()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 4, name: "four", score: 40));

        // Neither stage reads the state, so nothing a state edit does can reach either of them.
        ReactiveCollection<int, ItemIdentity, ItemState> view =
            collection
                .FilterByIdentity(static identity => identity.Number % 2 == 0)
                .SortByIdentityDescending(static identity => identity.Number);

        await Assert.That(KeysOf(view)).IsEquivalentTo([4, 2]);

        edits.Send(TestUtil.Score(key: 4, score: -1000));

        await Assert.That(KeysOf(view)).IsEquivalentTo([4, 2]);
    }

    [Test]
    public async Task SortByIdentityOrdersByTheIdentityAndDoesNotReFileOnAStateEdit()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30));

        // Codes are "C1", "C2", "C3", so descending by code is descending by number here.
        ReactiveCollection<int, ItemIdentity, ItemState> byCode =
            collection.SortByIdentityDescending(static identity => identity.Code);

        await Assert.That(KeysOf(byCode)).IsEquivalentTo([3, 2, 1]);

        List<string> operations = [];

        IListener l =
            byCode.KeyChangesStream.ListenStrong(change => operations.AddRange(change.Operations.Select(Describe)));

        // A score change cannot touch a code, so this must report the update and move nothing -
        // which is the licence the identity-only selector buys.
        edits.Send(TestUtil.Score(key: 3, score: -99));

        l.Unlisten();

        await Assert.That(operations).IsEquivalentTo(["ViewUpdate:3"]);
        await Assert.That(KeysOf(byCode)).IsEquivalentTo([3, 2, 1]);
    }

    [Test]
    public async Task SortByIdentityStillFollowsStructuralChange()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 3, name: "three", score: 30));

        ReactiveCollection<int, ItemIdentity, ItemState> byCode =
            collection.SortByIdentity(static identity => identity.Code);

        await Assert.That(KeysOf(byCode)).IsEquivalentTo([1, 3]);

        // An identity arriving or leaving is exactly what this order does follow.
        edits.Send(TestUtil.Add(TestUtil.Item(number: 2, name: "two", score: 20)));
        await Assert.That(KeysOf(byCode)).IsEquivalentTo([1, 2, 3]);

        edits.Send(TestUtil.Remove(1));
        await Assert.That(KeysOf(byCode)).IsEquivalentTo([2, 3]);
    }

    [Test]
    public async Task TakeWindowsTheUpstream()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 4, name: "four", score: 40));

        ReactiveCollection<int, ItemIdentity, ItemState> topTwo =
            collection
                .SortByDescending(static (_, state) => state.Score)
                .Take(2);

        await Assert.That(KeysOf(topTwo)).IsEquivalentTo([4, 3]);

        // A new item at the top pushes the last one out of the window.
        edits.Send(TestUtil.Add(TestUtil.Item(number: 5, name: "five", score: 50)));

        await Assert.That(KeysOf(topTwo)).IsEquivalentTo([5, 4]);
    }

    [Test]
    public async Task TakeFollowsAChangingLimit()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<int> limit = Cell.CreateSink(1);

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30));

        ReactiveCollection<int, ItemIdentity, ItemState> window = collection.Take(limit);

        await Assert.That(KeysOf(window)).IsEquivalentTo([1]);

        limit.Send(2);

        await Assert.That(KeysOf(window)).IsEquivalentTo([1, 2]);
    }

    [Test]
    public async Task SliceWindowsTheMiddleOfTheUpstream()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 4, name: "four", score: 40),
                TestUtil.Item(number: 5, name: "five", score: 50));

        ReactiveCollection<int, ItemIdentity, ItemState> page = collection.Slice(offset: 1, limit: 2);

        await Assert.That(KeysOf(page)).IsEquivalentTo([2, 3]);

        // Asserted by position as well as by content, because which keys land in the window is the
        // whole of what an offset does and a set comparison would not see it move.
        await Assert.That(KeysOf(page)[0]).IsEqualTo(2);
        await Assert.That(KeysOf(page)[1]).IsEqualTo(3);

        // A new key below the window shifts everything down one, so the window holds different
        // items without its bounds having changed.
        edits.Send(TestUtil.Add(TestUtil.Item(number: 0, name: "zero", score: 5)));

        await Assert.That(KeysOf(page)).IsEquivalentTo([1, 2]);
    }

    [Test]
    public async Task SliceFollowsAChangingOffset()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<int> offset = Cell.CreateSink(0);

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 4, name: "four", score: 40),
                TestUtil.Item(number: 5, name: "five", score: 50));

        ReactiveCollection<int, ItemIdentity, ItemState> page =
            collection.Slice(offsetCell: offset, limitCell: Cell.Constant(2));

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

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 4, name: "four", score: 40));

        // Scores descending are 4, 3, 2, 1 - so the second page of two is keys 2 and 1.
        ReactiveCollection<int, ItemIdentity, ItemState> page =
            collection
                .SortByDescending(static (_, state) => state.Score)
                .Slice(offset: 2, limit: 2);

        await Assert.That(KeysOf(page)[0]).IsEqualTo(2);
        await Assert.That(KeysOf(page)[1]).IsEqualTo(1);
    }

    [Test]
    public async Task TakeIsASliceFromZero()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30));

        ReactiveCollection<int, ItemIdentity, ItemState> taken = collection.Take(2);
        ReactiveCollection<int, ItemIdentity, ItemState> sliced = collection.Slice(offset: 0, limit: 2);

        await Assert.That(KeysOf(sliced)).IsEquivalentTo(KeysOf(taken));

        edits.Send(TestUtil.Add(TestUtil.Item(number: 0, name: "zero", score: 5)));

        await Assert.That(KeysOf(sliced)).IsEquivalentTo(KeysOf(taken));
    }

    [Test]
    public async Task AChainRunsInTheOrderItIsWritten()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 50),
                TestUtil.Item(number: 2, name: "two", score: 40),
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 4, name: "four", score: 20),
                TestUtil.Item(number: 5, name: "five", score: 10));

        ReactiveCollection<int, ItemIdentity, ItemState> topTwoOfTheEvens =
            collection
                .SortByDescending(static (_, state) => state.Score)
                .Filter(static (identity, _) => identity.Number % 2 == 0)
                .Take(2);

        await Assert.That(KeysOf(topTwoOfTheEvens)).IsEquivalentTo([2, 4]);

        // The filter sits above the window, so an odd item scoring highest changes nothing here.
        edits.Send(TestUtil.Score(key: 1, score: 99));

        await Assert.That(KeysOf(topTwoOfTheEvens)).IsEquivalentTo([2, 4]);
    }

    [Test]
    public async Task AViewsItemCellAnswersForTheView()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 20);

        // Two answers, so two cells. This asserted the opposite until a view became a collection
        // rather than a window onto one.
        await Assert.That(passing.StateCell(1)).IsNotSameReferenceAs(collection.StateCell(1));

        // The view has no value for a key it does not hold; the collection still does.
        await Assert.That(passing.StateCell(1).Sample().Match(onSome: static s => s.Name, onNone: static () => "none"))
            .IsEqualTo("none");

        await Assert.That(
                collection.StateCell(1).Sample().Match(onSome: static s => s.Name, onNone: static () => "none"))
            .IsEqualTo("one");

        await Assert.That(passing.KeysCell.Sample().Contains(1)).IsFalse();

        // Sharing still falls out of never copying - within one view, which is where it means
        // something.
        await Assert.That(passing.StateCell(2)).IsSameReferenceAs(passing.StateCell(2));
    }

    [Test]
    public async Task AViewsItemCellFollowsTheKeyInAndOutOfTheView()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 20);

        Cell<Maybe<ItemState>> cell = passing.StateCell(1);

        await Assert.That(cell.Sample().Match(onSome: static s => s.Name, onNone: static () => "none"))
            .IsEqualTo("none");

        // Scoring it into the view gives the cell a value.
        edits.Send(TestUtil.Score(key: 1, score: 99));

        await Assert.That(cell.Sample().Match(onSome: static s => s.Name, onNone: static () => "none"))
            .IsEqualTo("one");

        // A later edit while it is in the view reaches the cell.
        edits.Send(TestUtil.Rename(key: 1, name: "renamed"));

        await Assert.That(cell.Sample().Match(onSome: static s => s.Name, onNone: static () => "none"))
            .IsEqualTo("renamed");

        // And scoring it back out takes the value away again.
        edits.Send(TestUtil.Score(key: 1, score: 1));

        await Assert.That(cell.Sample().Match(onSome: static s => s.Name, onNone: static () => "none"))
            .IsEqualTo("none");
    }

    [Test]
    public async Task RemovingAnItemDropsItFromEveryStageOfTheChain()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection
                .SortByDescending(static (_, state) => state.Score)
                .Filter(static (_, state) => state.Score >= 20);

        await Assert.That(KeysOf(passing)).IsEquivalentTo([3, 2]);

        await Assert.That(
                passing.IdentityCell(3).Sample().Match(onSome: static i => i.Code, onNone: static () => "gone"))
            .IsEqualTo("C3");

        edits.Send(TestUtil.Remove(3));

        await Assert.That(KeysOf(passing)).IsEquivalentTo([2]);

        await Assert.That(
                passing.IdentityCell(3).Sample().Match(onSome: static i => i.Code, onNone: static () => "gone"))
            .IsEqualTo("gone");
    }

    [Test]
    public async Task ASortFollowsWhicheverOrderTheCellHolds()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 30),
                TestUtil.Item(number: 2, name: "two", score: 10),
                TestUtil.Item(number: 3, name: "three", score: 20));

        // One sorts by an int and the other by a string. The cell holds either, because the sort
        // value's type lives inside the order rather than in the type of the order.
        KeyOrder<int, ItemIdentity, ItemState> byScore =
            KeyOrder<int, ItemIdentity, ItemState>.By(static (_, state) => state.Score);

        KeyOrder<int, ItemIdentity, ItemState> byName =
            KeyOrder<int, ItemIdentity, ItemState>.By(static (_, state) => state.Name);

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(byScore);
        ReactiveCollection<int, ItemIdentity, ItemState> sorted = collection.SortBy(order);

        await Assert.That(KeysOf(sorted)).IsEquivalentTo([2, 3, 1]);

        List<bool> resets = [];
        IListener l = sorted.KeyChangesStream.ListenStrong(change => resets.Add(change.IsReset));

        order.Send(byName);

        l.Unlisten();

        // A new order is a criteria change, and a criteria change is a reset.
        await Assert.That(resets).IsEquivalentTo([true]);
        await Assert.That(KeysOf(sorted)).IsEquivalentTo([1, 3, 2]);
    }

    [Test]
    public async Task AStageBelowASortRefilesWhenTheOrderChanges()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 30),
                TestUtil.Item(number: 2, name: "two", score: 10),
                TestUtil.Item(number: 3, name: "three", score: 20),
                TestUtil.Item(number: 4, name: "four", score: 40));

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order =
            Cell.CreateSink(KeyOrder<int, ItemIdentity, ItemState>.By(static (_, state) => state.Score));

        // The filter is told nothing about the order. It builds from whatever its upstream's set
        // orders by, so re-filing under the new one needs no wiring of its own.
        ReactiveCollection<int, ItemIdentity, ItemState> filtered =
            collection
                .SortBy(order)
                .Filter(static (_, state) => state.Score < 40);

        await Assert.That(KeysOf(filtered)).IsEquivalentTo([2, 3, 1]);

        order.Send(KeyOrder<int, ItemIdentity, ItemState>.ByDescending(static (_, state) => state.Score));

        await Assert.That(KeysOf(filtered)).IsEquivalentTo([1, 3, 2]);
    }

    [Test]
    public async Task AnOrderOverIdentityAloneSurvivesAStateEdit()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 30),
                TestUtil.Item(number: 2, name: "two", score: 10));

        // Swapping a whole-item order for an identity-only one changes what a state edit costs,
        // because the stage reads DependsOnState off whichever order built its current set.
        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order =
            Cell.CreateSink(KeyOrder<int, ItemIdentity, ItemState>.By(static (_, state) => state.Score));

        ReactiveCollection<int, ItemIdentity, ItemState> sorted = collection.SortBy(order);

        await Assert.That(KeysOf(sorted)).IsEquivalentTo([2, 1]);

        order.Send(KeyOrder<int, ItemIdentity, ItemState>.ByIdentity(static identity => identity.Code));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo([1, 2]);

        // Under an identity order a state edit cannot move anything, and does not.
        edits.Send(TestUtil.Score(key: 2, score: 99));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo([1, 2]);
    }

    [Test]
    public async Task AViewsSnapshotHoldsOnlyWhatTheViewHolds()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 20);

        CollectionSnapshot<int, ItemIdentity, ItemState> view = passing.SnapshotCell.Sample();

        // The view's snapshot counts the view, not the store behind it.
        await Assert.That(view.Count).IsEqualTo(2);
        await Assert.That(view.ContainsKey(1)).IsFalse();
        await Assert.That(view.ContainsKey(2)).IsTrue();
        await Assert.That(view.TryGetItem(key: 1, item: out Item<ItemIdentity, ItemState>? _)).IsFalse();
        await Assert.That(view.TryGetItem(key: 2, item: out Item<ItemIdentity, ItemState>? _)).IsTrue();

        // And the two maps behind it agree with it rather than with the store.
        await Assert.That(view.States.Count).IsEqualTo(2);
        await Assert.That(TestUtil.Keys(view.Identities.Keys)).IsEquivalentTo([2, 3]);
        await Assert.That(view.States.TryGetState(key: 1, state: out ItemState _)).IsFalse();

        // The root still sees everything, which is what makes it the root.
        await Assert.That(collection.SnapshotCell.Sample().Count).IsEqualTo(3);
        await Assert.That(collection.SnapshotCell.Sample().ContainsKey(1)).IsTrue();
    }

    [Test]
    public async Task AViewsSnapshotFollowsItsMembership()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 20);

        await Assert.That(passing.SnapshotCell.Sample().ContainsKey(1)).IsFalse();

        // Scoring it into the view puts it in the view's snapshot too.
        edits.Send(TestUtil.Score(key: 1, score: 99));

        await Assert.That(passing.SnapshotCell.Sample().ContainsKey(1)).IsTrue();
        await Assert.That(passing.SnapshotCell.Sample().Count).IsEqualTo(2);
    }

    [Test]
    public async Task AViewChangeCarriesTheViewOnBothSides()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 20);

        List<CollectionViewChange<int, ItemIdentity, ItemState>> changes = [];
        IListener l = passing.KeyChangesStream.ListenStrong(changes.Add);

        edits.Send(TestUtil.Score(key: 1, score: 99));

        l.Unlisten();

        CollectionViewChange<int, ItemIdentity, ItemState> change = changes[0];

        // Before is the view as it stood, which did not hold key 1; After is the view now, which
        // does. Neither is the store, and that is the whole point of the scoping.
        await Assert.That(change.Before.ContainsKey(1)).IsFalse();
        await Assert.That(change.After.ContainsKey(1)).IsTrue();
        await Assert.That(change.Before.Count).IsEqualTo(1);
        await Assert.That(change.After.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AViewsIdentityCellAnswersForTheView()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 20);

        Cell<Maybe<ItemIdentity>> cell = passing.IdentityCell(1);

        await Assert.That(cell.Sample().Match(onSome: static i => i.Code, onNone: static () => "none"))
            .IsEqualTo("none");

        await Assert.That(
                collection.IdentityCell(1).Sample().Match(onSome: static i => i.Code, onNone: static () => "none"))
            .IsEqualTo("C1");

        // Observers of one key through one view share a cell. The old implementation mapped the
        // collection's shape cell and built a new node for every caller.
        await Assert.That(passing.IdentityCell(1)).IsSameReferenceAs(cell);

        // Scoring it into the view gives the identity a value.
        edits.Send(TestUtil.Score(key: 1, score: 99));

        await Assert.That(cell.Sample().Match(onSome: static i => i.Code, onNone: static () => "none")).IsEqualTo("C1");
    }

    [Test]
    public async Task AnIdentityCellSleepsThroughAStateEdit()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 20);

        List<Maybe<ItemIdentity>> onCollection = [];
        List<Maybe<ItemIdentity>> onView = [];

        IListener a = collection.IdentityCell(2).Updates().ListenStrong(onCollection.Add);
        IListener b = passing.IdentityCell(2).Updates().ListenStrong(onView.Add);

        // An identity cannot change while its key stays put, so neither observer should hear
        // anything - not the rename, and not the reorder the score edit causes in the view.
        edits.Send(TestUtil.Rename(key: 2, name: "renamed"));
        edits.Send(TestUtil.Score(key: 1, score: 99));

        a.Unlisten();
        b.Unlisten();

        await Assert.That(onCollection).IsEmpty();
        await Assert.That(onView).IsEmpty();
    }

    [Test]
    public async Task AViewsItemChangesReportOnlyItsOwnItems()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 20);

        List<ItemChange<int, ItemIdentity, ItemState>> onView = [];
        List<ItemChange<int, ItemIdentity, ItemState>> onCollection = [];

        IListener a = passing.ItemChangesStream.ListenStrong(onView.Add);
        IListener b = collection.ItemChangesStream.ListenStrong(onCollection.Add);

        // An item the view does not hold changes. The collection hears it; the view does not.
        edits.Send(TestUtil.Rename(key: 1, name: "renamed"));

        await Assert.That(onCollection.Count).IsEqualTo(1);
        await Assert.That(onView).IsEmpty();

        // Scoring it in reads to the view as the item arriving.
        edits.Send(TestUtil.Score(key: 1, score: 99));

        await Assert.That(onView.Count).IsEqualTo(1);
        await Assert.That(onView[0].Added).Contains(1);
        await Assert.That(onView[0].TryGetNewState(key: 1, state: out ItemState state)).IsTrue();
        await Assert.That(state.Name).IsEqualTo("renamed");

        // And scoring it back out reads as the item leaving, though the store still has it.
        edits.Send(TestUtil.Score(key: 1, score: 1));

        await Assert.That(onView.Count).IsEqualTo(2);
        await Assert.That(onView[1].Removed).Contains(1);

        a.Unlisten();
        b.Unlisten();

        await Assert.That(collection.SnapshotCell.Sample().ContainsKey(1)).IsTrue();
    }

    [Test]
    public async Task AViewsShapeCellHoldsItsOwnKeysAndSleepsThroughAStateEdit()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30));

        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 20);

        await Assert.That(TestUtil.Keys(passing.ShapeCell.Sample().Keys)).IsEquivalentTo([2, 3]);

        await Assert.That(TestUtil.Keys(collection.ShapeCell.Sample().Keys))
            .IsEquivalentTo([1, 2, 3]);

        List<IReadOnlyDictionary<int, ItemIdentity>> shapes = [];
        IListener l = passing.ShapeCell.Updates().ListenStrong(shapes.Add);

        // A rename changes no membership anywhere, so nothing fires.
        edits.Send(TestUtil.Rename(key: 2, name: "renamed"));

        await Assert.That(shapes).IsEmpty();

        // Scoring an item into the view does.
        edits.Send(TestUtil.Score(key: 1, score: 99));

        await Assert.That(shapes.Count).IsEqualTo(1);
        await Assert.That(TestUtil.Keys(shapes[0].Keys)).IsEquivalentTo([1, 2, 3]);

        l.Unlisten();
    }

    [Test]
    public async Task MapKeepsAnObjectPerKeyAndReusesIt()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        int projections = 0;

        using MappedItems<string> mapped =
            collection.Map(key =>
            {
                projections++;

                return "row " + key;
            });

        await Assert.That(mapped.Items.Sample()).IsEquivalentTo(["row 1", "row 2"]);
        await Assert.That(projections).IsEqualTo(2);

        // An edit that moves no key projects nothing new, and hands back the same objects.
        IReadOnlyList<string> before = mapped.Items.Sample();

        edits.Send(TestUtil.Rename(key: 1, name: "renamed"));

        await Assert.That(projections).IsEqualTo(2);
        await Assert.That(ReferenceEquals(objA: mapped.Items.Sample()[0], objB: before[0])).IsTrue();

        // A new key projects once.
        edits.Send(TestUtil.Add(TestUtil.Item(number: 3, name: "three", score: 30)));

        await Assert.That(projections).IsEqualTo(3);
        await Assert.That(mapped.Items.Sample()).IsEquivalentTo(["row 1", "row 2", "row 3"]);
    }

    [Test]
    public async Task MapKeepsWhatIsInViewHoweverSmallTheBound()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30));

        List<string> evicted = [];
        int projections = 0;

        // A bound of zero retains nothing that has left, and everything that has not.
        using MappedItems<string> mapped =
            collection.Map(
                project: key =>
                {
                    projections++;

                    return "row " + key;
                },
                retainedBeyondTheView: 0,
                onEvicted: evicted.Add);

        IReadOnlyList<string> first = mapped.Items.Sample();

        await Assert.That(projections).IsEqualTo(3);
        await Assert.That(evicted).IsEmpty();

        // Nothing left the view, so nothing was evicted and nothing is rebuilt.
        edits.Send(TestUtil.Rename(key: 2, name: "renamed"));

        await Assert.That(projections).IsEqualTo(3);
        await Assert.That(ReferenceEquals(objA: mapped.Items.Sample()[1], objB: first[1])).IsTrue();

        // Removing one does evict it, because it has left and the bound keeps none.
        edits.Send(TestUtil.Remove(2));

        await Assert.That(evicted).IsEquivalentTo(["row 2"]);
    }

    [Test]
    public async Task DisposingAMapReleasesWhatItStillHolds()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        List<string> released = [];

        MappedItems<string> mapped =
            collection.Map(
                project: static key => "row " + key,
                onEvicted: released.Add);

        _ = mapped.Items.Sample();

        await Assert.That(released).IsEmpty();

        // The rows never left the view, so eviction never fired for them. Disposal is what
        // releases them, and without it they would outlive the thing that built them.
        mapped.Dispose();

        await Assert.That(released).IsEquivalentTo(["row 1", "row 2"]);
    }
}
