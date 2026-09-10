using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     What a projection over a collection yields: the projected objects, and the means to let go
///     of them.
/// </summary>
/// <remarks>
///     <para>
///         The shape <c>MapAsync</c> already uses. A projection outlives any one version of its
///         input, so it has to be released rather than collected - the objects it holds may own
///         things, and the callback that releases one on eviction cannot fire for the ones still
///         held when the whole projection goes.
///     </para>
///     <para>
///         Take <see cref="Items" /> to bind to, and put this in whatever the caller disposes.
///     </para>
/// </remarks>
/// <typeparam name="TResult">What each key is projected to.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public readonly struct MappedItems<TResult> : IDisposable
{
    private readonly Action dispose;

    internal MappedItems(Cell<IReadOnlyList<TResult>> items, Action dispose)
    {
        this.Items = items;
        this.dispose = dispose;
    }

    /// <summary>One object per key, in the collection's order.</summary>
    public Cell<IReadOnlyList<TResult>> Items { get; }

    /// <inheritdoc />
    /// <remarks>
    ///     Releases everything still held, which is every object the projection built and has not
    ///     already evicted. The eviction callback is called for each, so a projection that builds
    ///     things needing disposal disposes all of them in one place whether they left early or
    ///     lasted to the end.
    /// </remarks>
    public void Dispose() => this.dispose();
}

/// <summary>
///     One projected object per key, kept so that a key still in the view keeps the object it had.
/// </summary>
/// <remarks>
///     <para>
///         What a list-backed screen needs and the collection alone does not give: the ordered keys
///         are here, and the per-item cells are here, but the object a row binds to has to come from
///         somewhere and be the same object next time or the list rebuilds under the view.
///     </para>
///     <para>
///         Bounded, because a projection over a large collection that never forgot anything would
///         hold an object per key ever seen. The bound governs keys that have <i>left</i> the view:
///         everything currently in it is kept whatever the bound says. A bound smaller than the view
///         would otherwise evict rows it is about to be asked for again, which is the one way a
///         cache can be worse than no cache at all.
///     </para>
///     <para>
///         Departed keys are dropped oldest first, and what "oldest" means is when the key left
///         rather than when it was last projected - a key that leaves and comes back moves to the
///         front, which is what makes paging back and forth cheap.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TResult">What each key is projected to.</typeparam>
internal sealed class MappedItemCache<TKey, TResult>
    where TKey : notnull
{
    private readonly Func<TKey, TResult> project;

    private readonly int retainedBeyondTheView;

    private readonly Action<TResult>? onEvicted;

    /// <summary>Everything projected and not yet evicted, in or out of the view.</summary>
    private readonly Dictionary<TKey, TResult> projected = new();

    /// <summary>The keys no longer in the view, most recently departed at the front.</summary>
    private readonly LinkedList<TKey> departed = new();

    /// <summary>Where each departed key sits, so leaving and returning are both O(1).</summary>
    private readonly Dictionary<TKey, LinkedListNode<TKey>> departedNodes = new();

    /// <summary>What the view held last time, to tell what has left it since.</summary>
    private HashSet<TKey> inView = new();

    internal MappedItemCache(
        Func<TKey, TResult> project,
        int retainedBeyondTheView,
        Action<TResult>? onEvicted)
    {
        this.project = project;
        this.retainedBeyondTheView = retainedBeyondTheView;
        this.onEvicted = onEvicted;
    }

    /// <summary>The projection of one version of the view's keys, in their order.</summary>
    internal IReadOnlyList<TResult> Project(IReadOnlyList<TKey> keys)
    {
        List<TResult> results = new(keys.Count);
        HashSet<TKey> current = new();

        // Indexed rather than enumerated, because keys arrives interface-typed and a foreach over
        // one boxes an enumerator - on every version of the view.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (int index = 0; index < keys.Count; index++)
        {
            TKey key = keys[index];

            current.Add(key);

            if (this.projected.TryGetValue(key, out TResult? existing))
            {
                // Back in the view, so no longer a candidate for eviction.
                this.Undepart(key);
                results.Add(existing);

                continue;
            }

            TResult created = this.project(key);
            this.projected.Add(key, created);
            results.Add(created);
        }

        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
        foreach (TKey key in this.inView)
        {
            if (!current.Contains(key))
            {
                this.Depart(key);
            }
        }

        this.inView = current;
        this.Trim();

        return results;
    }

    /// <summary>Drops everything held, notifying for each as an eviction would.</summary>
    internal void ReleaseAll()
    {
        if (this.onEvicted is not null)
        {
            foreach (TResult retained in this.projected.Values)
            {
                this.onEvicted(retained);
            }
        }

        this.projected.Clear();
        this.departed.Clear();
        this.departedNodes.Clear();
        this.inView = new HashSet<TKey>();
    }

    private void Depart(TKey key)
    {
        if (this.departedNodes.ContainsKey(key))
        {
            return;
        }

        this.departedNodes.Add(key, this.departed.AddFirst(key));
    }

    private void Undepart(TKey key)
    {
        if (!this.departedNodes.TryGetValue(key, out LinkedListNode<TKey>? node))
        {
            return;
        }

        this.departed.Remove(node);
        this.departedNodes.Remove(key);
    }

    private void Trim()
    {
        while (this.departed.Count > this.retainedBeyondTheView)
        {
            LinkedListNode<TKey>? oldest = this.departed.Last;

            if (oldest is null)
            {
                return;
            }

            TKey key = oldest.Value;

            this.departed.RemoveLast();
            this.departedNodes.Remove(key);

            // Two calls rather than the Remove overload that yields what it removed, which
            // netstandard2.0 and net472 do not have.
            // ReSharper disable once CanSimplifyDictionaryRemovingWithSingleCall
            if (this.projected.TryGetValue(key, out TResult? evicted))
            {
                this.projected.Remove(key);
                this.onEvicted?.Invoke(evicted);
            }
        }
    }
}
