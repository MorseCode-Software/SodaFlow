using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     One way of ordering a view's keys — what <c>SortBy</c> sorts by, as a value.
/// </summary>
/// <remarks>
///     <para>
///         An order carries its own sort value type inside itself, so this type does not mention
///         it. That is the whole point: two orders projecting sort values of different types are
///         still the same type here, so a cell can hold either and a sort stage can follow one -
///         which is what a clickable column header needs. Without it a header would need a
///         separately built view per column and something to choose between them, which is a graph
///         rebuilt on every click rather than a key set re-filed.
///     </para>
///     <para>
///         Nothing outside this assembly can implement one. Build them with the factories here,
///         which mirror the sort methods one for one: <c>By</c> is what <c>SortBy</c> sorts by,
///         <see cref="ByKey" /> what <c>SortByKey</c> does, and so on. An order is not tied to
///         the collection it was built for, only to its type parameters, so one built once can be
///         handed to any view of the same shape.
///     </para>
///     <para>
///         An order can have more than one level. <c>ThenBy</c> and its siblings return this order
///         refined by another level, which decides only between keys this order ranks equal - a
///         holder's name under a balance, or the column header clicked second - and what comes back
///         is one order like any other, so it goes in the same cell.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
public abstract class KeyOrder<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>
    ///     Internal, so that this assembly is the only thing that can produce one. The members
    ///     below it are the protocol between stages: an order exists to forget a type parameter
    ///     rather than to abstract over implementations, so the sets that hold one need not carry
    ///     the sort value's type through every signature that touches them.
    /// </summary>
    internal KeyOrder()
    {
    }

    /// <summary>
    ///     Whether a key's position under this order can be changed by a state edit.
    /// </summary>
    /// <remarks>
    ///     False for an order that projects its sort value from the key or the identity alone,
    ///     neither of which a state edit can touch - which is what lets a stage skip re-filing a
    ///     key it has been told merely changed. <see cref="ByKey" /> is false and so is
    ///     <c>ByIdentity</c>; an order over a caller's whole-item selector is conservatively true,
    ///     because nothing here can see whether that selector read the state it was handed.
    /// </remarks>
    internal abstract bool DependsOnState { get; }

    /// <summary>Orders by a value projected from each item.</summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the sort value from an item.</param>
    /// <returns>The order.</returns>
    public static KeyOrder<TKey, TIdentity, TState> By<TSortKey>(Func<TIdentity, TState, TSortKey> selector) =>
        By(
            selector: selector,
            sortComparer: Comparer<TSortKey>.Default,
            keyComparer: Comparer<TKey>.Default,
            descending: false);

    /// <summary>Orders, descending, by a value projected from each item.</summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the sort value from an item.</param>
    /// <returns>The order.</returns>
    public static KeyOrder<TKey, TIdentity, TState>
        ByDescending<TSortKey>(Func<TIdentity, TState, TSortKey> selector) =>
        By(
            selector: selector,
            sortComparer: Comparer<TSortKey>.Default,
            keyComparer: Comparer<TKey>.Default,
            descending: true);

    /// <summary>
    ///     Orders by a value projected from each item. <typeparamref name="TSortKey" /> stays a
    ///     real generic parameter all the way down to the comparer, so sort values are stored and
    ///     compared as themselves and never boxed.
    /// </summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the sort value from an item.</param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">Breaks ties, so that the order is total.</param>
    /// <param name="descending">Whether to reverse the sort comparison.</param>
    /// <returns>The order.</returns>
    public static KeyOrder<TKey, TIdentity, TState> By<TSortKey>(
        Func<TIdentity, TState, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool descending) =>
        new SortKeyOrder<TKey, TIdentity, TState, TSortKey>(
            selector: (_, identity, state) => selector(arg1: identity, arg2: state),
            sortComparer: sortComparer,
            keyComparer: keyComparer,
            descending: descending);

    /// <summary>
    ///     Orders by a value projected from each item's immutable half alone, which a state edit
    ///     cannot change.
    /// </summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the sort value from an identity.</param>
    /// <returns>The order.</returns>
    /// <remarks>
    ///     Cheaper to keep than <c>By</c> for the same values: a stage under this order skips
    ///     re-filing a key it is told merely changed, and building one never reads the state map.
    ///     The selector is not handed the state, which is what makes that checkable rather than
    ///     promised.
    /// </remarks>
    public static KeyOrder<TKey, TIdentity, TState> ByIdentity<TSortKey>(Func<TIdentity, TSortKey> selector) =>
        ByIdentity(
            selector: selector,
            sortComparer: Comparer<TSortKey>.Default,
            keyComparer: Comparer<TKey>.Default,
            descending: false);

    /// <summary>
    ///     Orders, descending, by a value projected from each item's immutable half alone.
    /// </summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the sort value from an identity.</param>
    /// <returns>The order.</returns>
    public static KeyOrder<TKey, TIdentity, TState>
        ByIdentityDescending<TSortKey>(Func<TIdentity, TSortKey> selector) =>
        ByIdentity(
            selector: selector,
            sortComparer: Comparer<TSortKey>.Default,
            keyComparer: Comparer<TKey>.Default,
            descending: true);

    /// <summary>
    ///     Orders by a value projected from each item's immutable half alone, which a state edit
    ///     cannot change.
    /// </summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the sort value from an identity.</param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">Breaks ties, so that the order is total.</param>
    /// <param name="descending">Whether to reverse the sort comparison.</param>
    /// <returns>The order.</returns>
    public static KeyOrder<TKey, TIdentity, TState> ByIdentity<TSortKey>(
        Func<TIdentity, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool descending) =>
        new SortKeyOrder<TKey, TIdentity, TState, TSortKey>(
            selector: (_, identity) => selector(identity),
            sortComparer: sortComparer,
            keyComparer: keyComparer,
            descending: descending);

    /// <summary>Orders by key — the root's own order, available over any stage.</summary>
    /// <param name="keyComparer">The comparer to order keys by.</param>
    /// <returns>The order.</returns>
    public static KeyOrder<TKey, TIdentity, TState> ByKey(IComparer<TKey> keyComparer) =>
        new SortKeyOrder<TKey, TIdentity, TState, TKey>(
            selector: static (key, _) => key,
            sortComparer: keyComparer,
            keyComparer: keyComparer,
            descending: false);

    /// <summary>This order, with its ties broken by a value projected from each item.</summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the next level's sort value from an item.</param>
    /// <returns>The refined order. This order is unchanged, and can still be used alone.</returns>
    /// <remarks>
    ///     What <c>ThenBy</c> is after <c>OrderBy</c>: the new level decides only between keys this
    ///     order ranks equal. Every level keeps its own sort value type down to its comparer, so
    ///     nothing is boxed however many levels there are, and the key still breaks the last tie with
    ///     the comparer the first level was given.
    /// </remarks>
    public KeyOrder<TKey, TIdentity, TState> ThenBy<TSortKey>(Func<TIdentity, TState, TSortKey> selector) =>
        this.ThenBy(selector: selector, sortComparer: Comparer<TSortKey>.Default, descending: false);

    /// <summary>This order, with its ties broken, descending, by a value projected from each item.</summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the next level's sort value from an item.</param>
    /// <returns>The refined order. This order is unchanged, and can still be used alone.</returns>
    public KeyOrder<TKey, TIdentity, TState> ThenByDescending<TSortKey>(Func<TIdentity, TState, TSortKey> selector) =>
        this.ThenBy(selector: selector, sortComparer: Comparer<TSortKey>.Default, descending: true);

    /// <summary>This order, with its ties broken by a value projected from each item.</summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the next level's sort value from an item.</param>
    /// <param name="sortComparer">Compares two of the next level's sort values.</param>
    /// <param name="descending">Whether this level runs in reverse, whichever way the levels above it run.</param>
    /// <returns>The refined order. This order is unchanged, and can still be used alone.</returns>
    /// <remarks>
    ///     There is no key comparer to give here. The key breaks the last tie in every order, and
    ///     which comparer does that was settled when the first level was built.
    /// </remarks>
    public KeyOrder<TKey, TIdentity, TState> ThenBy<TSortKey>(
        Func<TIdentity, TState, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        bool descending) =>
        this.Then(
            nextSelector: (_, identity, state) => selector(arg1: identity, arg2: state),
            nextIdentitySelector: null,
            nextComparer: sortComparer,
            nextDescending: descending);

    /// <summary>
    ///     This order, with its ties broken by a value projected from each item's immutable half
    ///     alone.
    /// </summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the next level's sort value from an identity.</param>
    /// <returns>The refined order. This order is unchanged, and can still be used alone.</returns>
    /// <remarks>
    ///     The refined order is over the identity alone only if this order is too. One level that
    ///     reads the state is enough for a state edit to move a key.
    /// </remarks>
    public KeyOrder<TKey, TIdentity, TState> ThenByIdentity<TSortKey>(Func<TIdentity, TSortKey> selector) =>
        this.ThenByIdentity(selector: selector, sortComparer: Comparer<TSortKey>.Default, descending: false);

    /// <summary>
    ///     This order, with its ties broken, descending, by a value projected from each item's
    ///     immutable half alone.
    /// </summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the next level's sort value from an identity.</param>
    /// <returns>The refined order. This order is unchanged, and can still be used alone.</returns>
    public KeyOrder<TKey, TIdentity, TState> ThenByIdentityDescending<TSortKey>(Func<TIdentity, TSortKey> selector) =>
        this.ThenByIdentity(selector: selector, sortComparer: Comparer<TSortKey>.Default, descending: true);

    /// <summary>
    ///     This order, with its ties broken by a value projected from each item's immutable half
    ///     alone.
    /// </summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the next level's sort value from an identity.</param>
    /// <param name="sortComparer">Compares two of the next level's sort values.</param>
    /// <param name="descending">Whether this level runs in reverse, whichever way the levels above it run.</param>
    /// <returns>The refined order. This order is unchanged, and can still be used alone.</returns>
    public KeyOrder<TKey, TIdentity, TState> ThenByIdentity<TSortKey>(
        Func<TIdentity, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        bool descending) =>
        this.Then(
            nextSelector: null,
            nextIdentitySelector: (_, identity) => selector(identity),
            nextComparer: sortComparer,
            nextDescending: descending);

    /// <summary>
    ///     This order refined by one more level, which decides only between keys this order ranks
    ///     equal.
    /// </summary>
    /// <param name="nextSelector">Projects the level's sort value from a whole item, or null.</param>
    /// <param name="nextIdentitySelector">Projects it from the key and identity alone, or null.</param>
    /// <param name="nextComparer">Compares two of the level's sort values.</param>
    /// <param name="nextDescending">Whether the level runs in reverse.</param>
    /// <returns>The refined order.</returns>
    /// <remarks>
    ///     Exactly one selector is set, for the reason the orders themselves keep one: a level built over
    ///     the identity is never handed the state, so the combined order can say truthfully whether a
    ///     state edit can move a key. Internal and generic because only the implementation knows its own
    ///     sort value type, and a level has to be paired with that type to be compared without boxing.
    /// </remarks>
    internal abstract KeyOrder<TKey, TIdentity, TState> Then<TNext>(
        Func<TKey, TIdentity, TState, TNext>? nextSelector,
        Func<TKey, TIdentity, TNext>? nextIdentitySelector,
        IComparer<TNext> nextComparer,
        bool nextDescending);

    /// <summary>
    ///     A key set holding <paramref name="keys" />, ordered the way this order orders them.
    /// </summary>
    /// <param name="keys">The keys to file. Any the snapshot does not have are skipped.</param>
    /// <param name="snapshot">The collection to project each key's sort value from.</param>
    /// <returns>The set.</returns>
    /// <remarks>
    ///     This is also what lets a filter preserve its upstream's order without knowing what that
    ///     order sorts by: it asks the upstream's set for its order and gets back a set that
    ///     compares exactly the same way, holding whichever of its members it chose to keep.
    ///     Bulk rather than a sequence of <see cref="OrderedKeys{TKey,TIdentity,TState}.Add" /> calls,
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
    internal abstract OrderedKeys<TKey, TIdentity, TState> CreateFrom(
        IEnumerable<TKey> keys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot);
}
