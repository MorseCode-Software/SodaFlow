using System;
using System.Collections.Generic;

namespace SodaFlow.Collections;

/// <summary>
///     The cells for one entry that one language surface read. A weak cache holds them, with the
///     entry as the key.
/// </summary>
/// <remarks>
///     <para>
///         There is one of these for each projected type, and that keeps a type on the cells. The
///         alternative is one dictionary with the pair of the type and the entry key as its key,
///         which holds an <see cref="object" />. That alternative needs a cast at each lookup, for
///         a type that the key gives. Here the cast is one time, when the root first gives a cache
///         for a projection, and each value after it is a <c>Cell&lt;TProjected&gt;</c> that the
///         compiler can see.
///     </para>
///     <para>
///         Two projections of the same key are two cells in two caches. Thus the C# surface and
///         the F# surface never give each other an incorrect cell, and the two share one store.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TProjected">The type that the wrapper gives to the cell.</typeparam>
internal sealed class ProjectedCellCache<TKey, TProjected>
    where TKey : notnull
{
    /// <summary>The number of entries that this cache holds before a sweep removes the entries
    /// with no observer.</summary>
    /// <remarks>
    ///     A sweep reads each entry, thus one sweep for each add makes the cost of a full cache
    ///     quadratic. This code waits for a sufficient number of entries, and that keeps the cost
    ///     of each add low.
    /// </remarks>
    private const int PruneThreshold = 64;

    /// <summary>
    ///     A weak cache holds these, thus the observers of one key share a node and that node goes
    ///     out of memory with the last observer, and not with the collection.
    /// </summary>
    private readonly Dictionary<TKey, WeakReference<Cell<TProjected>>> cells = new();

    /// <summary>The cell for a key, or <see langword="null" /> when no cell is in memory.</summary>
    /// <remarks>
    ///     This returns a nullable value and not a <c>TryGet</c>, because the caller makes one when
    ///     this method gives no value, and a null coalesce writes that in one line.
    /// </remarks>
    internal Cell<TProjected>? Get(TKey key) =>
        this.cells.TryGetValue(key: key, value: out WeakReference<Cell<TProjected>>? reference)
        && reference.TryGetTarget(out Cell<TProjected>? cached)
            ? cached
            : null;

    /// <summary>Records the cell for a key. It first removes the entries with no observer, when
    /// their count is sufficient.</summary>
    internal void Set(TKey key, Cell<TProjected> cell)
    {
        this.Prune();
        this.cells[key] = new WeakReference<Cell<TProjected>>(cell);
    }

    /// <summary>Removes each entry whose cell a GC collected.</summary>
    private void Prune()
    {
        if (this.cells.Count < PruneThreshold)
        {
            return;
        }

        List<TKey> dead = new();

        // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
        foreach (KeyValuePair<TKey, WeakReference<Cell<TProjected>>> pair in this.cells)
        {
            if (!pair.Value.TryGetTarget(out Cell<TProjected>? _))
            {
                dead.Add(pair.Key);
            }
        }

        foreach (TKey key in dead)
        {
            this.cells.Remove(key);
        }
    }
}
