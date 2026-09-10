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
///     sorts by: it asks the upstream's set for <see cref="CreateFrom" /> and gets back a set that
///     compares exactly the same way, holding whichever of its members it chose to keep.
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
public interface IKeyOrder<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>
    ///     Whether a key's position under this order can be changed by a state edit.
    /// </summary>
    /// <remarks>
    ///     False for an order that projects its sort value from the key or the identity alone,
    ///     neither of which a state edit can touch - which is what lets a stage skip re-filing a
    ///     key it has been told merely changed. The root's order and <c>SortByKey</c>'s are both
    ///     false; a <c>SortBy</c> over a caller's selector is conservatively true, because nothing
    ///     here can see whether that selector read the state it was handed.
    /// </remarks>
    bool DependsOnState { get; }

    /// <summary>
    ///     A key set holding <paramref name="keys" />, ordered the way this order orders them.
    /// </summary>
    /// <param name="keys">The keys to file. Any the snapshot does not have are skipped.</param>
    /// <param name="snapshot">The collection to project each key's sort value from.</param>
    /// <returns>The set.</returns>
    /// <remarks>
    ///     Bulk rather than a sequence of <see cref="IOrderedKeys{TKey,TIdentity,TState}.Add" /> calls,
    ///     and that is the whole reason it exists. Filing n keys one at a time means n persistent
    ///     writes, each copying its path through the tree and allocating a wrapper, which is what a
    ///     stage rebuild used to cost. Building through a builder writes into unfrozen nodes and
    ///     freezes once: two and a half times quicker and a thirteenth of the allocation, measured
    ///     on the criteria change in <c>KeyedCollectionViewBenchmarks</c>.
    ///     It does not make a rebuild cheap, and nothing here could. Building a persistent tree
    ///     costs an allocation per node where re-deriving the same view with LINQ sorts an array
    ///     for none, so a criteria change stays several times dearer than not having a chain -
    ///     which is the thing the documentation tells people to debounce for.
    /// </remarks>
    IOrderedKeys<TKey, TIdentity, TState> CreateFrom(
        IEnumerable<TKey> keys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot);
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
/// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IOrderedKeys<TKey, TIdentity, TState> : IReadOnlyList<TKey>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>The order this set files keys under.</summary>
    IKeyOrder<TKey, TIdentity, TState> Order { get; }

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
    IOrderedKeys<TKey, TIdentity, TState> Add(TKey key, CollectionSnapshot<TKey, TIdentity, TState> snapshot);

    /// <summary>Removes a key.</summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>The set without that key.</returns>
    IOrderedKeys<TKey, TIdentity, TState> Remove(TKey key);
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

        // The key breaks ties, so the order is total and two entries that sort equally are never
        // conflated.
        return this.keyComparer.Compare(left.Key, right.Key);
    }
}

/// <summary>
///     An order which files each key under a value projected from its item.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
/// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
/// <remarks>
///     Internal, because nothing outside this assembly can put an order into a view: the sort
///     stages build their own from the selector they are handed. <see cref="IKeyOrder{TKey,TIdentity,TState}" />
///     stays public because <see cref="IOrderedKeys{TKey,TIdentity,TState}.Order" /> answers with one, so
///     an order can be read and not supplied - which is what keeps the claim
///     <see cref="DependsOnState" /> makes checkable. An order that could be supplied from outside
///     could assert it falsely, and a view would silently stop re-filing.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class SortKeyOrder<TKey, TIdentity, TState, TSortKey> : IKeyOrder<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>
    ///     Exactly one of these is set, and which one is what <see cref="DependsOnState" />
    ///     answers. That is deliberate: an order cannot claim not to read the state while reading
    ///     it, because the selector that claims it is never handed any.
    /// </summary>
    private readonly Func<TKey, TIdentity, TState, TSortKey>? selector;

    private readonly Func<TKey, TIdentity, TSortKey>? identitySelector;

    private readonly IComparer<SortedEntry<TKey, TSortKey>> comparer;

    /// <summary>Creates an order whose sort value is projected from the whole item.</summary>
    /// <param name="selector">Projects the sort value from a key and its item.</param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">Breaks ties, so that the order is total.</param>
    /// <param name="descending">Whether to reverse the sort comparison.</param>
    public SortKeyOrder(
        Func<TKey, TIdentity, TState, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool descending)
        : this(sortComparer, keyComparer, descending) =>
        this.selector = selector;

    /// <summary>
    ///     Creates an order whose sort value is projected from the key and the immutable half
    ///     alone, neither of which a state edit can touch.
    /// </summary>
    /// <param name="selector">Projects the sort value from a key and its identity.</param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">Breaks ties, so that the order is total.</param>
    /// <param name="descending">Whether to reverse the sort comparison.</param>
    /// <remarks>
    ///     What this buys is in <see cref="DependsOnState" />: a stage under this order skips
    ///     re-filing a key on a state edit, and building one skips reading the state map at all.
    /// </remarks>
    public SortKeyOrder(
        Func<TKey, TIdentity, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool descending)
        : this(sortComparer, keyComparer, descending) =>
        this.identitySelector = selector;

    private SortKeyOrder(
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool descending) =>
        this.comparer = new SortedEntryComparer<TKey, TSortKey>(
            sortComparer,
            keyComparer,
            descending);

    /// <inheritdoc />
    public bool DependsOnState => this.identitySelector is null;

    /// <inheritdoc />
    public IOrderedKeys<TKey, TIdentity, TState> CreateFrom(
        IEnumerable<TKey> keys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
    {
        ImmutableSortedSet<SortedEntry<TKey, TSortKey>>.Builder entries =
            ImmutableSortedSet.CreateBuilder(this.comparer);

        ImmutableDictionary<TKey, SortedEntry<TKey, TSortKey>>.Builder byKey =
            ImmutableDictionary.CreateBuilder<TKey, SortedEntry<TKey, TSortKey>>();

        foreach (TKey key in keys)
        {
            if (!this.TryProject(key, snapshot, out TSortKey sortValue))
            {
                continue;
            }

            SortedEntry<TKey, TSortKey> entry = new(key, sortValue);

            entries.Add(entry);
            byKey[key] = entry;
        }

        return new SortedKeys<TKey, TIdentity, TState, TSortKey>(
            this,
            entries.ToImmutable(),
            byKey.ToImmutable());
    }

    /// <summary>
    ///     The sort value this order files <paramref name="key" /> under, if the snapshot still
    ///     holds it.
    /// </summary>
    /// <remarks>
    ///     An order that does not read the state does not read the state map either, which is one
    ///     fewer lookup per key - and a rebuild does this for every key it keeps.
    ///     The two branches disagree only for a key the identity map holds and the state map does
    ///     not, which the two being written together in <c>Resolve</c> rules out.
    /// </remarks>
    internal bool TryProject(
        TKey key,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot,
        out TSortKey sortValue)
    {
        // ReSharper disable once NullableWarningSuppressionIsUsed - an out parameter of an
        // unconstrained type can promise nothing beyond the default when it answers false, which is
        // the contract every TryGet in the framework keeps.
        sortValue = default!;

        if (this.identitySelector is not null)
        {
            if (!snapshot.TryGetIdentity(key, out TIdentity identityOnly))
            {
                return false;
            }

            sortValue = this.identitySelector(key, identityOnly);

            return true;
        }

        if (!snapshot.TryGetHalves(key, out TIdentity identity, out TState state))
        {
            return false;
        }

        // ReSharper disable once NullableWarningSuppressionIsUsed - exactly one of the two
        // selectors is set, and identitySelector being null is what says it is this one.
        sortValue = this.selector!(key, identity, state);

        return true;
    }
}

internal sealed class SortedKeys<TKey, TIdentity, TState, TSortKey> : IOrderedKeys<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly SortKeyOrder<TKey, TIdentity, TState, TSortKey> order;
    private readonly ImmutableSortedSet<SortedEntry<TKey, TSortKey>> entries;
    private readonly ImmutableDictionary<TKey, SortedEntry<TKey, TSortKey>> byKey;

    internal SortedKeys(
        SortKeyOrder<TKey, TIdentity, TState, TSortKey> order,
        ImmutableSortedSet<SortedEntry<TKey, TSortKey>> entries,
        ImmutableDictionary<TKey, SortedEntry<TKey, TSortKey>> byKey)
    {
        this.order = order;
        this.entries = entries;
        this.byKey = byKey;
    }

    public IKeyOrder<TKey, TIdentity, TState> Order => this.order;

    public int Count => this.entries.Count;

    public TKey this[int index] => this.entries[index].Key;

    public bool Contains(TKey key) => this.byKey.ContainsKey(key);

    public int IndexOf(TKey key) =>
        this.byKey.TryGet(key, out SortedEntry<TKey, TSortKey> entry)
            ? this.entries.IndexOf(entry)
            : -1;

    public IOrderedKeys<TKey, TIdentity, TState> Add(
        TKey key,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
    {
        if (!this.order.TryProject(key, snapshot, out TSortKey sortValue))
        {
            return this;
        }

        SortedEntry<TKey, TSortKey> entry = new(key, sortValue);

        return new SortedKeys<TKey, TIdentity, TState, TSortKey>(
            this.order,
            this.entries.Add(entry),
            this.byKey.SetItem(key, entry));
    }

    public IOrderedKeys<TKey, TIdentity, TState> Remove(TKey key) =>
        this.byKey.TryGet(key, out SortedEntry<TKey, TSortKey> entry)
            ? new SortedKeys<TKey, TIdentity, TState, TSortKey>(
                this.order,
                this.entries.Remove(entry),
                this.byKey.Remove(key))
            : this;

    public IEnumerator<TKey> GetEnumerator() =>
        this.entries.Select(static entry => entry.Key).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}

/// <summary>
///     A contiguous window of another ordered set, without copying it. This is what a
///     <c>Slice</c> stage holds - and a <c>Take</c> stage too, which is a window starting at zero
///     - so a stage chained after one still sees a real ordered set and can index into it in
///     O(log n).
/// </summary>
/// <remarks>
///     The window is bounded, and that is what makes the stage above it affordable: its process
///     step diffs the old window against the new one rather than translating operations, which
///     costs O(limit) and not O(n). A skip with no limit would have no such bound, which is why
///     there is no stage offering one.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class RangeKeys<TKey, TIdentity, TState> : IOrderedKeys<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly IOrderedKeys<TKey, TIdentity, TState> source;
    private readonly int offset;
    private readonly int limit;

    internal RangeKeys(IOrderedKeys<TKey, TIdentity, TState> source, int offset, int limit)
    {
        this.source = source;
        this.offset = Math.Max(offset, 0);
        this.limit = Math.Max(limit, 0);
    }

    public IKeyOrder<TKey, TIdentity, TState> Order => this.source.Order;

    public int Count => Math.Min(Math.Max(this.source.Count - this.offset, 0), this.limit);

    public TKey this[int index] => index >= 0 && index < this.Count
        ? this.source[index + this.offset]
        : throw new ArgumentOutOfRangeException(nameof(index));

    public bool Contains(TKey key) => this.IndexOf(key) >= 0;

    public int IndexOf(TKey key)
    {
        int index = this.source.IndexOf(key);

        if (index < this.offset)
        {
            return -1;
        }

        int shifted = index - this.offset;

        return shifted < this.Count ? shifted : -1;
    }

    public IOrderedKeys<TKey, TIdentity, TState> Add(
        TKey key,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot) =>
        new RangeKeys<TKey, TIdentity, TState>(this.source.Add(key, snapshot), this.offset, this.limit);

    public IOrderedKeys<TKey, TIdentity, TState> Remove(TKey key) =>
        new RangeKeys<TKey, TIdentity, TState>(this.source.Remove(key), this.offset, this.limit);

    public IEnumerator<TKey> GetEnumerator()
    {
        int count = this.Count;

        for (int index = 0; index < count; index++)
        {
            yield return this.source[index + this.offset];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
