using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

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
public abstract class OrderedKeys<TKey, TIdentity, TState> : IReadOnlyList<TKey>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>
    ///     Internal, so that this assembly is the only thing that can produce one. A key set is a
    ///     stage's own bookkeeping, and the members below that build the next version of one are
    ///     the protocol between stages rather than anything a consumer has business calling.
    /// </summary>
    internal OrderedKeys()
    {
    }

    /// <summary>The order this set files keys under.</summary>
    internal abstract KeyOrder<TKey, TIdentity, TState> Order { get; }

    /// <inheritdoc />
    public abstract int Count { get; }

    /// <inheritdoc />
    public abstract TKey this[int index] { get; }

    /// <inheritdoc />
    public abstract IEnumerator<TKey> GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    /// <summary>Whether a key is in this set.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns><see langword="true" /> if the key is present.</returns>
    public abstract bool Contains(TKey key);

    /// <summary>The position of a key in this set.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns>Its position, or -1 if the key is absent.</returns>
    /// <remarks>
    ///     -1 rather than an optional value, both because this assembly does not reference
    ///     SodaFlow.Functional and because it is the convention every <c>IndexOf</c> in the
    ///     framework already follows. Internal because that sentinel is an arrangement between
    ///     this assembly and the language surfaces rather than something a caller should have to
    ///     know: each surface exposes an <c>IndexOf</c> of its own answering with that language's
    ///     optional type.
    /// </remarks>
    internal abstract int IndexOfInternal(TKey key);

    /// <summary>
    ///     Files the key under the sort value it projects from <paramref name="snapshot" />. A key
    ///     absent from the snapshot is not added.
    /// </summary>
    /// <param name="key">The key to file.</param>
    /// <param name="snapshot">The collection to project the sort value from.</param>
    /// <returns>The set with the key filed in it.</returns>
    internal abstract OrderedKeys<TKey, TIdentity, TState> Add(
        TKey key,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot);

    /// <summary>Removes a key.</summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>The set without that key.</returns>
    internal abstract OrderedKeys<TKey, TIdentity, TState> Remove(TKey key);
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
    public bool IsDescending { get; }

    public int Compare(SortedEntry<TKey, TSortKey>? left, SortedEntry<TKey, TSortKey>? right)
    {
        if (ReferenceEquals(objA: left, objB: right))
        {
            return 0;
        }

        // A direction is applied by swapping the operands rather than negating the result. Negating
        // int.MinValue leaves it negative, so a comparer that returns it would sort the same way in
        // both directions; swapping cannot go wrong like that.
        //
        // ReSharper disable NullableWarningSuppressionIsUsed - The set only ever holds entries
        // filed by Add, and nothing outside this file constructs one, so neither side is null.
        int result = this.IsDescending
            ? this.SortComparer.Compare(x: right!.SortValue, y: left!.SortValue)
            : this.SortComparer.Compare(x: left!.SortValue, y: right!.SortValue);
        // ReSharper restore NullableWarningSuppressionIsUsed

        // The key breaks ties, so the order is total and two entries that sort equally are never
        // conflated.
        return result != 0 ? result : this.KeyComparer.Compare(x: left.Key, y: right.Key);
    }
}

/// <summary>Two sort values compared level by level - what a multi-level order files a key under.</summary>
/// <remarks>
///     A struct holding both levels as themselves, so an order with several levels keeps every level's
///     sort value type all the way down to its comparer and nothing is boxed. A third level is a pair
///     whose first half is a pair, which is how any number of levels fits a type with two parameters.
/// </remarks>
/// <typeparam name="TFirst">The type of the level that decides first.</typeparam>
/// <typeparam name="TSecond">The type of the level that decides between what the first ranks equal.</typeparam>
internal readonly struct SortPair<TFirst, TSecond>
{
    internal SortPair(TFirst first, TSecond second)
    {
        this.First = first;
        this.Second = second;
    }

    internal TFirst First { get; }

    internal TSecond Second { get; }
}

/// <summary>Compares two <see cref="SortPair{TFirst,TSecond}" /> values: the first level, then the second.</summary>
/// <remarks>
///     Each level carries its own direction, because a secondary level runs whichever way it was asked
///     to regardless of the level above it. Directions are applied by swapping operands, for the reason
///     given in <see cref="SortedEntryComparer{TKey,TSortKey}" />.
/// </remarks>
/// <typeparam name="TFirst">The type of the level that decides first.</typeparam>
/// <typeparam name="TSecond">The type of the level that decides between what the first ranks equal.</typeparam>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class SortPairComparer<TFirst, TSecond> : IComparer<SortPair<TFirst, TSecond>>
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
        int result = this.firstIsDescending
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
///     An order which files each key under a sort value it projects for that key from a snapshot.
/// </summary>
/// <remarks>
///     What the two kinds of order have in common, and all a sorted key set needs from either: how to
///     project a key's sort value, and how to compare two entries once they are projected. An order over
///     the items projects from an item; the arrival order projects from when the key arrived.
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
/// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
// ReSharper disable once InheritdocConsiderUsage
internal abstract class ProjectedKeyOrder<TKey, TIdentity, TState, TSortKey> : KeyOrder<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    protected ProjectedKeyOrder(IEqualityComparer<TKey> keyEqualityComparer) =>
        this.KeyEqualityComparer = keyEqualityComparer;

    /// <summary>Compares two entries this order has filed.</summary>
    protected internal abstract SortedEntryComparer<TKey, TSortKey> EntryComparer { get; }

    protected IEqualityComparer<TKey> KeyEqualityComparer { get; }

    /// <inheritdoc />
    internal override bool IsEquivalentTo(KeyOrder<TKey, TIdentity, TState> other) =>
        other is ProjectedKeyOrder<TKey, TIdentity, TState, TSortKey> otherTyped &&
        ReferenceEquals(objA: this.KeyEqualityComparer, objB: otherTyped.KeyEqualityComparer) &&
        ReferenceEquals(objA: this.EntryComparer.KeyComparer, objB: otherTyped.EntryComparer.KeyComparer) &&
        ReferenceEquals(objA: this.EntryComparer.SortComparer, objB: otherTyped.EntryComparer.SortComparer) &&
        this.EntryComparer.IsDescending == otherTyped.EntryComparer.IsDescending;

    /// <inheritdoc />
    internal override bool TryReverse(
        OrderedKeys<TKey, TIdentity, TState> keys,
        [NotNullWhen(true)]
        out OrderedKeys<TKey, TIdentity, TState>? reversedKeys)
    {
        if (keys.Order is ProjectedKeyOrder<TKey, TIdentity, TState, TSortKey> otherTyped &&
            keys is SortedKeys<TKey, TIdentity, TState, TSortKey> sortedKeys &&
            ReferenceEquals(objA: this.KeyEqualityComparer, objB: otherTyped.KeyEqualityComparer) &&
            ReferenceEquals(objA: this.EntryComparer.KeyComparer, objB: otherTyped.EntryComparer.KeyComparer) &&
            ReferenceEquals(objA: this.EntryComparer.SortComparer, objB: otherTyped.EntryComparer.SortComparer) &&
            this.EntryComparer.IsDescending != otherTyped.EntryComparer.IsDescending)
        {
            reversedKeys = sortedKeys.Reverse(this);
            return true;
        }

        reversedKeys = null;
        return false;
    }

    /// <summary>
    ///     The sort value this order files <paramref name="key" /> under, if the snapshot still holds
    ///     it.
    /// </summary>
    internal abstract bool TryProject(
        TKey key,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot,
        out TSortKey sortValue);

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
            if (!this.TryProject(key: key, snapshot: snapshot, sortValue: out TSortKey sortValue))
            {
                continue;
            }

            SortedEntry<TKey, TSortKey> entry = new(key: key, sortValue: sortValue);

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
///     The order items arrived in: the collection's own order, and what <c>ByArrival</c> returns.
/// </summary>
/// <remarks>
///     <para>
///         Every key is numbered once as it arrives, so no two keys share a sort value and the key is
///         never compared. That is why a collection can list keys of a type with no order of its own.
///     </para>
///     <para>
///         For the same reason a further level would never be consulted, so
///         <see cref="Then{TNext}" /> answers with this order unchanged rather than building a level
///         that could not decide anything. And it never depends on the state: an update is not an
///         arrival.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
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
    /// <inheritdoc />
    internal override bool IsEquivalentTo(KeyOrder<TKey, TIdentity, TState> other) =>
        other is ArrivalOrder<TKey, TIdentity, TState>;

    /// <inheritdoc />
    internal override bool TryReverse(
        OrderedKeys<TKey, TIdentity, TState> keys,
        [NotNullWhen(true)]
        out OrderedKeys<TKey, TIdentity, TState>? reversedKeys)
    {
        reversedKeys = null;
        return false;
    }

    /// <inheritdoc />
    protected internal override SortedEntryComparer<TKey, long> EntryComparer { get; } =
        new(sortComparer: Comparer<long>.Default, keyComparer: NoTieComparer<TKey>.Instance, isDescending: false);

    /// <inheritdoc />
    internal override KeyOrder<TKey, TIdentity, TState> Then<TNext>(
        Func<TKey, TIdentity, TState, TNext>? nextSelector,
        Func<TKey, TIdentity, TNext>? nextIdentitySelector,
        IComparer<TNext> nextComparer,
        bool nextIsDescending) =>
        this;

    /// <inheritdoc />
    internal override KeyOrder<TKey, TIdentity, TState> With(IEqualityComparer<TKey> keyEqualityComparer) =>
        new ArrivalOrder<TKey, TIdentity, TState>(keyEqualityComparer);

    /// <inheritdoc />
    internal override bool TryProject(
        TKey key,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot,
        out long sortValue) =>
        snapshot.TryGetArrival(key: key, arrival: out sortValue);
}

/// <summary>The tie-break of an order whose sort values are never equal, which has no tie to break.</summary>
/// <remarks>
///     Throws rather than answering. Being asked at all means two keys were filed under a sort value
///     that is only ever given out once - a set that no longer describes itself, which an answer would
///     hide.
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
///     An order which files each key under a value projected from its item.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
/// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
/// <remarks>
///     Internal, because nothing outside this assembly can put an order into a view: the sort
///     stages build their own from the selector they are handed. <see cref="KeyOrder{TKey,TIdentity,TState}" />
///     stays public because <see cref="OrderedKeys{TKey,TIdentity,TState}.Order" /> answers with one, so
///     an order can be read and not supplied - which is what keeps the claim
///     <see cref="DependsOnState" /> makes checkable. An order that could be supplied from outside
///     could assert it falsely, and a view would silently stop re-filing.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class SortKeyOrder<TKey, TIdentity, TState, TSortKey>
    : ProjectedKeyOrder<TKey, TIdentity, TState, TSortKey>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>
    ///     Exactly one of these is set, and which one is what <see cref="DependsOnState" />
    ///     answers. That is deliberate: an order cannot claim not to read the state while reading
    ///     it, because the selector that claims it is never handed any.
    /// </summary>
    private readonly Func<TKey, TIdentity, TSortKey>? identitySelector;

    /// <summary>
    ///     Exactly one of these is set, and which one is what <see cref="DependsOnState" />
    ///     answers. That is deliberate: an order cannot claim not to read the state while reading
    ///     it, because the selector that claims it is never handed any.
    /// </summary>
    private readonly Func<TKey, TIdentity, TState, TSortKey>? selector;

    /// <summary>Creates an order whose sort value is projected from the whole item.</summary>
    /// <param name="selector">Projects the sort value from a key and its item.</param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">Breaks ties, so that the order is total.</param>
    /// <param name="isDescending">Whether to reverse the sort comparison.</param>
    public SortKeyOrder(
        Func<TKey, TIdentity, TState, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool isDescending)
        : this(
            selector: selector,
            identitySelector: null,
            entryComparer: new SortedEntryComparer<TKey, TSortKey>(
                sortComparer: sortComparer,
                keyComparer: keyComparer,
                isDescending: isDescending),
            keyEqualityComparer: EqualityComparer<TKey>.Default)
    {
    }

    /// <summary>
    ///     Creates an order whose sort value is projected from the key and the immutable half
    ///     alone, neither of which a state edit can touch.
    /// </summary>
    /// <param name="selector">Projects the sort value from a key and its identity.</param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">Breaks ties, so that the order is total.</param>
    /// <param name="isDescending">Whether to reverse the sort comparison.</param>
    /// <remarks>
    ///     What this buys is in <see cref="DependsOnState" />: a stage under this order skips
    ///     re-filing a key on a state edit, and building one skips reading the state map at all.
    /// </remarks>
    public SortKeyOrder(
        Func<TKey, TIdentity, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool isDescending)
        : this(
            selector: null,
            identitySelector: selector,
            entryComparer: new SortedEntryComparer<TKey, TSortKey>(
                sortComparer: sortComparer,
                keyComparer: keyComparer,
                isDescending: isDescending),
            keyEqualityComparer: EqualityComparer<TKey>.Default)
    {
    }

    private SortKeyOrder(
        Func<TKey, TIdentity, TState, TSortKey>? selector,
        Func<TKey, TIdentity, TSortKey>? identitySelector,
        SortedEntryComparer<TKey, TSortKey> entryComparer,
        IEqualityComparer<TKey> keyEqualityComparer)
        : base(keyEqualityComparer)
    {
        this.selector = selector;
        this.identitySelector = identitySelector;
        this.EntryComparer = entryComparer;
    }

    /// <inheritdoc />
    internal override bool DependsOnState => this.identitySelector is null;

    /// <inheritdoc />
    internal override KeyOrder<TKey, TIdentity, TState> Then<TNext>(
        Func<TKey, TIdentity, TState, TNext>? nextSelector,
        Func<TKey, TIdentity, TNext>? nextIdentitySelector,
        IComparer<TNext> nextComparer,
        bool nextIsDescending)
    {
        // The pair carries both levels' directions, so the entry comparer above it compares
        // ascending. The key still breaks the last tie, with the comparer this order was built with:
        // a further level refines the order and has no say in what makes it total.
        SortPairComparer<TSortKey, TNext> pairComparer =
            new(
                first: this.EntryComparer.SortComparer,
                firstIsDescending: this.EntryComparer.IsDescending,
                second: nextComparer,
                secondIsDescending: nextIsDescending);

        // Over the identity alone only if every level is, which is what keeps DependsOnState honest
        // for the combined order: one level that reads the state makes the whole order read it.
        if (this.identitySelector is not null && nextIdentitySelector is not null)
        {
            Func<TKey, TIdentity, TSortKey> firstIdentity = this.identitySelector;

            return new SortKeyOrder<TKey, TIdentity, TState, SortPair<TSortKey, TNext>>(
                selector: null,
                identitySelector: (key, identity) =>
                    new SortPair<TSortKey, TNext>(
                        first: firstIdentity(arg1: key, arg2: identity),
                        second: nextIdentitySelector(arg1: key, arg2: identity)),
                entryComparer: new SortedEntryComparer<TKey, SortPair<TSortKey, TNext>>(
                    sortComparer: pairComparer,
                    keyComparer: this.EntryComparer.KeyComparer,
                    isDescending: false),
                keyEqualityComparer: this.KeyEqualityComparer);
        }

        Func<TKey, TIdentity, TState, TSortKey> firstWhole =
            WholeItem(selector: this.selector, identitySelector: this.identitySelector);

        Func<TKey, TIdentity, TState, TNext> nextWhole =
            WholeItem(selector: nextSelector, identitySelector: nextIdentitySelector);

        return new SortKeyOrder<TKey, TIdentity, TState, SortPair<TSortKey, TNext>>(
            selector: (key, identity, state) =>
                new SortPair<TSortKey, TNext>(
                    first: firstWhole(arg1: key, arg2: identity, arg3: state),
                    second: nextWhole(arg1: key, arg2: identity, arg3: state)),
            identitySelector: null,
            entryComparer: new SortedEntryComparer<TKey, SortPair<TSortKey, TNext>>(
                sortComparer: pairComparer,
                keyComparer: this.EntryComparer.KeyComparer,
                isDescending: false),
            keyEqualityComparer: this.KeyEqualityComparer);
    }

    /// <inheritdoc />
    internal override KeyOrder<TKey, TIdentity, TState> With(IEqualityComparer<TKey> keyEqualityComparer) =>
        new SortKeyOrder<TKey, TIdentity, TState, TSortKey>(
            selector: this.selector,
            identitySelector: this.identitySelector,
            entryComparer: this.EntryComparer,
            keyEqualityComparer: keyEqualityComparer);

    /// <summary>A level as a projection from the whole item, whichever kind it was built as.</summary>
    private static Func<TKey, TIdentity, TState, T> WholeItem<T>(
        Func<TKey, TIdentity, TState, T>? selector,
        Func<TKey, TIdentity, T>? identitySelector) =>
        identitySelector is null
            // ReSharper disable once NullableWarningSuppressionIsUsed - exactly one of the two is set,
            // and identitySelector being null is what says it is this one.
            ? selector!
            : (key, identity, _) => identitySelector(arg1: key, arg2: identity);

    /// <inheritdoc />
    protected internal override SortedEntryComparer<TKey, TSortKey> EntryComparer { get; }

    /// <inheritdoc />
    /// <remarks>
    ///     An order that does not read the state does not read the state map either, which is one
    ///     fewer lookup per key - and a rebuild does this for every key it keeps.
    ///     The two branches disagree only for a key the identity map holds and the state map does
    ///     not, which the two being written together in <c>Resolve</c> rules out.
    /// </remarks>
    internal override bool TryProject(
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
            if (!snapshot.TryGetIdentity(key: key, identity: out TIdentity identityOnly))
            {
                return false;
            }

            sortValue = this.identitySelector(arg1: key, arg2: identityOnly);

            return true;
        }

        if (!snapshot.TryGetHalves(key: key, identity: out TIdentity identity, state: out TState state))
        {
            return false;
        }

        // ReSharper disable once NullableWarningSuppressionIsUsed - exactly one of the two
        // selectors is set, and identitySelector being null is what says it is this one.
        sortValue = this.selector!(arg1: key, arg2: identity, arg3: state);

        return true;
    }
}

internal sealed class SortedKeys<TKey, TIdentity, TState, TSortKey> : OrderedKeys<TKey, TIdentity, TState>
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
    ///     If the key is filed but its entry is not in the ordering, which cannot happen and would
    ///     mean this set had been built wrongly.
    /// </exception>
    /// <remarks>
    ///     The two answers here are not the same kind of thing. A key this set does not hold is
    ///     absent, which is what -1 says and what every caller reads it as. A key it does hold whose
    ///     entry is not in the ordering is not an answer at all: the map and the ordering are only
    ///     ever written together, so one having what the other does not means they have drifted, and
    ///     returning -1 for it would report a broken set as an ordinary absence and leave a stage
    ///     quietly filing keys into something that no longer describes itself.
    /// </remarks>
    internal override int IndexOfInternal(TKey key)
    {
        if (!this.byKey.TryGet(key: key, value: out SortedEntry<TKey, TSortKey> entry))
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
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
    {
        //TODO: JAM: do we need Try here if we assume (and have tests to ensure) the invariant holds that keys and
        //snapshots are always kept in sync for a sort?  Wouldn't this be an error?
        if (!this.order.TryProject(key: key, snapshot: snapshot, sortValue: out TSortKey sortValue))
        {
            return this;
        }

        SortedEntry<TKey, TSortKey> entry = new(key: key, sortValue: sortValue);

        // A key already filed is re-filed rather than filed again. The map holds one entry per key
        // and the ordering holds one per sort value, so adding a key that is already in under a
        // different value would leave the old entry orphaned in the ordering - the set would count
        // it, enumerate the key twice, and disagree with its own map. No stage does that today,
        // because a re-file removes before it adds; nothing about this type said they had to.
        ImmutableSortedSet<SortedEntry<TKey, TSortKey>> ordering =
            this.byKey.TryGet(key: key, value: out SortedEntry<TKey, TSortKey> filed)
                ? this.entries.Remove(filed)
                : this.entries;

        return new SortedKeys<TKey, TIdentity, TState, TSortKey>(
            order: this.order,
            entries: ordering.Add(entry),
            byKey: this.byKey.SetItem(key: key, value: entry));
    }

    internal override OrderedKeys<TKey, TIdentity, TState> Remove(TKey key) =>
        this.byKey.TryGet(key: key, value: out SortedEntry<TKey, TSortKey> entry)
            ? new SortedKeys<TKey, TIdentity, TState, TSortKey>(
                order: this.order,
                entries: this.entries.Remove(entry),
                byKey: this.byKey.Remove(key))
            : this;

    public override IEnumerator<TKey> GetEnumerator() => this.entries.Select(static entry => entry.Key).GetEnumerator();

    internal OrderedKeys<TKey, TIdentity, TState> Reverse(ProjectedKeyOrder<TKey, TIdentity, TState, TSortKey> order) =>
        new SortedKeys<TKey, TIdentity, TState, TSortKey>(
            order: order,
            entries: this.entries.ToBuilder().Reverse().ToImmutableSortedSet(order.EntryComparer),
            byKey: this.byKey);
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
