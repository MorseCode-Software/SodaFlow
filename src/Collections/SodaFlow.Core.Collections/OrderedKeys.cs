using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     Knows how to build an empty ordered key set under one particular order.
/// </summary>
/// <remarks>
///     This is what lets a filter preserve its upstream's order without knowing what that order
///     sorts by: it asks the upstream's set for <see cref="CreateEmpty" /> and files its own members
///     into a set that compares exactly the same way.
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
public interface IKeyOrder<TKey, TId, TState>
    where TKey : notnull
    where TId : notnull
{
    /// <summary>An empty key set which orders keys the way this order does.</summary>
    /// <returns>The empty set.</returns>
    IOrderedKeys<TKey, TId, TState> CreateEmpty();
}

/// <summary>
///     An immutable ordered set of keys. Every stage of a view chain holds one of these.
/// </summary>
/// <remarks>
///     The sort key type never appears here. It stays a generic parameter of the implementation, so
///     sort values are stored and compared as themselves, with no boxing, while the chain composes
///     at this interface and its type does not grow with each stage.
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IOrderedKeys<TKey, TId, TState> : IReadOnlyList<TKey>
    where TKey : notnull
    where TId : notnull
{
    /// <summary>The order this set files keys under.</summary>
    IKeyOrder<TKey, TId, TState> Order { get; }

    /// <summary>Whether a key is in this set.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns><see langword="true" /> if the key is present.</returns>
    bool Contains(TKey key);

    /// <summary>The position of a key in this set.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns>Its position, or -1 if the key is absent.</returns>
    /// <remarks>
    ///     -1 rather than an optional value, both because this assembly does not reference
    ///     SodaFlow.Functional and because it is the convention every <c>IndexOf</c> in the
    ///     framework already follows.
    /// </remarks>
    int IndexOf(TKey key);

    /// <summary>
    ///     Files the key under the sort value it projects from <paramref name="snapshot" />. A key
    ///     absent from the snapshot is not added.
    /// </summary>
    /// <param name="key">The key to file.</param>
    /// <param name="snapshot">The collection to project the sort value from.</param>
    /// <returns>The set with the key filed in it.</returns>
    IOrderedKeys<TKey, TId, TState> Add(TKey key, CollectionSnapshot<TKey, TId, TState> snapshot);

    /// <summary>Removes a key.</summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>The set without that key.</returns>
    IOrderedKeys<TKey, TId, TState> Remove(TKey key);
}

/// <summary>A key together with the sort value it was filed under.</summary>
/// <remarks>
///     Keeping the projection alongside the key is what makes re-filing O(log n): the old entry is
///     removed with the comparison that placed it, rather than being hunted for after its sort value
///     has moved underneath the set.
/// </remarks>
internal sealed class SortedEntry<TKey, TSortKey>
    where TKey : notnull
{
    internal SortedEntry(TKey key, TSortKey sortValue)
    {
        this.Key = key;
        this.SortValue = sortValue;
    }

    internal TKey Key { get; }

    internal TSortKey SortValue { get; }
}

internal sealed class SortedEntryComparer<TKey, TSortKey> : IComparer<SortedEntry<TKey, TSortKey>>
    where TKey : notnull
{
    private readonly IComparer<TSortKey> sortComparer;
    private readonly IComparer<TKey> keyComparer;
    private readonly bool descending;

    internal SortedEntryComparer(
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool descending)
    {
        this.sortComparer = sortComparer;
        this.keyComparer = keyComparer;
        this.descending = descending;
    }

    public int Compare(SortedEntry<TKey, TSortKey>? left, SortedEntry<TKey, TSortKey>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        // ReSharper disable NullableWarningSuppressionIsUsed - The set only ever holds entries
        // filed by Add, and nothing outside this file constructs one, so neither side is null.
        int result = this.sortComparer.Compare(left!.SortValue, right!.SortValue);
        // ReSharper restore NullableWarningSuppressionIsUsed

        if (result != 0)
        {
            return this.descending ? -result : result;
        }

        // The key breaks ties, so the order is total and two items that sort equally are never
        // conflated.
        return this.keyComparer.Compare(left.Key, right.Key);
    }
}

/// <summary>
///     An order which files each key under a value projected from its item.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
/// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class SortKeyOrder<TKey, TId, TState, TSortKey> : IKeyOrder<TKey, TId, TState>
    where TKey : notnull
    where TId : notnull
{
    private readonly Func<TKey, TId, TState, TSortKey> selector;
    private readonly IComparer<SortedEntry<TKey, TSortKey>> comparer;

    /// <summary>Creates an order.</summary>
    /// <param name="selector">Projects the sort value from a key and its item.</param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">Breaks ties, so that the order is total.</param>
    /// <param name="descending">Whether to reverse the sort comparison.</param>
    public SortKeyOrder(
        Func<TKey, TId, TState, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool descending)
    {
        this.selector = selector;
        this.comparer = new SortedEntryComparer<TKey, TSortKey>(
            sortComparer,
            keyComparer,
            descending);
    }

    /// <inheritdoc />
    public IOrderedKeys<TKey, TId, TState> CreateEmpty() =>
        new SortedKeys<TKey, TId, TState, TSortKey>(
            this,
            ImmutableSortedSet.Create(this.comparer),
            ImmutableDictionary<TKey, SortedEntry<TKey, TSortKey>>.Empty);

    internal TSortKey Project(TKey key, TId identity, TState state) =>
        this.selector(key, identity, state);
}

internal sealed class SortedKeys<TKey, TId, TState, TSortKey> : IOrderedKeys<TKey, TId, TState>
    where TKey : notnull
    where TId : notnull
{
    private readonly SortKeyOrder<TKey, TId, TState, TSortKey> order;
    private readonly ImmutableSortedSet<SortedEntry<TKey, TSortKey>> entries;
    private readonly ImmutableDictionary<TKey, SortedEntry<TKey, TSortKey>> byKey;

    internal SortedKeys(
        SortKeyOrder<TKey, TId, TState, TSortKey> order,
        ImmutableSortedSet<SortedEntry<TKey, TSortKey>> entries,
        ImmutableDictionary<TKey, SortedEntry<TKey, TSortKey>> byKey)
    {
        this.order = order;
        this.entries = entries;
        this.byKey = byKey;
    }

    public IKeyOrder<TKey, TId, TState> Order => this.order;

    public int Count => this.entries.Count;

    public TKey this[int index] => this.entries[index].Key;

    public bool Contains(TKey key) => this.byKey.ContainsKey(key);

    public int IndexOf(TKey key) =>
        this.byKey.TryGet(key, out SortedEntry<TKey, TSortKey> entry)
            ? this.entries.IndexOf(entry)
            : -1;

    public IOrderedKeys<TKey, TId, TState> Add(
        TKey key,
        CollectionSnapshot<TKey, TId, TState> snapshot) =>
        snapshot.LookupInternal(key).Match<IOrderedKeys<TKey, TId, TState>>(
            item =>
            {
                SortedEntry<TKey, TSortKey> entry = new(
                    key,
                    this.order.Project(key, item.Identity, item.State));

                return new SortedKeys<TKey, TId, TState, TSortKey>(
                    this.order,
                    this.entries.Add(entry),
                    this.byKey.SetItem(key, entry));
            },
            () => this);

    public IOrderedKeys<TKey, TId, TState> Remove(TKey key) =>
        this.byKey.TryGet(key, out SortedEntry<TKey, TSortKey> entry)
            ? new SortedKeys<TKey, TId, TState, TSortKey>(
                this.order,
                this.entries.Remove(entry),
                this.byKey.Remove(key))
            : this;

    public IEnumerator<TKey> GetEnumerator() =>
        this.entries.Select(static entry => entry.Key).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}

/// <summary>
///     The first <c>Count</c> keys of another ordered set, without copying it. This is what a
///     <c>Take</c> stage holds, so a stage chained after it still sees a real ordered set and can
///     index into it in O(log n).
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class PrefixKeys<TKey, TId, TState> : IOrderedKeys<TKey, TId, TState>
    where TKey : notnull
    where TId : notnull
{
    private readonly IOrderedKeys<TKey, TId, TState> source;
    private readonly int limit;

    internal PrefixKeys(IOrderedKeys<TKey, TId, TState> source, int limit)
    {
        this.source = source;
        this.limit = limit;
    }

    public IKeyOrder<TKey, TId, TState> Order => this.source.Order;

    public int Count => Math.Min(this.source.Count, Math.Max(this.limit, 0));

    public TKey this[int index] => index < this.Count
        ? this.source[index]
        : throw new ArgumentOutOfRangeException(nameof(index));

    public bool Contains(TKey key) => this.IndexOf(key) >= 0;

    public int IndexOf(TKey key)
    {
        int index = this.source.IndexOf(key);

        return index >= 0 && index < this.Count ? index : -1;
    }

    public IOrderedKeys<TKey, TId, TState> Add(
        TKey key,
        CollectionSnapshot<TKey, TId, TState> snapshot) =>
        new PrefixKeys<TKey, TId, TState>(this.source.Add(key, snapshot), this.limit);

    public IOrderedKeys<TKey, TId, TState> Remove(TKey key) =>
        new PrefixKeys<TKey, TId, TState>(this.source.Remove(key), this.limit);

    public IEnumerator<TKey> GetEnumerator()
    {
        for (int index = 0; index < this.Count; index++)
        {
            yield return this.source[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
