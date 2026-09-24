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
    ///     A state edit cannot move a key into an identity filter or out of it, and it can move a
    ///     key in that filter. The filter keeps the order of its upstream collection, and an
    ///     upstream that sorts on the state moves keys at a state edit. This stage must follow that
    ///     move and must report its own positions, and not the positions of the upstream.
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
    ///     An update that moves no key changes the sort value of that key. A filter that only sends
    ///     the update on keeps the previous value, and sorts the next arrival against it.
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
    }

    /// <summary>
    ///     A stage builds again, and does not change its keys incrementally, when a change costs
    ///     more operations than a new build. The limit needs a minimum value. A limit from the size
    ///     of the stage alone is zero for an empty stage, and the first operation then causes a new
    ///     build. For a filter that is a test of the full upstream, and a reset for each stage
    ///     below.
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

    /// <summary>The same floor, at the other end: the first item ever added to a collection.</summary>
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

    /// <summary>Five thousand items with the scores 1 to 5,000. Each test of the limit starts from
    /// them.</summary>
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

    /// <summary>Records if each change from a view is a reset, and records its operations.</summary>
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
    ///     A limit counts the work that a change costs a stage, and not the number of keys in that
    ///     change. The root uses the order of arrival, and a state edit cannot move a key in that
    ///     order. Thus, an update costs the root one lookup. Three thousand updates, which is far
    ///     above its limit of 1,000, come as a list of operations and not as a reset.
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
    ///     This is the condition that causes the rule: a large update to items that a filter does
    ///     not show. The filter omits each one, thus no change comes to the stages below it. A
    ///     reset at the root makes each stage below build again.
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
    ///     The order of a filter is the order of its upstream collection, and a state edit cannot
    ///     move a key in the order of the root. Thus, an update to an item that the filter shows also
    ///     costs one lookup, and the change comes as a list of operations and not as a reset.
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
    ///     The same applies to a sort whose own order a state edit cannot move, when the change
    ///     also holds an add. Such a change does not use the short path of a change with only
    ///     updates.
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
    ///     Where a state edit costs work in a tree, which is a sort in an order that reads the
    ///     state, the limit applies. A change above that limit causes a new build and not a list of
    ///     operations.
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
    ///     The limit applies. A change whose list of operations costs more than a new build comes
    ///     as a reset, with no operations to apply.
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
    ///     A change of the predicate names the keys that entered and the keys that left, and it is
    ///     not a reset. Thus, a consumer can move the rows that it has and does not build the list
    ///     again.
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

    /// <summary>A predicate that gives the same answers for the collection is no change.</summary>
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
    ///     A stage below the filter gets the same change: operations to apply, and not a reset that
    ///     tells it to build again.
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

    /// <summary>A key with no order of its own, which the collection has to list without comparing.</summary>
    private sealed record Handle(int Number);

    /// <summary>Answers with int.MinValue and int.MaxValue rather than -1 and 1, which is allowed.</summary>
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
