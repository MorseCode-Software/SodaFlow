using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     Covers the sweep of the cache: it keeps each cell that stays alive, and it
///     removes the cells that the garbage collector removed.
/// </summary>
public sealed class ProjectedCellCacheTests
{
    private const int CellCount = 1_000;

    [Test]
    public async Task EachCellThatStaysAliveStaysInTheCache()
    {
        ProjectedCellCache<int, int> cache = new();
        List<Cell<int>> cells = [];

        for (int key = 0; key < CellCount; key++)
        {
            Cell<int> cell = Cell.CreateSink(key);
            cells.Add(cell);
            cache.Set(key: key, cell: cell);
        }

        int found = 0;

        for (int key = 0; key < CellCount; key++)
        {
            if (ReferenceEquals(objA: cache.Get(key), objB: cells[key]))
            {
                found++;
            }
        }

        await Assert.That(cache.Count).IsEqualTo(CellCount);
        await Assert.That(found).IsEqualTo(CellCount);
    }

    // The sweep occurs less frequently as the cache gets larger. It must continue to remove the entries
    // of the cells that the garbage collector removed. Thus, the cache never holds more than two
    // times the number of cells that stay alive.
    [Test]
    public async Task TheCacheRemovesTheCellsThatTheGarbageCollectorRemoved()
    {
        ProjectedCellCache<int, int> cache = new();

        AddCellsAndDropThem(cache: cache, firstKey: 0, count: CellCount);
        Collect();

        List<Cell<int>> alive = [];

        for (int key = CellCount; key < 2 * CellCount; key++)
        {
            Cell<int> cell = Cell.CreateSink(key);
            alive.Add(cell);
            cache.Set(key: key, cell: cell);
        }

        await Assert.That(cache.Count).IsLessThanOrEqualTo(2 * alive.Count);
        await Assert.That(cache.Get(0)).IsNull();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddCellsAndDropThem(ProjectedCellCache<int, int> cache, int firstKey, int count)
    {
        for (int key = firstKey; key < firstKey + count; key++)
        {
            cache.Set(key: key, cell: Cell.CreateSink(key));
        }
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
