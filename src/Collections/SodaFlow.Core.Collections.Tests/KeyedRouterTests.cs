using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     The router of the per-key cells holds each per-key stream weakly. Thus, the
///     key of a cell that nothing holds goes from the router, and the router keeps
///     no stream alive.
/// </summary>
public sealed class KeyedRouterTests
{
    private static readonly CellProjection<ItemState, int> Score =
        new(onPresent: static state => state.Score, onAbsent: static () => -1);

    /// <summary>A change that names the key of a dead cell removes that key from the router.</summary>
    [Test]
    public async Task AChangeRemovesTheKeyOfACellThatNothingHolds()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(edits: edits, count: 10);

        Cell<int> held = collection.StateCellImpl(key: 1, projection: Score);
        MakeCells(collection: collection, from: 5, count: 1);

        await Assert.That(collection.RoutedKeyCount).IsEqualTo(2);

        Collect();
        edits.Send(TestUtil.Score(key: 5, score: 55));

        await Assert.That(collection.RoutedKeyCount).IsEqualTo(1);
        await Assert.That(held.Sample()).IsEqualTo(1);
    }

    /// <summary>
    ///     Keys that no change names go when the number of keys reaches the threshold
    ///     of the prune, at the next registration. That is the procedure for a page
    ///     of rows that the user moved away from.
    /// </summary>
    [Test]
    public async Task ARegistrationPrunesTheKeysOfCellsThatNothingHolds()
    {
        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(edits: Stream.Never<CollectionEdit<int, ItemIdentity, ItemState>>(), count: 300);

        Cell<int> held = collection.StateCellImpl(key: 0, projection: Score);
        // A young collection during this loop can let a prune at 64 keys remove some of these keys. Thus,
        // this test reads the count only after the full collection below.
        MakeCells(collection: collection, from: 100, count: 100);
        Collect();

        List<Cell<int>> alive = [.. Enumerable.Range(start: 200, count: 30).Select(key => collection.StateCellImpl(key: key, projection: Score))];

        await Assert.That(collection.RoutedKeyCount).IsEqualTo(31);
        await Assert.That(held.Sample()).IsEqualTo(0);
        GC.KeepAlive(alive);
    }

    /// <summary>The same on a view stage, which has a router of its own.</summary>
    [Test]
    public async Task AChangeRemovesTheKeyOfADeadCellOfAView()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> view =
            CollectionViewUtility.FilterImpl(
                upstream: Create(edits: edits, count: 10),
                predicateCell: Cell.Constant<Func<ItemIdentity, ItemState, bool>>(static (_, state) => state.Score >= 0));

        Cell<int> held = view.StateCellImpl(key: 1, projection: Score);
        MakeCells(collection: view, from: 5, count: 1);

        await Assert.That(view.RoutedKeyCount).IsEqualTo(2);

        Collect();
        edits.Send(TestUtil.Score(key: 5, score: 55));

        await Assert.That(view.RoutedKeyCount).IsEqualTo(1);
        await Assert.That(held.Sample()).IsEqualTo(1);
    }

    private static ReactiveCollection<int, ItemIdentity, ItemState> Create(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        int count) =>
        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: TestUtil.KeyOf,
            initialItems: [.. Enumerable.Range(start: 0, count: count).Select(static n => TestUtil.Item(number: n, name: "n" + n, score: n))],
            edits);

    // Not inline, thus the JIT holds no reference to a cell after the return.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MakeCells(ReactiveCollection<int, ItemIdentity, ItemState> collection, int from, int count)
    {
        for (int key = from; key < from + count; key++)
        {
            _ = collection.StateCellImpl(key: key, projection: Score).Sample();
        }
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
