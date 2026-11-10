using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

public sealed class StateMapTests
{
    private static string StateOf(StateMap<int, string> states, int key) =>
        states.TryGetState(key: key, state: out string? state) ? state : "?";

    [Test]
    public async Task WithAppliesUpdatesAndRemovalsAndLeavesTheOriginalAlone()
    {
        ImmutableStateMap<int, string> first =
            ImmutableStateMap<string>.Create(EqualityComparer<int>.Default)
                .With(
                    updated: new Dictionary<int, string> { [1] = "one", [2] = "two" },
                    removed: []);

        ImmutableStateMap<int, string> second =
            first.With(
                updated: new Dictionary<int, string> { [2] = "TWO", [3] = "three" },
                removed: [1]);

        await Assert.That(first.Count).IsEqualTo(2);
        await Assert.That(StateOf(states: first, key: 1)).IsEqualTo("one");
        await Assert.That(StateOf(states: first, key: 2)).IsEqualTo("two");

        await Assert.That(second.Count).IsEqualTo(2);
        await Assert.That(second.ContainsKey(1)).IsFalse();
        await Assert.That(StateOf(states: second, key: 2)).IsEqualTo("TWO");
        await Assert.That(StateOf(states: second, key: 3)).IsEqualTo("three");
    }

    [Test]
    public async Task WithNothingToDoReturnsTheSameInstance()
    {
        ImmutableStateMap<int, string> map =
            ImmutableStateMap<string>.Create(EqualityComparer<int>.Default)
                .With(
                    updated: new Dictionary<int, string> { [1] = "one" },
                    removed: []);

        ImmutableStateMap<int, string> same = map.With(updated: new Dictionary<int, string>(), removed: []);

        await Assert.That(same).IsSameReferenceAs(map);
    }
}

public sealed class OrderedKeysTests
{
    private static CollectionSnapshot<int, ItemIdentity, ItemState> Snapshot(
        params Item<ItemIdentity, ItemState>[] items)
    {
        ImmutableDictionary<int, ItemIdentity>.Builder identities =
            ImmutableDictionary.CreateBuilder<int, ItemIdentity>();

        Dictionary<int, ItemState> states = new();

        ImmutableDictionary<int, long>.Builder arrivals = ImmutableDictionary.CreateBuilder<int, long>();

        foreach (Item<ItemIdentity, ItemState> item in items)
        {
            identities[item.Identity.Number] = item.Identity;
            states[item.Identity.Number] = item.State;
            arrivals[item.Identity.Number] = arrivals.Count;
        }

        return new CollectionSnapshot<int, ItemIdentity, ItemState>(
            identities: identities.ToImmutable(),
            states: ImmutableStateMap<ItemState>.Create(EqualityComparer<int>.Default)
                .With(updated: states, removed: []),
            arrivals: arrivals.ToImmutable(),
            nextArrival: arrivals.Count);
    }

    private static KeyOrder<int, ItemIdentity, ItemState> ByScore(bool isDescending) =>
        KeyOrder<int, ItemIdentity, ItemState>.By(
            selector: static (_, state) => state.Score,
            sortComparer: Comparer<int>.Default,
            keyComparer: Comparer<int>.Default,
            isDescending: isDescending);

    /// <summary>
    ///     One delegate for each direction, because an order identifies a level by
    ///     the selector instance.
    /// </summary>
    private static readonly Func<ItemIdentity, ItemState, string> ByName = static (_, state) => state.Name;

    private static readonly Func<ItemIdentity, string> ByCode = static identity => identity.Code;

    private static KeyOrder<int, ItemIdentity, ItemState> ByScoreThenName(
        bool scoreIsDescending,
        bool nameIsDescending) =>
        ByScore(scoreIsDescending)
            .ThenBy(selector: ByName, sortComparer: StringComparer.Ordinal, isDescending: nameIsDescending);

    private static KeyOrder<int, ItemIdentity, ItemState> ByScoreThenNameThenCode(bool isDescending) =>
        ByScoreThenName(scoreIsDescending: isDescending, nameIsDescending: isDescending)
            .ThenByIdentity(selector: ByCode, sortComparer: StringComparer.Ordinal, isDescending: isDescending);

    private static OrderedKeys<int, ItemIdentity, ItemState> Empty(
        bool isDescending,
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot) =>
        ByScore(isDescending).CreateFrom(keys: [], snapshot: snapshot);

    [Test]
    public async Task AnOrderIgnoresStateEditsOnlyIfEveryLevelDoes()
    {
        KeyOrder<int, ItemIdentity, ItemState> identityOnly =
            KeyOrder<int, ItemIdentity, ItemState>
                .ByIdentity(static identity => identity.Code)
                .ThenByIdentityDescending(static identity => identity.Number);

        KeyOrder<int, ItemIdentity, ItemState> stateSecond =
            KeyOrder<int, ItemIdentity, ItemState>
                .ByIdentity(static identity => identity.Code)
                .ThenBy(static (_, state) => state.Score);

        KeyOrder<int, ItemIdentity, ItemState> stateFirst =
            KeyOrder<int, ItemIdentity, ItemState>
                .By(static (_, state) => state.Score)
                .ThenByIdentity(static identity => identity.Code);

        // A stage omits a sort of a key at a state edit only when no level can move that key. Thus,
        // one level that reads the state must remove that short path.
        await Assert.That(KeyOrder<int, ItemIdentity, ItemState>.ByArrival().DependsOnState).IsFalse();
        await Assert.That(identityOnly.DependsOnState).IsFalse();
        await Assert.That(stateSecond.DependsOnState).IsTrue();
        await Assert.That(stateFirst.DependsOnState).IsTrue();
    }

    /// <summary>
    ///     A stage that gets an order equal to the order that it holds does nothing,
    ///     and a stage that gets that order in the opposite direction turns its list.
    ///     The stage does that only when it can identify the order. This code compares
    ///     two orders with the selector instance and the comparer instances of their
    ///     construction, thus a factory must give the same selector at each call.
    /// </summary>
    [Test]
    public async Task OrdersBuiltTheSameWayAreRecognisedAsTheSameOrder()
    {
        IComparer<int> keyComparer = Comparer<int>.Default;

        await Assert.That(
                KeyOrder<int, ItemIdentity, ItemState>.ByKey(keyComparer: keyComparer, isDescending: false)
                    .IsEquivalentTo(
                        KeyOrder<int, ItemIdentity, ItemState>.ByKey(keyComparer: keyComparer, isDescending: false)))
            .IsTrue();

        await Assert.That(
                KeyOrder<int, ItemIdentity, ItemState>.ByKey()
                    .IsEquivalentTo(KeyOrder<int, ItemIdentity, ItemState>.ByKey()))
            .IsTrue();

        await Assert.That(
                KeyOrder<int, ItemIdentity, ItemState>.ByKey()
                    .IsEquivalentTo(KeyOrder<int, ItemIdentity, ItemState>.ByKeyDescending()))
            .IsFalse();

        await Assert.That(
                KeyOrder<int, ItemIdentity, ItemState>.ByArrival()
                    .IsEquivalentTo(KeyOrder<int, ItemIdentity, ItemState>.ByArrival()))
            .IsTrue();

        await Assert.That(ByScore(isDescending: false).IsEquivalentTo(ByScore(isDescending: false))).IsTrue();

        // The same selector in the opposite direction is not the same order, and this code does
        // not accept a different comparer instance as equal.
        await Assert.That(ByScore(isDescending: false).IsEquivalentTo(ByScore(isDescending: true))).IsFalse();

        await Assert.That(
                KeyOrder<int, ItemIdentity, ItemState>.ByKey(keyComparer: keyComparer, isDescending: false)
                    .IsEquivalentTo(
                        KeyOrder<int, ItemIdentity, ItemState>.ByKey(
                            keyComparer: Comparer<int>.Create(static (x, y) => x.CompareTo(y)),
                            isDescending: false)))
            .IsFalse();

        // An order with more than one level is the same order when each level agrees. One level in
        // the opposite direction makes a different order. One level less, or a second level that
        // reads a different value, also makes a different order.
        await Assert.That(
                ByScoreThenName(scoreIsDescending: false, nameIsDescending: true)
                    .IsEquivalentTo(ByScoreThenName(scoreIsDescending: false, nameIsDescending: true)))
            .IsTrue();

        await Assert.That(
                ByScoreThenName(scoreIsDescending: false, nameIsDescending: true)
                    .IsEquivalentTo(ByScoreThenName(scoreIsDescending: false, nameIsDescending: false)))
            .IsFalse();

        await Assert.That(
                ByScoreThenName(scoreIsDescending: false, nameIsDescending: false)
                    .IsEquivalentTo(ByScore(isDescending: false)))
            .IsFalse();

        await Assert.That(
                ByScoreThenName(scoreIsDescending: false, nameIsDescending: false)
                    .IsEquivalentTo(ByScore(isDescending: false).ThenBy(static (_, state) => state.Name)))
            .IsFalse();

        await Assert.That(ByScoreThenNameThenCode(false).IsEquivalentTo(ByScoreThenNameThenCode(false))).IsTrue();
    }

    [Test]
    public async Task KeysComeBackInSortOrderAndIndexOfAgrees()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot =
            Snapshot(
                TestUtil.Item(number: 1, name: "one", score: 30),
                TestUtil.Item(number: 2, name: "two", score: 10),
                TestUtil.Item(number: 3, name: "three", score: 20));

        OrderedKeys<int, ItemIdentity, ItemState> keys =
            Empty(isDescending: false, snapshot: snapshot)
                .Add(key: 1, snapshot: snapshot)
                .Add(key: 2, snapshot: snapshot)
                .Add(key: 3, snapshot: snapshot);

        await Assert.That(TestUtil.Keys(keys))
            .IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);

        await Assert.That(keys.IndexOfInternal(1)).IsEqualTo(2);
        await Assert.That(keys.IndexOfInternal(99)).IsEqualTo(-1);
        await Assert.That(keys.Contains(3)).IsTrue();
        await Assert.That(keys[0]).IsEqualTo(2);
    }

    [Test]
    public async Task EqualSortValuesAreBrokenByKeySoTheOrderIsTotal()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot =
            Snapshot(
                TestUtil.Item(number: 5, name: "five", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 10),
                TestUtil.Item(number: 9, name: "nine", score: 10));

        OrderedKeys<int, ItemIdentity, ItemState> keys =
            Empty(isDescending: false, snapshot: snapshot)
                .Add(key: 5, snapshot: snapshot)
                .Add(key: 2, snapshot: snapshot)
                .Add(key: 9, snapshot: snapshot);

        await Assert.That(TestUtil.Keys(keys))
            .IsEquivalentTo(expected: [2, 5, 9], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task DescendingReversesTheSortComparisonOnly()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot =
            Snapshot(
                TestUtil.Item(number: 1, name: "one", score: 30),
                TestUtil.Item(number: 2, name: "two", score: 10),
                TestUtil.Item(number: 3, name: "three", score: 20));

        OrderedKeys<int, ItemIdentity, ItemState> keys =
            Empty(isDescending: true, snapshot: snapshot)
                .Add(key: 1, snapshot: snapshot)
                .Add(key: 2, snapshot: snapshot)
                .Add(key: 3, snapshot: snapshot);

        await Assert.That(TestUtil.Keys(keys))
            .IsEquivalentTo(expected: [1, 3, 2], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     An order in the opposite direction is not the list in the opposite
    ///     direction. A descending order puts the sort values in the opposite
    ///     direction and keeps the ascending key as its last level. Thus, two keys
    ///     with equal sort values keep their sequence when the stage turns the list,
    ///     and a simple reversal of the positions does not keep it.
    /// </summary>
    [Test]
    public async Task ReversingAnOrderKeepsTiedKeysInKeyOrder()
    {
        // There are three scores, two keys or three keys have each score, and this code adds them
        // in a sequence that is not the key order.
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot =
            Snapshot(
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 10),
                TestUtil.Item(number: 4, name: "four", score: 20),
                TestUtil.Item(number: 5, name: "five", score: 30),
                TestUtil.Item(number: 6, name: "six", score: 10),
                TestUtil.Item(number: 7, name: "seven", score: 30),
                TestUtil.Item(number: 8, name: "eight", score: 20));

        int[] keys = [1, 2, 3, 4, 5, 6, 7, 8];

        OrderedKeys<int, ItemIdentity, ItemState> ascending =
            ByScore(isDescending: false).CreateFrom(keys: keys, snapshot: snapshot);

        await Assert.That(TestUtil.Keys(ascending))
            .IsEquivalentTo(expected: [1, 3, 6, 2, 4, 8, 5, 7], ordering: CollectionOrdering.Matching);

        KeyOrder<int, ItemIdentity, ItemState> descending = ByScore(isDescending: true);

        // This test must measure the reversal, and not a new build, which hides an incorrect
        // reversal.
        bool reversed =
            descending.TryReverse(keys: ascending, reversedKeys: out OrderedKeys<int, ItemIdentity, ItemState>? result);

        await Assert.That(reversed).IsTrue();
        await Assert.That(result).IsNotNull();

        // The keys with each score are in ascending key order, as a new descending order puts
        // them. This is not [7, 5, 8, 4, 2, 6, 3, 1], which is the ascending list in the opposite
        // direction.
#pragma warning disable CS8604 // Possible null reference argument.
        await Assert.That(TestUtil.Keys(result))
#pragma warning restore CS8604 // Possible null reference argument.
            .IsEquivalentTo(expected: [5, 7, 2, 4, 8, 1, 3, 6], ordering: CollectionOrdering.Matching);

#pragma warning disable CS8604 // Possible null reference argument.
        await Assert.That(TestUtil.Keys(result))
#pragma warning restore CS8604 // Possible null reference argument.
            .IsEquivalentTo(
                expected: TestUtil.Keys(descending.CreateFrom(keys: keys, snapshot: snapshot)),
                ordering: CollectionOrdering.Matching);

        // The set in the opposite direction answers for itself, in the order that it now holds.
        await Assert.That(ReferenceEquals(objA: result.Order, objB: descending)).IsTrue();

        foreach ((int key, int index) in TestUtil.Keys(result).Select(static (key, index) => (key, index)))
        {
            await Assert.That(result.IndexOfInternal(key)).IsEqualTo(index);
        }
    }

    /// <summary>
    ///     The set in the opposite direction reads the set that it came from, and does
    ///     not copy it. Thus, each read maps its position through the runs of equal
    ///     sort values, and an edit must keep that map correct. This test compares
    ///     each answer against a new build in the same order. It does that after a
    ///     reversal, after two adds, after two removals, and after a reversal back.
    ///     One add goes into a run, and the other makes a run. One removal takes a
    ///     key out of a run, and the other removes a full run.
    /// </summary>
    [Test]
    public async Task AReversedSetAnswersEachPositionAsANewBuildDoes()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot =
            Snapshot(
                TestUtil.Item(number: 0, name: "zero", score: 20),
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20),
                TestUtil.Item(number: 3, name: "three", score: 10),
                TestUtil.Item(number: 4, name: "four", score: 20),
                TestUtil.Item(number: 5, name: "five", score: 30),
                TestUtil.Item(number: 6, name: "six", score: 10),
                TestUtil.Item(number: 7, name: "seven", score: 30),
                TestUtil.Item(number: 8, name: "eight", score: 20),
                TestUtil.Item(number: 9, name: "nine", score: 25));

        List<int> keys = [1, 2, 3, 4, 5, 6, 7, 8];

        KeyOrder<int, ItemIdentity, ItemState> ascending = ByScore(isDescending: false);
        KeyOrder<int, ItemIdentity, ItemState> descending = ByScore(isDescending: true);

        OrderedKeys<int, ItemIdentity, ItemState> set = ascending.CreateFrom(keys: keys, snapshot: snapshot);

        set = Reverse(order: descending, keys: set);
        await AssertAnswersAsANewBuild(set: set, order: descending, keys: keys, snapshot: snapshot);

        // Key 0 goes into the run of 20, at its start. Key 9 then makes a run of its own, between
        // the run of 30 and the run of 20.
        keys.Add(0);
        set = set.Add(key: 0, snapshot: snapshot);
        await AssertAnswersAsANewBuild(set: set, order: descending, keys: keys, snapshot: snapshot);

        keys.Add(9);
        set = set.Add(key: 9, snapshot: snapshot);
        await AssertAnswersAsANewBuild(set: set, order: descending, keys: keys, snapshot: snapshot);

        // Out of the middle of the run of 20, and then the full run of 25.
        keys.Remove(2);
        set = set.Remove(2);
        await AssertAnswersAsANewBuild(set: set, order: descending, keys: keys, snapshot: snapshot);

        keys.Remove(9);
        set = set.Remove(9);
        await AssertAnswersAsANewBuild(set: set, order: descending, keys: keys, snapshot: snapshot);

        // A key that the set does not hold.
        await Assert.That(set.Contains(99)).IsFalse();
        await Assert.That(set.IndexOfInternal(99)).IsEqualTo(-1);
        await Assert.That(set.Remove(99)).IsSameReferenceAs(set);

        // Back to the ascending direction, and then to the descending direction again.
        set = Reverse(order: ascending, keys: set);
        await AssertAnswersAsANewBuild(set: set, order: ascending, keys: keys, snapshot: snapshot);

        set = Reverse(order: descending, keys: set);
        await AssertAnswersAsANewBuild(set: set, order: descending, keys: keys, snapshot: snapshot);
    }

    /// <summary>
    ///     An order with more than one level reverses when each level changes
    ///     direction. A run is then the keys with equal sort values at each level.
    ///     The test has ties at the first level that the second level breaks,
    ///     and ties at both levels that the key breaks. An order whose levels change
    ///     direction one by one is a different order, and does not reverse.
    /// </summary>
    [Test]
    public async Task AReversedSetWithMoreThanOneLevelAnswersEachPositionAsANewBuildDoes()
    {
        // The scores tie in runs, and the names tie inside those runs. Thus, the second level
        // decides between some keys with equal scores, and the key decides between the rest.
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot =
            Snapshot(
                TestUtil.Item(number: 0, name: "b", score: 20),
                TestUtil.Item(number: 1, name: "a", score: 10),
                TestUtil.Item(number: 2, name: "a", score: 20),
                TestUtil.Item(number: 3, name: "b", score: 10),
                TestUtil.Item(number: 4, name: "b", score: 20),
                TestUtil.Item(number: 5, name: "a", score: 30),
                TestUtil.Item(number: 6, name: "a", score: 10),
                TestUtil.Item(number: 7, name: "a", score: 30),
                TestUtil.Item(number: 8, name: "a", score: 20),
                TestUtil.Item(number: 9, name: "a", score: 25));

        List<int> keys = [1, 2, 3, 4, 5, 6, 7, 8];

        KeyOrder<int, ItemIdentity, ItemState> scoreUpNameDown =
            ByScoreThenName(scoreIsDescending: false, nameIsDescending: true);

        KeyOrder<int, ItemIdentity, ItemState> scoreDownNameUp =
            ByScoreThenName(scoreIsDescending: true, nameIsDescending: false);

        OrderedKeys<int, ItemIdentity, ItemState> set = scoreUpNameDown.CreateFrom(keys: keys, snapshot: snapshot);

        await Assert.That(TestUtil.Keys(set))
            .IsEquivalentTo(expected: [3, 1, 6, 4, 2, 8, 5, 7], ordering: CollectionOrdering.Matching);

        // One level in the opposite direction is a different order. It keeps the sequence of the
        // second level inside each run of the first, and no read of the set from its end does.
        await Assert.That(
                ByScoreThenName(scoreIsDescending: true, nameIsDescending: true)
                    .TryReverse(keys: set, reversedKeys: out OrderedKeys<int, ItemIdentity, ItemState>? _))
            .IsFalse();

        set = Reverse(order: scoreDownNameUp, keys: set);

        await Assert.That(TestUtil.Keys(set))
            .IsEquivalentTo(expected: [5, 7, 2, 8, 4, 1, 6, 3], ordering: CollectionOrdering.Matching);

        await AssertAnswersAsANewBuild(set: set, order: scoreDownNameUp, keys: keys, snapshot: snapshot);

        // Key 0 goes into the run of 20 and "b", at its start. Key 9 then makes a run of its own.
        keys.Add(0);
        set = set.Add(key: 0, snapshot: snapshot);
        await AssertAnswersAsANewBuild(set: set, order: scoreDownNameUp, keys: keys, snapshot: snapshot);

        keys.Add(9);
        set = set.Add(key: 9, snapshot: snapshot);
        await AssertAnswersAsANewBuild(set: set, order: scoreDownNameUp, keys: keys, snapshot: snapshot);

        keys.Remove(2);
        set = set.Remove(2);
        await AssertAnswersAsANewBuild(set: set, order: scoreDownNameUp, keys: keys, snapshot: snapshot);

        keys.Remove(9);
        set = set.Remove(9);
        await AssertAnswersAsANewBuild(set: set, order: scoreDownNameUp, keys: keys, snapshot: snapshot);

        set = Reverse(order: scoreUpNameDown, keys: set);
        await AssertAnswersAsANewBuild(set: set, order: scoreUpNameDown, keys: keys, snapshot: snapshot);

        // A third level is a pair in a pair, and the levels still agree one by one.
        KeyOrder<int, ItemIdentity, ItemState> threeLevelsDown = ByScoreThenNameThenCode(true);

        set = ByScoreThenNameThenCode(false).CreateFrom(keys: keys, snapshot: snapshot);
        set = Reverse(order: threeLevelsDown, keys: set);
        await AssertAnswersAsANewBuild(set: set, order: threeLevelsDown, keys: keys, snapshot: snapshot);
    }

    private static OrderedKeys<int, ItemIdentity, ItemState> Reverse(
        KeyOrder<int, ItemIdentity, ItemState> order,
        OrderedKeys<int, ItemIdentity, ItemState> keys)
    {
        // This test must measure the reversal, and not a new build, which hides an incorrect
        // reversal.
        bool reversed =
            order.TryReverse(keys: keys, reversedKeys: out OrderedKeys<int, ItemIdentity, ItemState>? result);

        return reversed && result is not null
            ? result
            : throw new InvalidOperationException("The order did not reverse the keys.");
    }

    private static async Task AssertAnswersAsANewBuild(
        OrderedKeys<int, ItemIdentity, ItemState> set,
        KeyOrder<int, ItemIdentity, ItemState> order,
        IEnumerable<int> keys,
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot)
    {
        List<int> expected = TestUtil.Keys(order.CreateFrom(keys: keys, snapshot: snapshot));

        await Assert.That(TestUtil.Keys(set)).IsEquivalentTo(expected: expected, ordering: CollectionOrdering.Matching);
        await Assert.That(set.Count).IsEqualTo(expected.Count);
        await Assert.That(ReferenceEquals(objA: set.Order, objB: order)).IsTrue();

        for (int index = 0; index < expected.Count; index++)
        {
            await Assert.That(set[index]).IsEqualTo(expected[index]);
            await Assert.That(set.IndexOfInternal(expected[index])).IsEqualTo(index);
            await Assert.That(set.Contains(expected[index])).IsTrue();
        }

        await Assert.That(() => set[-1]).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => set[expected.Count]).Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     A key that the snapshot does not hold has no sort value, and each stage
    ///     adds only the keys in its snapshot. Thus, a call to add such a key is a
    ///     defect. This code throws an exception and does not omit the key with no
    ///     message, at an add of one key and at a build of a full set.
    /// </summary>
    [Test]
    public async Task FilingAKeyTheSnapshotDoesNotHoldThrows()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot =
            Snapshot(TestUtil.Item(number: 1, name: "one", score: 30));

        OrderedKeys<int, ItemIdentity, ItemState> empty = Empty(isDescending: false, snapshot: snapshot);

        await Assert.That(() => empty.Add(key: 99, snapshot: snapshot)).Throws<InvalidOperationException>();

        await Assert.That(() => ByScore(isDescending: false).CreateFrom(keys: [1, 99], snapshot: snapshot))
            .Throws<InvalidOperationException>();

        await Assert
            .That(() => KeyOrder<int, ItemIdentity, ItemState>.ByArrival().CreateFrom(keys: [99], snapshot: snapshot))
            .Throws<InvalidOperationException>();

        await Assert.That(() =>
                KeyOrder<int, ItemIdentity, ItemState>.ByIdentity(static identity => identity.Code)
                    .CreateFrom(keys: [99], snapshot: snapshot))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CreateFromFilesEveryKeyAsAddWouldHaveOneAtATime()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot =
            Snapshot(
                TestUtil.Item(number: 1, name: "one", score: 30),
                TestUtil.Item(number: 2, name: "two", score: 10),
                TestUtil.Item(number: 3, name: "three", score: 20));

        // This compares the input of a new build of a stage against the input before this change.
        // The path for all keys together is here because n keys one at a time are n immutable
        // writes. It must put the keys at the same positions.
        OrderedKeys<int, ItemIdentity, ItemState> inBulk =
            ByScore(false).CreateFrom(keys: [1, 2, 3], snapshot: snapshot);

        OrderedKeys<int, ItemIdentity, ItemState> oneAtATime =
            Empty(isDescending: false, snapshot: snapshot)
                .Add(key: 1, snapshot: snapshot)
                .Add(key: 2, snapshot: snapshot)
                .Add(key: 3, snapshot: snapshot);

        await Assert.That(TestUtil.Keys(inBulk))
            .IsEquivalentTo(expected: [2, 3, 1], ordering: CollectionOrdering.Matching);

        await Assert.That(TestUtil.Keys(inBulk))
            .IsEquivalentTo(expected: TestUtil.Keys(oneAtATime), ordering: CollectionOrdering.Matching);

        await Assert.That(inBulk.IndexOfInternal(3)).IsEqualTo(1);
    }

    [Test]
    public async Task ARemovedKeyIsFoundByTheComparisonThatFiledIt()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> before =
            Snapshot(
                TestUtil.Item(number: 1, name: "one", score: 30),
                TestUtil.Item(number: 2, name: "two", score: 10));

        OrderedKeys<int, ItemIdentity, ItemState> keys =
            Empty(isDescending: false, snapshot: before).Add(key: 1, snapshot: before).Add(key: 2, snapshot: before);

        // The sort value of the item changed below the set. A removal finds it, because the entry
        // holds the value of its position and this code does not calculate that value again.
        CollectionSnapshot<int, ItemIdentity, ItemState> after =
            Snapshot(
                TestUtil.Item(number: 1, name: "one", score: 5),
                TestUtil.Item(number: 2, name: "two", score: 10));

        OrderedKeys<int, ItemIdentity, ItemState> refiled = keys.Remove(1).Add(key: 1, snapshot: after);

        await Assert.That(TestUtil.Keys(refiled))
            .IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TheKeyMapAndTheOrderingStayInStepThroughAnySequence()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot =
            Snapshot(
                TestUtil.Item(number: 1, name: "one", score: 30),
                TestUtil.Item(number: 2, name: "two", score: 10),
                TestUtil.Item(number: 3, name: "three", score: 20),
                TestUtil.Item(number: 4, name: "four", score: 40),
                TestUtil.Item(number: 5, name: "five", score: 50));

        OrderedKeys<int, ItemIdentity, ItemState> keys = Empty(isDescending: false, snapshot: snapshot);

        // This is a sequence that adds a key, removes it, adds it again, and removes it again, with
        // a test after each step. This code writes the two structures together, and this test shows
        // that. Contains reads the map, IndexOf reads the two, and Count and the enumeration read
        // the ordering. Thus, the answers agree only when the two structures are equal.
        int[] toAdd = [3, 1, 5, 2, 4];

        foreach (int key in toAdd)
        {
            keys = keys.Add(key: key, snapshot: snapshot);
            await AssertConsistent(keys);
        }

        foreach (int key in new[] { 5, 1 })
        {
            keys = keys.Remove(key);
            await AssertConsistent(keys);
        }

        keys = keys.Add(key: 5, snapshot: snapshot);
        await AssertConsistent(keys);

        await Assert.That(TestUtil.Keys(keys))
            .IsEquivalentTo(expected: [2, 3, 4, 5], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task ReAddingAKeyAlreadyFiledDoesNotFileItTwice()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> before =
            Snapshot(
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        OrderedKeys<int, ItemIdentity, ItemState> keys =
            Empty(isDescending: false, snapshot: before).Add(key: 1, snapshot: before).Add(key: 2, snapshot: before);

        // The stages never do this, because a second sort removes the key before it adds the key.
        // The type does not give that rule. An add of one key two times, at two different sort
        // values, puts two entries in the ordering for one entry in the map. That is the one path
        // to a difference between the two structures, thus this test holds it.
        CollectionSnapshot<int, ItemIdentity, ItemState> after =
            Snapshot(
                TestUtil.Item(number: 1, name: "one", score: 99),
                TestUtil.Item(number: 2, name: "two", score: 20));

        keys = keys.Add(key: 1, snapshot: after);

        await AssertConsistent(keys);
        await Assert.That(keys.Count).IsEqualTo(2);
        await Assert.That(TestUtil.Keys(keys)).IsEquivalentTo(expected: [2, 1], ordering: CollectionOrdering.Matching);
    }

    /// <summary>The items 1 to <paramref name="count" />, each with a score of ten times its number.</summary>
    private static CollectionSnapshot<int, ItemIdentity, ItemState> Numbered(int count) =>
        Snapshot(
            [
                .. Enumerable.Range(start: 1, count: count)
                    .Select(static n => TestUtil.Item(number: n, name: "n" + n, score: 10 * n))
            ]);

    /// <summary>The keys of a rebase in their order, or a word for a null answer.</summary>
    private static string Describe(OrderedKeys<int, ItemIdentity, ItemState>? keys) =>
        keys is null ? "build" : string.Join(separator: ",", values: keys);

    [Test]
    public async Task ARebaseThatChangesNoKeyAnswersTheSameSet()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot = Numbered(4);

        OrderedKeys<int, ItemIdentity, ItemState> keys =
            ByScore(isDescending: false).CreateFrom(keys: [1, 2, 3, 4], snapshot: snapshot);

        // The members are a different set, in the opposite order, with the same keys.
        OrderedKeys<int, ItemIdentity, ItemState> members =
            ByScore(isDescending: true).CreateFrom(keys: [1, 2, 3, 4], snapshot: snapshot);

        await Assert.That(ReferenceEquals(objA: keys.Rebase(members: members, snapshot: snapshot), objB: keys))
            .IsTrue();

        // A set that reads a sorted set from its end answers itself also.
        OrderedKeys<int, ItemIdentity, ItemState> reversed = Reverse(order: ByScore(isDescending: true), keys: keys);

        await Assert.That(ReferenceEquals(objA: reversed.Rebase(members: members, snapshot: snapshot), objB: reversed))
            .IsTrue();
    }

    [Test]
    public async Task ARebaseCostsOneForEachKeyThatLeavesAndTwoForEachKeyThatComes()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot = Numbered(10);
        KeyOrder<int, ItemIdentity, ItemState> order = ByScore(isDescending: false);

        OrderedKeys<int, ItemIdentity, ItemState> ten =
            order.CreateFrom(keys: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10], snapshot: snapshot);

        OrderedKeys<int, ItemIdentity, ItemState> four = order.CreateFrom(keys: [1, 2, 3, 4], snapshot: snapshot);

        // Five keys leave, and five keys stay. The cost is five, which is the number of members.
        OrderedKeys<int, ItemIdentity, ItemState> five =
            ByScore(isDescending: true).CreateFrom(keys: [1, 2, 3, 4, 5], snapshot: snapshot);

        await Assert.That(Describe(ten.Rebase(members: five, snapshot: snapshot))).IsEqualTo("1,2,3,4,5");

        // Six keys leave, and four keys stay. The cost is six, which is more than four.
        await Assert.That(Describe(ten.Rebase(members: four, snapshot: snapshot))).IsEqualTo("build");

        // Four keys come to four keys. The cost is eight, which is the number of members.
        OrderedKeys<int, ItemIdentity, ItemState> eight =
            ByScore(isDescending: true).CreateFrom(keys: [1, 2, 3, 4, 5, 6, 7, 8], snapshot: snapshot);

        await Assert.That(Describe(four.Rebase(members: eight, snapshot: snapshot))).IsEqualTo("1,2,3,4,5,6,7,8");

        // One key leaves, and four keys come. The cost is nine, which is more than seven.
        OrderedKeys<int, ItemIdentity, ItemState> seven =
            order.CreateFrom(keys: [1, 2, 3, 5, 6, 7, 8], snapshot: snapshot);

        await Assert.That(Describe(four.Rebase(members: seven, snapshot: snapshot))).IsEqualTo("build");
    }

    [Test]
    public async Task ARebaseOfAWindowAnswersNull()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot = Numbered(4);

        OrderedKeys<int, ItemIdentity, ItemState> keys =
            ByScore(isDescending: false).CreateFrom(keys: [1, 2, 3, 4], snapshot: snapshot);

        // Only a sort asks for a rebase, and a sort holds a sorted set. Each other set answers null,
        // and a stage that holds it builds.
        OrderedKeys<int, ItemIdentity, ItemState> window =
            new RangeKeys<int, ItemIdentity, ItemState>(source: keys, offset: 1, limit: 2);

        await Assert.That(window.Rebase(members: keys, snapshot: snapshot)).IsNull();
    }

    /// <summary>
    ///     Each answer of a key set about itself, from the two structures together.
    /// </summary>
    private static async Task AssertConsistent(OrderedKeys<int, ItemIdentity, ItemState> keys)
    {
        List<int> enumerated = TestUtil.Keys(keys);

        await Assert.That(enumerated.Count).IsEqualTo(keys.Count);
        await Assert.That(enumerated.Distinct().Count()).IsEqualTo(keys.Count);

        for (int index = 0; index < enumerated.Count; index++)
        {
            int key = enumerated[index];

            // IndexOf reads the map and then the ordering. Thus, an answer equal to the position
            // from the enumeration shows that the two structures agree.
            await Assert.That(keys.IndexOfInternal(key)).IsEqualTo(index);
            await Assert.That(keys.Contains(key)).IsTrue();
            await Assert.That(keys[index]).IsEqualTo(key);
        }
    }
}
