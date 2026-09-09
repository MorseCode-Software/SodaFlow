using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

public sealed class StateMapTests
{
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
        await Assert.That(first.Lookup(1).Match(static v => v, static () => "?")).IsEqualTo("one");
        await Assert.That(first.Lookup(2).Match(static v => v, static () => "?")).IsEqualTo("two");

        await Assert.That(second.Count).IsEqualTo(2);
        await Assert.That(second.ContainsKey(1)).IsFalse();
        await Assert.That(second.Lookup(2).Match(static v => v, static () => "?")).IsEqualTo("TWO");
        await Assert.That(second.Lookup(3).Match(static v => v, static () => "?")).IsEqualTo("three");
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
    private static CollectionSnapshot<int, ItemId, ItemState> Snapshot(
        params Entry<ItemId, ItemState>[] entries)
    {
        Dictionary<int, ItemId> identities = new();
        Dictionary<int, ItemState> states = new();

        foreach (Entry<ItemId, ItemState> entry in entries)
        {
            identities[entry.Identity.Number] = entry.Identity;
            states[entry.Identity.Number] = entry.State;
        }

        return new CollectionSnapshot<int, ItemId, ItemState>(
            identities,
            ImmutableStateMap<int, ItemState>.Empty.With(states, []));
    }

    private static IOrderedKeys<int, ItemId, ItemState> ByScore(bool descending) =>
        new SortKeyOrder<int, ItemId, ItemState, int>(
                static (_, _, state) => state.Score,
                Comparer<int>.Default,
                Comparer<int>.Default,
                descending)
            .CreateEmpty();

    [Test]
    public async Task KeysComeBackInSortOrderAndIndexOfAgrees()
    {
        CollectionSnapshot<int, ItemId, ItemState> snapshot = Snapshot(
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(3, "three", 20));

        IOrderedKeys<int, ItemId, ItemState> keys = ByScore(false)
            .Add(1, snapshot)
            .Add(2, snapshot)
            .Add(3, snapshot);

        await Assert.That(TestUtil.Keys(keys)).IsEquivalentTo([2, 3, 1]);
        await Assert.That(keys.IndexOf(1).Match(static i => i, static () => -1)).IsEqualTo(2);
        await Assert.That(keys.IndexOf(99).Match(static i => i, static () => -1)).IsEqualTo(-1);
        await Assert.That(keys.Contains(3)).IsTrue();
        await Assert.That(keys[0]).IsEqualTo(2);
    }

    [Test]
    public async Task EqualSortValuesAreBrokenByKeySoTheOrderIsTotal()
    {
        CollectionSnapshot<int, ItemId, ItemState> snapshot = Snapshot(
            TestUtil.Item(5, "five", 10),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(9, "nine", 10));

        IOrderedKeys<int, ItemId, ItemState> keys = ByScore(false)
            .Add(5, snapshot)
            .Add(2, snapshot)
            .Add(9, snapshot);

        await Assert.That(TestUtil.Keys(keys)).IsEquivalentTo([2, 5, 9]);
    }

    [Test]
    public async Task DescendingReversesTheSortComparisonOnly()
    {
        CollectionSnapshot<int, ItemId, ItemState> snapshot = Snapshot(
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10),
            TestUtil.Item(3, "three", 20));

        IOrderedKeys<int, ItemId, ItemState> keys = ByScore(true)
            .Add(1, snapshot)
            .Add(2, snapshot)
            .Add(3, snapshot);

        await Assert.That(TestUtil.Keys(keys)).IsEquivalentTo([1, 3, 2]);
    }

    [Test]
    public async Task AddingAKeyTheSnapshotDoesNotHaveIsANoOp()
    {
        CollectionSnapshot<int, ItemId, ItemState> snapshot = Snapshot(TestUtil.Item(1, "one", 30));

        IOrderedKeys<int, ItemId, ItemState> keys = ByScore(false).Add(99, snapshot);

        await Assert.That(keys.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ARemovedKeyIsFoundByTheComparisonThatFiledIt()
    {
        CollectionSnapshot<int, ItemId, ItemState> before = Snapshot(
            TestUtil.Item(1, "one", 30),
            TestUtil.Item(2, "two", 10));

        IOrderedKeys<int, ItemId, ItemState> keys = ByScore(false).Add(1, before).Add(2, before);

        // The item's sort value has moved underneath the set. Removal still finds it, because the
        // entry carries the value it was filed under rather than being re-projected here.
        CollectionSnapshot<int, ItemId, ItemState> after = Snapshot(
            TestUtil.Item(1, "one", 5),
            TestUtil.Item(2, "two", 10));

        IOrderedKeys<int, ItemId, ItemState> refiled = keys.Remove(1).Add(1, after);

        await Assert.That(TestUtil.Keys(refiled)).IsEquivalentTo([1, 2]);
    }
}
