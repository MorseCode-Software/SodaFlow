using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     The index that a wide change builds for its per-key cells, against a query
///     over its operations as the oracle.
/// </summary>
/// <remarks>
///     A change answers its first lookups with a scan of its operations, and then
///     builds the index. A per-key cell on a view is one lookup at each change.
///     Each test here holds more cells than the scans. Thus, the answers from the
///     two paths are in the comparison. The oracle is the query that the scan was
///     before the index existed.
/// </remarks>
public sealed class ViewChangeIndexTests
{
    private const int ItemCount = 100;
    private const int ReplacedKey = 60;

    private static readonly CellProjection<ItemState, int> Score =
        new(onPresent: static state => state.Score, onAbsent: static () => -1);

    private static readonly CellProjection<ItemIdentity, int> Number =
        new(onPresent: static identity => identity.Number, onAbsent: static () => -1);

    private static readonly CellProjection<Item<ItemIdentity, ItemState>, int> ItemScore =
        new(onPresent: static item => item.State.Score, onAbsent: static () => -1);

    [Test]
    public async Task AWideChangeWakesTheCellsThatTheScanWould()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> view = View(edits);

        List<CollectionViewChange<int, ItemIdentity, ItemState>> changes = [];
        HashSet<int> stateWoke = [];
        HashSet<int> itemWoke = [];
        HashSet<int> identityWoke = [];
        List<IListener> listeners = [view.KeyChangesStream.Listen(changes.Add)];

        // Two keys that no side holds are in the range too. Thus, a lookup for a key that is in no
        // operation is in the comparison.
        for (int key = -1; key <= ItemCount + 1; key++)
        {
            int cellKey = key;

            listeners.Add(
                view.StateCellImpl(key: key, projection: Score).Updates().Listen(_ => stateWoke.Add(cellKey)));

            listeners.Add(
                view.ItemCellImpl(key: key, projection: ItemScore).Updates().Listen(_ => itemWoke.Add(cellKey)));

            listeners.Add(
                view.IdentityCellImpl(key: key, projection: Number)
                    .Updates()
                    .Listen(_ => identityWoke.Add(cellKey)));
        }

        SendWideEdit(edits);

        foreach (IListener listener in listeners)
        {
            listener.Unlisten();
        }

        CollectionViewChange<int, ItemIdentity, ItemState> change = changes.Single();

        await Assert.That(change.Operations.Count)
            .IsGreaterThanOrEqualTo(CollectionViewChange<int, ItemIdentity, ItemState>.MinimumOperationsForIndex);

        await Assert.That(change.IsIndexed).IsTrue();

        // The replaced key is in a removal and then an insert, thus the index holds it two times.
        await Assert.That(change.Operations.Count(static operation => operation.Key == ReplacedKey)).IsEqualTo(2);

        List<int> named = [.. change.Operations.Select(static operation => operation.Key).Distinct()];

        List<int> membershipNamed =
        [
            .. change.Operations
                .Where(static operation => operation is ViewInsert<int> or ViewRemove<int>)
                .Select(static operation => operation.Key)
                .Distinct()
        ];

        await Assert.That(named.Count).IsGreaterThan(membershipNamed.Count);
        await Assert.That(stateWoke).IsEquivalentTo(expected: named, ordering: CollectionOrdering.Any);
        await Assert.That(itemWoke).IsEquivalentTo(expected: named, ordering: CollectionOrdering.Any);
        await Assert.That(identityWoke).IsEquivalentTo(expected: membershipNamed, ordering: CollectionOrdering.Any);
    }

    [Test]
    public async Task TheIndexComesAtTheLookupAfterTheScans()
    {
        const int scans = CollectionViewChange<int, ItemIdentity, ItemState>.LookupsBeforeIndex;

        await Assert.That(IsIndexedWith(cellCount: scans)).IsFalse();
        await Assert.That(IsIndexedWith(cellCount: scans + 1)).IsTrue();
    }

    [Test]
    public async Task ANarrowChangeNeverBuildsTheIndex()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> view = View(edits);

        List<CollectionViewChange<int, ItemIdentity, ItemState>> changes = [];
        HashSet<int> woke = [];
        List<IListener> listeners = [view.KeyChangesStream.Listen(changes.Add)];

        for (int key = 0; key < ItemCount; key++)
        {
            int cellKey = key;

            listeners.Add(
                view.StateCellImpl(key: key, projection: Score).Updates().Listen(_ => woke.Add(cellKey)));
        }

        edits.Send(TestUtil.Score(key: 3, score: 1000));

        foreach (IListener listener in listeners)
        {
            listener.Unlisten();
        }

        CollectionViewChange<int, ItemIdentity, ItemState> change = changes.Single();

        await Assert.That(change.Operations.Count).IsEqualTo(1);
        await Assert.That(change.IsIndexed).IsFalse();
        await Assert.That(woke).IsEquivalentTo(expected: [3], ordering: CollectionOrdering.Any);
    }

    /// <summary>
    ///     Sends the wide edit through a view with the given number of state cells, and
    ///     reads if the change built its index.
    /// </summary>
    private static bool IsIndexedWith(int cellCount)
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> view = View(edits);

        List<CollectionViewChange<int, ItemIdentity, ItemState>> changes = [];
        List<IListener> listeners = [view.KeyChangesStream.Listen(changes.Add)];

        for (int key = 0; key < cellCount; key++)
        {
            listeners.Add(
                view.StateCellImpl(key: key, projection: Score)
                    .Updates()
                    .Listen(static _ =>
                    {
                    }));
        }

        SendWideEdit(edits);

        foreach (IListener listener in listeners)
        {
            listener.Unlisten();
        }

        return changes.Single().IsIndexed;
    }

    /// <summary>
    ///     A filter that keeps each item, over a collection of <see cref="ItemCount" />
    ///     items with their numbers as scores.
    /// </summary>
    private static ReactiveCollection<int, ItemIdentity, ItemState> View(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits)
    {
        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                keySelector: TestUtil.KeyOf,
                initialItems:
                [
                    .. Enumerable.Range(start: 0, count: ItemCount)
                        .Select(static number => TestUtil.Item(number: number, name: $"item {number}", score: number))
                ],
                edits);

        return CollectionViewUtility.FilterImpl(
            upstream: collection,
            predicateCell: Cell.Constant<Func<ItemIdentity, ItemState, bool>>(static (_, state) => state.Score >= 0));
    }

    /// <summary>
    ///     One edit with updates to the first forty keys, a removal, an add of a new
    ///     key, and a replacement of <see cref="ReplacedKey" />. A replacement is a
    ///     removal and an add of one key in one edit. The view reports it as a
    ///     removal and then an insert.
    /// </summary>
    private static void SendWideEdit(StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits)
    {
        Dictionary<int, Func<ItemState, ItemState>> updates = new();

        for (int key = 0; key < 40; key++)
        {
            updates.Add(key: key, value: static state => state with { Score = state.Score + 1000 });
        }

        edits.Send(
            new CollectionEdit<int, ItemIdentity, ItemState>(
                updates: updates,
                adds:
                [
                    TestUtil.Item(number: ItemCount, name: "new", score: ItemCount),
                    TestUtil.Item(number: ReplacedKey, name: "replaced", score: ReplacedKey)
                ],
                removes: [50, ReplacedKey]));
    }
}
