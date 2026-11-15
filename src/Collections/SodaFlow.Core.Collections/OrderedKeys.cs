using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     An immutable ordered set of keys. Each stage of a view chain holds one of
///     these sets.
/// </summary>
/// <remarks>
///     The sort key type is not in this interface. It stays a generic parameter of
///     the implementation. Thus, an order with one level or two levels keeps and
///     compares each sort value as its own type, and boxes none of them. The chain
///     composes at this interface, and the type of the interface does not become
///     larger at each stage.
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public abstract class OrderedKeys<TKey, TIdentity, TState> : IReadOnlyList<TKey>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>
    ///     This is internal, thus only this assembly can make one. A key set is the
    ///     record of one stage, and the members below that build the next version of a
    ///     set are the protocol between two stages. A consumer must not call them.
    /// </summary>
    internal OrderedKeys()
    {
    }

    /// <summary>The order of the keys in this set.</summary>
    internal abstract KeyOrder<TKey, TIdentity, TState> Order { get; }

    /// <inheritdoc />
    public abstract int Count { get; }

    /// <inheritdoc />
    public abstract TKey this[int index] { get; }

    /// <inheritdoc />
    public abstract IEnumerator<TKey> GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    /// <summary>True when a key is in this set.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns><see langword="true" /> when the key is available.</returns>
    public abstract bool Contains(TKey key);

    /// <summary>The position of a key in this set.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns>Its position, or -1 when the key is missing.</returns>
    /// <remarks>
    ///     This answers -1 and not an optional value, for two causes. This assembly
    ///     does not reference SodaFlow.Functional, and -1 is the convention of each
    ///     <c>IndexOf</c> in the framework. It is internal because that value is an
    ///     agreement between this assembly and the language surfaces, and a caller
    ///     must not know it. Each surface has its own <c>IndexOf</c> that answers with
    ///     the optional type of that language.
    /// </remarks>
    internal abstract int IndexOfInternal(TKey key);

    /// <summary>
    ///     Puts the key at the sort value that this order makes from
    ///     <paramref name="snapshot" />. This code does not add a key that the
    ///     snapshot does not hold.
    /// </summary>
    /// <param name="key">The key to add.</param>
    /// <param name="snapshot">The collection that gives the sort value.</param>
    /// <returns>The set with the key in it.</returns>
    internal abstract OrderedKeys<TKey, TIdentity, TState> Add(
        TKey key,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot);

    /// <summary>Removes a key.</summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>The set without that key.</returns>
    internal abstract OrderedKeys<TKey, TIdentity, TState> Remove(TKey key);
}

/// <summary>A key with the sort value of its position.</summary>
/// <remarks>
///     This code keeps the sort value with the key, and that makes a second sort
///     cost <c>O(log n)</c>. It removes the previous entry with the comparer that
///     put it there, and does not search for it after its sort value changed.
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
    internal SortedEntryComparer(
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool isDescending)
    {
        this.SortComparer = sortComparer;
        this.KeyComparer = keyComparer;
        this.IsDescending = isDescending;
    }

    public IComparer<TSortKey> SortComparer { get; }
    public IComparer<TKey> KeyComparer { get; }
    private bool IsDescending { get; }

    public int Compare(SortedEntry<TKey, TSortKey>? left, SortedEntry<TKey, TSortKey>? right)
    {
        if (ReferenceEquals(objA: left, objB: right))
        {
            return 0;
        }

        // This code applies a direction with an interchange of the two operands, and does not
        // negate the result. A negation of int.MinValue gives a negative value, thus a comparer
        // that returns it sorts in the same direction at each value of isDescending. An
        // interchange cannot have that error.
        //
        // ReSharper disable NullableWarningSuppressionIsUsed - The set only ever holds entries
        // filed by Add, and nothing outside this file constructs one, so neither side is null.
        int result =
            this.IsDescending
                ? this.SortComparer.Compare(x: right!.SortValue, y: left!.SortValue)
                : this.SortComparer.Compare(x: left!.SortValue, y: right!.SortValue);
        // ReSharper restore NullableWarningSuppressionIsUsed

        // The key is the last level, thus the order is total and this code never puts two entries
        // with equal sort values together.
        return result != 0 ? result : this.KeyComparer.Compare(x: left.Key, y: right.Key);
    }
}

/// <summary>
///     Two sort values, compared one level at a time. An order with two levels
///     uses this type as the sort value of a key.
/// </summary>
/// <remarks>
///     <para>
///         This struct holds the two levels as their own types. Thus, an order
///         with two levels keeps the sort value type of each level to its comparer,
///         and boxes no value.
///     </para>
///     <para>
///         A pair never holds a pair. A pair in a pair gives a type for each count
///         of levels, and each level then makes a deeper generic type. The native
///         AOT compiler makes the code of each generic type before the app runs.
///         It stops at a depth, and an order with more levels than that depth
///         throws. An order with three levels or more uses
///         <see cref="SortValues" />.
///     </para>
/// </remarks>
/// <typeparam name="TFirst">The type of the level that decides first.</typeparam>
/// <typeparam name="TSecond">
///     The type of the level that selects between the keys that the first level
///     ranks equal.
/// </typeparam>
file readonly struct SortPair<TFirst, TSecond>
{
    internal SortPair(TFirst first, TSecond second)
    {
        this.First = first;
        this.Second = second;
    }

    internal TFirst First { get; }

    internal TSecond Second { get; }
}

/// <summary>
///     Compares two <see cref="SortPair{TFirst,TSecond}" /> values: the first
///     level, then the second.
/// </summary>
/// <remarks>
///     Each level holds its own direction, because a second level uses the
///     direction that the caller gave, at each direction of the level above it.
///     This code applies a direction with an interchange of the two operands, for
///     the cause in <see cref="SortedEntryComparer{TKey,TSortKey}" />.
/// </remarks>
/// <typeparam name="TFirst">The type of the level that decides first.</typeparam>
/// <typeparam name="TSecond">
///     The type of the level that selects between the keys that the first level
///     ranks equal.
/// </typeparam>
// ReSharper disable once InheritdocConsiderUsage
file sealed class SortPairComparer<TFirst, TSecond> : IComparer<SortPair<TFirst, TSecond>>
{
    private readonly IComparer<TFirst> first;
    private readonly bool firstIsDescending;
    private readonly IComparer<TSecond> second;
    private readonly bool secondIsDescending;

    internal SortPairComparer(
        IComparer<TFirst> first,
        bool firstIsDescending,
        IComparer<TSecond> second,
        bool secondIsDescending)
    {
        this.first = first;
        this.firstIsDescending = firstIsDescending;
        this.second = second;
        this.secondIsDescending = secondIsDescending;
    }

    public int Compare(SortPair<TFirst, TSecond> left, SortPair<TFirst, TSecond> right)
    {
        int result =
            this.firstIsDescending
                ? this.first.Compare(x: right.First, y: left.First)
                : this.first.Compare(x: left.First, y: right.First);

        if (result != 0)
        {
            return result;
        }

        return this.secondIsDescending
            ? this.second.Compare(x: right.Second, y: left.Second)
            : this.second.Compare(x: left.Second, y: right.Second);
    }
}

/// <summary>
///     The sort values of a key in an order with three levels or more, one for
///     each level.
/// </summary>
/// <remarks>
///     This type holds the value of each level as an object and has no type
///     parameter, thus each count of levels uses the same code. A level whose sort
///     value is a value type boxes it one time for each key that the order sorts.
///     A comparison allocates nothing.
/// </remarks>
file readonly struct SortValues
{
    private readonly object?[] values;

    internal SortValues(object?[] values) => this.values = values;

    internal object? this[int level] => this.values[level];
}

/// <summary>
///     Compares two <see cref="SortValues" /> values one level at a time.
/// </summary>
/// <remarks>
///     Each level holds its own direction and applies it, for the cause in
///     <see cref="SortPairComparer{TFirst,TSecond}" />.
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
// ReSharper disable once InheritdocConsiderUsage
file sealed class SortValuesComparer<TKey, TIdentity, TState> : IComparer<SortValues>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly ImmutableArray<SortLevel<TKey, TIdentity, TState>> levels;

    internal SortValuesComparer(ImmutableArray<SortLevel<TKey, TIdentity, TState>> levels) => this.levels = levels;

    public int Compare(SortValues left, SortValues right)
    {
        // ReSharper disable once LoopCanBeConvertedToQuery - The query allocates at each comparison, and one sort makes many comparisons.
        for (int index = 0; index < this.levels.Length; index++)
        {
            int result = this.levels[index].Compare(left: left[level: index], right: right[level: index]);

            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }
}

/// <summary>
///     Makes the order of three levels or more.
/// </summary>
file static class SortValuesOrder
{
    /// <summary>An order that sorts on each of the levels in sequence.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="levels">The levels, from the level that decides first.</param>
    /// <param name="keyComparer">Compares two keys that each level ranks equal.</param>
    /// <param name="keyEqualityComparer">The equality of the keys.</param>
    /// <returns>The order.</returns>
    /// <remarks>
    ///     The order uses the identity only when each level uses the identity only, and
    ///     that makes <c>DependsOnState</c> correct for it. One level that reads the
    ///     state makes the full order read the state.
    /// </remarks>
    internal static KeyOrder<TKey, TIdentity, TState> Create<TKey, TIdentity, TState>(
        ImmutableArray<SortLevel<TKey, TIdentity, TState>> levels,
        IComparer<TKey> keyComparer,
        IEqualityComparer<TKey> keyEqualityComparer)
        where TKey : notnull
        where TIdentity : notnull
    {
        // Each level holds its direction, thus the entry comparer above the levels compares in the
        // ascending direction. The key is the last level.
        SortedEntryComparer<TKey, SortValues> entryComparer =
            new(
                sortComparer: new SortValuesComparer<TKey, TIdentity, TState>(levels),
                keyComparer: keyComparer,
                isDescending: false);

        if (levels.Any(static level => level.ReadsState))
        {
            return new SortKeyOrder<TKey, TIdentity, TState, SortValues>(
                selector: (key, identity, state) =>
                {
                    object?[] values = new object?[levels.Length];

                    for (int index = 0; index < values.Length; index++)
                    {
                        values[index] = levels[index].Project(key: key, identity: identity, state: state);
                    }

                    return new SortValues(values);
                },
                identitySelector: null,
                levels: levels,
                entryComparer: entryComparer,
                keyEqualityComparer: keyEqualityComparer);
        }

        return new SortKeyOrder<TKey, TIdentity, TState, SortValues>(
            selector: null,
            identitySelector: (key, identity) =>
            {
                object?[] values = new object?[levels.Length];

                for (int index = 0; index < values.Length; index++)
                {
                    values[index] = levels[index].ProjectFromIdentity(key: key, identity: identity);
                }

                return new SortValues(values);
            },
            levels: levels,
            entryComparer: entryComparer,
            keyEqualityComparer: keyEqualityComparer);
    }
}

/// <summary>
///     One level of an order. Two orders are the same order, or the reverse of
///     each other, when their levels agree.
/// </summary>
/// <remarks>
///     <para>
///         An order with more than one level has a comparer that is a new instance
///         at each construction. Thus, a comparison of the comparers cannot identify
///         two such orders. A level holds the comparer and the selector that the
///         caller gave, and those are the same instances at each construction from
///         the same code.
///     </para>
///     <para>
///         This type has no parameter for the type of the sort value. Thus, an order
///         can hold a list of levels of different types. An order with three levels
///         or more reads and compares the sort values through this type, as objects.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
internal abstract class SortLevel<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>The selector from the caller. This code compares two orders with this reference.</summary>
    internal abstract object Selector { get; }

    /// <summary>The comparer from the caller. This code compares two orders with this reference.</summary>
    internal abstract object Comparer { get; }

    internal abstract bool IsDescending { get; }

    /// <summary>True when the selector of this level receives the state.</summary>
    internal abstract bool ReadsState { get; }

    /// <summary>The sort value of this level for an item, as an object.</summary>
    internal abstract object? Project(TKey key, TIdentity identity, TState state);

    /// <summary>
    ///     The sort value of this level for a key and an identity, as an object. Only a
    ///     level that does not read the state has one.
    /// </summary>
    /// <exception cref="InvalidOperationException">This level reads the state.</exception>
    internal abstract object? ProjectFromIdentity(TKey key, TIdentity identity);

    /// <summary>Compares two sort values of this level, in the direction of this level.</summary>
    internal abstract int Compare(object? left, object? right);

    /// <summary>The order of two levels: this level, and then <paramref name="next" />.</summary>
    /// <typeparam name="TNext">The type of the sort value of the next level.</typeparam>
    /// <param name="next">The level that selects between the keys that this level ranks equal.</param>
    /// <param name="keyComparer">Compares two keys that the two levels rank equal.</param>
    /// <param name="keyEqualityComparer">The equality of the keys.</param>
    /// <returns>The order.</returns>
    /// <remarks>
    ///     This method is generic, because only a level knows the type of its own sort
    ///     value. The pair of two levels needs the two types for a compare operation
    ///     with no boxing.
    /// </remarks>
    internal abstract KeyOrder<TKey, TIdentity, TState> PairWith<TNext>(
        SortLevel<TKey, TIdentity, TState, TNext> next,
        IComparer<TKey> keyComparer,
        IEqualityComparer<TKey> keyEqualityComparer);
}

/// <summary>One level of an order, with the type of its sort value.</summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
/// <typeparam name="TSortKey">The type of the sort value of the level.</typeparam>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class SortLevel<TKey, TIdentity, TState, TSortKey> : SortLevel<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>Makes a level from one of the two selectors.</summary>
    /// <param name="selector">Makes the sort value from a full item, or null.</param>
    /// <param name="identitySelector">Makes it from the key and the identity only, or null.</param>
    /// <param name="selectorReference">
    ///     The selector from the caller. Give the delegate that the caller wrote, and
    ///     not a delegate from it, for the cause in the constructors of
    ///     <see cref="SortKeyOrder{TKey,TIdentity,TState,TSortKey}" />.
    /// </param>
    /// <param name="comparer">Compares two sort values of the level.</param>
    /// <param name="isDescending">True when the level uses the opposite direction.</param>
    /// <remarks>
    ///     One of the two selectors has a value. A level on the identity never
    ///     receives the state, thus an order of such levels can say that it does not
    ///     read the state.
    /// </remarks>
    internal SortLevel(
        Func<TKey, TIdentity, TState, TSortKey>? selector,
        Func<TKey, TIdentity, TSortKey>? identitySelector,
        object selectorReference,
        IComparer<TSortKey> comparer,
        bool isDescending)
    {
        this.IdentitySelector = identitySelector;
        this.Selector = selectorReference;
        this.SortComparer = comparer;
        this.IsDescending = isDescending;

        this.WholeItemSelector =
            identitySelector is null
                // ReSharper disable once NullableWarningSuppressionIsUsed - exactly one of the two is set,
                // and identitySelector being null is what says it is this one.
                ? selector!
                : (key, identity, _) => identitySelector(arg1: key, arg2: identity);
    }

    // The selector on the key and the identity only, or null.
    private Func<TKey, TIdentity, TSortKey>? IdentitySelector { get; }

    // The level as a function from the full item, at each type of its construction.
    private Func<TKey, TIdentity, TState, TSortKey> WholeItemSelector { get; }

    private IComparer<TSortKey> SortComparer { get; }

    /// <inheritdoc />
    internal override object Selector { get; }

    /// <inheritdoc />
    internal override object Comparer => this.SortComparer;

    /// <inheritdoc />
    internal override bool IsDescending { get; }

    /// <inheritdoc />
    internal override bool ReadsState => this.IdentitySelector is null;

    /// <inheritdoc />
    internal override object? Project(TKey key, TIdentity identity, TState state) =>
        this.WholeItemSelector(arg1: key, arg2: identity, arg3: state);

    /// <inheritdoc />
    internal override object? ProjectFromIdentity(TKey key, TIdentity identity) =>
        this.IdentitySelector is null
            ? throw new InvalidOperationException(
                "This level reads the state, so it has no sort value from the key and the identity only. "
                + "An order reads a level this way only when each of its levels uses the identity only.")
            : this.IdentitySelector(arg1: key, arg2: identity);

    /// <inheritdoc />
    /// <remarks>
    ///     This code applies the direction with an interchange of the two operands,
    ///     for the cause in <see cref="SortedEntryComparer{TKey,TSortKey}" />.
    /// </remarks>
    // ReSharper disable NullableWarningSuppressionIsUsed - Each value comes from Project or
    // ProjectFromIdentity of this level, thus it has the type of the level. It is null only when the
    // selector gave null, and the comparer from the caller then gets that null as the selector gave it.
    internal override int Compare(object? left, object? right) =>
        this.IsDescending
            ? this.SortComparer.Compare(x: (TSortKey)right!, y: (TSortKey)left!)
            : this.SortComparer.Compare(x: (TSortKey)left!, y: (TSortKey)right!);
    // ReSharper restore NullableWarningSuppressionIsUsed

    /// <inheritdoc />
    internal override KeyOrder<TKey, TIdentity, TState> PairWith<TNext>(
        SortLevel<TKey, TIdentity, TState, TNext> next,
        IComparer<TKey> keyComparer,
        IEqualityComparer<TKey> keyEqualityComparer)
    {
        // The pair holds the direction of each of the two levels, thus the entry comparer above it
        // compares in the ascending direction. The key is the last level. One more level refines the
        // order and does not change the level that makes the order total.
        SortedEntryComparer<TKey, SortPair<TSortKey, TNext>> entryComparer =
            new(
                sortComparer: new SortPairComparer<TSortKey, TNext>(
                    first: this.SortComparer,
                    firstIsDescending: this.IsDescending,
                    second: next.SortComparer,
                    secondIsDescending: next.IsDescending),
                keyComparer: keyComparer,
                isDescending: false);

        ImmutableArray<SortLevel<TKey, TIdentity, TState>> levels = [this, next];

        // The order uses the identity only when each level uses the identity only, and that makes
        // DependsOnState correct for the combined order. One level that reads the state makes the
        // full order read the state.
        if (this.IdentitySelector is not null && next.IdentitySelector is not null)
        {
            Func<TKey, TIdentity, TSortKey> firstIdentity = this.IdentitySelector;
            Func<TKey, TIdentity, TNext> nextIdentity = next.IdentitySelector;

            return new SortKeyOrder<TKey, TIdentity, TState, SortPair<TSortKey, TNext>>(
                selector: null,
                identitySelector: (key, identity) =>
                    new SortPair<TSortKey, TNext>(
                        first: firstIdentity(arg1: key, arg2: identity),
                        second: nextIdentity(arg1: key, arg2: identity)),
                levels: levels,
                entryComparer: entryComparer,
                keyEqualityComparer: keyEqualityComparer);
        }

        Func<TKey, TIdentity, TState, TSortKey> firstWhole = this.WholeItemSelector;
        Func<TKey, TIdentity, TState, TNext> nextWhole = next.WholeItemSelector;

        return new SortKeyOrder<TKey, TIdentity, TState, SortPair<TSortKey, TNext>>(
            selector: (key, identity, state) =>
                new SortPair<TSortKey, TNext>(
                    first: firstWhole(arg1: key, arg2: identity, arg3: state),
                    second: nextWhole(arg1: key, arg2: identity, arg3: state)),
            identitySelector: null,
            levels: levels,
            entryComparer: entryComparer,
            keyEqualityComparer: keyEqualityComparer);
    }
}

/// <summary>
///     An order that puts each key at a sort value that it makes for that key from
///     a snapshot.
/// </summary>
/// <remarks>
///     This is the part that is the same in the two types of order, and a sorted
///     key set needs only this part from each one. It gives the sort value of a
///     key, and it compares two entries with their sort values. An order on the
///     items makes the sort value from an item, and the arrival order makes it
///     from the time of the arrival of the key.
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
/// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
// ReSharper disable once InheritdocConsiderUsage
internal abstract class ProjectedKeyOrder<TKey, TIdentity, TState, TSortKey>(
    in IEqualityComparer<TKey> keyEqualityComparer)
    : KeyOrder<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>Compares two entries in this order.</summary>
    protected internal abstract SortedEntryComparer<TKey, TSortKey> EntryComparer { get; }

    protected IEqualityComparer<TKey> KeyEqualityComparer { get; } = keyEqualityComparer;

    /// <summary>The sort value of <paramref name="key" /> in this order.</summary>
    /// <param name="key">A key that the snapshot holds.</param>
    /// <param name="snapshot">The snapshot that gives the sort value.</param>
    /// <returns>The sort value.</returns>
    /// <exception cref="InvalidOperationException">The snapshot does not hold the key.</exception>
    /// <remarks>
    ///     Each caller adds only the keys that the snapshot holds. Those are the keys
    ///     from that snapshot, the keys that the caller tested against it, and the
    ///     keys in an operation of the stage above, which the snapshot of that stage
    ///     accepts. A key that the snapshot does not hold has no sort value, and a
    ///     read of one is a defect in the stage that does it.
    /// </remarks>
    internal abstract TSortKey Project(TKey key, CollectionSnapshot<TKey, TIdentity, TState> snapshot);

    /// <summary>
    ///     The exception that <see cref="Project" /> throws for a key that the
    ///     snapshot does not hold.
    /// </summary>
    protected static InvalidOperationException KeyNotInSnapshot(TKey key) =>
        new(
            $"The snapshot does not hold the key {key}, so it has no sort value to file it under. "
            + "A stage only files keys its snapshot holds, so the stage that asked is at fault.");

    /// <inheritdoc />
    internal sealed override OrderedKeys<TKey, TIdentity, TState> CreateFrom(
        IEnumerable<TKey> keys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
    {
        ImmutableSortedSet<SortedEntry<TKey, TSortKey>>.Builder entries =
            ImmutableSortedSet.CreateBuilder(this.EntryComparer);

        ImmutableDictionary<TKey, SortedEntry<TKey, TSortKey>>.Builder byKey =
            ImmutableDictionary.CreateBuilder<TKey, SortedEntry<TKey, TSortKey>>(this.KeyEqualityComparer);

        foreach (TKey key in keys)
        {
            SortedEntry<TKey, TSortKey> entry = new(key: key, sortValue: this.Project(key: key, snapshot: snapshot));

            entries.Add(entry);
            byKey[key] = entry;
        }

        return new SortedKeys<TKey, TIdentity, TState, TSortKey>(
            order: this,
            entries: entries.ToImmutable(),
            byKey: byKey.ToImmutable());
    }
}

/// <summary>
///     The sequence of the arrival of the items, which is the order of the
///     collection.
///     <c>ByArrival</c> returns it.
/// </summary>
/// <remarks>
///     <para>
///         This code gives a number to each key one time, at its arrival. Thus,
///         two keys never have the same sort value, and this code never compares
///         the keys. For that cause a collection can list keys of a type with no
///         order of its own.
///     </para>
///     <para>
///         For the same cause, this code never reads a second level. Thus,
///         <see cref="Then{TNext}" /> answers with this order, and does not add
///         a level with no effect. This order also never reads the state, because
///         an update is not an arrival.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class ArrivalOrder<TKey, TIdentity, TState> : ProjectedKeyOrder<TKey, TIdentity, TState, long>
    where TKey : notnull
    where TIdentity : notnull
{
    internal static readonly ArrivalOrder<TKey, TIdentity, TState> Instance = new(EqualityComparer<TKey>.Default);

    private ArrivalOrder(IEqualityComparer<TKey> keyEqualityComparer)
        : base(keyEqualityComparer)
    {
    }

    /// <inheritdoc />
    internal override bool DependsOnState => false;

    /// <inheritdoc />
    protected internal override SortedEntryComparer<TKey, long> EntryComparer { get; } =
        new(sortComparer: Comparer<long>.Default, keyComparer: NoTieComparer<TKey>.Instance, isDescending: false);

    /// <inheritdoc />
    internal override bool IsEquivalentTo(KeyOrder<TKey, TIdentity, TState> other) =>
        other is ArrivalOrder<TKey, TIdentity, TState> otherTyped
        && ReferenceEquals(objA: this.KeyEqualityComparer, objB: otherTyped.KeyEqualityComparer);

    /// <inheritdoc />
    internal override bool TryReverse(
        OrderedKeys<TKey, TIdentity, TState> keys,
        [NotNullWhen(true)] out OrderedKeys<TKey, TIdentity, TState>? reversedKeys)
    {
        reversedKeys = null;
        return false;
    }

    /// <inheritdoc />
    internal override KeyOrder<TKey, TIdentity, TState> Then<TNext>(SortLevel<TKey, TIdentity, TState, TNext> next) =>
        this;

    /// <inheritdoc />
    internal override KeyOrder<TKey, TIdentity, TState> With(IEqualityComparer<TKey> keyEqualityComparer) =>
        new ArrivalOrder<TKey, TIdentity, TState>(keyEqualityComparer);

    /// <inheritdoc />
    internal override long Project(TKey key, CollectionSnapshot<TKey, TIdentity, TState> snapshot) =>
        snapshot.TryGetArrival(key: key, arrival: out long arrival) ? arrival : throw KeyNotInSnapshot(key);
}

/// <summary>
///     The second level of an order whose sort values are never equal. Such an
///     order needs no second level.
/// </summary>
/// <remarks>
///     This throws an exception and does not answer. A call to it means that two
///     keys have the same sort value, and this code gives each sort value one
///     time. Such a set is incorrect, and an answer hides that.
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class NoTieComparer<TKey> : IComparer<TKey>
{
    internal static readonly NoTieComparer<TKey> Instance = new();

    private NoTieComparer()
    {
    }

    public int Compare(TKey? x, TKey? y) =>
        throw new InvalidOperationException(
            $"The keys {x} and {y} were filed under the same arrival, which is only ever given to one key. "
            + "This set was not built by this library.");
}

/// <summary>
///     An order that puts each key at a value from its item.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
/// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
/// <remarks>
///     This is internal, because code out of this assembly cannot put an order
///     into a view. Each sort stage builds its own order from the selector that it
///     receives.
///     <see cref="KeyOrder{TKey,TIdentity,TState}" /> is public because
///     <see cref="OrderedKeys{TKey,TIdentity,TState}.Order" /> answers with one.
///     Thus, other code can read an order and cannot give one, and that lets a
///     reader test the statement of <see cref="DependsOnState" />. An order from
///     external code can make that statement incorrectly, and a view then stops
///     its sorts and gives no message.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class SortKeyOrder<TKey, TIdentity, TState, TSortKey>
    : ProjectedKeyOrder<TKey, TIdentity, TState, TSortKey>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>
    ///     One of these two fields has a value, and <see cref="DependsOnState" />
    ///     gives which one.
    ///     That is deliberate. An order cannot say that it does not read the state and
    ///     then read the state, because the selector that says it receives no state.
    /// </summary>
    private readonly Func<TKey, TIdentity, TSortKey>? identitySelector;

    /// <summary>
    ///     The levels of this order, as the references from the caller. Each level
    ///     holds the selector and the comparer that the caller gave, and its
    ///     direction.
    /// </summary>
    /// <remarks>
    ///     <see cref="IsEquivalentTo" /> and <see cref="TryReverse" /> compare two
    ///     orders with these levels, and <see cref="Then{TNext}" /> makes the next
    ///     order from them. The changed selector is a new delegate at each
    ///     construction of an order, thus two of them are never equal. The selector
    ///     from the caller is the same instance at each construction from the same
    ///     lambda. The comparer from the caller is also the same instance. An order
    ///     with more than one level has a combined comparer that is new at each
    ///     construction. Thus, its levels identify it and that comparer does not.
    /// </remarks>
    private readonly ImmutableArray<SortLevel<TKey, TIdentity, TState>> levels;

    /// <summary>
    ///     One of these two fields has a value, and <see cref="DependsOnState" />
    ///     gives which one.
    ///     That is deliberate. An order cannot say that it does not read the state and
    ///     then read the state, because the selector that says it receives no state.
    /// </summary>
    private readonly Func<TKey, TIdentity, TState, TSortKey>? selector;

    /// <summary>Makes an order whose sort value comes from the full item.</summary>
    /// <param name="selector">Projects the sort value from a key and its item.</param>
    /// <param name="originalSelectorReference">
    ///     The selector from the caller. This code compares two orders with it.
    /// </param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">
    ///     Compares two keys with equal sort values, thus the
    ///     order is total.
    /// </param>
    /// <param name="isDescending">True when the sort uses the opposite direction.</param>
    /// <remarks>
    ///     Give the reference from a position as near to the code of the caller as
    ///     possible, which is the delegate that the caller wrote and not a delegate
    ///     from it. This code reads two orders as the same order only when these
    ///     references are the same instance.
    /// </remarks>
    public SortKeyOrder(
        Func<TKey, TIdentity, TState, TSortKey> selector,
        object originalSelectorReference,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool isDescending)
        : this(
            selector: selector,
            identitySelector: null,
            levels:
            [
                new SortLevel<TKey, TIdentity, TState, TSortKey>(
                    selector: selector,
                    identitySelector: null,
                    selectorReference: originalSelectorReference,
                    comparer: sortComparer,
                    isDescending: isDescending)
            ],
            entryComparer: new SortedEntryComparer<TKey, TSortKey>(
                sortComparer: sortComparer,
                keyComparer: keyComparer,
                isDescending: isDescending),
            keyEqualityComparer: EqualityComparer<TKey>.Default)
    {
    }

    /// <summary>
    ///     Makes an order whose sort value comes from the key and the immutable part
    ///     only. A state edit cannot change the key or that part.
    /// </summary>
    /// <param name="selector">Projects the sort value from a key and its identity.</param>
    /// <param name="originalSelectorReference">
    ///     The selector from the caller. This code compares two orders with it.
    /// </param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">
    ///     Compares two keys with equal sort values, thus the
    ///     order is total.
    /// </param>
    /// <param name="isDescending">True when the sort uses the opposite direction.</param>
    /// <remarks>
    ///     <para>
    ///         <see cref="DependsOnState" /> gives the benefit. A stage in this order
    ///         does not sort a key again at a state edit, and the construction of a
    ///         stage does not read the state map.
    ///     </para>
    ///     <para>
    ///         Give the reference from a position as near to the code of the caller as
    ///         possible, for the cause in the other constructor.
    ///     </para>
    /// </remarks>
    public SortKeyOrder(
        Func<TKey, TIdentity, TSortKey> selector,
        object originalSelectorReference,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool isDescending)
        : this(
            selector: null,
            identitySelector: selector,
            levels:
            [
                new SortLevel<TKey, TIdentity, TState, TSortKey>(
                    selector: null,
                    identitySelector: selector,
                    selectorReference: originalSelectorReference,
                    comparer: sortComparer,
                    isDescending: isDescending)
            ],
            entryComparer: new SortedEntryComparer<TKey, TSortKey>(
                sortComparer: sortComparer,
                keyComparer: keyComparer,
                isDescending: isDescending),
            keyEqualityComparer: EqualityComparer<TKey>.Default)
    {
    }

    // Makes an order from its parts. An order with more than one level uses this. One of the two
    // selectors has a value.
    internal SortKeyOrder(
        Func<TKey, TIdentity, TState, TSortKey>? selector,
        Func<TKey, TIdentity, TSortKey>? identitySelector,
        ImmutableArray<SortLevel<TKey, TIdentity, TState>> levels,
        SortedEntryComparer<TKey, TSortKey> entryComparer,
        IEqualityComparer<TKey> keyEqualityComparer)
        : base(keyEqualityComparer)
    {
        this.selector = selector;
        this.identitySelector = identitySelector;
        this.levels = levels;
        this.EntryComparer = entryComparer;
    }

    /// <inheritdoc />
    internal override bool DependsOnState => this.identitySelector is null;

    /// <inheritdoc />
    protected internal override SortedEntryComparer<TKey, TSortKey> EntryComparer { get; }

    /// <summary>
    ///     True when <paramref name="other" /> sorts on the same values with the same
    ///     comparers at each level. The direction is the same at each level, or it is
    ///     the opposite at each level.
    /// </summary>
    /// <param name="other">The order to compare against.</param>
    /// <param name="isSameDirection">
    ///     True when each level of the two orders has the same direction. It has a
    ///     value only when the two orders agree.
    /// </param>
    /// <returns>True when the direction is the only difference between the two orders.</returns>
    /// <remarks>
    ///     <para>
    ///         This code compares references only. It does not identify two orders
    ///         that use two equal comparers with different instances, or two lambdas
    ///         that read the same field. That costs one build that the stage can
    ///         omit, and it never puts a list in an incorrect order.
    ///     </para>
    ///     <para>
    ///         Two orders whose levels change direction one by one are different
    ///         orders. The keys that one level ranks equal keep their sequence from
    ///         the next level. No read of the set from its end gives that sequence.
    ///     </para>
    /// </remarks>
    private bool SortsTheSameValueAs(KeyOrder<TKey, TIdentity, TState> other, out bool isSameDirection)
    {
        isSameDirection = false;

        if (other is not SortKeyOrder<TKey, TIdentity, TState, TSortKey> otherTyped
            || !ReferenceEquals(objA: this.KeyEqualityComparer, objB: otherTyped.KeyEqualityComparer)
            || !ReferenceEquals(objA: this.EntryComparer.KeyComparer, objB: otherTyped.EntryComparer.KeyComparer)
            || this.levels.Length != otherTyped.levels.Length)
        {
            return false;
        }

        bool isEachSame = true;
        bool isEachOpposite = true;

        for (int index = 0; index < this.levels.Length; index++)
        {
            SortLevel<TKey, TIdentity, TState> level = this.levels[index];
            SortLevel<TKey, TIdentity, TState> otherLevel = otherTyped.levels[index];

            if (!ReferenceEquals(objA: level.Selector, objB: otherLevel.Selector)
                || !ReferenceEquals(objA: level.Comparer, objB: otherLevel.Comparer))
            {
                return false;
            }

            if (level.IsDescending == otherLevel.IsDescending)
            {
                isEachOpposite = false;
            }
            else
            {
                isEachSame = false;
            }
        }

        isSameDirection = isEachSame;

        return isEachSame || isEachOpposite;
    }

    /// <inheritdoc />
    internal override bool IsEquivalentTo(KeyOrder<TKey, TIdentity, TState> other) =>
        this.SortsTheSameValueAs(other: other, isSameDirection: out bool isSameDirection) && isSameDirection;

    /// <inheritdoc />
    internal override bool TryReverse(
        OrderedKeys<TKey, TIdentity, TState> keys,
        [NotNullWhen(true)] out OrderedKeys<TKey, TIdentity, TState>? reversedKeys)
    {
        if (this.SortsTheSameValueAs(other: keys.Order, isSameDirection: out bool isSameDirection)
            && !isSameDirection)
        {
            // This code reads a set in its own direction from its end. A set that it reads from its
            // end goes back to its own direction. Neither copies the set.
            switch (keys)
            {
                case SortedKeys<TKey, TIdentity, TState, TSortKey> sortedKeys:
                    reversedKeys =
                        new ReversedKeys<TKey, TIdentity, TState, TSortKey>(source: sortedKeys, order: this);

                    return true;
                case ReversedKeys<TKey, TIdentity, TState, TSortKey> reversedSortedKeys:
                    reversedKeys = reversedSortedKeys.Source.WithOrder(this);
                    return true;
            }
        }

        reversedKeys = null;
        return false;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The second level makes a pair of the two sort value types. The third level
    ///     and each level after it make an order that holds the sort values as objects.
    ///     A deeper pair for each level makes a deeper generic type for each level, and
    ///     the native AOT compiler stops at a depth.
    /// </remarks>
    internal override KeyOrder<TKey, TIdentity, TState> Then<TNext>(SortLevel<TKey, TIdentity, TState, TNext> next) =>
        this.levels.Length == 1
            ? this.levels[0]
                .PairWith(
                    next: next,
                    keyComparer: this.EntryComparer.KeyComparer,
                    keyEqualityComparer: this.KeyEqualityComparer)
            : SortValuesOrder.Create(
                levels: this.levels.Add(next),
                keyComparer: this.EntryComparer.KeyComparer,
                keyEqualityComparer: this.KeyEqualityComparer);

    /// <inheritdoc />
    internal override KeyOrder<TKey, TIdentity, TState> With(IEqualityComparer<TKey> keyEqualityComparer) =>
        new SortKeyOrder<TKey, TIdentity, TState, TSortKey>(
            selector: this.selector,
            identitySelector: this.identitySelector,
            levels: this.levels,
            entryComparer: this.EntryComparer,
            keyEqualityComparer: keyEqualityComparer);

    /// <inheritdoc />
    /// <remarks>
    ///     An order that does not read the state also does not read the state map,
    ///     which is one lookup less for each key. A new build does this for each key
    ///     that it keeps.
    ///     The two paths give different answers only for a key that the identity map
    ///     holds and the state map does not hold. <c>Resolve</c> writes the two maps
    ///     together, thus that condition cannot occur.
    /// </remarks>
    internal override TSortKey Project(TKey key, CollectionSnapshot<TKey, TIdentity, TState> snapshot)
    {
        if (this.identitySelector is not null)
        {
            return snapshot.TryGetIdentity(key: key, identity: out TIdentity? identityOnly)
                ? this.identitySelector(arg1: key, arg2: identityOnly)
                : throw KeyNotInSnapshot(key);
        }

        return snapshot.TryGetHalves(key: key, identity: out TIdentity? identity, state: out TState? state)
            // ReSharper disable once NullableWarningSuppressionIsUsed - exactly one of the two
            // selectors is set, and identitySelector being null is what says it is this one.
            ? this.selector!(arg1: key, arg2: identity, arg3: state)
            : throw KeyNotInSnapshot(key);
    }
}

file sealed class SortedKeys<TKey, TIdentity, TState, TSortKey> : OrderedKeys<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly ImmutableDictionary<TKey, SortedEntry<TKey, TSortKey>> byKey;
    private readonly ImmutableSortedSet<SortedEntry<TKey, TSortKey>> entries;
    private readonly ProjectedKeyOrder<TKey, TIdentity, TState, TSortKey> order;

    internal SortedKeys(
        ProjectedKeyOrder<TKey, TIdentity, TState, TSortKey> order,
        ImmutableSortedSet<SortedEntry<TKey, TSortKey>> entries,
        ImmutableDictionary<TKey, SortedEntry<TKey, TSortKey>> byKey)
    {
        this.order = order;
        this.entries = entries;
        this.byKey = byKey;
    }

    internal override KeyOrder<TKey, TIdentity, TState> Order => this.order;

    public override int Count => this.entries.Count;

    public override TKey this[int index] => this.entries[index].Key;

    public override bool Contains(TKey key) => this.byKey.ContainsKey(key);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    ///     The map holds the key and the ordering does not hold its entry. That
    ///     condition cannot occur, and it means an incorrect build of this set.
    /// </exception>
    /// <remarks>
    ///     The two answers here are different types of answer. A key that this set
    ///     does not hold is missing, which is the meaning of -1 and the meaning that
    ///     each caller reads. A key that the map holds, and whose entry is not in the
    ///     ordering, is not an answer. This code writes the map and the ordering
    ///     together, thus one with a key that the other does not have means that the
    ///     two are no longer equal. An answer of -1 reports an incorrect set as a
    ///     usual missing key, and a stage then adds keys to a set that is incorrect.
    /// </remarks>
    internal override int IndexOfInternal(TKey key)
    {
        if (!this.byKey.TryGet(key: key, value: out SortedEntry<TKey, TSortKey>? entry))
        {
            return -1;
        }

        int index = this.entries.IndexOf(entry);

        return index >= 0
            ? index
            : throw new InvalidOperationException(
                $"The key {key} is filed under a sort value that is not in the ordering. The two "
                + "are only ever written together, so this set was not built by this library.");
    }

    internal override OrderedKeys<TKey, TIdentity, TState> Add(
        TKey key,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot) =>
        this.AddSorted(key: key, snapshot: snapshot);

    internal override OrderedKeys<TKey, TIdentity, TState> Remove(TKey key) => this.RemoveSorted(key);

    public override IEnumerator<TKey> GetEnumerator() => this.entries.Select(static entry => entry.Key).GetEnumerator();

    /// <summary>
    ///     Compares two sort values. A set read from its end uses this to find the
    ///     two ends of a run of equal sort values.
    /// </summary>
    internal IComparer<TSortKey> SortComparer => this.order.EntryComparer.SortComparer;

    /// <summary>The entry at a position, at a cost of <c>O(log n)</c>.</summary>
    internal SortedEntry<TKey, TSortKey> EntryAt(int index) => this.entries[index];

    /// <summary>The entries from the last to the first, with no copy.</summary>
    internal IEnumerable<SortedEntry<TKey, TSortKey>> EntriesFromEnd() => this.entries.Reverse();

    /// <summary>
    ///     This set with a different order. The order must put the entries in the
    ///     sequence that they have, because the entries keep the comparer of their
    ///     construction. A set read from its end and then from its start again gets
    ///     the order of that second change through this method.
    /// </summary>
    internal SortedKeys<TKey, TIdentity, TState, TSortKey> WithOrder(
        ProjectedKeyOrder<TKey, TIdentity, TState, TSortKey> order) =>
        new(order: order, entries: this.entries, byKey: this.byKey);

    /// <summary>
    ///     <see cref="Add" />, with the result as this type. A set read from its end
    ///     adds through this method and keeps the result as its source.
    /// </summary>
    internal SortedKeys<TKey, TIdentity, TState, TSortKey> AddSorted(
        TKey key,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
    {
        // This throws an exception for a key that the snapshot does not hold. See
        // ProjectedKeyOrder.Project.
        SortedEntry<TKey, TSortKey> entry = new(key: key, sortValue: this.order.Project(key: key, snapshot: snapshot));

        // This code sorts a key in the set again, and does not add it a second time. The map holds
        // one entry for each key and the ordering holds one entry for each sort value. An add of a
        // key in the set, at a different value, leaves the previous entry in the ordering with no
        // key. The set then counts that entry, gives the key two times, and does not agree with its
        // own map. No stage does that now, because a second sort removes the key before it adds the
        // key, but this type did not give that rule.
        ImmutableSortedSet<SortedEntry<TKey, TSortKey>> ordering =
            this.byKey.TryGet(key: key, value: out SortedEntry<TKey, TSortKey>? filed)
                ? this.entries.Remove(filed)
                : this.entries;

        return new SortedKeys<TKey, TIdentity, TState, TSortKey>(
            order: this.order,
            entries: ordering.Add(entry),
            byKey: this.byKey.SetItem(key: key, value: entry));
    }

    /// <summary><see cref="Remove" />, with the result as this type.</summary>
    internal SortedKeys<TKey, TIdentity, TState, TSortKey> RemoveSorted(TKey key) =>
        this.byKey.TryGet(key: key, value: out SortedEntry<TKey, TSortKey>? entry)
            ? new SortedKeys<TKey, TIdentity, TState, TSortKey>(
                order: this.order,
                entries: this.entries.Remove(entry),
                byKey: this.byKey.Remove(key))
            : this;
}

/// <summary>
///     A sorted set read from its end to its start, with no copy of that set. A
///     sort stage holds one of these when its order becomes the same order in the
///     opposite direction. The stage then sorts no key again.
/// </summary>
/// <remarks>
///     <para>
///         This is not the inner set in the opposite sequence. The key breaks a
///         tie in ascending order at each direction of a sort. Thus, two keys with
///         equal sort values keep their sequence when the direction changes. This
///         set reads the runs of equal sort values from the last run to the first,
///         and reads each run from its start. A build of the set in the new order
///         gives the same sequence, and the tests compare the two.
///     </para>
///     <para>
///         A position in this set maps to a position in the inner set through the
///         run that holds it. The inner set gives the entry at a position at a cost
///         of <c>O(log n)</c>. This code finds the two ends of a run with a binary
///         search over the positions. Thus, a read of one position costs
///         <c>O(log² n)</c>, and a read of all the keys costs <c>O(n)</c>. Before,
///         the stage copied the set into a new sorted set at each change of
///         direction. That cost <c>O(n log n)</c> and one allocation for each key.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
file sealed class ReversedKeys<TKey, TIdentity, TState, TSortKey> : OrderedKeys<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly ProjectedKeyOrder<TKey, TIdentity, TState, TSortKey> order;

    internal ReversedKeys(
        SortedKeys<TKey, TIdentity, TState, TSortKey> source,
        ProjectedKeyOrder<TKey, TIdentity, TState, TSortKey> order)
    {
        this.Source = source;
        this.order = order;
    }

    /// <summary>The set in its own direction.</summary>
    internal SortedKeys<TKey, TIdentity, TState, TSortKey> Source { get; }

    internal override KeyOrder<TKey, TIdentity, TState> Order => this.order;

    public override int Count => this.Source.Count;

    public override TKey this[int index]
    {
        get
        {
            if (index < 0 || index >= this.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            // The runs mirror as blocks, thus the entry at the mirrored position is in the run that
            // holds the answer. The answer is at the same distance from the start of that run as
            // this index is from the start of the mirrored block.
            (int first, int last) = this.RunAround(this.Count - 1 - index);

            return this.Source.EntryAt(first + index - (this.Count - 1 - last)).Key;
        }
    }

    public override bool Contains(TKey key) => this.Source.Contains(key);

    internal override int IndexOfInternal(TKey key)
    {
        int index = this.Source.IndexOfInternal(key);

        if (index < 0)
        {
            return -1;
        }

        (int first, int last) = this.RunAround(index);

        return this.Count - 1 - last + index - first;
    }

    internal override OrderedKeys<TKey, TIdentity, TState> Add(
        TKey key,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot) =>
        new ReversedKeys<TKey, TIdentity, TState, TSortKey>(
            source: this.Source.AddSorted(key: key, snapshot: snapshot),
            order: this.order);

    internal override OrderedKeys<TKey, TIdentity, TState> Remove(TKey key)
    {
        SortedKeys<TKey, TIdentity, TState, TSortKey> source = this.Source.RemoveSorted(key);

        return ReferenceEquals(objA: source, objB: this.Source)
            ? this
            : new ReversedKeys<TKey, TIdentity, TState, TSortKey>(source: source, order: this.order);
    }

    public override IEnumerator<TKey> GetEnumerator()
    {
        // The inner set gives its entries from its end. This code collects one run, and gives the
        // run from its start when the next entry has a different sort value.
        List<TKey> run = [];
        SortedEntry<TKey, TSortKey>? previous = null;

        foreach (SortedEntry<TKey, TSortKey> entry in this.Source.EntriesFromEnd())
        {
            if (previous is not null
                && this.Source.SortComparer.Compare(x: entry.SortValue, y: previous.SortValue) != 0)
            {
                for (int index = run.Count - 1; index >= 0; index--)
                {
                    yield return run[index];
                }

                run.Clear();
            }

            run.Add(entry.Key);
            previous = entry;
        }

        for (int index = run.Count - 1; index >= 0; index--)
        {
            yield return run[index];
        }
    }

    /// <summary>
    ///     The first and the last position of the run of equal sort values that holds
    ///     a position of the inner set.
    /// </summary>
    /// <remarks>
    ///     Two binary searches over the positions, because the inner set can find an
    ///     entry and cannot find the ends of a run. Each probe reads one entry at a
    ///     cost of <c>O(log n)</c>.
    /// </remarks>
    private (int First, int Last) RunAround(int index)
    {
        IComparer<TSortKey> comparer = this.Source.SortComparer;
        TSortKey value = this.Source.EntryAt(index).SortValue;

        int low = 0;
        int high = index;

        while (low < high)
        {
            int middle = low + (high - low) / 2;

            if (comparer.Compare(x: this.Source.EntryAt(middle).SortValue, y: value) == 0)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        int first = low;

        low = index;
        high = this.Count - 1;

        while (low < high)
        {
            int middle = low + (high - low + 1) / 2;

            if (comparer.Compare(x: this.Source.EntryAt(middle).SortValue, y: value) == 0)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return (first, low);
    }
}

/// <summary>
///     A continuous window of a second ordered set, with no copy of that set. A
///     <c>Slice</c> stage holds one of these windows, and a <c>Take</c> stage also
///     holds one, because <c>Take</c> is a window that starts at zero. Thus, a
///     stage after one of those stages reads an ordered set and can find a
///     position in it at a cost of <c>O(log n)</c>.
/// </summary>
/// <remarks>
///     The window has a limit, and that limit keeps the cost of the stage above it
///     low. The step of that stage compares the previous window against the new
///     window and does not change the operations from above, at a cost of
///     <c>O(limit)</c> and not <c>O(n)</c>. A skip has no limit, and for that
///     cause there is no stage for a skip.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class RangeKeys<TKey, TIdentity, TState> : OrderedKeys<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly int limit;
    private readonly int offset;
    private readonly OrderedKeys<TKey, TIdentity, TState> source;

    internal RangeKeys(OrderedKeys<TKey, TIdentity, TState> source, int offset, int limit)
    {
        this.source = source;

        // A negative value counts as zero. The constant overloads of Take and Slice throw before
        // this point. A cell can move to a negative value in a transaction, and a throw here
        // fails that transaction.
        this.offset = Math.Max(val1: offset, val2: 0);
        this.limit = Math.Max(val1: limit, val2: 0);
    }

    internal override KeyOrder<TKey, TIdentity, TState> Order => this.source.Order;

    public override int Count =>
        Math.Min(val1: Math.Max(val1: this.source.Count - this.offset, val2: 0), val2: this.limit);

    public override TKey this[int index] =>
        index >= 0 && index < this.Count
            ? this.source[index + this.offset]
            : throw new ArgumentOutOfRangeException(nameof(index));

    public override bool Contains(TKey key) => this.IndexOfInternal(key) >= 0;

    internal override int IndexOfInternal(TKey key)
    {
        int index = this.source.IndexOfInternal(key);

        if (index < this.offset)
        {
            return -1;
        }

        int shifted = index - this.offset;

        return shifted < this.Count ? shifted : -1;
    }

    internal override OrderedKeys<TKey, TIdentity, TState> Add(
        TKey key,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot) =>
        new RangeKeys<TKey, TIdentity, TState>(
            source: this.source.Add(key: key, snapshot: snapshot),
            offset: this.offset,
            limit: this.limit);

    internal override OrderedKeys<TKey, TIdentity, TState> Remove(TKey key) =>
        new RangeKeys<TKey, TIdentity, TState>(source: this.source.Remove(key), offset: this.offset, limit: this.limit);

    public override IEnumerator<TKey> GetEnumerator()
    {
        int count = this.Count;

        for (int index = 0; index < count; index++)
        {
            yield return this.source[index + this.offset];
        }
    }
}
