using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     Covers the key of the per-key cell caches: the projection, and not only its
///     projected type.
/// </summary>
public sealed class CellProjectionCacheTests
{
    private static readonly CellProjection<ItemState, int> Score =
        new(onPresent: static state => state.Score, onAbsent: static () => -1);

    private static readonly CellProjection<ItemState, int> TwiceTheScore =
        new(onPresent: static state => 2 * state.Score, onAbsent: static () => -2);

    private static readonly CellProjection<ItemIdentity, int> Length =
        new(onPresent: static identity => identity.Code.Length, onAbsent: static () => -3);

    [Test]
    public async Task OneProjectionGivesOneCellForAKey()
    {
        ReactiveCollection<int, ItemIdentity, ItemState> collection = Empty();

        Cell<int> first = collection.StateCellImpl(key: 1, projection: Score);
        Cell<int> second = collection.StateCellImpl(key: 1, projection: Score);

        await Assert.That(ReferenceEquals(objA: first, objB: second)).IsTrue();
        await Assert.That(first.Sample()).IsEqualTo(-1);
    }

    // The two projections have the same projected type. A cache keyed by that type alone gives the
    // second caller the cell of the first, with the values of the first.
    [Test]
    public async Task TwoProjectionsToOneTypeGiveTwoCellsForAKey()
    {
        ReactiveCollection<int, ItemIdentity, ItemState> collection = Empty();

        Cell<int> score = collection.StateCellImpl(key: 1, projection: Score);
        Cell<int> twice = collection.StateCellImpl(key: 1, projection: TwiceTheScore);

        await Assert.That(ReferenceEquals(objA: score, objB: twice)).IsFalse();
        await Assert.That(score.Sample()).IsEqualTo(-1);
        await Assert.That(twice.Sample()).IsEqualTo(-2);
    }

    // The three caches are different objects. A state projection and an identity projection to one type
    // do not share a cell, whichever of them a caller asks for first.
    [Test]
    public async Task AStateProjectionAndAnIdentityProjectionToOneTypeGiveTwoCellsForAKey()
    {
        ReactiveCollection<int, ItemIdentity, ItemState> collection = Empty();

        Cell<int> length = collection.IdentityCellImpl(key: 1, projection: Length);
        Cell<int> score = collection.StateCellImpl(key: 1, projection: Score);

        await Assert.That(ReferenceEquals(objA: length, objB: score)).IsFalse();
        await Assert.That(length.Sample()).IsEqualTo(-3);
        await Assert.That(score.Sample()).IsEqualTo(-1);
    }

    private static ReactiveCollection<int, ItemIdentity, ItemState> Empty() =>
        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: static identity => identity.Number,
            initialItems: [],
            Stream.Never<CollectionEdit<int, ItemIdentity, ItemState>>());
}
