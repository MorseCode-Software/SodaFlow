using System;
using System.Collections.Generic;

namespace SodaFlow.Collections;

/// <summary>
///     The per-entry cells one language surface has asked for, held weakly and keyed by entry.
/// </summary>
/// <remarks>
///     <para>
///         One of these per projected type, which is what keeps the cells typed. The alternative -
///         a single dictionary keyed by the pair of type and entry key, holding <see cref="object" />
///         - needs a cast on every lookup to recover what the type in the key had already promised.
///         Here the cast happens once, when the root first hands out a cache for a projection, and
///         everything downstream of it is a <c>Cell&lt;TProjected&gt;</c> the compiler can see.
///     </para>
///     <para>
///         Two projections of the same key are two cells in two caches, which is what keeps the C#
///         and F# surfaces from handing each other the wrong one while still sharing a store.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TProjected">What the wrapper asked the cell to hold.</typeparam>
internal sealed class ProjectedCellCache<TKey, TProjected>
    where TKey : notnull
{
    /// <summary>How many entries may accumulate before dead ones are swept.</summary>
    /// <remarks>
    ///     A sweep walks every entry, so doing one per insert would make filling the cache
    ///     quadratic. Waiting until there are enough entries for a sweep to be worth its walk keeps
    ///     that amortized.
    /// </remarks>
    private const int PruneThreshold = 64;

    /// <summary>
    ///     Weakly held, so that observers of one key share a node and the node goes away when the
    ///     last of them does rather than when the collection does.
    /// </summary>
    private readonly Dictionary<TKey, WeakReference<Cell<TProjected>>> cells = new();

    /// <summary>The cell for a key, or <see langword="null" /> if none is still alive.</summary>
    /// <remarks>
    ///     A nullable return rather than a <c>TryGet</c>, because the caller's next move is to
    ///     create one when this yields nothing and a null coalesce says that in one line.
    /// </remarks>
    internal Cell<TProjected>? Get(TKey key) =>
        this.cells.TryGetValue(key: key, value: out WeakReference<Cell<TProjected>>? reference) &&
        reference.TryGetTarget(out Cell<TProjected>? cached)
            ? cached
            : null;

    /// <summary>Records the cell for a key, sweeping dead entries first if there are enough.</summary>
    internal void Set(TKey key, Cell<TProjected> cell)
    {
        this.Prune();
        this.cells[key] = new WeakReference<Cell<TProjected>>(cell);
    }

    /// <summary>Drops the entries whose cells have been collected.</summary>
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
