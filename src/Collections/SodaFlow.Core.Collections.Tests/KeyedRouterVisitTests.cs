using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     The router sends a change one time to each route of a key, also when the
///     change names that key two times.
/// </summary>
/// <remarks>
///     <para>
///         A change can name one key two times. An add of a key with an edit of its
///         state in one transaction puts the key in <c>Added</c> and in
///         <c>NewStates</c> of a root change. A page of a view reports a replaced
///         key as a removal and an insert in one view change.
///     </para>
///     <para>
///         A cell keeps the last value of a transaction. Thus, a second send of the
///         same change to a route gives no different value, and a test of values
///         cannot find it. These tests count the calls of the projection. A route
///         sends a value only when its projection gives one, and the projection
///         gives one only through <c>onPresent</c> or <c>onAbsent</c>. Thus, one
///         call is one send.
///     </para>
///     <para>
///         The router visits a key one time for each change through a generation
///         check. A key with one route and a key with two routes are two cases.
///         The router can keep the check for each case in a different place.
///     </para>
/// </remarks>
public sealed class KeyedRouterVisitTests
{
    private const int ItemCount = 10;

    /// <summary>A key that the collection does not have before the edit.</summary>
    private const int AddedKey = ItemCount;

    /// <summary>A key in the page.</summary>
    private const int ReplacedKey = 3;

    private static readonly CellProjection<ItemState, int> Score =
        new(onPresent: static state => state.Score, onAbsent: static () => -1);

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task AnAddWithAnEditSendsOneTimeToEachRouteOfTheKey(int routeCount)
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(edits);

        List<ItemChange<int, ItemIdentity, ItemState>> changes = [];
        using IListener listener = collection.ItemChangesStream.Listen(changes.Add);

        Counter counter = new();

        List<Cell<int>> counted = MakeCountedCells(
            collection: collection,
            key: AddedKey,
            routeCount: routeCount,
            counter: counter);

        List<Cell<int>> others = MakeOtherCells(collection: collection, except: AddedKey);

        counter.Calls = 0;

        edits.Send(
            new CollectionEdit<int, ItemIdentity, ItemState>(
                updates: new Dictionary<int, Func<ItemState, ItemState>>
                {
                    [AddedKey] = static state => state with { Score = state.Score + 1 }
                },
                adds: [TestUtil.Item(number: AddedKey, name: "added", score: AddedKey)],
                removes: []));

        // The change must name the key two times, or this test proves nothing.
        ItemChange<int, ItemIdentity, ItemState> change = changes.Single();
        await Assert.That(change.Added.Contains(AddedKey)).IsTrue();
        await Assert.That(change.NewStates.ContainsKey(AddedKey)).IsTrue();

        await Assert.That(counter.Calls).IsEqualTo(routeCount);

        foreach (Cell<int> cell in counted)
        {
            await Assert.That(cell.Sample()).IsEqualTo(AddedKey + 1);
        }

        GC.KeepAlive(others);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task AReplacementInAPageSendsOneTimeToEachRouteOfTheKey(int routeCount)
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> page = Page(edits);

        List<CollectionViewChange<int, ItemIdentity, ItemState>> changes = [];
        using IListener listener = page.KeyChangesStream.Listen(changes.Add);

        Counter counter = new();

        List<Cell<int>> counted = MakeCountedCells(
            collection: page,
            key: ReplacedKey,
            routeCount: routeCount,
            counter: counter);

        List<Cell<int>> others = MakeOtherCells(collection: page, except: ReplacedKey);

        counter.Calls = 0;

        // A replacement is a removal and an add of one key in one edit.
        edits.Send(
            new CollectionEdit<int, ItemIdentity, ItemState>(
                updates: new Dictionary<int, Func<ItemState, ItemState>>(),
                adds: [TestUtil.Item(number: ReplacedKey, name: "replaced", score: ReplacedKey)],
                removes: [ReplacedKey]));

        // The change must name the key two times, or this test proves nothing.
        CollectionViewChange<int, ItemIdentity, ItemState> change = changes.Single();
        int removals = change.Operations.Count(static operation => operation is ViewRemove<int> { Key: ReplacedKey });
        int inserts = change.Operations.Count(static operation => operation is ViewInsert<int> { Key: ReplacedKey });
        await Assert.That(change.IsReset).IsFalse();
        await Assert.That(removals).IsEqualTo(1);
        await Assert.That(inserts).IsEqualTo(1);

        await Assert.That(counter.Calls).IsEqualTo(routeCount);

        foreach (Cell<int> cell in counted)
        {
            await Assert.That(cell.Sample()).IsEqualTo(ReplacedKey);
        }

        GC.KeepAlive(others);
    }

    private static ReactiveCollection<int, ItemIdentity, ItemState> Create(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits) =>
        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: TestUtil.KeyOf,
            initialItems:
            [
                .. Enumerable.Range(start: 0, count: ItemCount)
                    .Select(static number => TestUtil.Item(number: number, name: "n" + number, score: number))
            ],
            edits);

    /// <summary>
    ///     The second page of four rows, in the order of the score. A replaced key
    ///     keeps its score, thus it stays at its position in the page.
    /// </summary>
    private static ReactiveCollection<int, ItemIdentity, ItemState> Page(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits) =>
        CollectionViewUtility.SliceImpl(
            upstream: CollectionViewUtility.SortByImpl(
                upstream: Create(edits),
                selector: static (_, state) => state.Score,
                sortComparer: Comparer<int>.Default,
                keyComparer: Comparer<int>.Default,
                isDescending: false),
            offset: 2,
            limit: 4);

    /// <summary>
    ///     Makes the cells of one key, one for each route. Each cell has its own
    ///     projection, thus its own cache and its own route. This samples each
    ///     cell, thus its lazy seed runs before the test sets the count to zero.
    /// </summary>
    private static List<Cell<int>> MakeCountedCells(
        ReactiveCollection<int, ItemIdentity, ItemState> collection,
        int key,
        int routeCount,
        Counter counter)
    {
        List<Cell<int>> cells = [];

        for (int route = 0; route < routeCount; route++)
        {
            CellProjection<ItemState, int> projection = new(
                onPresent: state =>
                {
                    counter.Calls++;
                    return state.Score;
                },
                onAbsent: () =>
                {
                    counter.Calls++;
                    return -1;
                });

            Cell<int> cell = collection.StateCellImpl(key: key, projection: projection);
            _ = cell.Sample();
            cells.Add(cell);
        }

        return cells;
    }

    /// <summary>
    ///     Makes a cell for each other key of the collection. The router then has
    ///     more keys than the change names, and it visits only the keys that the
    ///     change names. With fewer keys, it visits each key one time. The counted
    ///     key gets no cell here, because a cell here is one more route.
    /// </summary>
    private static List<Cell<int>> MakeOtherCells(
        ReactiveCollection<int, ItemIdentity, ItemState> collection,
        int except) =>
    [
        .. Enumerable.Range(start: 0, count: ItemCount).Where(key => key != except).Select(key =>
        {
            Cell<int> cell = collection.StateCellImpl(key: key, projection: Score);
            _ = cell.Sample();
            return cell;
        })
    ];

    /// <summary>The number of calls of the counted projections.</summary>
    private sealed class Counter
    {
        internal int Calls;
    }
}
