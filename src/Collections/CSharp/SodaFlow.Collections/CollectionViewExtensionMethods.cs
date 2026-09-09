using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     The C# surface over a collection's view chain: <c>Filter</c>, <c>SortBy</c> and
///     <c>Take</c> take an <see cref="IFrpCollection{TKey,TId,TState}" /> and return one, the way
///     <c>Where</c> takes and returns an <c>IEnumerable</c>.
/// </summary>
/// <remarks>
///     <para>
///         Stages chain: each consumes the ordered keys of the stage above it and produces its own,
///         so the order written is the order that runs. The root collection is ordered by key, and
///         a stage's own ordering is built only when something asks for it.
///     </para>
///     <para>
///         Three costs are worth knowing before writing a chain. Changing a predicate or a limit
///         rebuilds that stage and everything below it, reports the change as a reset, and costs
///         O(m log m) — debounce keystroke-driven criteria upstream.
///         <see cref="Take{TKey,TId,TState}(IFrpCollection{TKey,TId,TState},int)" /> diffs its old
///         and new windows rather than translating operations, so a reorder inside the window
///         arrives as removes and inserts rather than as moves. And <c>Filter</c> after <c>Take</c>
///         filters the window, so it yields at most <c>limit</c> items; write <c>Filter</c> first
///         if that is not what was meant.
///     </para>
/// </remarks>
[PublicAPI]
public static class CollectionViewExtensionMethods
{
    /// <summary>Reorders by key — the root's own order, available over any stage.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="keyComparer">The comparer to order keys by.</param>
    /// <returns>A view of <paramref name="upstream" /> ordered by key.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IFrpCollection<TKey, TId, TState> SortByKey<TKey, TId, TState>(
        this IFrpCollection<TKey, TId, TState> upstream,
        IComparer<TKey> keyComparer)
        where TKey : notnull
        where TId : notnull =>
        CollectionViewUtility.SortByKeyImpl(upstream, keyComparer);

    /// <summary>Narrows the view, preserving the upstream order.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="upstream">The collection or view to narrow.</param>
    /// <param name="predicate">Whether an item belongs in the view.</param>
    /// <returns>A view holding the items which pass.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IFrpCollection<TKey, TId, TState> Filter<TKey, TId, TState>(
        this IFrpCollection<TKey, TId, TState> upstream,
        Func<TId, TState, bool> predicate)
        where TKey : notnull
        where TId : notnull =>
        CollectionViewUtility.FilterImpl(upstream, Cell.Constant(predicate));

    /// <summary>
    ///     Narrows the view by a predicate which can itself change. Each change to the predicate
    ///     rebuilds this stage, which is O(m log m) in the upstream size.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="upstream">The collection or view to narrow.</param>
    /// <param name="predicateCell">The predicate in force.</param>
    /// <returns>A view holding the items which pass.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IFrpCollection<TKey, TId, TState> Filter<TKey, TId, TState>(
        this IFrpCollection<TKey, TId, TState> upstream,
        Cell<Func<TId, TState, bool>> predicateCell)
        where TKey : notnull
        where TId : notnull =>
        CollectionViewUtility.FilterImpl(upstream, predicateCell);

    /// <summary>
    ///     Narrows the view by a predicate driven by something else — a search box, a toggle. Each
    ///     change to the criteria rebuilds this stage, so keystroke-driven criteria are worth
    ///     debouncing upstream.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <typeparam name="TCriteria">The type of the criteria driving the predicate.</typeparam>
    /// <param name="upstream">The collection or view to narrow.</param>
    /// <param name="criteriaCell">The criteria in force.</param>
    /// <param name="predicate">Whether an item belongs in the view, given the criteria.</param>
    /// <returns>A view holding the items which pass.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IFrpCollection<TKey, TId, TState> Filter<TKey, TId, TState, TCriteria>(
        this IFrpCollection<TKey, TId, TState> upstream,
        Cell<TCriteria> criteriaCell,
        Func<TCriteria, TId, TState, bool> predicate)
        where TKey : notnull
        where TId : notnull =>
        CollectionViewUtility.FilterImpl(
            upstream,
            criteriaCell.Map(
                criteria => (Func<TId, TState, bool>)(
                    (identity, state) => predicate(criteria, identity, state))));

    /// <summary>Reorders the view by a value projected from each item.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="selector">Projects the sort value from an item.</param>
    /// <returns>A view ordered by that value.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IFrpCollection<TKey, TId, TState> SortBy<TKey, TId, TState, TSortKey>(
        this IFrpCollection<TKey, TId, TState> upstream,
        Func<TId, TState, TSortKey> selector)
        where TKey : notnull
        where TId : notnull =>
        upstream.SortBy(selector, Comparer<TSortKey>.Default, Comparer<TKey>.Default, false);

    /// <summary>Reorders the view, descending, by a value projected from each item.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="selector">Projects the sort value from an item.</param>
    /// <returns>A view ordered by that value, descending.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IFrpCollection<TKey, TId, TState> SortByDescending<TKey, TId, TState, TSortKey>(
        this IFrpCollection<TKey, TId, TState> upstream,
        Func<TId, TState, TSortKey> selector)
        where TKey : notnull
        where TId : notnull =>
        upstream.SortBy(selector, Comparer<TSortKey>.Default, Comparer<TKey>.Default, true);

    /// <summary>
    ///     Reorders the view. <typeparamref name="TSortKey" /> stays a real generic parameter all
    ///     the way down to the comparer, so sort values are stored and compared as themselves and
    ///     never boxed.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="selector">Projects the sort value from an item.</param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">Breaks ties, so that the order is total.</param>
    /// <param name="descending">Whether to reverse the sort comparison.</param>
    /// <returns>A view in that order.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IFrpCollection<TKey, TId, TState> SortBy<TKey, TId, TState, TSortKey>(
        this IFrpCollection<TKey, TId, TState> upstream,
        Func<TId, TState, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool descending)
        where TKey : notnull
        where TId : notnull =>
        CollectionViewUtility.SortByImpl(upstream, selector, sortComparer, keyComparer, descending);

    /// <summary>
    ///     The first <paramref name="limit" /> keys of the upstream — the top-n of whatever ordering
    ///     and filtering precedes it.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="upstream">The collection or view to window.</param>
    /// <param name="limit">How many keys to keep.</param>
    /// <returns>A view of that window.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IFrpCollection<TKey, TId, TState> Take<TKey, TId, TState>(
        this IFrpCollection<TKey, TId, TState> upstream,
        int limit)
        where TKey : notnull
        where TId : notnull =>
        CollectionViewUtility.TakeImpl(upstream, Cell.Constant(limit));

    /// <summary>The first however many keys of the upstream, where that count can itself change.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="upstream">The collection or view to window.</param>
    /// <param name="limitCell">How many keys to keep.</param>
    /// <returns>A view of that window.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IFrpCollection<TKey, TId, TState> Take<TKey, TId, TState>(
        this IFrpCollection<TKey, TId, TState> upstream,
        Cell<int> limitCell)
        where TKey : notnull
        where TId : notnull =>
        CollectionViewUtility.TakeImpl(upstream, limitCell);

    /// <summary>
    ///     Follows whichever view the cell currently holds — the way to switch between sorts whose
    ///     sort keys are different types, as clickable column headers need.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="source">The collection the views are derived from, which owns the store.</param>
    /// <param name="viewCell">The view in force.</param>
    /// <returns>A view following whichever view <paramref name="viewCell" /> holds.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IFrpCollection<TKey, TId, TState> Switch<TKey, TId, TState>(
        this IFrpCollection<TKey, TId, TState> source,
        Cell<IFrpCollection<TKey, TId, TState>> viewCell)
        where TKey : notnull
        where TId : notnull =>
        CollectionViewUtility.SwitchImpl(source, viewCell);
}
