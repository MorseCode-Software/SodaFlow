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

/// <summary>
///     A collection makes some objects at their first use and keeps them. These
///     are the router of the per-key cells, the cache of those cells, and the
///     streams that its views read. These tests make that first use in the body of a
///     transaction that throws, and require each object to work in a later
///     transaction.
/// </summary>
public sealed class FailedTransactionTests
{
    private static (ReactiveCollection<int, ItemIdentity, ItemState> Collection,
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> Edits) Create()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                keySelector: TestUtil.KeyOf,
                initialItems:
                [
                    TestUtil.Item(number: 1, name: "one", score: 10),
                    TestUtil.Item(number: 2, name: "two", score: 20)
                ],
                edits);

        return (collection, edits);
    }

    private static async Task FailInBody(Action body) =>
        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    body();
                    throw new InvalidOperationException("body");
                }))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage("body");

    private static int ScoreOf(Maybe<ItemState> state) =>
        state.Match(onSome: static s => s.Score, onNone: static () => -1);

    /// <summary>
    ///     The first per-key cell of a collection makes its router. A router that a
    ///     failed body made must send the changes of each key, the keys of cells
    ///     made later included.
    /// </summary>
    [Test]
    public async Task TheRouterThatABodyThatThrowsMadeSendsTheChangesOfEachKey()
    {
        (ReactiveCollection<int, ItemIdentity, ItemState> collection,
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits) = Create();

        await FailInBody(() => _ = collection.StateCell(1));

        Cell<Maybe<ItemState>> one = collection.StateCell(1);
        Cell<Maybe<ItemState>> two = collection.StateCell(2);
        edits.Send(TestUtil.Score(key: 1, score: 11).CombineWith(TestUtil.Score(key: 2, score: 21)));

        await Assert.That(ScoreOf(one.Sample())).IsEqualTo(11);
        await Assert.That(ScoreOf(two.Sample())).IsEqualTo(21);
    }

    /// <summary>
    ///     The same for the router of a view stage.
    /// </summary>
    [Test]
    public async Task TheRouterOfAViewThatABodyThatThrowsMadeSendsTheChangesOfEachKey()
    {
        (ReactiveCollection<int, ItemIdentity, ItemState> collection,
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits) = Create();

        ReactiveCollection<int, ItemIdentity, ItemState> sorted = collection.SortBy(static (_, s) => s.Score);
        _ = sorted.KeysCell.Sample();

        await FailInBody(() => _ = sorted.StateCell(1));

        Cell<Maybe<ItemState>> two = sorted.StateCell(2);
        edits.Send(TestUtil.Score(key: 2, score: 21));

        await Assert.That(ScoreOf(two.Sample())).IsEqualTo(21);
    }

    /// <summary>
    ///     A failed body makes a per-key cell for a key that is not present yet.
    ///     The cell stays in the cache with the router that it made. It must take
    ///     the item when the key arrives.
    /// </summary>
    [Test]
    public async Task ACellThatABodyThatThrowsMadeForAnAbsentKeyTakesTheItemLater()
    {
        (ReactiveCollection<int, ItemIdentity, ItemState> collection,
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits) = Create();

        await FailInBody(() => _ = collection.StateCell(3));

        // The first sample takes the lazy first value now, before the key arrives. Thus, only the
        // router can give the cell the item.
        Cell<Maybe<ItemState>> three = collection.StateCell(3);
        int before = ScoreOf(three.Sample());
        edits.Send(TestUtil.Add(TestUtil.Item(number: 3, name: "three", score: 30)));

        await Assert.That(before).IsEqualTo(-1);
        await Assert.That(ScoreOf(three.Sample())).IsEqualTo(30);
    }

    /// <summary>
    ///     The change stream of the keys of a root is made at the first read, and
    ///     each view that the root has later reads it.
    /// </summary>
    [Test]
    public async Task TheKeyChangesThatABodyThatThrowsReadFirstFireInALaterTransaction()
    {
        (ReactiveCollection<int, ItemIdentity, ItemState> collection,
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits) = Create();

        await FailInBody(() => _ = collection.KeyChangesStream);

        List<int> changed = [];

        IListener l =
            collection.KeyChangesStream.Listen(change => changed.AddRange(change.Operations.Select(static o => o.Key)));

        ReactiveCollection<int, ItemIdentity, ItemState> filtered = collection.Filter(static (_, s) => s.Score > 0);
        _ = filtered.KeysCell.Sample();

        edits.Send(TestUtil.Add(TestUtil.Item(number: 3, name: "three", score: 30)));
        l.Dispose();

        await Assert.That(changed).IsEquivalentTo(expected: [3], ordering: CollectionOrdering.Matching);

        await Assert.That(TestUtil.Keys(filtered.KeysCell.Sample()))
            .IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     The item change stream of a view is made at the first read.
    /// </summary>
    [Test]
    public async Task TheItemChangesOfAViewThatABodyThatThrowsReadFirstFireInALaterTransaction()
    {
        (ReactiveCollection<int, ItemIdentity, ItemState> collection,
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits) = Create();

        ReactiveCollection<int, ItemIdentity, ItemState> filtered = collection.Filter(static (_, s) => s.Score > 0);
        _ = filtered.KeysCell.Sample();

        await FailInBody(() => _ = filtered.ItemChangesStream);

        List<int> changed = [];
        IListener l = filtered.ItemChangesStream.Listen(change => changed.AddRange(change.ChangedKeys));
        edits.Send(TestUtil.Add(TestUtil.Item(number: 3, name: "three", score: 30)));
        l.Dispose();

        await Assert.That(changed).IsEquivalentTo(expected: [3], ordering: CollectionOrdering.Matching);
    }
}
