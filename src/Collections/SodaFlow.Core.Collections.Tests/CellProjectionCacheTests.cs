using System;
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

    // A per-key cell made during a transaction seeds from the snapshot that the transaction is
    // about to commit. The transaction can fail after it took the seed, when a later lazy
    // initial value throws in the sample phase. The cell stays in the cache, thus it must not
    // keep a value from the snapshot that the failure discarded.
    [Test]
    public async Task APerKeyCellMadeInATransactionThatFailsDoesNotKeepTheDiscardedSnapshot()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                keySelector: TestUtil.KeyOf,
                initialItems: [],
                edits);

        Cell<int>? scoreOfOne = null;

        IListener listener = collection.ItemChangesStream.ListenStrong(_ =>
        {
            scoreOfOne = collection.StateCellImpl(key: 1, projection: Score);

            Stream.Never<int>()
                .HoldLazy(
                    LazySample.Defer<int>(static () => throw new InvalidOperationException("from the sample phase")));
        });

        await Assert.That(() => edits.Send(TestUtil.Add(TestUtil.Item(number: 1, name: "one", score: 10))))
            .ThrowsExactly<InvalidOperationException>();

        listener.Unlisten();

        await Assert.That(scoreOfOne?.Sample()).IsEqualTo(-1).Because("the add went with the transaction");
        await Assert.That(collection.StateCellImpl(key: 1, projection: Score).Sample()).IsEqualTo(-1);

        edits.Send(TestUtil.Add(TestUtil.Item(number: 1, name: "one", score: 20)));

        await Assert.That(scoreOfOne?.Sample()).IsEqualTo(20);
    }

    private static ReactiveCollection<int, ItemIdentity, ItemState> Empty() =>
        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: static identity => identity.Number,
            initialItems: [],
            Stream.Never<CollectionEdit<int, ItemIdentity, ItemState>>());
}
