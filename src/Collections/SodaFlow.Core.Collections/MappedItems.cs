using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>The position of the default limit of a projection. Thus the two language surfaces name
/// one value.</summary>
[PublicAPI]
public static class MappedItems
{
    /// <summary>
    ///     The default number of keys that left for which a projection keeps objects.
    /// </summary>
    /// <remarks>
    ///     This value is for the usual shape: a screen with tens of rows and pages across
    ///     thousands of items. It covers many pages on each side of the current page. It also
    ///     limits a projection across one hundred thousand items to the rows that a screen showed,
    ///     and not to the content of the collection.
    /// </remarks>
    public const int DefaultRetainedBeyondTheView = 512;
}

/// <summary>
///     The result of a projection across a collection: the projected objects, and the path to
///     release them.
/// </summary>
/// <remarks>
///     <para>
///         This is the shape of <c>MapAsync</c>. A projection continues after each version of its
///         input, thus other code must release it and a GC cannot collect it. An object in a
///         projection can hold resources, and the callback that releases one object at an eviction
///         does not run for the objects that the projection holds at its end.
///     </para>
///     <para>
///         Bind to <see cref="Items" />, and put this object with the other resources of the
///         caller.
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
    ///     Releases each object that the projection holds, which is each object that it built and
    ///     did not remove. This code calls the eviction callback for each one. Thus a projection
    ///     that builds objects with resources releases all of them in one position, at each time of
    ///     their departure.
    /// </remarks>
    public void Dispose() => this.dispose();
}

/// <summary>
///     One projected object for each key. This code keeps them, thus a key in the view keeps its
///     object.
/// </summary>
/// <remarks>
///     <para>
///         A screen with a list needs this, and the collection alone does not give it. The
///         collection has the ordered keys and the cells for one item. Other code must give the
///         object that a row binds to, and that object must be the same object at the next read,
///         or the list builds again below the view.
///     </para>
///     <para>
///         This has a limit, because a projection across a large collection that keeps each object
///         holds one object for each key that it showed. The limit applies to the keys that
///         <i>left</i> the view. This code keeps each key in the view, at each value of the limit.
///         Without that rule, a limit below the size of the view removes rows that the code reads
///         again immediately, and a cache is then worse than no cache.
///     </para>
///     <para>
///         The keys that left go out in the sequence of their departure, and the first key to go
///         out is the key with the longest interval since it left. This code does not use the time
///         of the projection. A key that leaves and returns moves to the front, and that keeps the
///         cost of a move between two pages low.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TResult">What each key is projected to.</typeparam>
internal sealed class MappedItemCache<TKey, TResult>
    where TKey : notnull
{
    /// <summary>The keys no longer in the view, most recently departed at the front.</summary>
    private readonly LinkedList<TKey> departed = new();

    /// <summary>The position of each key that left. Thus a departure and a return each cost
    /// <c>O(1)</c>.</summary>
    private readonly Dictionary<TKey, LinkedListNode<TKey>> departedNodes;

    private readonly IEqualityComparer<TKey> keyEqualityComparer;

    private readonly Action<TResult>? onEvicted;
    private readonly Func<TKey, TResult> project;

    /// <summary>Each projected object that an eviction did not remove, in the view and out of
    /// it.</summary>
    private readonly Dictionary<TKey, TResult> projected;

    private readonly int retainedBeyondTheView;

    /// <summary>What the view held last time, to tell what has left it since.</summary>
    private HashSet<TKey> inView;

    internal MappedItemCache(
        IEqualityComparer<TKey> keyEqualityComparer,
        Func<TKey, TResult> project,
        int retainedBeyondTheView,
        Action<TResult>? onEvicted)
    {
        this.keyEqualityComparer = keyEqualityComparer;
        this.project = project;
        this.retainedBeyondTheView = retainedBeyondTheView;
        this.onEvicted = onEvicted;

        this.departedNodes = new Dictionary<TKey, LinkedListNode<TKey>>(keyEqualityComparer);
        this.projected = new Dictionary<TKey, TResult>(keyEqualityComparer);
        this.inView = new HashSet<TKey>(this.keyEqualityComparer);
    }

    /// <summary>The projection of one version of the view's keys, in their order.</summary>
    internal IReadOnlyList<TResult> Project(IReadOnlyList<TKey> keys)
    {
        List<TResult> results = new(keys.Count);
        HashSet<TKey> current = new(this.keyEqualityComparer);

        // This code uses an index and not an enumeration, because keys has an interface type and a
        // foreach on one boxes an enumerator at each version of the view.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (int index = 0; index < keys.Count; index++)
        {
            TKey key = keys[index];

            current.Add(key);

            if (this.projected.TryGetValue(key: key, value: out TResult? existing))
            {
                // The key is in the view again, thus an eviction cannot remove it.
                this.Undepart(key);
                results.Add(existing);

                continue;
            }

            TResult created = this.project(key);
            this.projected.Add(key: key, value: created);
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

    /// <summary>Removes each object that this code holds, and sends a message for each one as an
    /// eviction does.</summary>
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
        this.inView = new HashSet<TKey>(this.keyEqualityComparer);
    }

    private void Depart(TKey key)
    {
        if (this.departedNodes.ContainsKey(key))
        {
            return;
        }

        this.departedNodes.Add(key: key, value: this.departed.AddFirst(key));
    }

    private void Undepart(TKey key)
    {
        if (!this.departedNodes.TryGetValue(key: key, value: out LinkedListNode<TKey>? node))
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

            // This code uses two calls and not the Remove overload that returns the value that it
            // removed. netstandard2.0 and net472 do not have that overload.
            // ReSharper disable once CanSimplifyDictionaryRemovingWithSingleCall
            if (this.projected.TryGetValue(key: key, value: out TResult? evicted))
            {
                this.projected.Remove(key);
                this.onEvicted?.Invoke(evicted);
            }
        }
    }
}
