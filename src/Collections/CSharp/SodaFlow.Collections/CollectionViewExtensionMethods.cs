using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     The C# surface of the view chain of a collection. <c>Filter</c>, <c>SortBy</c>, and
///     <c>Take</c> accept an <see cref="ReactiveCollection{TKey,TIdentity,TState}" /> and return
///     one, as <c>Where</c> takes an <c>IEnumerable</c> and returns one.
/// </summary>
/// <remarks>
///     <para>
///         The stages make a chain. Each stage reads the ordered keys of the stage above it and
///         makes its own ordered keys, thus the sequence in the code is the sequence of the
///         operations. The root collection keeps its items in the sequence of their arrival, and
///         this code builds the order of a stage only at the first read of it.
///     </para>
///     <para>
///         Know these three costs before you write a chain. First, a change to a predicate or to
///         a limit builds that stage and each stage below it again, reports the change as a reset,
///         and costs <c>O(m log m)</c>. Put a Calm stage above a criteria from a keystroke.
///         Second,
///         <see cref="Take{TKey,TIdentity,TState}(ReactiveCollection{TKey,TIdentity,TState},int)" />
///         and
///         <see cref="Slice{TKey,TIdentity,TState}(ReactiveCollection{TKey,TIdentity,TState},int,int)" />,
///         of which the first is the condition with an offset of zero, compare their previous
///         window against their new window and do not change the operations from above. Thus, a
///         change of order in the window comes as removals and adds, and not as moves. For the
///         same cause there is no <c>Skip</c>: that step has a low cost because the
///         window has a limit at each end. Third, a <c>Filter</c> after a <c>Take</c> filters the
///         window and thus gives <c>limit</c> items or fewer. Write the <c>Filter</c> first for a
///         different result.
///     </para>
/// </remarks>
[PublicAPI]
public static class CollectionViewExtensionMethods
{
    /// <summary>Reorders by key, over any stage.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="keyComparer">The comparer to order keys by.</param>
    /// <returns>A view of <paramref name="upstream" /> ordered by key.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortByKey<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        IComparer<TKey> keyComparer)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.SortByKeyImpl(upstream: upstream, keyComparer: keyComparer);

    /// <summary>Reorders by arrival, which is the order of the collection. It is available above
    /// each stage.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <returns>A view of <paramref name="upstream" /> in the order its items arrived.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortByArrival<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.SortByArrivalImpl(upstream);

    /// <summary>Narrows the view and keeps the upstream order.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="upstream">The collection or view to narrow.</param>
    /// <param name="predicate">True when an item is in the view.</param>
    /// <returns>A view with the items that the predicate accepts.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Filter<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TState, bool> predicate)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.FilterImpl(upstream: upstream, predicateCell: Cell.Constant(predicate));

    /// <summary>
    ///     Narrows the view with a predicate that can change. Each change to the predicate builds
    ///     this stage again, at a cost of <c>O(m log m)</c> in the size of the upstream.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="upstream">The collection or view to narrow.</param>
    /// <param name="predicateCell">The predicate in force.</param>
    /// <returns>A view with the items that the predicate accepts.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Filter<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<Func<TIdentity, TState, bool>> predicateCell)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.FilterImpl(upstream: upstream, predicateCell: predicateCell);

    /// <summary>
    ///     Narrows the view with a predicate from a different source, such as a search box or a
    ///     toggle. Each change to the criteria builds this stage again, thus a criteria from a
    ///     keystroke needs a Calm stage above this one.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <typeparam name="TCriteria">The type of the criteria for the predicate.</typeparam>
    /// <param name="upstream">The collection or view to narrow.</param>
    /// <param name="criteriaCell">The criteria in force.</param>
    /// <param name="predicate">Whether an item belongs in the view, given the criteria.</param>
    /// <returns>A view with the items that the predicate accepts.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Filter<TKey, TIdentity, TState, TCriteria>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<TCriteria> criteriaCell,
        Func<TCriteria, TIdentity, TState, bool> predicate)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.FilterImpl(
            upstream: upstream,
            predicateCell: criteriaCell.Map(criteria =>
                (Func<TIdentity, TState, bool>)(
                    (identity, state) => predicate(arg1: criteria, arg2: identity, arg3: state))));

    /// <summary>Reorders the view by a value projected from each item.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="selector">Projects the sort value from an item.</param>
    /// <returns>A view ordered by that value.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortBy<TKey, TIdentity, TState, TSortKey>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TState, TSortKey> selector)
        where TKey : notnull
        where TIdentity : notnull =>
        upstream.SortBy(
            selector: selector,
            sortComparer: Comparer<TSortKey>.Default,
            keyComparer: Comparer<TKey>.Default,
            isDescending: false);

    /// <summary>Reorders the view, descending, by a value projected from each item.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="selector">Projects the sort value from an item.</param>
    /// <returns>A view ordered by that value, descending.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortByDescending<TKey, TIdentity, TState, TSortKey>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TState, TSortKey> selector)
        where TKey : notnull
        where TIdentity : notnull =>
        upstream.SortBy(
            selector: selector,
            sortComparer: Comparer<TSortKey>.Default,
            keyComparer: Comparer<TKey>.Default,
            isDescending: true);

    /// <summary>
    ///     Reorders the view. <typeparamref name="TSortKey" /> stays a generic parameter to the
    ///     comparer, thus this code keeps and compares each sort value as its own type and never
    ///     boxes it.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="selector">Projects the sort value from an item.</param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">Compares two keys with equal sort values, thus the order is total.</param>
    /// <param name="isDescending">True when the sort uses the opposite direction.</param>
    /// <returns>A view in that order.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortBy<TKey, TIdentity, TState, TSortKey>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TState, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool isDescending)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.SortByImpl(
            upstream: upstream,
            selector: selector,
            sortComparer: sortComparer,
            keyComparer: keyComparer,
            isDescending: isDescending);

    /// <summary>Reorders the view by whichever order the cell currently holds.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="orderCell">The order for the sort. It can change.</param>
    /// <returns>A view in whichever order that cell holds.</returns>
    /// <remarks>
    ///     <para>
    ///         This stage is the base of the other sorts, and the order of each one does not
    ///         change. This stage is also the shape of a column header that a user can click.
    ///         Build the orders with the factories of
    ///         <see cref="KeyOrder{TKey,TIdentity,TState}" />, which are the equivalent of those
    ///         sorts. An order holds its own sort value type, thus one cell can hold orders that
    ///         sort on values of different types.
    ///     </para>
    ///     <para>
    ///         A new order is a change of criteria, as each other criteria is. It builds this
    ///         stage again and reports a reset. A stage below sorts again in the new order and no
    ///         code tells it to, because a filter builds from the current order of its upstream. It
    ///         is always a reset, also when the new order is the previous order in the opposite
    ///         direction. A change of order is never a set of moves, because a move is for a key
    ///         that a change of value moved.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortBy<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<KeyOrder<TKey, TIdentity, TState>> orderCell)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.SortByImpl(upstream: upstream, orderCell: orderCell);

    /// <summary>Reorders the view by an order that does not change.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="order">The order to sort by.</param>
    /// <returns>A view in that order.</returns>
    /// <remarks>
    ///     This is the shape of a sort with more than one level whose levels do not change. Build
    ///     the order with the factories of <see cref="KeyOrder{TKey,TIdentity,TState}" /> and with
    ///     <c>ThenBy</c>, and give it here. An order that changes goes in a cell. See the overload
    ///     that takes one.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortBy<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        KeyOrder<TKey, TIdentity, TState> order)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.SortByImpl(upstream: upstream, orderCell: Cell.Constant(order));

    /// <summary>
    ///     Narrows the view with a predicate on the immutable part of each item, which is its
    ///     identity. A state edit cannot change that part.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="upstream">The collection or view to narrow.</param>
    /// <param name="predicate">Whether an item belongs in the view, given its identity.</param>
    /// <returns>A view with the items that the predicate accepts.</returns>
    /// <remarks>
    ///     This gives the same members as <c>Filter</c> for the same answers, and it costs less to
    ///     keep. A state edit cannot move a key into this filter or out of it, thus the stage does
    ///     not test the predicate again and does not read the previous membership of the key. It
    ///     sends the update on.
    ///     This is not a statement that a reader must accept. The predicate receives the identity
    ///     and not the state, thus it cannot read the state that it says it does not read.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> FilterByIdentity<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, bool> predicate)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.FilterByIdentityImpl(upstream: upstream, predicate: predicate);

    /// <summary>
    ///     Reorders the view by a value from the immutable part of each item, which is its
    ///     identity. A state edit cannot change that part.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="selector">Projects the sort value from an item's identity.</param>
    /// <returns>A view ordered by that value.</returns>
    /// <remarks>
    ///     This gives the same order as <c>SortBy</c> for the same values, and it costs less to
    ///     keep. A state edit cannot move a key in this order, thus a stage does not sort a key
    ///     again when it hears only that the key changed, and the construction of the stage never
    ///     reads the state map.
    ///     This is not a statement that a reader must accept. The selector receives the identity
    ///     and not the state, thus it cannot read the state that it says it does not read.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortByIdentity<TKey, TIdentity, TState, TSortKey>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TSortKey> selector)
        where TKey : notnull
        where TIdentity : notnull =>
        upstream.SortByIdentity(
            selector: selector,
            sortComparer: Comparer<TSortKey>.Default,
            keyComparer: Comparer<TKey>.Default,
            isDescending: false);

    /// <summary>
    ///     Reorders the view, descending, by a value from the identity of each item.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="selector">Projects the sort value from an item's identity.</param>
    /// <returns>A view ordered by that value, descending.</returns>
    /// <remarks>
    ///     See
    ///     <see
    ///         cref="SortByIdentity{TKey,TIdentity,TState,TSortKey}(ReactiveCollection{TKey,TIdentity,TState},Func{TIdentity,TSortKey})" />
    ///     .
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortByIdentityDescending<TKey, TIdentity, TState,
        TSortKey>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TSortKey> selector)
        where TKey : notnull
        where TIdentity : notnull =>
        upstream.SortByIdentity(
            selector: selector,
            sortComparer: Comparer<TSortKey>.Default,
            keyComparer: Comparer<TKey>.Default,
            isDescending: true);

    /// <summary>
    ///     Reorders the view by a value from the identity of each item, with explicit
    ///     comparers.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="selector">Projects the sort value from an item's identity.</param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">Compares two keys with equal sort values, thus the order is total.</param>
    /// <param name="isDescending">True when the sort uses the opposite direction.</param>
    /// <returns>A view in that order.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortByIdentity<TKey, TIdentity, TState, TSortKey>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool isDescending)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.SortByIdentityImpl(
            upstream: upstream,
            selector: selector,
            sortComparer: sortComparer,
            keyComparer: keyComparer,
            isDescending: isDescending);

    /// <summary>
    ///     The first <paramref name="limit" /> keys of the upstream, which are the highest keys of
    ///     the order and the filter above this stage.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="upstream">The collection or view to window.</param>
    /// <param name="limit">How many keys to keep.</param>
    /// <returns>A view of that window.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Take<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        int limit)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.TakeImpl(upstream: upstream, limitCell: Cell.Constant(limit));

    /// <summary>The first keys of the upstream, and that count can change.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="upstream">The collection or view to window.</param>
    /// <param name="limitCell">How many keys to keep.</param>
    /// <returns>A view of that window.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Take<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<int> limitCell)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.TakeImpl(upstream: upstream, limitCell: limitCell);

    /// <summary>
    ///     <paramref name="limit" /> keys of the upstream that start at
    ///     <paramref name="offset" />, which are a page of the order and the filter above this
    ///     stage.
    /// </summary>
    /// <remarks>
    ///     There is no <c>Skip</c> for a pair with <c>Take</c>, for this cause. A window with the
    ///     two ends has a limit, thus this stage stays <c>O(limit)</c> for each transaction. A skip
    ///     alone gives a view whose size follows the collection. A page also needs the two ends.
    /// </remarks>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="upstream">The collection or view to window.</param>
    /// <param name="offset">The number of keys before the start of the window.</param>
    /// <param name="limit">How many keys to keep.</param>
    /// <returns>A view of that window.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Slice<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        int offset,
        int limit)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.SliceImpl(
            upstream: upstream,
            offsetCell: Cell.Constant(offset),
            limitCell: Cell.Constant(limit));

    /// <summary>
    ///     A page of the upstream where each end can change. Send a new offset to move to a
    ///     different page.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="upstream">The collection or view to window.</param>
    /// <param name="offsetCell">The number of keys before the start of the window.</param>
    /// <param name="limitCell">How many keys to keep.</param>
    /// <returns>A view of that window.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Slice<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<int> offsetCell,
        Cell<int> limitCell)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.SliceImpl(upstream: upstream, offsetCell: offsetCell, limitCell: limitCell);

    /// <summary>
    ///     One object for each key, in the order of this collection, thus a list can bind to a
    ///     stable object.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the last step of a chain and not one more stage in one. The result is a cell
    ///         of projected objects and not a collection, because a projection has no identity and
    ///         no state for a subsequent stage to filter on or to sort on.
    ///     </para>
    ///     <para>
    ///         <paramref name="project" /> runs one time for each key and this code keeps the
    ///         object. Thus, a collection whose items changed, and whose members and order did not
    ///         change, gives the same objects in the same sequence. That stops a new build of each
    ///         row of a bound list at a change to the value of one row. Build the bindings of a row
    ///         from <see cref="CollectionExtensionMethods.StateCell{TKey,TIdentity,TState}" /> and
    ///         <see cref="CollectionExtensionMethods.IdentityCell{TKey,TIdentity,TState}" /> in the
    ///         projection, and each row then follows its own item.
    ///     </para>
    ///     <para>
    ///         The objects that this code keeps have a limit. Without a limit, a projection across
    ///         a large collection holds an object for each key that it showed. The limit counts the
    ///         keys that <i>left</i>. This code keeps each key that is here now, at each value of
    ///         the limit, thus a limit below the size of the collection cannot remove a row that
    ///         the code reads again immediately. The keys that left go out in the sequence of their
    ///         departure, and the first key to go out is the key with the longest interval since it
    ///         left. Thus, a move between the same two pages costs nothing.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <typeparam name="TResult">The type of the object for each key.</typeparam>
    /// <param name="collection">The collection or view to project.</param>
    /// <param name="project">Builds the object for one key. It runs one time for each key that this code keeps.</param>
    /// <param name="retainedBeyondTheView">
    ///     The number of keys that left to keep objects for. The default is correct for a screen
    ///     with pages across a large collection. Use a larger value when a move back to a page is
    ///     frequent and a new build of a row is expensive, and a smaller value when the objects are
    ///     large.
    /// </param>
    /// <param name="onEvicted">
    ///     This receives an object whose key this code removed, and it is the position to release
    ///     the resources of that object. A projection that builds bindables must dispose them here,
    ///     or they continue after the rows that held them.
    /// </param>
    /// <returns>
    ///     The projected objects and the path to release them. Bind to <c>Items</c>, and dispose
    ///     this object with the other resources of the caller.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static MappedItems<TResult> Map<TKey, TIdentity, TState, TResult>(
        this ReactiveCollection<TKey, TIdentity, TState> collection,
        Func<TKey, TResult> project,
        int retainedBeyondTheView = MappedItems.DefaultRetainedBeyondTheView,
        Action<TResult>? onEvicted = null)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.MapImpl(
            collection: collection,
            project: project,
            retainedBeyondTheView: retainedBeyondTheView,
            onEvicted: onEvicted);
}
