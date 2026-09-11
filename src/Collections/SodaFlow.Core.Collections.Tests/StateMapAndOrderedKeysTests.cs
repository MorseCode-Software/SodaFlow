using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
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
            ImmutableStateMap<int, string>.Empty.With(
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
            ImmutableStateMap<int, string>.Empty.With(
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

        foreach (Item<ItemIdentity, ItemState> item in items)
        {
            identities[item.Identity.Number] = item.Identity;
            states[item.Identity.Number] = item.State;
        }

        return new CollectionSnapshot<int, ItemIdentity, ItemState>(
            identities: identities.ToImmutable(),
            states: ImmutableStateMap<int, ItemState>.Empty.With(updated: states, removed: []));
    }

    private static SortKeyOrder<int, ItemIdentity, ItemState, int> ByScore(bool descending) =>
        new(
            selector: static (_, _, state) => state.Score,
            sortComparer: Comparer<int>.Default,
            keyComparer: Comparer<int>.Default,
            descending: descending);

    private static OrderedKeys<int, ItemIdentity, ItemState> Empty(
        bool descending,
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot) =>
        ByScore(descending).CreateFrom(keys: [], snapshot: snapshot);

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

        await Assert.That(TestUtil.Keys(keys)).IsEquivalentTo([2, 3, 1]);
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

        await Assert.That(TestUtil.Keys(keys)).IsEquivalentTo([2, 5, 9]);
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

        await Assert.That(TestUtil.Keys(keys)).IsEquivalentTo([1, 3, 2]);
    }

    [Test]
    public async Task AddingAKeyTheSnapshotDoesNotHaveIsANoOp()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot =
            Snapshot(TestUtil.Item(number: 1, name: "one", score: 30));

        OrderedKeys<int, ItemIdentity, ItemState> keys =
            Empty(descending: false, snapshot: snapshot).Add(key: 99, snapshot: snapshot);

        await Assert.That(keys.Count).IsEqualTo(0);
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
            ByScore(false).CreateFrom(keys: [1, 2, 3, 99], snapshot: snapshot);

        OrderedKeys<int, ItemIdentity, ItemState> oneAtATime =
            Empty(descending: false, snapshot: snapshot)
                .Add(key: 1, snapshot: snapshot)
                .Add(key: 2, snapshot: snapshot)
                .Add(key: 3, snapshot: snapshot)
                .Add(key: 99, snapshot: snapshot);

        await Assert.That(TestUtil.Keys(inBulk)).IsEquivalentTo([2, 3, 1]);
        await Assert.That(TestUtil.Keys(inBulk)).IsEquivalentTo(TestUtil.Keys(oneAtATime));

        // Including the key the snapshot does not have, which neither path files.
        await Assert.That(inBulk.Contains(99)).IsFalse();
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

        await Assert.That(TestUtil.Keys(refiled)).IsEquivalentTo([1, 2]);
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

        await Assert.That(TestUtil.Keys(keys)).IsEquivalentTo([2, 3, 4, 5]);
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
        await Assert.That(TestUtil.Keys(keys)).IsEquivalentTo([2, 1]);
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
