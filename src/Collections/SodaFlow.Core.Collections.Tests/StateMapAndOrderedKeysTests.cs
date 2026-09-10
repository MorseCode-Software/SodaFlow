using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

public sealed class StateMapTests
{
    private static string StateOf(IStateMap<int, string> states, int key) =>
        states.TryGetState(key, out string state) ? state : "?";

    [Test]
    public async Task WithAppliesUpdatesAndRemovalsAndLeavesTheOriginalAlone()
    {
        IStateMap<int, string> first = ImmutableStateMap<int, string>.Empty.With(
            new Dictionary<int, string> { [1] = "one", [2] = "two" },
            []);

        IStateMap<int, string> second = first.With(
            new Dictionary<int, string> { [2] = "TWO", [3] = "three" },
            [1]);

        await Assert.That(first.Count).IsEqualTo(2);
        await Assert.That(StateOf(first, 1)).IsEqualTo("one");
        await Assert.That(StateOf(first, 2)).IsEqualTo("two");

        await Assert.That(second.Count).IsEqualTo(2);
        await Assert.That(second.ContainsKey(1)).IsFalse();
        await Assert.That(StateOf(second, 2)).IsEqualTo("TWO");
        await Assert.That(StateOf(second, 3)).IsEqualTo("three");
    }

    [Test]
    public async Task WithNothingToDoReturnsTheSameInstance()
    {
        IStateMap<int, string> map = ImmutableStateMap<int, string>.Empty.With(
            new Dictionary<int, string> { [1] = "one" },
            []);

        IStateMap<int, string> same = map.With(new Dictionary<int, string>(), []);

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
            identities.ToImmutable(),
            ImmutableStateMap<int, ItemState>.Empty.With(states, []));
    }

    private static SortKeyOrder<int, ItemIdentity, ItemState, int> ByScore(bool descending) =>
        new(
            static (_, _, state) => state.Score,
            Comparer<int>.Default,
            Comparer<int>.Default,
            descending);

    private static IOrderedKeys<int, ItemIdentity, ItemState> Empty(
        bool descending,
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot) =>
        ByScore(descending).CreateFrom([], snapshot);

    [Test]
    public async Task KeysComeBackInSortOrderAndIndexOfAgrees()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot = Snapshot(
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(3, "three", 20));

        IOrderedKeys<int, ItemIdentity, ItemState> keys = Empty(false, snapshot)
            .Add(1, snapshot)
            .Add(2, snapshot)
            .Add(3, snapshot);

        await Assert.That(TestUtil.Keys(keys)).IsEquivalentTo([2, 3, 1]);
        await Assert.That(keys.IndexOf(1)).IsEqualTo(2);
        await Assert.That(keys.IndexOf(99)).IsEqualTo(-1);
        await Assert.That(keys.Contains(3)).IsTrue();
        await Assert.That(keys[0]).IsEqualTo(2);
    }

    [Test]
    public async Task EqualSortValuesAreBrokenByKeySoTheOrderIsTotal()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot = Snapshot(
            TestUtil.Item(5, "five", 10),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(9, "nine", 10));

        IOrderedKeys<int, ItemIdentity, ItemState> keys = Empty(false, snapshot)
            .Add(5, snapshot)
            .Add(2, snapshot)
            .Add(9, snapshot);

        await Assert.That(TestUtil.Keys(keys)).IsEquivalentTo([2, 5, 9]);
    }

    [Test]
    public async Task DescendingReversesTheSortComparisonOnly()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot = Snapshot(
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(3, "three", 20));

        IOrderedKeys<int, ItemIdentity, ItemState> keys = Empty(true, snapshot)
            .Add(1, snapshot)
            .Add(2, snapshot)
            .Add(3, snapshot);

        await Assert.That(TestUtil.Keys(keys)).IsEquivalentTo([1, 3, 2]);
    }

    [Test]
    public async Task AddingAKeyTheSnapshotDoesNotHaveIsANoOp()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot = Snapshot(TestUtil.Item(1, "one", 30));

        IOrderedKeys<int, ItemIdentity, ItemState> keys = Empty(false, snapshot).Add(99, snapshot);

        await Assert.That(keys.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CreateFromFilesEveryKeyAsAddWouldHaveOneAtATime()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> snapshot = Snapshot(
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(3, "three", 20));

        // What a stage rebuild takes, against what it used to take. The bulk path exists because
        // filing n keys one at a time is n persistent writes; it has to land them in the same
        // places.
        IOrderedKeys<int, ItemIdentity, ItemState> inBulk =
            ByScore(false).CreateFrom([1, 2, 3, 99], snapshot);

        IOrderedKeys<int, ItemIdentity, ItemState> oneAtATime = Empty(false, snapshot)
            .Add(1, snapshot)
            .Add(2, snapshot)
            .Add(3, snapshot)
            .Add(99, snapshot);

        await Assert.That(TestUtil.Keys(inBulk)).IsEquivalentTo([2, 3, 1]);
        await Assert.That(TestUtil.Keys(inBulk)).IsEquivalentTo(TestUtil.Keys(oneAtATime));

        // Including the key the snapshot does not have, which neither path files.
        await Assert.That(inBulk.Contains(99)).IsFalse();
        await Assert.That(inBulk.IndexOf(3)).IsEqualTo(1);
    }

    [Test]
    public async Task ARemovedKeyIsFoundByTheComparisonThatFiledIt()
    {
        CollectionSnapshot<int, ItemIdentity, ItemState> before = Snapshot(
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10));

        IOrderedKeys<int, ItemIdentity, ItemState> keys = Empty(false, before).Add(1, before).Add(2, before);

        // The item's sort value has moved underneath the set. Removal still finds it, because the
        // entry carries the value it was filed under rather than being re-projected here.
        CollectionSnapshot<int, ItemIdentity, ItemState> after = Snapshot(
            TestUtil.Item(1, "one", 5),
            TestUtil.Item(2, "two", 10));

        IOrderedKeys<int, ItemIdentity, ItemState> refiled = keys.Remove(1).Add(1, after);

        await Assert.That(TestUtil.Keys(refiled)).IsEquivalentTo([1, 2]);
    }
}
