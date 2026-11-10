using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
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

    /// <summary>An operation as "type:key". These tests assert on that text.</summary>
    private static string Describe(ViewOperation<int> operation) =>
        $"{operation.GetType().Name.Replace(oldValue: "`1", newValue: string.Empty)}:{operation.Key}";

    [Test]
    public async Task TheRootKeepsTheOrderItemsArrivedIn()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        // This is the sequence of the enumeration of the initial items, and not the sequence of
        // their keys.
        await Assert.That(KeysOf(collection))
            .IsEquivalentTo(expected: [3, 1, 2], ordering: CollectionOrdering.Matching);

        // The collection adds them at the end, in the sequence of the edit.
        edits.Send(
            TestUtil.Add(
                TestUtil.Item(number: 5, name: "five", score: 50),
                TestUtil.Item(number: 0, name: "zero", score: 0)));

        await Assert.That(KeysOf(collection))
            .IsEquivalentTo(expected: [3, 1, 2, 5, 0], ordering: CollectionOrdering.Matching);

        // An update is not an arrival.
        edits.Send(TestUtil.Score(key: 3, score: 99));

        await Assert.That(KeysOf(collection))
            .IsEquivalentTo(expected: [3, 1, 2, 5, 0], ordering: CollectionOrdering.Matching);

        // A key that an edit removes, and that a subsequent edit adds, is a new arrival.
        edits.Send(TestUtil.Remove(1));

        await Assert.That(KeysOf(collection))
            .IsEquivalentTo(expected: [3, 2, 5, 0], ordering: CollectionOrdering.Matching);

        edits.Send(TestUtil.Add(TestUtil.Item(number: 1, name: "one again", score: 10)));

        await Assert.That(KeysOf(collection))
            .IsEquivalentTo(expected: [3, 2, 5, 0, 1], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task AnItemReplacedInOneEditMovesToTheEndAndIsReportedOnce()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> removals =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> additions =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                keySelector: TestUtil.KeyOf,
                initialItems:
                [
                    TestUtil.Item(number: 1, name: "one", score: 10),
                    TestUtil.Item(number: 2, name: "two", score: 20),
                    TestUtil.Item(number: 3, name: "three", score: 30)
                ],
                removals,
                additions);

        List<string> operations = [];

        IListener l =
            collection.KeyChangesStream.ListenStrong(change => operations.AddRange(change.Operations.Select(Describe)));

        // A removal and an add of one key in one transaction replaces an item, and the new item is
        // a new arrival.
        Transaction.RunVoid(() =>
        {
            removals.Send(TestUtil.Remove(1));
            additions.Send(TestUtil.Add(TestUtil.Item(number: 1, name: "one, replaced", score: 11)));
        });

        l.Unlisten();

        await Assert.That(KeysOf(collection))
            .IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);

        // This is the position that the key left, and then its new position. With only an add, a
        // list that binds to this counts the key two times.
        await Assert.That(operations)
            .IsEquivalentTo(expected: ["ViewRemove:1", "ViewInsert:1"], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task AddsInOneTransactionFollowTheOrderTheStreamsWereGiven()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> first =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> second =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                keySelector: TestUtil.KeyOf,
                initialItems: [],
                first,
                second);

        // The sends are in the opposite sequence to the streams, in the two conditions. The
        // sequence of the streams at the Create call selects, and the sequence of the sends does
        // not.
        Transaction.RunVoid(() =>
        {
            second.Send(TestUtil.Add(TestUtil.Item(number: 2, name: "two", score: 20)));
            first.Send(TestUtil.Add(TestUtil.Item(number: 1, name: "one", score: 10)));
        });

        Transaction.RunVoid(() =>
        {
            first.Send(TestUtil.Add(TestUtil.Item(number: 3, name: "three", score: 30)));
            second.Send(TestUtil.Add(TestUtil.Item(number: 4, name: "four", score: 40)));
        });

        await Assert.That(KeysOf(collection))
            .IsEquivalentTo(expected: [1, 2, 3, 4], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task AKeyWithNoOrderOfItsOwnCanStillBeListed()
    {
        StreamSink<CollectionEdit<Handle, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<Handle, ItemIdentity, ItemState>>();

        // Handle has no order, thus a default compare operation on two of them throws an
        // exception. The collection never compares two of them.
        ReactiveCollection<Handle, ItemIdentity, ItemState> collection =
            ReactiveCollection<Handle, ItemIdentity, ItemState>.Create(
                keySelector: static identity => new Handle(identity.Number),
                initialItems:
                [
                    TestUtil.Item(number: 2, name: "two", score: 20), TestUtil.Item(number: 1, name: "one", score: 10)
                ],
                edits);

        edits.Send(
            CollectionEdit<Handle, ItemIdentity, ItemState>.Add(TestUtil.Item(number: 3, name: "three", score: 30)));

        await Assert.That(collection.KeysCell.Sample().Select(static handle => handle.Number).ToList())
            .IsEquivalentTo(expected: [2, 1, 3], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task ByArrivalTakesASortBackToTheOrderItemsArrivedIn()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order =
            Cell.CreateSink(KeyOrder<int, ItemIdentity, ItemState>.By(static (_, state) => state.Score));

        ReactiveCollection<int, ItemIdentity, ItemState> sorted = collection.SortBy(order);

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);

        // The third state of a column header: off.
        order.Send(KeyOrder<int, ItemIdentity, ItemState>.ByArrival());

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [3, 1, 2], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(collection.SortBy(static (_, state) => state.Score).SortByArrival()))
            .IsEquivalentTo(expected: [3, 1, 2], ordering: CollectionOrdering.Matching);

        // Two keys never come at the same time, thus this code never reads a second level.
        KeyOrder<int, ItemIdentity, ItemState> byArrival = KeyOrder<int, ItemIdentity, ItemState>.ByArrival();

        await Assert.That(byArrival.ThenBy(static (_, state) => state.Name)).IsSameReferenceAs(byArrival);
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

        await Assert.That(KeysOf(byScore)).IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);

        // A move of item 1 to the end of the range sorts it again and does not build the stage
        // again.
        edits.Send(TestUtil.Score(key: 1, score: 5));

        await Assert.That(KeysOf(byScore)).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);
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

        await Assert.That(KeysOf(byScore)).IsEquivalentTo(expected: [1, 3, 2], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task SortByKeyOrdersByKeyInEitherDirection()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 2, name: "two", score: 10),
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 1, name: "one", score: 20));

        ReactiveCollection<int, ItemIdentity, ItemState> ascending = collection.SortByKey();
        ReactiveCollection<int, ItemIdentity, ItemState> descending = collection.SortByKeyDescending();

        ReactiveCollection<int, ItemIdentity, ItemState> explicitDescending =
            collection.SortByKey(keyComparer: Comparer<int>.Default, isDescending: true);

        await Assert.That(KeysOf(ascending)).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(descending))
            .IsEquivalentTo(expected: [3, 2, 1], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(explicitDescending))
            .IsEquivalentTo(expected: [3, 2, 1], ordering: CollectionOrdering.Matching);

        edits.Send(TestUtil.Add(TestUtil.Item(number: 4, name: "four", score: 0)));

        await Assert.That(KeysOf(ascending))
            .IsEquivalentTo(expected: [1, 2, 3, 4], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(descending))
            .IsEquivalentTo(expected: [4, 3, 2, 1], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task AReFilingUpdateReportsAMove()
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

        await Assert.That(operations)
            .IsEquivalentTo(expected: ["ViewMove:1"], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(byScore)).IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task AReFilingUpdateFiresStateCell()
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
            byScore.StateCell(1).ListenStrong(state => state.MatchSome(state => scores.Add(state.Score)));

        edits.Send(TestUtil.Score(key: 1, score: 99));

        l.Unlisten();

        await Assert.That(scores)
            .IsEquivalentTo(expected: [10, 99], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(byScore)).IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);
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

        // The root uses the order of arrival, thus this moves no key. A stage below can sort on
        // the state that changed, thus this code must send the change to it.
        edits.Send(TestUtil.Score(key: 1, score: 99));

        l.Unlisten();

        await Assert.That(operations).IsEquivalentTo(expected: ["ViewUpdate:1"], ordering: CollectionOrdering.Matching);
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

        // This stage is directly above the collection, thus it gets the order of the root. That
        // order uses the key, and a key cannot change. Thus, a state edit can move no key here,
        // which is the condition where Refile takes the short path and does not remove the key and
        // add it again.
        ReactiveCollection<int, ItemIdentity, ItemState> passing =
            collection.Filter(static (_, state) => state.Score >= 0);

        List<string> operations = [];

        IListener l =
            passing.KeyChangesStream.ListenStrong(change => operations.AddRange(change.Operations.Select(Describe)));

        edits.Send(TestUtil.Score(key: 2, score: 99));

        l.Unlisten();

        // There is one update, no move, and no change to the order.
        await Assert.That(operations).IsEquivalentTo(expected: ["ViewUpdate:2"], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);
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

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [3, 1, 4], ordering: CollectionOrdering.Matching);
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

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);

        edits.Send(TestUtil.Score(key: 1, score: 25));
        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching);

        edits.Send(TestUtil.Score(key: 2, score: 5));
        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task ChangingThePredicateRebuildsTheStageAndDoesNotReportAReset()
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

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [2, 3], ordering: CollectionOrdering.Matching);

        List<bool> resets = [];
        IListener l = passing.KeyChangesStream.ListenStrong(change => resets.Add(change.IsReset));

        threshold.Send(5);

        l.Unlisten();

        await Assert.That(resets).IsEquivalentTo(expected: [false], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);
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

        await Assert.That(KeysOf(evens)).IsEquivalentTo(expected: [2, 4], ordering: CollectionOrdering.Matching);

        List<string> operations = [];

        IListener l =
            evens.KeyChangesStream.ListenStrong(change => operations.AddRange(change.Operations.Select(Describe)));

        // The key is in the view. A change of score cannot move it out, thus this reports the
        // update and nothing more. The membership cannot change here.
        edits.Send(TestUtil.Score(key: 2, score: -1));

        // The key is not in the view. An update for a key that this filter does not hold reports
        // nothing.
        edits.Send(TestUtil.Score(key: 1, score: -1));

        l.Unlisten();

        await Assert.That(operations).IsEquivalentTo(expected: ["ViewUpdate:2"], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(evens)).IsEquivalentTo(expected: [2, 4], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A predicate cell on the identity names what entered and what left when it
    ///     changes, and a state edit after that change still does not test the
    ///     predicate again.
    /// </summary>
    [Test]
    public async Task FilterByIdentityFollowsAChangingPredicate()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<Func<ItemIdentity, bool>> predicate =
            Cell.CreateSink<Func<ItemIdentity, bool>>(static identity => identity.Number % 2 == 0);

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 4, name: "four", score: 40));

        ReactiveCollection<int, ItemIdentity, ItemState> view = collection.FilterByIdentity(predicateCell: predicate);

        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: [2, 4], ordering: CollectionOrdering.Matching);

        List<string> operations = [];
        List<bool> resets = [];

        IListener l =
            view.KeyChangesStream.ListenStrong(change =>
            {
                resets.Add(change.IsReset);
                operations.AddRange(change.Operations.Select(Describe));
            });

        predicate.Send(static identity => identity.Number % 2 == 1);

        await Assert.That(resets).IsEquivalentTo(expected: [false], ordering: CollectionOrdering.Matching);

        await Assert.That(operations)
            .IsEquivalentTo(
                expected: ["ViewInsert:1", "ViewRemove:2", "ViewInsert:3", "ViewRemove:4"],
                ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: [1, 3], ordering: CollectionOrdering.Matching);

        operations.Clear();

        edits.Send(TestUtil.Score(key: 3, score: -1));
        edits.Send(TestUtil.Score(key: 2, score: -1));

        l.Unlisten();

        await Assert.That(operations).IsEquivalentTo(expected: ["ViewUpdate:3"], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: [1, 3], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A criteria cell with a predicate on the identity narrows as the criteria
    ///     change, and a state edit cannot move a key into the view.
    /// </summary>
    [Test]
    public async Task FilterByIdentityFollowsChangingCriteria()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<int> minimum = Cell.CreateSink(3);

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 4, name: "four", score: 40));

        ReactiveCollection<int, ItemIdentity, ItemState> view =
            collection.FilterByIdentity(
                criteriaCell: minimum,
                predicate: static (limit, identity) => identity.Number >= limit);

        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: [3, 4], ordering: CollectionOrdering.Matching);

        List<string> operations = [];

        IListener l =
            view.KeyChangesStream.ListenStrong(change => operations.AddRange(change.Operations.Select(Describe)));

        minimum.Send(2);

        await Assert.That(operations).IsEquivalentTo(expected: ["ViewInsert:2"], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: [2, 3, 4], ordering: CollectionOrdering.Matching);

        // A state edit to a key that the criteria keep out cannot bring it in.
        operations.Clear();
        edits.Send(TestUtil.Score(key: 1, score: 99));

        l.Unlisten();

        await Assert.That(operations).IsEmpty();
        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: [2, 3, 4], ordering: CollectionOrdering.Matching);
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

        await Assert.That(KeysOf(evens)).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);

        // The arrival of an identity is the one event that can change these members, and this code
        // tests it.
        edits.Send(TestUtil.Add(TestUtil.Item(number: 4, name: "four", score: 40)));
        await Assert.That(KeysOf(evens)).IsEquivalentTo(expected: [2, 4], ordering: CollectionOrdering.Matching);

        edits.Send(TestUtil.Add(TestUtil.Item(number: 5, name: "five", score: 50)));
        await Assert.That(KeysOf(evens)).IsEquivalentTo(expected: [2, 4], ordering: CollectionOrdering.Matching);

        edits.Send(TestUtil.Remove(2));
        await Assert.That(KeysOf(evens)).IsEquivalentTo(expected: [4], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A state edit cannot move a key into an identity filter or out of it, and it
    ///     can move a key in that filter. The filter keeps the order of its upstream
    ///     collection, and an upstream that sorts on the state moves keys at a state
    ///     edit. This stage must follow that move and must report its own positions,
    ///     and not the positions of the upstream.
    /// </summary>
    [Test]
    public async Task FilterByIdentityFollowsAMoveInAStateOrderAboveIt()
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

        ReactiveCollection<int, ItemIdentity, ItemState> byScore = collection.SortBy(static (_, state) => state.Score);

        ReactiveCollection<int, ItemIdentity, ItemState> odds =
            byScore.FilterByIdentity(static identity => identity.Number % 2 == 1);

        await Assert.That(KeysOf(odds)).IsEquivalentTo(expected: [1, 3], ordering: CollectionOrdering.Matching);

        List<string> operations = [];

        IListener l =
            odds.KeyChangesStream.ListenStrong(change =>
                operations.AddRange(
                    change.Operations.Select(static operation =>
                        operation is ViewMove<int> move
                            ? $"{Describe(move)}:{move.FromIndex}->{move.ToIndex}"
                            : Describe(operation))));

        // A change from 10 to 35 moves key 1 from the front of the sort to the third position,
        // which is [2, 3, 1, 4], and thus from the front of this filter to the end.
        edits.Send(TestUtil.Score(key: 1, score: 35));

        l.Unlisten();

        await Assert.That(KeysOf(byScore))
            .IsEquivalentTo(expected: [2, 3, 1, 4], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(odds)).IsEquivalentTo(expected: [3, 1], ordering: CollectionOrdering.Matching);

        // These are the positions of this stage: 0 to 1 here, and the sort above moved the key
        // from 0 to 2.
        await Assert.That(operations)
            .IsEquivalentTo(expected: ["ViewMove:1:0->1"], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task FilterByIdentityFollowsAMoveInASortCriteriaChangeAboveItThatDoesNotDependOnState()
    {
        StreamSink<KeyOrder<int, ItemIdentity, ItemState>> sorts =
            Stream.CreateSink<KeyOrder<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: Stream.Never<CollectionEdit<int, ItemIdentity, ItemState>>(),
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 4, name: "four", score: 40));

        ReactiveCollection<int, ItemIdentity, ItemState> sorted =
            collection.SortBy(
                sorts.Hold(KeyOrder<int, ItemIdentity, ItemState>.ByIdentity(static identity => identity.Code)));

        ReactiveCollection<int, ItemIdentity, ItemState> filtered =
            sorted.FilterByIdentity(static identity => identity.Number % 2 == 1);

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [1, 2, 3, 4], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(filtered)).IsEquivalentTo(expected: [1, 3], ordering: CollectionOrdering.Matching);

        sorts.Send(KeyOrder<int, ItemIdentity, ItemState>.ByIdentityDescending(static identity => identity.Code));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [4, 3, 2, 1], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(filtered)).IsEquivalentTo(expected: [3, 1], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task FilterFollowsAMoveInASortCriteriaChangeAboveItThatDoesNotDependOnState()
    {
        StreamSink<KeyOrder<int, ItemIdentity, ItemState>> sorts =
            Stream.CreateSink<KeyOrder<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: Stream.Never<CollectionEdit<int, ItemIdentity, ItemState>>(),
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30),
                TestUtil.Item(number: 4, name: "four", score: 40));

        ReactiveCollection<int, ItemIdentity, ItemState> sorted =
            collection.SortBy(
                sorts.Hold(KeyOrder<int, ItemIdentity, ItemState>.ByIdentity(static identity => identity.Code)));

        ReactiveCollection<int, ItemIdentity, ItemState> filtered =
            sorted.Filter(static (_, state) => state.Score / 10 % 2 == 1);

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [1, 2, 3, 4], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(filtered)).IsEquivalentTo(expected: [1, 3], ordering: CollectionOrdering.Matching);

        sorts.Send(KeyOrder<int, ItemIdentity, ItemState>.ByIdentityDescending(static identity => identity.Code));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [4, 3, 2, 1], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(filtered)).IsEquivalentTo(expected: [3, 1], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     An update that moves no key changes the sort value of that key. A filter
    ///     that only sends the update on keeps the previous value, and sorts the next
    ///     arrival against it.
    /// </summary>
    [Test]
    public async Task FilterByIdentityFilesTheNextArrivalAgainstAnUpdatedSortValue()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30));

        ReactiveCollection<int, ItemIdentity, ItemState> byScore = collection.SortBy(static (_, state) => state.Score);

        ReactiveCollection<int, ItemIdentity, ItemState> all =
            byScore.FilterByIdentity(static identity => identity.Number > 0);

        // A change from 20 to 25 keeps key 2 between 1 and 3, thus the sort reports an update and
        // no move.
        edits.Send(TestUtil.Score(key: 2, score: 25));

        await Assert.That(KeysOf(all)).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);

        // A value of 22 goes before 25. Against the previous value of 20, it goes after key 2.
        edits.Send(TestUtil.Add(TestUtil.Item(number: 4, name: "four", score: 22)));

        await Assert.That(KeysOf(byScore))
            .IsEquivalentTo(expected: [1, 4, 2, 3], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(all)).IsEquivalentTo(expected: [1, 4, 2, 3], ordering: CollectionOrdering.Matching);
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

        // The two stages read no state, thus a state edit cannot come to one of them.
        ReactiveCollection<int, ItemIdentity, ItemState> view =
            collection
                .FilterByIdentity(static identity => identity.Number % 2 == 0)
                .SortByIdentityDescending(static identity => identity.Number);

        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: [4, 2], ordering: CollectionOrdering.Matching);

        edits.Send(TestUtil.Score(key: 4, score: -1000));

        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: [4, 2], ordering: CollectionOrdering.Matching);
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

        // The codes are "C1", "C2", and "C3", thus a descending order on the code is a descending
        // order on the number here.
        ReactiveCollection<int, ItemIdentity, ItemState> byCode =
            collection.SortByIdentityDescending(static identity => identity.Code);

        await Assert.That(KeysOf(byCode)).IsEquivalentTo(expected: [3, 2, 1], ordering: CollectionOrdering.Matching);

        List<string> operations = [];

        IListener l =
            byCode.KeyChangesStream.ListenStrong(change => operations.AddRange(change.Operations.Select(Describe)));

        // A change of score cannot change a code, thus this reports the update and moves no key.
        // The selector on the identity only gives that permission.
        edits.Send(TestUtil.Score(key: 3, score: -99));

        l.Unlisten();

        await Assert.That(operations).IsEquivalentTo(expected: ["ViewUpdate:3"], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(byCode)).IsEquivalentTo(expected: [3, 2, 1], ordering: CollectionOrdering.Matching);
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

        await Assert.That(KeysOf(byCode)).IsEquivalentTo(expected: [1, 3], ordering: CollectionOrdering.Matching);

        // This order follows the arrival of an identity and the departure of one.
        edits.Send(TestUtil.Add(TestUtil.Item(number: 2, name: "two", score: 20)));
        await Assert.That(KeysOf(byCode)).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);

        edits.Send(TestUtil.Remove(1));
        await Assert.That(KeysOf(byCode)).IsEquivalentTo(expected: [2, 3], ordering: CollectionOrdering.Matching);
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

        await Assert.That(KeysOf(topTwo)).IsEquivalentTo(expected: [4, 3], ordering: CollectionOrdering.Matching);

        // A new item at the front moves the last item out of the window.
        edits.Send(TestUtil.Add(TestUtil.Item(number: 5, name: "five", score: 50)));

        await Assert.That(KeysOf(topTwo)).IsEquivalentTo(expected: [5, 4], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TakeReportsAReplacedItemThatKeepsItsPosition()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 30));

        ReactiveCollection<int, ItemIdentity, ItemState> lowestTwo =
            collection
                .SortBy(static (_, state) => state.Score)
                .Take(2);

        Cell<Maybe<ItemState>> state = lowestTwo.StateCell(1);
        Cell<Maybe<ItemIdentity>> identity = lowestTwo.IdentityCell(1);

        // One edit replaces the item of key 1, and the new score keeps the key first. The window
        // holds the same keys in the same sequence, thus a comparison of the two windows finds no
        // difference. The sort above names key 1 as a removal and an insert, and not as an update.
        edits.Send(
            TestUtil.Remove(1)
                .CombineWith(
                    TestUtil.Add(
                        new Item<ItemIdentity, ItemState>(
                            identity: new ItemIdentity(Number: 1, Code: "R1"),
                            state: new ItemState(Name: "replaced", Score: 15)))));

        await Assert.That(KeysOf(lowestTwo)).IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching);

        await Assert.That(state.Sample().Match(onSome: static s => s.Score, onNone: static () => -1))
            .IsEqualTo(15)
            .Because("the window must report the new state of a key that keeps its position");

        await Assert.That(identity.Sample().Match(onSome: static i => i.Code, onNone: static () => "gone"))
            .IsEqualTo("R1")
            .Because("the replacement can change the identity");
    }

    [Test]
    public async Task TakeReportsAMovedItemThatKeepsItsPosition()
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

        ReactiveCollection<int, ItemIdentity, ItemState> lowestThree =
            collection
                .SortBy(static (_, state) => state.Score)
                .Take(3);

        Cell<Maybe<ItemState>> state = lowestThree.StateCell(3);

        // The sort files key 4 first, which puts it before key 3. It then files key 3 at its
        // previous position, and reports that as a move and not as an update. The window holds the
        // keys 1, 2, and 3 before the edit and after it.
        edits.Send(TestUtil.Score(key: 4, score: 25).CombineWith(TestUtil.Score(key: 3, score: 22)));

        await Assert.That(KeysOf(lowestThree))
            .IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);

        await Assert.That(state.Sample().Match(onSome: static s => s.Score, onNone: static () => -1))
            .IsEqualTo(22)
            .Because("a move above the window carries the new state of the key");
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

        await Assert.That(KeysOf(window)).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);

        limit.Send(2);

        await Assert.That(KeysOf(window)).IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching);
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

        await Assert.That(KeysOf(page)).IsEquivalentTo(expected: [2, 3], ordering: CollectionOrdering.Matching);

        // This test uses the positions and the content, because the keys in the window are the
        // full result of an offset, and a test of the set alone does not see a move.
        await Assert.That(KeysOf(page)[0]).IsEqualTo(2);
        await Assert.That(KeysOf(page)[1]).IsEqualTo(3);

        // A removal of a key before the window moves each key after it one position earlier, thus
        // the window holds different items and its limits do not change.
        edits.Send(TestUtil.Remove(1));

        await Assert.That(KeysOf(page)).IsEquivalentTo(expected: [3, 4], ordering: CollectionOrdering.Matching);
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

        await Assert.That(KeysOf(page)).IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching);

        // A move to a different page is one send.
        offset.Send(2);

        await Assert.That(KeysOf(page)).IsEquivalentTo(expected: [3, 4], ordering: CollectionOrdering.Matching);

        // The last page has fewer keys and no fill values, and an offset above the end gives an
        // empty page and not an error.
        offset.Send(4);

        await Assert.That(KeysOf(page)).IsEquivalentTo(expected: [5], ordering: CollectionOrdering.Matching);

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

        // The descending scores are 4, 3, 2, and 1, thus the second page of two keys is key 2 and
        // key 1.
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

        await Assert.That(KeysOf(sliced))
            .IsEquivalentTo(expected: KeysOf(taken), ordering: CollectionOrdering.Matching);

        edits.Send(TestUtil.Add(TestUtil.Item(number: 0, name: "zero", score: 5)));

        await Assert.That(KeysOf(sliced))
            .IsEquivalentTo(expected: KeysOf(taken), ordering: CollectionOrdering.Matching);
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

        await Assert.That(KeysOf(topTwoOfTheEvens))
            .IsEquivalentTo(expected: [2, 4], ordering: CollectionOrdering.Matching);

        // The filter is above the window, thus an odd item with the highest score changes nothing
        // here.
        edits.Send(TestUtil.Score(key: 1, score: 99));

        await Assert.That(KeysOf(topTwoOfTheEvens))
            .IsEquivalentTo(expected: [2, 4], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task AViewsStateCellAnswersForTheView()
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

        // There are two answers, thus there are two cells. This test asserted the opposite before
        // a view became a collection and not a window on one.
        await Assert.That(passing.StateCell(1)).IsNotSameReferenceAs(collection.StateCell(1));

        // The view has no value for a key that it does not hold, and the collection has one.
        await Assert.That(passing.StateCell(1).Sample().Match(onSome: static s => s.Name, onNone: static () => "none"))
            .IsEqualTo("none");

        await Assert.That(
                collection.StateCell(1).Sample().Match(onSome: static s => s.Name, onNone: static () => "none"))
            .IsEqualTo("one");

        await Assert.That(passing.KeysCell.Sample().Contains(1)).IsFalse();

        // Two observers share a cell because this code never copies one, in one view, which is
        // where that result is important.
        await Assert.That(passing.StateCell(2)).IsSameReferenceAs(passing.StateCell(2));
    }

    [Test]
    public async Task AViewsStateCellFollowsTheKeyInAndOutOfTheView()
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

        // A score that moves the item into the view gives the cell a value.
        edits.Send(TestUtil.Score(key: 1, score: 99));

        await Assert.That(cell.Sample().Match(onSome: static s => s.Name, onNone: static () => "none"))
            .IsEqualTo("one");

        // A subsequent edit, while the item is in the view, comes to the cell.
        edits.Send(TestUtil.Rename(key: 1, name: "renamed"));

        await Assert.That(cell.Sample().Match(onSome: static s => s.Name, onNone: static () => "none"))
            .IsEqualTo("renamed");

        // A score that moves the item out of the view removes the value again.
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

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [3, 2], ordering: CollectionOrdering.Matching);

        await Assert.That(
                passing.IdentityCell(3).Sample().Match(onSome: static i => i.Code, onNone: static () => "gone"))
            .IsEqualTo("C3");

        edits.Send(TestUtil.Remove(3));

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);

        await Assert.That(
                passing.IdentityCell(3).Sample().Match(onSome: static i => i.Code, onNone: static () => "gone"))
            .IsEqualTo("gone");
    }

    [Test]
    public async Task ThenByBreaksTheTiesTheFirstLevelLeaves()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "b", score: 10),
                TestUtil.Item(number: 2, name: "a", score: 10),
                TestUtil.Item(number: 3, name: "c", score: 5),
                TestUtil.Item(number: 4, name: "a", score: 5));

        KeyOrder<int, ItemIdentity, ItemState> byScore =
            KeyOrder<int, ItemIdentity, ItemState>.ByDescending(static (_, state) => state.Score);

        // With one level, the key selects between two equal scores. With a second level, the name
        // selects first.
        await Assert.That(KeysOf(collection.SortBy(byScore)))
            .IsEquivalentTo(expected: [1, 2, 3, 4], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(collection.SortBy(byScore.ThenBy(static (_, state) => state.Name))))
            .IsEquivalentTo(expected: [2, 1, 4, 3], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task EachLevelRunsInItsOwnDirection()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "b", score: 10),
                TestUtil.Item(number: 2, name: "a", score: 10),
                TestUtil.Item(number: 3, name: "c", score: 5),
                TestUtil.Item(number: 4, name: "a", score: 5));

        KeyOrder<int, ItemIdentity, ItemState> order =
            KeyOrder<int, ItemIdentity, ItemState>
                .By(static (_, state) => state.Score)
                .ThenByDescending(static (_, state) => state.Name);

        await Assert.That(KeysOf(collection.SortBy(order)))
            .IsEquivalentTo(expected: [3, 4, 1, 2], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TheKeyBreaksTheLastTieAscendingWhateverTheLevelsDo()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 3, name: "x", score: 1),
                TestUtil.Item(number: 1, name: "x", score: 1),
                TestUtil.Item(number: 2, name: "x", score: 1));

        KeyOrder<int, ItemIdentity, ItemState> order =
            KeyOrder<int, ItemIdentity, ItemState>
                .ByDescending(static (_, state) => state.Score)
                .ThenByDescending(static (_, state) => state.Name);

        await Assert.That(KeysOf(collection.SortBy(order)))
            .IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task AThirdLevelDecidesWhatTheSecondLeavesEqual()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "a", score: 1),
                TestUtil.Item(number: 2, name: "a", score: 1),
                TestUtil.Item(number: 3, name: "b", score: 1));

        // There are three levels, two types of selector, and two directions. The sort value is a
        // pair whose first part is a pair.
        KeyOrder<int, ItemIdentity, ItemState> order =
            KeyOrder<int, ItemIdentity, ItemState>
                .By(static (_, state) => state.Score)
                .ThenBy(static (_, state) => state.Name)
                .ThenByIdentityDescending(static identity => identity.Code);

        await Assert.That(KeysOf(collection.SortBy(order)))
            .IsEquivalentTo(expected: [2, 1, 3], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task ALevelThatReadsTheStateRefilesUnderAnIdentityLevel()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 30),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 5),
                TestUtil.Item(number: 4, name: "four", score: 10));

        // A state edit cannot move the first level and can move the second level, thus it can move
        // the full order. A stage that omits a sort because of the first level leaves key 4 at its
        // position.
        ReactiveCollection<int, ItemIdentity, ItemState> sorted =
            collection.SortBy(
                KeyOrder<int, ItemIdentity, ItemState>
                    .ByIdentity(static identity => identity.Number % 2)
                    .ThenBy(static (_, state) => state.Score));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [4, 2, 3, 1], ordering: CollectionOrdering.Matching);

        edits.Send(TestUtil.Score(key: 4, score: 25));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [2, 4, 3, 1], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task DescendingHoldsForAComparerThatAnswersWithTheExtremes()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 30),
                TestUtil.Item(number: 3, name: "three", score: 20));

        // A negation of int.MinValue gives a negative value, thus a direction from a negation sorts
        // this in the same sequence in the two directions, and that sequence is not always the
        // same.
        KeyOrder<int, ItemIdentity, ItemState> isDescending =
            KeyOrder<int, ItemIdentity, ItemState>.By(
                selector: static (_, state) => state.Score,
                sortComparer: ExtremeComparer.Instance,
                keyComparer: Comparer<int>.Default,
                isDescending: true);

        await Assert.That(KeysOf(collection.SortBy(isDescending)))
            .IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);

        // This is the same comparer as a second level, below a first level that ranks each key
        // equal.
        KeyOrder<int, ItemIdentity, ItemState> secondLevel =
            KeyOrder<int, ItemIdentity, ItemState>
                .ByIdentity(static _ => 0)
                .ThenBy(
                    selector: static (_, state) => state.Score,
                    sortComparer: ExtremeComparer.Instance,
                    isDescending: true);

        await Assert.That(KeysOf(collection.SortBy(secondLevel)))
            .IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);
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

        // One order sorts on an int and the other order sorts on a string. The cell holds each
        // one, because the sort value type is in the order and not in the type of the order.
        KeyOrder<int, ItemIdentity, ItemState> byScore =
            KeyOrder<int, ItemIdentity, ItemState>.By(static (_, state) => state.Score);

        KeyOrder<int, ItemIdentity, ItemState> byName =
            KeyOrder<int, ItemIdentity, ItemState>.By(static (_, state) => state.Name);

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(byScore);
        ReactiveCollection<int, ItemIdentity, ItemState> sorted = collection.SortBy(order);

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);

        List<bool> resets = [];
        IListener l = sorted.KeyChangesStream.ListenStrong(change => resets.Add(change.IsReset));

        order.Send(byName);

        l.Unlisten();

        // A new order is a change of criteria, and a change of criteria is a reset.
        await Assert.That(resets).IsEquivalentTo(expected: [true], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [1, 3, 2], ordering: CollectionOrdering.Matching);
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

        // No code tells the filter about the order. It builds from the order of the set of its
        // upstream collection, thus a sort in the new order needs no other code.
        ReactiveCollection<int, ItemIdentity, ItemState> filtered =
            collection
                .SortBy(order)
                .Filter(static (_, state) => state.Score < 40);

        await Assert.That(KeysOf(filtered)).IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);

        order.Send(KeyOrder<int, ItemIdentity, ItemState>.ByDescending(static (_, state) => state.Score));

        await Assert.That(KeysOf(filtered)).IsEquivalentTo(expected: [1, 3, 2], ordering: CollectionOrdering.Matching);
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

        // A change from a full-item order to an order on the identity only changes the cost of a
        // state edit, because the stage reads DependsOnState from the order of its current set.
        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order =
            Cell.CreateSink(KeyOrder<int, ItemIdentity, ItemState>.By(static (_, state) => state.Score));

        ReactiveCollection<int, ItemIdentity, ItemState> sorted = collection.SortBy(order);

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [2, 1], ordering: CollectionOrdering.Matching);

        order.Send(KeyOrder<int, ItemIdentity, ItemState>.ByIdentity(static identity => identity.Code));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching);

        // In an identity order a state edit cannot move a key, and it moves none.
        edits.Send(TestUtil.Score(key: 2, score: 99));

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching);
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

        // The snapshot of the view counts the view and not the store below it.
        await Assert.That(view.Count).IsEqualTo(2);
        await Assert.That(view.ContainsKey(1)).IsFalse();
        await Assert.That(view.ContainsKey(2)).IsTrue();
        await Assert.That(view.TryGetItem(key: 1, item: out Item<ItemIdentity, ItemState>? _)).IsFalse();
        await Assert.That(view.TryGetItem(key: 2, item: out Item<ItemIdentity, ItemState>? _)).IsTrue();

        // The two maps below it agree with the view and not with the store.
        await Assert.That(view.States.Count).IsEqualTo(2);

        await Assert.That(TestUtil.Keys(view.Identities.Keys))
            .IsEquivalentTo(expected: [2, 3], ordering: CollectionOrdering.Any);

        await Assert.That(view.States.TryGetState(key: 1, state: out _)).IsFalse();

        // The root reads each item, and that makes it the root.
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

        // A score that moves the item into the view also puts it in the snapshot of the view.
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

        // Before is the previous view, which did not hold key 1. After is the current view, which
        // holds it. The two are not the store, and that is the purpose of the scope.
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

        // The observers of one key through one view share a cell. The previous implementation
        // mapped the shape cell of the collection and built a new node for each caller.
        await Assert.That(passing.IdentityCell(1)).IsSameReferenceAs(cell);

        // A score that moves the item into the view gives the identity a value.
        edits.Send(TestUtil.Score(key: 1, score: 99));

        await Assert.That(cell.Sample().Match(onSome: static i => i.Code, onNone: static () => "none")).IsEqualTo("C1");
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

        Cell<Maybe<Item<ItemIdentity, ItemState>>> cell = passing.ItemCell(1);

        List<string> seen = [];

        IListener l =
            cell.Updates()
                .ListenStrong(item =>
                    seen.Add(
                        item.Match(
                            onSome: static value => value.Identity.Code + ":" + value.State.Name,
                            onNone: static () => "gone")));

        // The filter removes this key, thus the view answers with no value where the collection
        // answers with the item.
        await Assert.That(cell.Sample().Match(onSome: static i => i.State.Name, onNone: static () => "none"))
            .IsEqualTo("none");

        await Assert.That(
                collection.ItemCell(1).Sample().Match(onSome: static i => i.State.Name, onNone: static () => "none"))
            .IsEqualTo("one");

        await Assert.That(passing.ItemCell(1)).IsSameReferenceAs(cell);

        // A score that moves the item into the view gives the cell a value, and an edit to an
        // item that the view holds gives it another one.
        edits.Send(TestUtil.Score(key: 1, score: 99));
        edits.Send(TestUtil.Rename(key: 1, name: "renamed"));

        // And one that moves it out takes the value away again.
        edits.Send(TestUtil.Score(key: 1, score: 1));

        l.Unlisten();

        await Assert.That(seen)
            .IsEquivalentTo(expected: ["C1:one", "C1:renamed", "gone"], ordering: CollectionOrdering.Matching);
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

        // An identity cannot change while its key stays in the collection, thus no observer gets a
        // value. The new name gives none, and the change of order from the score edit gives
        // none.
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

        // An item that the view does not hold changes. The collection gets that change and the
        // view does not.
        edits.Send(TestUtil.Rename(key: 1, name: "renamed"));

        await Assert.That(onCollection.Count).IsEqualTo(1);
        await Assert.That(onView).IsEmpty();

        // A score that moves the item in is an arrival for the view.
        edits.Send(TestUtil.Score(key: 1, score: 99));

        await Assert.That(onView.Count).IsEqualTo(1);
        await Assert.That(onView[0].Added).Contains(1);
        await Assert.That(onView[0].TryGetNewState(key: 1, state: out ItemState? state)).IsTrue();
        await Assert.That(state).IsNotNull();
#pragma warning disable CS8602 // Dereference of a possibly null reference.
        await Assert.That(state.Name).IsEqualTo("renamed");
#pragma warning restore CS8602 // Dereference of a possibly null reference.

        // A score that moves the item out is a departure for the view, and the store keeps the
        // item.
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

        await Assert.That(TestUtil.Keys(passing.ShapeCell.Sample().Keys))
            .IsEquivalentTo(expected: [2, 3], ordering: CollectionOrdering.Any);

        await Assert.That(TestUtil.Keys(collection.ShapeCell.Sample().Keys))
            .IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Any);

        List<IReadOnlyDictionary<int, ItemIdentity>> shapes = [];
        IListener l = passing.ShapeCell.Updates().ListenStrong(shapes.Add);

        // A new name changes no members, thus no cell sends a value.
        edits.Send(TestUtil.Rename(key: 2, name: "renamed"));

        await Assert.That(shapes).IsEmpty();

        // A score that moves an item into the view does change the members.
        edits.Send(TestUtil.Score(key: 1, score: 99));

        await Assert.That(shapes.Count).IsEqualTo(1);

        await Assert.That(TestUtil.Keys(shapes[0].Keys))
            .IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Any);

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

        await Assert.That(mapped.Items.Sample())
            .IsEquivalentTo(expected: ["row 1", "row 2"], ordering: CollectionOrdering.Matching);

        await Assert.That(projections).IsEqualTo(2);

        // An edit that moves no key makes no new object, and gives the same objects.
        IReadOnlyList<string> before = mapped.Items.Sample();

        edits.Send(TestUtil.Rename(key: 1, name: "renamed"));

        await Assert.That(projections).IsEqualTo(2);
        await Assert.That(ReferenceEquals(objA: mapped.Items.Sample()[0], objB: before[0])).IsTrue();

        // A new key makes one object.
        edits.Send(TestUtil.Add(TestUtil.Item(number: 3, name: "three", score: 30)));

        await Assert.That(projections).IsEqualTo(3);

        await Assert.That(mapped.Items.Sample())
            .IsEquivalentTo(expected: ["row 1", "row 2", "row 3"], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task KeysCellMovesOnlyWhenMembershipOrOrderDoes()
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

        ReactiveCollection<int, ItemIdentity, ItemState> page =
            collection
                .Filter(static (_, state) => state.Score < 100)
                .SortBy(static (_, state) => state.Score)
                .Slice(offset: 0, limit: 3);

        using MappedItems<string> rows = page.Map(static key => "row " + key);

        int collectionUpdates = 0;
        int pageUpdates = 0;
        List<string> pageOperations = [];

        IListener collectionListener = collection.KeysCell.Updates().ListenStrong(_ => collectionUpdates++);
        IListener pageListener = page.KeysCell.Updates().ListenStrong(_ => pageUpdates++);

        IListener operationsListener =
            page.KeyChangesStream.ListenStrong(change => pageOperations.AddRange(change.Operations.Select(Describe)));

        IReadOnlyList<string> before = rows.Items.Sample();

        // A new name does not change the sort value, and a new score between the two adjacent
        // scores sorts the key to its current position. The two come to the page as updates and
        // move no key, thus no keys cell sends a value and the projection gives the same list.
        edits.Send(TestUtil.Rename(key: 2, name: "renamed"));
        edits.Send(TestUtil.Score(key: 2, score: 25));

        await Assert.That(collectionUpdates).IsEqualTo(0);
        await Assert.That(pageUpdates).IsEqualTo(0);
        await Assert.That(ReferenceEquals(objA: rows.Items.Sample(), objB: before)).IsTrue();

        await Assert.That(pageOperations)
            .IsEquivalentTo(expected: ["ViewUpdate:2", "ViewUpdate:2"], ordering: CollectionOrdering.Matching);

        // The sort holds the score that it did not publish. Key 3 is at 25 and not at 20, thus it
        // goes between key 1 and key 2.
        edits.Send(TestUtil.Score(key: 3, score: 22));

        await Assert.That(pageUpdates).IsEqualTo(1);
        await Assert.That(KeysOf(page)).IsEquivalentTo(expected: [1, 3, 2], ordering: CollectionOrdering.Matching);

        // A departure from the filter changes the members, and key 4 enters the page behind it.
        edits.Send(TestUtil.Score(key: 1, score: 500));

        await Assert.That(pageUpdates).IsEqualTo(2);
        await Assert.That(KeysOf(page)).IsEquivalentTo(expected: [3, 2, 4], ordering: CollectionOrdering.Matching);

        // No part of that changed the arrival order of the collection. An add changes it.
        await Assert.That(collectionUpdates).IsEqualTo(0);

        edits.Send(TestUtil.Add(TestUtil.Item(number: 5, name: "five", score: 5)));

        await Assert.That(collectionUpdates).IsEqualTo(1);
        await Assert.That(pageUpdates).IsEqualTo(3);
        await Assert.That(KeysOf(page)).IsEquivalentTo(expected: [5, 3, 2], ordering: CollectionOrdering.Matching);

        collectionListener.Unlisten();
        pageListener.Unlisten();
        operationsListener.Unlisten();
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

        // A limit of zero keeps no key that left, and each key that is here.
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

        // No key left the view, thus an eviction removed no object and this code builds none
        // again.
        edits.Send(TestUtil.Rename(key: 2, name: "renamed"));

        await Assert.That(projections).IsEqualTo(3);
        await Assert.That(ReferenceEquals(objA: mapped.Items.Sample()[1], objB: first[1])).IsTrue();

        // A removal of one key removes its object, because the key left and the limit keeps
        // none.
        edits.Send(TestUtil.Remove(2));

        await Assert.That(evicted).IsEquivalentTo(expected: ["row 2"], ordering: CollectionOrdering.Matching);
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

        // The rows never left the view, thus an eviction never ran for them. A disposal releases
        // them, and without a disposal they continue after the code that built them.
        mapped.Dispose();

        await Assert.That(released).IsEquivalentTo(expected: ["row 1", "row 2"], ordering: CollectionOrdering.Any);

        // A second disposal does not release them again.
        mapped.Dispose();

        await Assert.That(released).IsEquivalentTo(expected: ["row 1", "row 2"], ordering: CollectionOrdering.Any);
    }

    /// <summary>
    ///     A stage builds again, and does not change its keys incrementally, when a
    ///     change costs more operations than a new build. The limit needs a minimum
    ///     value. A limit from the size of the stage alone is zero for an empty stage,
    ///     and the first operation then causes a new build. For a filter that is a
    ///     test of the full upstream, and a reset for each stage below.
    /// </summary>
    [Test]
    public async Task OneItemEnteringAnEmptyFilterIsReportedAsAnInsert()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        // The upstream is sufficiently large to make a new build expensive, and no key agrees with
        // the predicate.
        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial:
                [
                    .. Enumerable.Range(start: 1, count: 5_000)
                        .Select(static n => TestUtil.Item(number: n, name: $"n{n}", score: 0))
                ]);

        ReactiveCollection<int, ItemIdentity, ItemState> highScores =
            collection.Filter(static (_, state) => state.Score > 100);

        await Assert.That(KeysOf(highScores)).IsEmpty();

        List<bool> resets = [];
        List<string> operations = [];

        IListener l =
            highScores.KeyChangesStream.ListenStrong(change =>
            {
                resets.Add(change.IsReset);
                operations.AddRange(change.Operations.Select(Describe));
            });

        edits.Send(TestUtil.Score(key: 42, score: 999));

        l.Unlisten();

        await Assert.That(resets).IsEquivalentTo(expected: [false], ordering: CollectionOrdering.Matching);

        await Assert.That(operations)
            .IsEquivalentTo(expected: ["ViewInsert:42"], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(highScores)).IsEquivalentTo(expected: [42], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     The same floor, at the other end: the first item ever added to a
    ///     collection.
    /// </summary>
    [Test]
    public async Task TheFirstItemAddedToAnEmptyCollectionIsReportedAsAnInsert()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(edits: edits);

        List<bool> resets = [];
        List<string> operations = [];

        IListener l =
            collection.KeyChangesStream.ListenStrong(change =>
            {
                resets.Add(change.IsReset);
                operations.AddRange(change.Operations.Select(Describe));
            });

        edits.Send(TestUtil.Add(TestUtil.Item(number: 1, name: "one", score: 10)));

        l.Unlisten();

        await Assert.That(resets).IsEquivalentTo(expected: [false], ordering: CollectionOrdering.Matching);
        await Assert.That(operations).IsEquivalentTo(expected: ["ViewInsert:1"], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     Five thousand items with the scores 1 to 5,000. Each test of the limit
    ///     starts from them.
    /// </summary>
    private static ReactiveCollection<int, ItemIdentity, ItemState> FiveThousand(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits) =>
        Create(
            edits: edits,
            initial:
            [
                .. Enumerable.Range(start: 1, count: 5_000)
                    .Select(static n => TestUtil.Item(number: n, name: $"n{n}", score: n))
            ]);

    /// <summary>One edit that gives a new score to each of <paramref name="keys" />.</summary>
    private static CollectionEdit<int, ItemIdentity, ItemState> Rescore(
        IEnumerable<int> keys,
        Func<int, int> score) =>
        new(
            updates: keys.ToDictionary(
                keySelector: static key => key,
                elementSelector: key => (Func<ItemState, ItemState>)(state => state with { Score = score(key) })),
            adds: [],
            removes: []);

    /// <summary>
    ///     Records if each change from a view is a reset, and records its
    ///     operations.
    /// </summary>
    private static (List<bool> Resets, List<string> Kinds, IListener Listener) Record(
        ReactiveCollection<int, ItemIdentity, ItemState> view)
    {
        List<bool> resets = [];
        List<string> kinds = [];

        IListener listener =
            view.KeyChangesStream.ListenStrong(change =>
            {
                resets.Add(change.IsReset);

                kinds.AddRange(
                    change.Operations.Select(static operation =>
                        operation.GetType().Name.Replace(oldValue: "`1", newValue: string.Empty)));
            });

        return (resets, kinds, listener);
    }

    /// <summary>
    ///     A limit counts the work that a change costs a stage, and not the number of
    ///     keys in that change. The root uses the order of arrival, and a state edit
    ///     cannot move a key in that order. Thus, an update costs the root one lookup.
    ///     Three thousand updates, which is far above its limit of 1,000, come as a
    ///     list of operations and not as a reset.
    /// </summary>
    [Test]
    public async Task TheRootListsALargeEditThatOnlyUpdates()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = FiveThousand(edits);

        (List<bool> resets, List<string> kinds, IListener l) = Record(collection);

        edits.Send(Rescore(keys: Enumerable.Range(start: 1, count: 3_000), score: static key => key + 10_000));

        l.Unlisten();

        await Assert.That(resets).IsEquivalentTo(expected: [false], ordering: CollectionOrdering.Matching);
        await Assert.That(kinds.Count).IsEqualTo(3_000);

        await Assert.That(kinds.Distinct())
            .IsEquivalentTo(expected: ["ViewUpdate"], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     This is the condition that causes the rule: a large update to items that a
    ///     filter does not show. The filter omits each one, thus no change comes to
    ///     the stages below it. A reset at the root makes each stage below build
    ///     again.
    /// </summary>
    [Test]
    public async Task ALargeUpdateToItemsAFilterDoesNotShowReachesNothingBelowIt()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = FiveThousand(edits);

        ReactiveCollection<int, ItemIdentity, ItemState> highScores =
            collection.Filter(static (_, state) => state.Score > 4_000);

        ReactiveCollection<int, ItemIdentity, ItemState> sorted =
            highScores.SortByDescending(static (_, state) => state.Score);

        (List<bool> filterResets, _, IListener filterListener) = Record(highScores);
        (List<bool> sortResets, _, IListener sortListener) = Record(sorted);

        // These are three thousand of the items that the filter does not show, and each one stays
        // below its limit.
        edits.Send(Rescore(keys: Enumerable.Range(start: 1, count: 3_000), score: static key => key + 1));

        filterListener.Unlisten();
        sortListener.Unlisten();

        await Assert.That(filterResets).IsEmpty();
        await Assert.That(sortResets).IsEmpty();
        await Assert.That(KeysOf(sorted).Count).IsEqualTo(1_000);
    }

    /// <summary>
    ///     The order of a filter is the order of its upstream collection, and a state
    ///     edit cannot move a key in the order of the root. Thus, an update to an item
    ///     that the filter shows also costs one lookup, and the change comes as a list
    ///     of operations and not as a reset.
    /// </summary>
    [Test]
    public async Task AFilterListsALargeUpdateToItemsItShowsInAnOrderThatCannotMoveThem()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = FiveThousand(edits);

        ReactiveCollection<int, ItemIdentity, ItemState> positive =
            collection.Filter(static (_, state) => state.Score > 0);

        ReactiveCollection<int, ItemIdentity, ItemState> odd =
            collection.FilterByIdentity(static identity => identity.Number % 2 == 1);

        (List<bool> positiveResets, List<string> positiveKinds, IListener positiveListener) = Record(positive);
        (List<bool> oddResets, List<string> oddKinds, IListener oddListener) = Record(odd);

        edits.Send(Rescore(keys: Enumerable.Range(start: 1, count: 3_000), score: static key => key + 1));

        positiveListener.Unlisten();
        oddListener.Unlisten();

        await Assert.That(positiveResets).IsEquivalentTo(expected: [false], ordering: CollectionOrdering.Matching);
        await Assert.That(positiveKinds.Count).IsEqualTo(3_000);

        await Assert.That(oddResets).IsEquivalentTo(expected: [false], ordering: CollectionOrdering.Matching);
        await Assert.That(oddKinds.Count).IsEqualTo(1_500);
    }

    /// <summary>
    ///     The same applies to a sort whose own order a state edit cannot move, when
    ///     the change also holds an add. Such a change does not use the short path of
    ///     a change with only updates.
    /// </summary>
    [Test]
    public async Task ASortByKeyListsALargeUpdateThatArrivesWithAnInsert()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = FiveThousand(edits);

        ReactiveCollection<int, ItemIdentity, ItemState> byNumber =
            collection.SortByIdentity(static identity => -identity.Number);

        (List<bool> resets, List<string> kinds, IListener l) = Record(byNumber);

        edits.Send(
            Rescore(keys: Enumerable.Range(start: 1, count: 3_000), score: static key => key + 1)
                .CombineWith(TestUtil.Add(TestUtil.Item(number: 9_999, name: "new", score: 0))));

        l.Unlisten();

        await Assert.That(resets).IsEquivalentTo(expected: [false], ordering: CollectionOrdering.Matching);
        await Assert.That(kinds.Count(static kind => kind == "ViewInsert")).IsEqualTo(1);
        await Assert.That(kinds.Count(static kind => kind == "ViewUpdate")).IsEqualTo(3_000);
        await Assert.That(KeysOf(byNumber)[0]).IsEqualTo(9_999);
    }

    /// <summary>
    ///     Where a state edit costs work in a tree, which is a sort in an order that
    ///     reads the state, the limit applies. A change above that limit causes a new
    ///     build and not a list of operations.
    /// </summary>
    [Test]
    public async Task ASortByStateStillResetsOnALargeUpdateItHasToReFile()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = FiveThousand(edits);

        ReactiveCollection<int, ItemIdentity, ItemState> byScore =
            collection.SortBy(static (_, state) => state.Score);

        (List<bool> resets, _, IListener l) = Record(byScore);

        // This moves the first 3,000 keys to the front of the order.
        edits.Send(Rescore(keys: Enumerable.Range(start: 1, count: 3_000), score: static key => 20_000 - key));

        l.Unlisten();

        await Assert.That(resets).IsEquivalentTo(expected: [true], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(byScore)[0]).IsEqualTo(3_001);
        await Assert.That(KeysOf(byScore).Last()).IsEqualTo(1);
    }

    /// <summary>
    ///     The limit applies. A change whose list of operations costs more than a new
    ///     build comes as a reset, with no operations to apply.
    /// </summary>
    [Test]
    public async Task AChangeLargerThanTheBudgetIsReportedAsAReset()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial:
                [
                    .. Enumerable.Range(start: 1, count: 2_000)
                        .Select(static n => TestUtil.Item(number: n, name: $"n{n}", score: n))
                ]);

        List<bool> resets = [];
        List<int> operationCounts = [];

        IListener l =
            collection.KeyChangesStream.ListenStrong(change =>
            {
                resets.Add(change.IsReset);
                operationCounts.Add(change.Operations.Count);
            });

        // This is far above max(1000, 2000 / 10).
        edits.Send(TestUtil.Remove([.. Enumerable.Range(start: 1, count: 1_500)]));

        l.Unlisten();

        await Assert.That(resets).IsEquivalentTo(expected: [true], ordering: CollectionOrdering.Matching);
        await Assert.That(operationCounts).IsEquivalentTo(expected: [0], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(collection).Count).IsEqualTo(500);
    }

    /// <summary>
    ///     A change of the predicate names the keys that entered and the keys that
    ///     left, and it is not a reset. Thus, a consumer can move the rows that it has
    ///     and does not build the list again.
    /// </summary>
    [Test]
    public async Task ChangingThePredicateNamesWhatEnteredAndLeft()
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

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [2, 3], ordering: CollectionOrdering.Matching);

        List<string> operations = [];

        IListener l =
            passing.KeyChangesStream.ListenStrong(change => operations.AddRange(change.Operations.Select(Describe)));

        // One key enters at the front and no key leaves.
        threshold.Send(5);

        await Assert.That(operations).IsEquivalentTo(expected: ["ViewInsert:1"], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);

        operations.Clear();

        // Two keys go out and no key enters.
        threshold.Send(25);

        await Assert.That(operations)
            .IsEquivalentTo(expected: ["ViewRemove:1", "ViewRemove:2"], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [3], ordering: CollectionOrdering.Matching);

        l.Unlisten();
    }

    /// <summary>
    ///     A predicate that gives the same answers for the collection is no
    ///     change.
    /// </summary>
    [Test]
    public async Task ChangingThePredicateToOneThatChoosesTheSameItemsReportsNothing()
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

        int changes = 0;
        IListener l = passing.KeyChangesStream.ListenStrong(_ => changes++);

        // This is a different limit that accepts the same two items.
        threshold.Send(15);

        l.Unlisten();

        await Assert.That(changes).IsEqualTo(0);
        await Assert.That(KeysOf(passing)).IsEquivalentTo(expected: [2, 3], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A stage below the filter gets the same change: operations to apply, and not
    ///     a reset that tells it to build again.
    /// </summary>
    [Test]
    public async Task AStageBelowAPredicateChangeIsNotResetEither()
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

        ReactiveCollection<int, ItemIdentity, ItemState> sorted =
            collection
                .Filter(criteriaCell: threshold, predicate: static (limit, _, state) => state.Score >= limit)
                .SortByDescending(static (_, state) => state.Score);

        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [3, 2], ordering: CollectionOrdering.Matching);

        List<bool> resets = [];
        List<string> operations = [];

        IListener l =
            sorted.KeyChangesStream.ListenStrong(change =>
            {
                resets.Add(change.IsReset);
                operations.AddRange(change.Operations.Select(Describe));
            });

        threshold.Send(5);

        l.Unlisten();

        await Assert.That(resets).IsEquivalentTo(expected: [false], ordering: CollectionOrdering.Matching);
        await Assert.That(operations).IsEquivalentTo(expected: ["ViewInsert:1"], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(sorted)).IsEquivalentTo(expected: [3, 2, 1], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A filter over <see cref="FiveThousand" /> that keeps each key from the
    ///     criteria up. One form tests the state, and one form tests the identity.
    ///     The score of each item is its number, thus the two forms keep the same
    ///     keys.
    /// </summary>
    private static ReactiveCollection<int, ItemIdentity, ItemState> FromMinimum(
        ReactiveCollection<int, ItemIdentity, ItemState> collection,
        Cell<int> minimum,
        bool byIdentity) =>
        byIdentity
            ? collection.FilterByIdentity(
                criteriaCell: minimum,
                predicate: static (limit, identity) => identity.Number >= limit)
            : collection.Filter(
                criteriaCell: minimum,
                predicate: static (limit, _, state) => state.Score >= limit);

    /// <summary>
    ///     A filter over <see cref="FiveThousand" /> that keeps 2,000 keys from the
    ///     criteria up, in the same two forms.
    /// </summary>
    private static ReactiveCollection<int, ItemIdentity, ItemState> TwoThousandFrom(
        ReactiveCollection<int, ItemIdentity, ItemState> collection,
        Cell<int> first,
        bool byIdentity) =>
        byIdentity
            ? collection.FilterByIdentity(
                criteriaCell: first,
                predicate: static (from, identity) => identity.Number >= from && identity.Number < from + 2_000)
            : collection.Filter(
                criteriaCell: first,
                predicate: static (from, _, state) => state.Score >= from && state.Score < from + 2_000);

    /// <summary>An insert or a removal as "type:key@index".</summary>
    private static string DescribePosition(ViewOperation<int> operation) =>
        operation switch
        {
            ViewInsert<int> insert => $"ViewInsert:{insert.Key}@{insert.Index}",
            ViewRemove<int> remove => $"ViewRemove:{remove.Key}@{remove.Index}",
            _ => Describe(operation)
        };

    /// <summary>
    ///     Records if each change from a view is a reset, and records each operation
    ///     with its position.
    /// </summary>
    private static (List<bool> Resets, List<string> Operations, IListener Listener) RecordPositions(
        ReactiveCollection<int, ItemIdentity, ItemState> view)
    {
        List<bool> resets = [];
        List<string> operations = [];

        IListener listener =
            view.KeyChangesStream.Listen(change =>
            {
                resets.Add(change.IsReset);
                operations.AddRange(change.Operations.Select(DescribePosition));
            });

        return (resets, operations, listener);
    }

    /// <summary>
    ///     A change of criteria that moves as many keys as the limit of the filter is
    ///     a list of operations, and not a reset. The filter names each key in the
    ///     order of the stage above. Each operation gives the position of its key at
    ///     that point in the sequence.
    /// </summary>
    private static async Task ACriteriaChangeAtTheLimitNamesEachKey(bool byIdentity)
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<int> minimum = Cell.CreateSink(1);

        ReactiveCollection<int, ItemIdentity, ItemState> view =
            FromMinimum(collection: FiveThousand(edits), minimum: minimum, byIdentity: byIdentity);

        await Assert.That(KeysOf(view).Count).IsEqualTo(5_000);

        (List<bool> resets, List<string> operations, IListener listener) = RecordPositions(view);

        // The limit of a filter that holds 5,000 keys is 1,000 operations. This takes the keys
        // 1 to 1,000 out, and each of them is the first key when the filter removes it.
        minimum.Send(1_001);

        await Assert.That(resets).IsEquivalentTo(expected: [false], ordering: CollectionOrdering.Matching);

        await Assert.That(operations)
            .IsEquivalentTo(
                expected: [.. Enumerable.Range(start: 1, count: 1_000).Select(static key => $"ViewRemove:{key}@0")],
                ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(view))
            .IsEquivalentTo(
                expected: [.. Enumerable.Range(start: 1_001, count: 4_000)],
                ordering: CollectionOrdering.Matching);

        resets.Clear();
        operations.Clear();

        // The filter holds 4,000 keys, and its limit is 1,000 operations still. This puts the
        // 1,000 keys back, each at the position of its arrival.
        minimum.Send(1);

        listener.Unlisten();

        await Assert.That(resets).IsEquivalentTo(expected: [false], ordering: CollectionOrdering.Matching);

        await Assert.That(operations)
            .IsEquivalentTo(
                expected:
                [
                    .. Enumerable.Range(start: 1, count: 1_000).Select(static key => $"ViewInsert:{key}@{key - 1}")
                ],
                ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(view))
            .IsEquivalentTo(expected: [.. Enumerable.Range(start: 1, count: 5_000)], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public Task AFilterNamesEachKeyOfACriteriaChangeAtItsLimit() =>
        ACriteriaChangeAtTheLimitNamesEachKey(byIdentity: false);

    [Test]
    public Task AFilterByIdentityNamesEachKeyOfACriteriaChangeAtItsLimit() =>
        ACriteriaChangeAtTheLimitNamesEachKey(byIdentity: true);

    /// <summary>
    ///     A change of criteria that moves one key more than the limit of the filter
    ///     is a reset, with no operation. The keys of the filter are the keys that a
    ///     list of operations gives.
    /// </summary>
    private static async Task ACriteriaChangeOverTheLimitIsAReset(bool byIdentity)
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<int> minimum = Cell.CreateSink(1);

        ReactiveCollection<int, ItemIdentity, ItemState> view =
            FromMinimum(collection: FiveThousand(edits), minimum: minimum, byIdentity: byIdentity);

        await Assert.That(KeysOf(view).Count).IsEqualTo(5_000);

        (List<bool> resets, List<string> operations, IListener listener) = RecordPositions(view);

        // This takes the keys 1 to 1,001 out, which is one key more than the limit.
        minimum.Send(1_002);

        await Assert.That(resets).IsEquivalentTo(expected: [true], ordering: CollectionOrdering.Matching);
        await Assert.That(operations).IsEmpty();

        await Assert.That(KeysOf(view))
            .IsEquivalentTo(
                expected: [.. Enumerable.Range(start: 1_002, count: 3_999)],
                ordering: CollectionOrdering.Matching);

        resets.Clear();

        // This puts the 1,001 keys back, which is one key more than the limit also.
        minimum.Send(1);

        listener.Unlisten();

        await Assert.That(resets).IsEquivalentTo(expected: [true], ordering: CollectionOrdering.Matching);
        await Assert.That(operations).IsEmpty();

        await Assert.That(KeysOf(view))
            .IsEquivalentTo(expected: [.. Enumerable.Range(start: 1, count: 5_000)], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public Task AFilterBuildsAgainAtACriteriaChangeOverItsLimit() => ACriteriaChangeOverTheLimitIsAReset(byIdentity: false);

    [Test]
    public Task AFilterByIdentityBuildsAgainAtACriteriaChangeOverItsLimit() =>
        ACriteriaChangeOverTheLimitIsAReset(byIdentity: true);

    /// <summary>
    ///     One change of criteria can take keys out and put keys in. The operations
    ///     are in the order of the stage above. Thus, the keys that come in are first
    ///     in one direction, and the keys that go out are first in the opposite one.
    /// </summary>
    private static async Task ACriteriaChangeMovesKeysOutAndIn(bool byIdentity)
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<int> first = Cell.CreateSink(301);

        ReactiveCollection<int, ItemIdentity, ItemState> view =
            TwoThousandFrom(collection: FiveThousand(edits), first: first, byIdentity: byIdentity);

        await Assert.That(KeysOf(view))
            .IsEquivalentTo(expected: [.. Enumerable.Range(start: 301, count: 2_000)], ordering: CollectionOrdering.Matching);

        (List<bool> resets, List<string> operations, IListener listener) = RecordPositions(view);

        // The keys 1 to 300 come in at the front. Then the keys 2,001 to 2,300 go out, and each
        // of them is at the position 2,000 when the filter removes it.
        first.Send(1);

        await Assert.That(resets).IsEquivalentTo(expected: [false], ordering: CollectionOrdering.Matching);

        await Assert.That(operations)
            .IsEquivalentTo(
                expected:
                [
                    .. Enumerable.Range(start: 1, count: 300).Select(static key => $"ViewInsert:{key}@{key - 1}"),
                    .. Enumerable.Range(start: 2_001, count: 300).Select(static key => $"ViewRemove:{key}@2000")
                ],
                ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(view))
            .IsEquivalentTo(expected: [.. Enumerable.Range(start: 1, count: 2_000)], ordering: CollectionOrdering.Matching);

        resets.Clear();
        operations.Clear();

        // The keys 1 to 300 go out from the front. Then the keys 2,001 to 2,300 come in at the
        // end of a set that holds 1,700 keys.
        first.Send(301);

        listener.Unlisten();

        await Assert.That(resets).IsEquivalentTo(expected: [false], ordering: CollectionOrdering.Matching);

        await Assert.That(operations)
            .IsEquivalentTo(
                expected:
                [
                    .. Enumerable.Range(start: 1, count: 300).Select(static key => $"ViewRemove:{key}@0"),
                    .. Enumerable.Range(start: 2_001, count: 300).Select(static key => $"ViewInsert:{key}@{key - 301}")
                ],
                ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(view))
            .IsEquivalentTo(expected: [.. Enumerable.Range(start: 301, count: 2_000)], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public Task AFilterMovesKeysOutAndInAtOneCriteriaChange() => ACriteriaChangeMovesKeysOutAndIn(byIdentity: false);

    [Test]
    public Task AFilterByIdentityMovesKeysOutAndInAtOneCriteriaChange() =>
        ACriteriaChangeMovesKeysOutAndIn(byIdentity: true);

    /// <summary>
    ///     At a change of criteria that its limit permits, a filter tests each key one
    ///     time. At a change over its limit, it stops at the key that passes the
    ///     limit, and then the build tests each key one time.
    /// </summary>
    private static async Task ACriteriaChangeTestsEachKeyOneTime(bool byIdentity)
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<int> minimum = Cell.CreateSink(1);
        ReactiveCollection<int, ItemIdentity, ItemState> collection = FiveThousand(edits);
        int tests = 0;

        ReactiveCollection<int, ItemIdentity, ItemState> view =
            byIdentity
                ? collection.FilterByIdentity(
                    criteriaCell: minimum,
                    predicate: (limit, identity) =>
                    {
                        tests++;
                        return identity.Number >= limit;
                    })
                : collection.Filter(
                    criteriaCell: minimum,
                    predicate: (limit, _, state) =>
                    {
                        tests++;
                        return state.Score >= limit;
                    });

        // The first read builds the view.
        await Assert.That(KeysOf(view).Count).IsEqualTo(5_000);

        IListener listener =
            view.KeyChangesStream.Listen(static _ =>
            {
            });

        // 1,000 keys go out, which the limit permits.
        int testsAtTheStart = tests;
        minimum.Send(1_001);
        int testsAtTheLimit = tests;

        await Assert.That(KeysOf(view).Count).IsEqualTo(4_000);
        await Assert.That(testsAtTheLimit - testsAtTheStart).IsEqualTo(5_000);

        // The keys 1,001 to 2,001 go out, which is one key more than the limit. The filter tests
        // the keys 1 to 2,001 before it knows that, and then the build tests the 5,000 keys.
        minimum.Send(2_002);
        int testsOverTheLimit = tests;

        listener.Unlisten();

        await Assert.That(KeysOf(view).Count).IsEqualTo(2_999);
        await Assert.That(testsOverTheLimit - testsAtTheLimit).IsEqualTo(7_001);
    }

    [Test]
    public Task AFilterTestsEachKeyOneTimeAtACriteriaChange() => ACriteriaChangeTestsEachKeyOneTime(byIdentity: false);

    [Test]
    public Task AFilterByIdentityTestsEachKeyOneTimeAtACriteriaChange() =>
        ACriteriaChangeTestsEachKeyOneTime(byIdentity: true);

    /// <summary>
    ///     A filter over <see cref="FiveThousand" /> that drops each key whose
    ///     number is a multiple of the criteria. A criteria of zero drops no key.
    ///     Thus, the keys that a criteria drops are spread through the order. One
    ///     form tests the state, and one form tests the identity.
    /// </summary>
    private static ReactiveCollection<int, ItemIdentity, ItemState> DropMultiples(
        ReactiveCollection<int, ItemIdentity, ItemState> collection,
        Cell<int> multiple,
        bool byIdentity) =>
        byIdentity
            ? collection.FilterByIdentity(
                criteriaCell: multiple,
                predicate: static (step, identity) => step == 0 || identity.Number % step != 0)
            : collection.Filter(
                criteriaCell: multiple,
                predicate: static (step, _, state) => step == 0 || state.Score % step != 0);

    /// <summary>The numbers from 1 to 5,000 that are not a multiple of <paramref name="step" />.</summary>
    private static List<int> NotMultiplesOf(int step) =>
        [.. Enumerable.Range(start: 1, count: 5_000).Where(n => step == 0 || n % step != 0)];

    /// <summary>
    ///     A filter builds its set again at a change of criteria over its limit, from
    ///     the entries of the set above. It removes the keys that fail from that set,
    ///     or builds a set of the keys that pass, or keeps the set above. The three
    ///     give the keys that pass, in the order of the stage above.
    /// </summary>
    private static async Task EachBuildOfAFilterGivesTheKeysThatPass(bool byIdentity)
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<int> multiple = Cell.CreateSink(0);
        ReactiveCollection<int, ItemIdentity, ItemState> collection = FiveThousand(edits);
        ReactiveCollection<int, ItemIdentity, ItemState> view = DropMultiples(collection: collection, multiple: multiple, byIdentity: byIdentity);

        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: NotMultiplesOf(0), ordering: CollectionOrdering.Matching);

        (List<bool> resets, List<string> _, IListener listener) = RecordPositions(view);

        // 1,250 keys go out, which is over the limit. A quarter of the keys fail, thus the filter
        // removes them from the set above.
        multiple.Send(4);

        await Assert.That(resets).IsEquivalentTo(expected: [true], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: NotMultiplesOf(4), ordering: CollectionOrdering.Matching);

        // 1,250 more keys go out. A half of the keys fail, thus the filter builds a set of the
        // keys that pass.
        resets.Clear();
        multiple.Send(2);

        await Assert.That(resets).IsEquivalentTo(expected: [true], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: NotMultiplesOf(2), ordering: CollectionOrdering.Matching);

        // Keys go out and come in. A third of the keys fail, thus the filter removes them.
        resets.Clear();
        multiple.Send(3);

        await Assert.That(resets).IsEquivalentTo(expected: [true], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: NotMultiplesOf(3), ordering: CollectionOrdering.Matching);

        // Each key comes back. No key fails, thus the set of the filter is the set above.
        resets.Clear();
        multiple.Send(0);

        listener.Unlisten();

        await Assert.That(resets).IsEquivalentTo(expected: [true], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: NotMultiplesOf(0), ordering: CollectionOrdering.Matching);
        await Assert.That(ReferenceEquals(objA: view.KeysCell.Sample(), objB: collection.KeysCell.Sample())).IsTrue();
    }

    [Test]
    public Task EachBuildOfAFilterGivesTheKeysThatPass() => EachBuildOfAFilterGivesTheKeysThatPass(byIdentity: false);

    [Test]
    public Task EachBuildOfAFilterByIdentityGivesTheKeysThatPass() =>
        EachBuildOfAFilterGivesTheKeysThatPass(byIdentity: true);

    /// <summary>
    ///     A filter that is off keeps each key. At a reset from above, its set is the
    ///     set above, with no copy. When it turns on, it builds a set of its own.
    /// </summary>
    private static async Task AFilterThatKeepsEachKeyHoldsTheSetAbove(bool byIdentity)
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<int> minimum = Cell.CreateSink(1);
        CellSink<int> multiple = Cell.CreateSink(0);

        ReactiveCollection<int, ItemIdentity, ItemState> above =
            FromMinimum(collection: FiveThousand(edits), minimum: minimum, byIdentity: !byIdentity);

        ReactiveCollection<int, ItemIdentity, ItemState> view = DropMultiples(collection: above, multiple: multiple, byIdentity: byIdentity);

        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: NotMultiplesOf(0), ordering: CollectionOrdering.Matching);

        (List<bool> resets, List<string> _, IListener listener) = RecordPositions(view);

        // 1,499 keys go out of the stage above, which is over its limit. Thus, this filter gets a
        // reset, and it keeps each key of the new set above.
        minimum.Send(1_500);

        await Assert.That(resets).IsEquivalentTo(expected: [true], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(view))
            .IsEquivalentTo(expected: [.. Enumerable.Range(start: 1_500, count: 3_501)], ordering: CollectionOrdering.Matching);

        await Assert.That(ReferenceEquals(objA: view.KeysCell.Sample(), objB: above.KeysCell.Sample())).IsTrue();

        // The filter turns on, and drops each even key above, which is over its limit.
        resets.Clear();
        multiple.Send(2);

        listener.Unlisten();

        await Assert.That(resets).IsEquivalentTo(expected: [true], ordering: CollectionOrdering.Matching);

        await Assert.That(KeysOf(view))
            .IsEquivalentTo(
                expected: [.. Enumerable.Range(start: 1_500, count: 3_501).Where(static n => n % 2 != 0)],
                ordering: CollectionOrdering.Matching);

        await Assert.That(ReferenceEquals(objA: view.KeysCell.Sample(), objB: above.KeysCell.Sample())).IsFalse();
    }

    [Test]
    public Task AFilterThatKeepsEachKeyHoldsTheSetAbove() => AFilterThatKeepsEachKeyHoldsTheSetAbove(byIdentity: false);

    [Test]
    public Task AFilterByIdentityThatKeepsEachKeyHoldsTheSetAbove() =>
        AFilterThatKeepsEachKeyHoldsTheSetAbove(byIdentity: true);

    /// <summary>
    ///     A filter below a sort keeps the order of the sort when it builds again,
    ///     from the entries of the sort. When the sort reads its set from the end, the
    ///     filter builds from the keys of that view, in that order.
    /// </summary>
    [Test]
    public async Task AFilterBelowASortBuildsInTheOrderOfTheSort()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        Func<ItemIdentity, ItemState, int> byScore = static (_, state) => state.Score;

        KeyOrder<int, ItemIdentity, ItemState> descending =
            KeyOrder<int, ItemIdentity, ItemState>.By(
                selector: byScore,
                sortComparer: Comparer<int>.Default,
                keyComparer: Comparer<int>.Default,
                isDescending: true);

        KeyOrder<int, ItemIdentity, ItemState> ascending =
            KeyOrder<int, ItemIdentity, ItemState>.By(
                selector: byScore,
                sortComparer: Comparer<int>.Default,
                keyComparer: Comparer<int>.Default,
                isDescending: false);

        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order = Cell.CreateSink(descending);
        CellSink<int> multiple = Cell.CreateSink(0);

        ReactiveCollection<int, ItemIdentity, ItemState> view =
            DropMultiples(collection: FiveThousand(edits).SortBy(order), multiple: multiple, byIdentity: false);

        IListener listener =
            view.KeyChangesStream.Listen(static _ =>
            {
            });

        // The filter builds from the entries of the sort, and removes a quarter of them.
        multiple.Send(4);

        await Assert.That(KeysOf(view))
            .IsEquivalentTo(expected: [.. NotMultiplesOf(4).AsEnumerable().Reverse()], ordering: CollectionOrdering.Matching);

        // The sort reads its set from the end. The filter moves its keys to that order.
        order.Send(ascending);

        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: NotMultiplesOf(4), ordering: CollectionOrdering.Matching);

        // The filter builds from the keys of the view that the sort holds.
        multiple.Send(2);

        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: NotMultiplesOf(2), ordering: CollectionOrdering.Matching);

        multiple.Send(0);

        listener.Unlisten();

        await Assert.That(KeysOf(view)).IsEquivalentTo(expected: NotMultiplesOf(0), ordering: CollectionOrdering.Matching);
    }

    // A negative count or offset is an error in the arithmetic of the caller. The constant
    // overloads throw at the call, where the clamp hid it.
    [Test]
    public async Task ANegativeConstantCountOrOffsetThrows()
    {
        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>(),
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        await Assert.That(() => _ = collection.Take(-1)).ThrowsExactly<ArgumentOutOfRangeException>();

        await Assert.That(() => _ = collection.Slice(offset: -1, limit: 1))
            .ThrowsExactly<ArgumentOutOfRangeException>();

        await Assert.That(() => _ = collection.Slice(offset: 0, limit: -1))
            .ThrowsExactly<ArgumentOutOfRangeException>();

        // Zero is a window with nothing in it, and not an error.
        await Assert.That(KeysOf(collection.Take(0))).IsEmpty();
        await Assert.That(KeysOf(collection.Slice(offset: 0, limit: 0))).IsEmpty();
    }

    // A cell can move to a negative value in a transaction, and a throw there fails that
    // transaction. Thus, the cell overloads count a negative value as zero.
    [Test]
    public async Task ANegativeCountOrOffsetInACellCountsAsZero()
    {
        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>(),
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        CellSink<int> limit = Cell.CreateSink(1);
        CellSink<int> offset = Cell.CreateSink(1);
        ReactiveCollection<int, ItemIdentity, ItemState> window = collection.Take(limit);

        ReactiveCollection<int, ItemIdentity, ItemState> page =
            collection.Slice(offsetCell: offset, limitCell: Cell.Constant(1));

        await Assert.That(KeysOf(window)).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);
        await Assert.That(KeysOf(page)).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);

        limit.Send(-1);
        offset.Send(-1);

        await Assert.That(KeysOf(window)).IsEmpty();
        await Assert.That(KeysOf(page)).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A key with no order of its own, which the collection has to list
    ///     without comparing.
    /// </summary>
    private sealed record Handle(int Number);

    /// <summary>
    ///     Answers with int.MinValue and int.MaxValue rather than -1 and 1, which
    ///     is allowed.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class ExtremeComparer : IComparer<int>
    {
        internal static readonly ExtremeComparer Instance = new();

        public int Compare(int x, int y) =>
            x < y
                ? int.MinValue
                : x > y
                    ? int.MaxValue
                    : 0;
    }
}
