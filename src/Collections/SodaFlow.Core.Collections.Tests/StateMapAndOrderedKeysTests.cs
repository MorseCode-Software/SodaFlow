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
        states.TryGetState(key: key, state: out string state) ? state : "?";

    [Test]
    public async Task WithAppliesUpdatesAndRemovalsAndLeavesTheOriginalAlone()
    {
        ImmutableStateMap<int, string> first =
            ImmutableStateMap<string>.Create(EqualityComparer<int>.Default).With(
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
            ImmutableStateMap<string>.Create(EqualityComparer<int>.Default).With(
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
        bool descending,
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot) =>
        ByScore(descending).CreateFrom(keys: [], snapshot: snapshot);

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

        // A stage skips re-filing a key on a state edit only when no level could have moved it, so
        // one level that reads the state has to be enough to lose the skip.
        await Assert.That(KeyOrder<int, ItemIdentity, ItemState>.ByArrival().DependsOnState).IsFalse();
        await Assert.That(identityOnly.DependsOnState).IsFalse();
        await Assert.That(stateSecond.DependsOnState).IsTrue();
        await Assert.That(stateFirst.DependsOnState).IsTrue();
    }

    /// <summary>
    ///     A stage handed an order it already holds does nothing, and one it holds run the other way
    ///     turns its list around - but only if it can tell. Orders are told apart by the selector and
    ///     comparer instances they were built from, so a factory has to hand out the same selector
    ///     every time it is called.
    /// </summary>
    [Test]
    public async Task OrdersBuiltTheSameWayAreRecognisedAsTheSameOrder()
    {
        IComparer<int> keyComparer = Comparer<int>.Default;

        await Assert.That(
                KeyOrder<int, ItemIdentity, ItemState>.ByKey(keyComparer)
                    .IsEquivalentTo(KeyOrder<int, ItemIdentity, ItemState>.ByKey(keyComparer)))
            .IsTrue();

        await Assert.That(
                KeyOrder<int, ItemIdentity, ItemState>.ByArrival()
                    .IsEquivalentTo(KeyOrder<int, ItemIdentity, ItemState>.ByArrival()))
            .IsTrue();

        await Assert.That(ByScore(isDescending: false).IsEquivalentTo(ByScore(isDescending: false))).IsTrue();

        // The same selector run the other way is not the same order, and a different comparer
        // instance is not taken on trust.
        await Assert.That(ByScore(isDescending: false).IsEquivalentTo(ByScore(isDescending: true))).IsFalse();

        await Assert.That(
                KeyOrder<int, ItemIdentity, ItemState>.ByKey(keyComparer)
                    .IsEquivalentTo(KeyOrder<int, ItemIdentity, ItemState>.ByKey(Comparer<int>.Create(static (x, y) => x.CompareTo(y)))))
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
            Empty(descending: false, snapshot: snapshot)
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
            Empty(descending: false, snapshot: snapshot)
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
            Empty(descending: true, snapshot: snapshot)
                .Add(key: 1, snapshot: snapshot)
                .Add(key: 2, snapshot: snapshot)
                .Add(key: 3, snapshot: snapshot);

        await Assert.That(TestUtil.Keys(keys))
            .IsEquivalentTo(expected: [1, 3, 2], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     Reversing an order is not reversing the list. A descending order reverses the sort values
    ///     and still breaks their ties by key ascending, so keys that tie keep their relative order
    ///     when the list is turned around - which a plain flip of the positions would not.
    /// </summary>
    [Test]
    public async Task ReversingAnOrderKeepsTiedKeysInKeyOrder()
    {
        // Three scores, each shared by two or three keys, filed out of key order.
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

        // The reversal has to be what is under test, not a rebuild that would hide a flip.
        bool reversed = descending.TryReverse(keys: ascending, reversedKeys: out OrderedKeys<int, ItemIdentity, ItemState>? result);

        await Assert.That(reversed).IsTrue();

        // Ties by key ascending within each score, as a descending order built afresh files them - and
        // not [7, 5, 8, 4, 2, 6, 3, 1], the ascending list flipped.
        await Assert.That(TestUtil.Keys(result!))
            .IsEquivalentTo(expected: [5, 7, 2, 4, 8, 1, 3, 6], ordering: CollectionOrdering.Matching);

        await Assert.That(TestUtil.Keys(result!))
            .IsEquivalentTo(
                expected: TestUtil.Keys(descending.CreateFrom(keys: keys, snapshot: snapshot)),
                ordering: CollectionOrdering.Matching);

        // And the reversed set answers for itself under the order it now carries.
        await Assert.That(ReferenceEquals(objA: result!.Order, objB: descending)).IsTrue();

        foreach ((int key, int index) in TestUtil.Keys(result!).Select(static (key, index) => (key, index)))
        {
            await Assert.That(result!.IndexOfInternal(key)).IsEqualTo(index);
        }
    }

    /// <summary>
    ///     A key the snapshot does not hold has no sort value, and every stage files only keys its
    ///     snapshot holds, so being asked to file one is a fault - thrown, not quietly left out, whether
    ///     one key is added or a set is built in bulk.
    /// </summary>
    [Test]
    public async Task FilingAKeyTheSnapshotDoesNotHoldThrows()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot =
            Snapshot(TestUtil.Item(number: 1, name: "one", score: 30));

        OrderedKeys<int, ItemIdentity, ItemState> empty = Empty(descending: false, snapshot: snapshot);

        await Assert.That(() => empty.Add(key: 99, snapshot: snapshot)).Throws<InvalidOperationException>();

        await Assert.That(() => ByScore(isDescending: false).CreateFrom(keys: [1, 99], snapshot: snapshot))
            .Throws<InvalidOperationException>();

        await Assert.That(() => KeyOrder<int, ItemIdentity, ItemState>.ByArrival().CreateFrom(keys: [99], snapshot: snapshot))
            .Throws<InvalidOperationException>();

        await Assert.That(
                () => KeyOrder<int, ItemIdentity, ItemState>.ByIdentity(static identity => identity.Code)
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

        // What a stage rebuild takes, against what it used to take. The bulk path exists because
        // filing n keys one at a time is n persistent writes; it has to land them in the same
        // places.
        OrderedKeys<int, ItemIdentity, ItemState> inBulk =
            ByScore(false).CreateFrom(keys: [1, 2, 3], snapshot: snapshot);

        OrderedKeys<int, ItemIdentity, ItemState> oneAtATime =
            Empty(descending: false, snapshot: snapshot)
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
            Empty(descending: false, snapshot: before).Add(key: 1, snapshot: before).Add(key: 2, snapshot: before);

        // The item's sort value has moved underneath the set. Removal still finds it, because the
        // entry carries the value it was filed under rather than being re-projected here.
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

        OrderedKeys<int, ItemIdentity, ItemState> keys = Empty(descending: false, snapshot: snapshot);

        // A sequence that adds, removes, re-adds and removes again, checked after every step. The
        // two structures are only ever written together, and this is what says so: Contains reads
        // the map, IndexOf reads both, and Count and the enumeration read the ordering - so they
        // can only agree if nothing has drifted.
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
            Empty(descending: false, snapshot: before).Add(key: 1, snapshot: before).Add(key: 2, snapshot: before);

        // The stages never do this - a re-file removes before it adds - but nothing about the type
        // says they must, and adding a key twice under two different sort values would put two
        // entries in the ordering under one entry in the map. That is the one way these two can be
        // made to disagree, so it is the one worth pinning down.
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
    ///     Everything a key set says about itself, asked of both structures at once.
    /// </summary>
    private static async Task AssertConsistent(OrderedKeys<int, ItemIdentity, ItemState> keys)
    {
        List<int> enumerated = TestUtil.Keys(keys);

        await Assert.That(enumerated.Count).IsEqualTo(keys.Count);
        await Assert.That(enumerated.Distinct().Count()).IsEqualTo(keys.Count);

        for (int index = 0; index < enumerated.Count; index++)
        {
            int key = enumerated[index];

            // IndexOf reads the map and then the ordering, so agreeing with the position the
            // enumeration gave is the two of them agreeing.
            await Assert.That(keys.IndexOfInternal(key)).IsEqualTo(index);
            await Assert.That(keys.Contains(key)).IsTrue();
            await Assert.That(keys[index]).IsEqualTo(key);
        }
    }
}
