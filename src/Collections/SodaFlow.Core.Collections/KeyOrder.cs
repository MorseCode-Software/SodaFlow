using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     One order of the keys of a view, as a value. <c>SortBy</c> sorts on it.
/// </summary>
/// <remarks>
///     <para>
///         An order holds its own sort value type, thus this type does not name that type. That
///         is the purpose. Two orders with sort values of different types have the same type here,
///         thus one cell can hold each one and a sort stage can follow it. A column header that a
///         user can click needs that. Without it, a header needs one view for each column and code
///         to select between them, which builds a graph again at each click and does not sort a
///         key set again.
///     </para>
///     <para>
///         Code out of this assembly cannot make one. Build them with the factories here, which
///         are the equivalent of the sort methods. <c>SortBy</c> sorts on <c>By</c>,
///         <c>SortByKey</c> sorts on <see cref="ByKey" />, and the other pairs agree in the same
///         manner. An order is not attached to the collection of its construction, and only to
///         its type parameters. Thus each view of the same shape can use one order.
///     </para>
///     <para>
///         An order can have more than one level. <c>ThenBy</c> and the functions with it return
///         this order with one more level, and that level selects only between keys that this
///         order ranks equal. Examples are the name of a holder with a balance, and the second
///         column header that a user clicks. The result is one order, as each other order is, thus
///         it goes in the same cell.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
[PublicAPI]
public abstract class KeyOrder<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>
    ///     This is internal, thus only this assembly can make one. The members below it are the
    ///     protocol between two stages. An order removes a type parameter, and it is not an
    ///     abstraction across implementations. Thus a set that holds one does not keep the sort
    ///     value type through each signature that uses it.
    /// </summary>
    internal KeyOrder()
    {
    }

    /// <summary>
    ///     True when a state edit can change the position of a key in this order.
    /// </summary>
    /// <remarks>
    ///     It is false for an order that makes its sort value from the key or from the identity
    ///     only, and a state edit cannot change the key or the identity. Thus a stage does not sort
    ///     a key again when it hears only that the key changed. <see cref="ByKey" /> is false, and
    ///     <c>ByIdentity</c> is also false. An order on a full-item selector from the caller is
    ///     true, because this code cannot see if that selector read the state that it got.
    /// </remarks>
    internal abstract bool DependsOnState { get; }

    /// <summary>True when this code knows that <paramref name="other" /> orders the keys as this
    /// order does.</summary>
    /// <param name="other">The order to compare against.</param>
    /// <returns>
    ///     True only when the two orders are the same and this code knows it. False means that
    ///     this code cannot show that the two are the same, and not that the two are
    ///     different.
    /// </returns>
    /// <remarks>
    ///     This lets a sort stage that gets a new order equal to the order that it holds report
    ///     nothing. An incorrect false costs one build. An incorrect true leaves a list in the
    ///     incorrect order, thus an implementation that cannot give the answer must answer
    ///     false.
    /// </remarks>
    internal abstract bool IsEquivalentTo(KeyOrder<TKey, TIdentity, TState> other);

    /// <summary>
    ///     The keys in this order, when this order is their order in the opposite direction. This
    ///     code then sorts them again with the sort values that they hold, and does not make each
    ///     sort value again.
    /// </summary>
    /// <param name="keys">Keys in an order, which can be this order in the opposite direction.</param>
    /// <param name="reversedKeys">The same keys in this order, when this method returns true.</param>
    /// <returns>True when this code put the keys in the opposite direction.</returns>
    /// <remarks>
    ///     This has the agreement of <see cref="IsEquivalentTo" />. Answer false when the result
    ///     is not sure, because the caller builds again at false and uses the list at true.
    /// </remarks>
    internal abstract bool TryReverse(
        OrderedKeys<TKey, TIdentity, TState> keys,
        [NotNullWhen(true)] out OrderedKeys<TKey, TIdentity, TState>? reversedKeys);

    /// <summary>Orders by a value projected from each item.</summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the sort value from an item.</param>
    /// <returns>The order.</returns>
    public static KeyOrder<TKey, TIdentity, TState> By<TSortKey>(Func<TIdentity, TState, TSortKey> selector) =>
        By(
            selector: selector,
            sortComparer: Comparer<TSortKey>.Default,
            keyComparer: Comparer<TKey>.Default,
            isDescending: false);

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
            isDescending: true);

    /// <summary>
    ///     Orders by a value from each item. <typeparamref name="TSortKey" /> stays a generic
    ///     parameter to the comparer, thus this code keeps and compares each sort value as its own
    ///     type and never boxes it.
    /// </summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the sort value from an item.</param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">Compares two keys with equal sort values, thus the order is total.</param>
    /// <param name="isDescending">True when the sort uses the opposite direction.</param>
    /// <returns>The order.</returns>
    public static KeyOrder<TKey, TIdentity, TState> By<TSortKey>(
        Func<TIdentity, TState, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool isDescending) =>
        new SortKeyOrder<TKey, TIdentity, TState, TSortKey>(
            selector: (_, identity, state) => selector(arg1: identity, arg2: state),
            originalSelectorReference: selector,
            sortComparer: sortComparer,
            keyComparer: keyComparer,
            isDescending: isDescending);

    /// <summary>
    ///     Orders by a value from the immutable part of each item only. A state edit cannot change
    ///     that part.
    /// </summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the sort value from an identity.</param>
    /// <returns>The order.</returns>
    /// <remarks>
    ///     This costs less to keep than <c>By</c> for the same values. A stage in this order does
    ///     not sort a key again when it hears only that the key changed, and the construction of a
    ///     stage never reads the state map. The selector does not receive the state, thus a reader
    ///     can test this property and does not use a statement.
    /// </remarks>
    public static KeyOrder<TKey, TIdentity, TState> ByIdentity<TSortKey>(Func<TIdentity, TSortKey> selector) =>
        ByIdentity(
            selector: selector,
            sortComparer: Comparer<TSortKey>.Default,
            keyComparer: Comparer<TKey>.Default,
            isDescending: false);

    /// <summary>
    ///     Orders, descending, by a value from the immutable part of each item only.
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
            isDescending: true);

    /// <summary>
    ///     Orders by a value from the immutable part of each item only. A state edit cannot change
    ///     that part.
    /// </summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the sort value from an identity.</param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">Compares two keys with equal sort values, thus the order is total.</param>
    /// <param name="isDescending">True when the sort uses the opposite direction.</param>
    /// <returns>The order.</returns>
    public static KeyOrder<TKey, TIdentity, TState> ByIdentity<TSortKey>(
        Func<TIdentity, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool isDescending) =>
        new SortKeyOrder<TKey, TIdentity, TState, TSortKey>(
            selector: (_, identity) => selector(identity),
            originalSelectorReference: selector,
            sortComparer: sortComparer,
            keyComparer: keyComparer,
            isDescending: isDescending);

    /// <summary>Orders by key, over any stage.</summary>
    /// <param name="keyComparer">The comparer to order keys by.</param>
    /// <returns>The order.</returns>
    /// <remarks>
    ///     The selector is a lambda and not a method group, thus each order from this code shares
    ///     one delegate instance. That lets this code identify two orders with the same comparer as
    ///     the same order. The compiler caches a lambda that captures nothing. It caches a method
    ///     group only from C# 11, and this assembly also compiles at C# 10.
    /// </remarks>
    public static KeyOrder<TKey, TIdentity, TState> ByKey(IComparer<TKey> keyComparer)
    {
        Func<TKey, TIdentity, TKey> selector = static (key, _) => key;

        return new SortKeyOrder<TKey, TIdentity, TState, TKey>(
            selector: selector,
            originalSelectorReference: selector,
            sortComparer: keyComparer,
            keyComparer: keyComparer,
            isDescending: false);
    }

    /// <summary>Orders by arrival - the collection's own order, available over any stage.</summary>
    /// <returns>The order.</returns>
    /// <remarks>
    ///     The items come in the sequence of their enumeration at the construction of the
    ///     collection, and then in the sequence of the edits that added them. A key that an edit
    ///     removes and then adds is a new arrival. With a sort, this is the path from a cell back
    ///     to no sort. It is the third state of a column header that moves between ascending,
    ///     descending, and off. This code never compares the keys, thus an order of the keys is
    ///     not necessary. It also never reads a second level, because two keys never come at the
    ///     same time.
    /// </remarks>
    public static KeyOrder<TKey, TIdentity, TState> ByArrival() => ArrivalOrder<TKey, TIdentity, TState>.Instance;

    /// <summary>This order, with its ties broken by a value projected from each item.</summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the next level's sort value from an item.</param>
    /// <returns>The refined order. This order does not change, and other code can use it alone.</returns>
    /// <remarks>
    ///     This is the equivalent of <c>ThenBy</c> after <c>OrderBy</c>. The new level selects only
    ///     between keys that this order ranks equal. Each level keeps its own sort value type to
    ///     its comparer, thus this code boxes nothing at each count of levels. The key is the last
    ///     level, with the comparer of the first level.
    /// </remarks>
    public KeyOrder<TKey, TIdentity, TState> ThenBy<TSortKey>(Func<TIdentity, TState, TSortKey> selector) =>
        this.ThenBy(selector: selector, sortComparer: Comparer<TSortKey>.Default, isDescending: false);

    /// <summary>This order, with its ties broken, descending, by a value projected from each item.</summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the next level's sort value from an item.</param>
    /// <returns>The refined order. This order does not change, and other code can use it alone.</returns>
    public KeyOrder<TKey, TIdentity, TState> ThenByDescending<TSortKey>(Func<TIdentity, TState, TSortKey> selector) =>
        this.ThenBy(selector: selector, sortComparer: Comparer<TSortKey>.Default, isDescending: true);

    /// <summary>This order, with its ties broken by a value projected from each item.</summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the next level's sort value from an item.</param>
    /// <param name="sortComparer">Compares two of the next level's sort values.</param>
    /// <param name="isDescending">
    ///     True when this level uses the opposite direction, at each direction of the levels above
    ///     it.
    /// </param>
    /// <returns>The refined order. This order does not change, and other code can use it alone.</returns>
    /// <remarks>
    ///     There is no key comparer for this call. The key is the last level in each order, and the
    ///     construction of the first level selected the comparer for it.
    /// </remarks>
    public KeyOrder<TKey, TIdentity, TState> ThenBy<TSortKey>(
        Func<TIdentity, TState, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        bool isDescending) =>
        this.Then(
            nextSelector: (_, identity, state) => selector(arg1: identity, arg2: state),
            nextIdentitySelector: null,
            nextComparer: sortComparer,
            nextIsDescending: isDescending);

    /// <summary>
    ///     This order, with a second level for keys with equal sort values, by a value from the
    ///     immutable part of each item only.
    /// </summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the next level's sort value from an identity.</param>
    /// <returns>The refined order. This order does not change, and other code can use it alone.</returns>
    /// <remarks>
    ///     The refined order uses the identity only when this order also uses the identity only.
    ///     One level that reads the state is sufficient to let a state edit move a key.
    /// </remarks>
    public KeyOrder<TKey, TIdentity, TState> ThenByIdentity<TSortKey>(Func<TIdentity, TSortKey> selector) =>
        this.ThenByIdentity(selector: selector, sortComparer: Comparer<TSortKey>.Default, isDescending: false);

    /// <summary>
    ///     This order, with a second level for keys with equal sort values, descending, by a value
    ///     from the immutable part of each item only.
    /// </summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the next level's sort value from an identity.</param>
    /// <returns>The refined order. This order does not change, and other code can use it alone.</returns>
    public KeyOrder<TKey, TIdentity, TState> ThenByIdentityDescending<TSortKey>(Func<TIdentity, TSortKey> selector) =>
        this.ThenByIdentity(selector: selector, sortComparer: Comparer<TSortKey>.Default, isDescending: true);

    /// <summary>
    ///     This order, with a second level for keys with equal sort values, by a value from the
    ///     immutable part of each item only.
    /// </summary>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="selector">Projects the next level's sort value from an identity.</param>
    /// <param name="sortComparer">Compares two of the next level's sort values.</param>
    /// <param name="isDescending">
    ///     True when this level uses the opposite direction, at each direction of the levels above
    ///     it.
    /// </param>
    /// <returns>The refined order. This order does not change, and other code can use it alone.</returns>
    public KeyOrder<TKey, TIdentity, TState> ThenByIdentity<TSortKey>(
        Func<TIdentity, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        bool isDescending) =>
        this.Then(
            nextSelector: null,
            nextIdentitySelector: (_, identity) => selector(identity),
            nextComparer: sortComparer,
            nextIsDescending: isDescending);

    /// <summary>
    ///     This order with one more level, and that level selects only between keys that this
    ///     order ranks equal.
    /// </summary>
    /// <param name="nextSelector">Makes the sort value of the level from a full item, or null.</param>
    /// <param name="nextIdentitySelector">Makes it from the key and the identity only, or null.</param>
    /// <param name="nextComparer">Compares two sort values of the level.</param>
    /// <param name="nextIsDescending">True when the level uses the opposite direction.</param>
    /// <returns>The refined order.</returns>
    /// <remarks>
    ///     One of the two selectors has a value, for the cause that applies to an order. A level
    ///     on the identity never receives the state, thus the combined order can give a correct
    ///     answer for a state edit that moves a key. This method is internal and generic, because
    ///     only the implementation knows its own sort value type, and a level needs that type for a
    ///     compare operation with no boxing.
    /// </remarks>
    internal abstract KeyOrder<TKey, TIdentity, TState> Then<TNext>(
        Func<TKey, TIdentity, TState, TNext>? nextSelector,
        Func<TKey, TIdentity, TNext>? nextIdentitySelector,
        IComparer<TNext> nextComparer,
        bool nextIsDescending);

    /// <summary>
    ///     A key set that holds <paramref name="keys" />, in this order.
    /// </summary>
    /// <param name="keys">The keys to add. This code omits a key that the snapshot does not have.</param>
    /// <param name="snapshot">The collection that gives the sort value of each key.</param>
    /// <returns>The set.</returns>
    /// <remarks>
    ///     This also lets a filter keep the order of its upstream collection and not know the sort
    ///     value of that order. The filter reads the order from the set of the upstream, and gets a
    ///     set that compares in the same manner and holds the members that the filter keeps.
    ///     This method operates on all keys together and does not call
    ///     <see cref="OrderedKeys{TKey,TIdentity,TState}.Add" /> for each key, and that is its
    ///     purpose. One key at a time is n immutable writes, and each write copies its path
    ///     through the tree and allocates a wrapper. A stage build had that cost. A build through a
    ///     builder writes into nodes that are not frozen and freezes them one time. That is two
    ///     and one half times faster, with one thirteenth of the allocation, on the criteria change
    ///     in <c>KeyedCollectionViewBenchmarks</c>.
    ///     A build stays expensive, and no code here can change that. A build of an immutable tree
    ///     costs one allocation for each node, and the same view from LINQ sorts an array with
    ///     no allocation. Thus a change of criteria stays some times more expensive than the same
    ///     result with no chain, and the documentation tells a reader to add a Calm stage for
    ///     it.
    /// </remarks>
    internal abstract OrderedKeys<TKey, TIdentity, TState> CreateFrom(
        IEnumerable<TKey> keys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot);

    internal abstract KeyOrder<TKey, TIdentity, TState> With(IEqualityComparer<TKey> keyEqualityComparer);
}
