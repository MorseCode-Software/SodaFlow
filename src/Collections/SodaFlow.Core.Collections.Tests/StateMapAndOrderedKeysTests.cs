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
    ///     A stage that gets an order equal to the order that it holds does nothing, and a stage
    ///     that gets that order in the opposite direction turns its list. The stage does that only
    ///     when it can identify the order. This code compares two orders with the selector instance
    ///     and the comparer instances of their construction, thus a factory must give the same
    ///     selector at each call.
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
    ///     An order in the opposite direction is not the list in the opposite direction. A
    ///     descending order puts the sort values in the opposite direction and keeps the ascending
    ///     key as its last level. Thus, two keys with equal sort values keep their sequence when the
    ///     stage turns the list, and a simple reversal of the positions does not keep it.
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
    ///     A key that the snapshot does not hold has no sort value, and each stage adds only the
    ///     keys in its snapshot. Thus, a call to add such a key is a defect. This code throws an
    ///     exception and does not omit the key with no message, at an add of one key and at a build
    ///     of a full set.
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
