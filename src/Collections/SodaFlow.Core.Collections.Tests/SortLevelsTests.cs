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

/// <summary>
///     Checks an order with each count of levels. An order with one level holds its
///     sort value in its own type. An order with two levels holds a pair of the two
///     types. An order with three levels or more holds one object for each level.
/// </summary>
public sealed class SortLevelsTests
{
    private const int MostLevels = 7;

    private static readonly Func<ItemIdentity, ItemState, int> ByParity = static (_, state) => state.Score % 2;

    private static readonly Func<ItemIdentity, ItemState, string> ByName = static (_, state) => state.Name;

    private static readonly Func<ItemIdentity, ItemState, long> ByThird = static (_, state) => state.Score % 3;

    // Each level after the first, with the same level for LINQ. LINQ is the reference for each
    // sequence, thus no test holds a sequence that a person wrote. The levels have different types,
    // some of them value types. One level reads the identity only, and the last one gives no value
    // for some keys.
    private static readonly ImmutableArray<Level> NextLevels =
    [
        new(
            Then: static order =>
                order.ThenBy(selector: ByName, sortComparer: StringComparer.Ordinal, isDescending: false),
            ThenLinq: static items =>
                items.ThenBy(keySelector: static item => item.State.Name, comparer: StringComparer.Ordinal)),
        new(
            Then: static order => order.ThenByDescending(selector: ByThird),
            ThenLinq: static items => items.ThenByDescending(static item => (long)(item.State.Score % 3))),
        new(
            Then: static order =>
                order.ThenByIdentity(
                    selector: static identity => identity.Code,
                    sortComparer: StringComparer.Ordinal,
                    isDescending: false),
            ThenLinq: static items =>
                items.ThenBy(keySelector: static item => item.Identity.Code, comparer: StringComparer.Ordinal)),
        new(
            Then: static order => order.ThenBy(selector: static (_, state) => state.Score % 5 / 2.0),
            ThenLinq: static items => items.ThenBy(static item => item.State.Score % 5 / 2.0)),
        new(
            Then: static order => order.ThenByDescending(selector: static (_, state) => state.Score % 7 == 0),
            ThenLinq: static items => items.ThenByDescending(static item => item.State.Score % 7 == 0)),
        new(
            Then: static order => order.ThenBy(selector: static (_, state) => Quarter(score: state.Score)),
            ThenLinq: static items => items.ThenBy(static item => Quarter(score: item.State.Score)))
    ];

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(MostLevels)]
    public async Task AnOrderWithEachCountOfLevelsSortsAsLinqDoes(int levelCount)
    {
        ImmutableArray<Item<ItemIdentity, ItemState>> items = Items();

        OrderedKeys<int, ItemIdentity, ItemState> sorted =
            Order(levelCount: levelCount)
                .CreateFrom(keys: items.Select(static item => item.Identity.Number), snapshot: Snapshot(items: items));

        // The key is the last level, in the ascending direction.
        List<int> reference =
        [
            .. NextLevels.Take(count: levelCount - 1)
                .Aggregate(
                    seed: items.OrderBy(static item => item.State.Score % 2),
                    func: static (ordered, level) => level.ThenLinq(arg: ordered))
                .ThenBy(static item => item.Identity.Number)
                .Select(static item => item.Identity.Number)
        ];

        await Assert.That(TestUtil.Keys(sorted))
            .IsEquivalentTo(expected: reference, ordering: CollectionOrdering.Matching);
    }

    // A pair in a pair gave a type for each count of levels, and each level then made a deeper generic
    // type. The native AOT compiler stops at a depth, and an order with more levels than that depth
    // throws in an app that it compiled. The tests do not run in such an app, thus this test reads the
    // types.
    [Test]
    public async Task EachOrderWithThreeLevelsOrMoreHasTheSameType()
    {
        Type threeLevels = Order(levelCount: 3).GetType();

        await Assert.That(Order(levelCount: 2).GetType() == threeLevels).IsFalse();

        foreach (int levelCount in Enumerable.Range(start: 4, count: MostLevels - 3))
        {
            await Assert.That(Order(levelCount: levelCount).GetType() == threeLevels).IsTrue();
        }
    }

    [Test]
    public async Task AnOrderWithManyLevelsReadsTheStateOnlyWhenOneOfItsLevelsDoes()
    {
        KeyOrder<int, ItemIdentity, ItemState> identityOnly =
            KeyOrder<int, ItemIdentity, ItemState>.ByIdentity(static identity => identity.Number % 2)
                .ThenByIdentity(static identity => identity.Code.Length)
                .ThenByIdentity(static identity => identity.Number % 3)
                .ThenByIdentity(static identity => identity.Number % 5);

        await Assert.That(identityOnly.DependsOnState).IsFalse();
        await Assert.That(identityOnly.ThenBy(selector: ByName).DependsOnState).IsTrue();

        ImmutableArray<Item<ItemIdentity, ItemState>> items = Items();

        // The order sorts with no read of the state map, and the sequence is the sequence of LINQ.
        List<int> reference =
        [
            .. items.OrderBy(static item => item.Identity.Number % 2)
                .ThenBy(static item => item.Identity.Code.Length)
                .ThenBy(static item => item.Identity.Number % 3)
                .ThenBy(static item => item.Identity.Number % 5)
                .ThenBy(static item => item.Identity.Number)
                .Select(static item => item.Identity.Number)
        ];

        await Assert
            .That(
                TestUtil.Keys(
                    identityOnly.CreateFrom(
                        keys: items.Select(static item => item.Identity.Number),
                        snapshot: Snapshot(items: items))))
            .IsEquivalentTo(expected: reference, ordering: CollectionOrdering.Matching);
    }

    // Two orders are the same order when each level has the same selector, comparer, and direction
    // from the caller. An order with three levels has a new comparer at each construction, thus only
    // its levels can say that.
    [Test]
    public async Task TwoOrdersWithThreeLevelsFromTheSameCodeAreEquivalent()
    {
        KeyOrder<int, ItemIdentity, ItemState> order =
            ThreeLevels(isReversed: false, lastComparer: Comparer<long>.Default);

        // A comparer that gives the same answers, as a different instance.
        IComparer<long> otherComparer = Comparer<long>.Create(static (x, y) => x.CompareTo(value: y));

        await Assert
            .That(order.IsEquivalentTo(other: ThreeLevels(isReversed: false, lastComparer: Comparer<long>.Default)))
            .IsTrue();

        await Assert
            .That(order.IsEquivalentTo(other: ThreeLevels(isReversed: true, lastComparer: Comparer<long>.Default)))
            .IsFalse();

        await Assert.That(order.IsEquivalentTo(other: ThreeLevels(isReversed: false, lastComparer: otherComparer)))
            .IsFalse();

        await Assert.That(order.IsEquivalentTo(other: order.ThenBy(selector: ByName))).IsFalse();
    }

    private static KeyOrder<int, ItemIdentity, ItemState> ThreeLevels(bool isReversed, IComparer<long> lastComparer) =>
        KeyOrder<int, ItemIdentity, ItemState>.By(
                selector: ByParity,
                sortComparer: Comparer<int>.Default,
                keyComparer: Comparer<int>.Default,
                isDescending: isReversed)
            .ThenBy(selector: ByName, sortComparer: StringComparer.Ordinal, isDescending: isReversed)
            .ThenBy(selector: ByThird, sortComparer: lastComparer, isDescending: !isReversed);

    private static KeyOrder<int, ItemIdentity, ItemState> Order(int levelCount) =>
        NextLevels.Take(count: levelCount - 1)
            .Aggregate(
                seed: KeyOrder<int, ItemIdentity, ItemState>.By(selector: ByParity),
                func: static (order, level) => level.Then(arg: order));

    // No value for each fourth score. The default comparer puts no value first, and LINQ uses the
    // same comparer.
    private static int? Quarter(int score) => score % 4 == 0 ? null : score % 4;

    // Each value repeats with a different period, thus each level decides between some keys that the
    // levels before it rank equal.
    private static ImmutableArray<Item<ItemIdentity, ItemState>> Items() =>
    [
        .. Enumerable.Range(start: 0, count: 48)
            .Select(
                static number =>
                    new Item<ItemIdentity, ItemState>(
                        identity: new ItemIdentity(Number: number, Code: number % 2 == 0 ? "even" : "odd"),
                        state: new ItemState(
                            Name: (number % 3) switch
                            {
                                0 => "a",
                                1 => "b",
                                _ => "c"
                            },
                            Score: number / 2)))
    ];

    private static CollectionSnapshot<int, ItemIdentity, ItemState> Snapshot(
        ImmutableArray<Item<ItemIdentity, ItemState>> items) =>
        new(
            identities: items.ToImmutableDictionary(
                keySelector: static item => item.Identity.Number,
                elementSelector: static item => item.Identity),
            states: ImmutableStateMap<ItemState>.Create(EqualityComparer<int>.Default)
                .With(
                    updated: items.ToDictionary(
                        keySelector: static item => item.Identity.Number,
                        elementSelector: static item => item.State),
                    removed: []),
            arrivals: items.ToImmutableDictionary(
                keySelector: static item => item.Identity.Number,
                elementSelector: static item => (long)item.Identity.Number),
            nextArrival: items.Length);

    private sealed record Level(
        Func<KeyOrder<int, ItemIdentity, ItemState>, KeyOrder<int, ItemIdentity, ItemState>> Then,
        Func<IOrderedEnumerable<Item<ItemIdentity, ItemState>>, IOrderedEnumerable<Item<ItemIdentity, ItemState>>>
            ThenLinq);
}
