using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     The C# surface over a collection's view chain: <c>Filter</c>, <c>SortBy</c> and
///     <c>Take</c> take an <see cref="ReactiveCollection{TKey,TIdentity,TState}" /> and return one, the way
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
///         <see cref="Take{TKey,TIdentity,TState}(ReactiveCollection{TKey,TIdentity,TState},int)" /> - and
///         <see cref="Slice{TKey,TIdentity,TState}(ReactiveCollection{TKey,TIdentity,TState},int,int)" />, which
///         it is a zero-offset case of - diff their old and new windows rather than translating
///         operations, so a reorder inside the window arrives as removes and inserts rather than as
///         moves. That is also why there is no <c>Skip</c>: the diff is affordable because the
///         window is bounded at both ends. And <c>Filter</c> after <c>Take</c> filters the window,
///         so it yields at most <c>limit</c> items; write <c>Filter</c> first if that is not what
///         was meant.
///     </para>
/// </remarks>
[PublicAPI]
public static class CollectionViewExtensionMethods
{
    /// <summary>Reorders by key — the root's own order, available over any stage.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="keyComparer">The comparer to order keys by.</param>
    /// <returns>A view of <paramref name="upstream" /> ordered by key.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortByKey<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        IComparer<TKey> keyComparer)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.SortByKeyImpl(upstream, keyComparer);

    /// <summary>Narrows the view, preserving the upstream order.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="upstream">The collection or view to narrow.</param>
    /// <param name="predicate">Whether an item belongs in the view.</param>
    /// <returns>A view holding the items which pass.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Filter<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TState, bool> predicate)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.FilterImpl(upstream, Cell.Constant(predicate));

    /// <summary>
    ///     Narrows the view by a predicate which can itself change. Each change to the predicate
    ///     rebuilds this stage, which is O(m log m) in the upstream size.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="upstream">The collection or view to narrow.</param>
    /// <param name="predicateCell">The predicate in force.</param>
    /// <returns>A view holding the items which pass.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Filter<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<Func<TIdentity, TState, bool>> predicateCell)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.FilterImpl(upstream, predicateCell);

    /// <summary>
    ///     Narrows the view by a predicate driven by something else — a search box, a toggle. Each
    ///     change to the criteria rebuilds this stage, so keystroke-driven criteria are worth
    ///     debouncing upstream.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <typeparam name="TCriteria">The type of the criteria driving the predicate.</typeparam>
    /// <param name="upstream">The collection or view to narrow.</param>
    /// <param name="criteriaCell">The criteria in force.</param>
    /// <param name="predicate">Whether an item belongs in the view, given the criteria.</param>
    /// <returns>A view holding the items which pass.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Filter<TKey, TIdentity, TState, TCriteria>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<TCriteria> criteriaCell,
        Func<TCriteria, TIdentity, TState, bool> predicate)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.FilterImpl(
            upstream,
            criteriaCell.Map(
                criteria => (Func<TIdentity, TState, bool>)(
                    (identity, state) => predicate(criteria, identity, state))));

    /// <summary>Reorders the view by a value projected from each item.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
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
        upstream.SortBy(selector, Comparer<TSortKey>.Default, Comparer<TKey>.Default, false);

    /// <summary>Reorders the view, descending, by a value projected from each item.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
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
        upstream.SortBy(selector, Comparer<TSortKey>.Default, Comparer<TKey>.Default, true);

    /// <summary>
    ///     Reorders the view. <typeparamref name="TSortKey" /> stays a real generic parameter all
    ///     the way down to the comparer, so sort values are stored and compared as themselves and
    ///     never boxed.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="selector">Projects the sort value from an item.</param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">Breaks ties, so that the order is total.</param>
    /// <param name="descending">Whether to reverse the sort comparison.</param>
    /// <returns>A view in that order.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortBy<TKey, TIdentity, TState, TSortKey>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TState, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool descending)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.SortByImpl(upstream, selector, sortComparer, keyComparer, descending);

    /// <summary>
    ///     Narrows the view by a predicate over each item's immutable half — its identity — which a
    ///     state edit cannot change.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="upstream">The collection or view to narrow.</param>
    /// <param name="predicate">Whether an item belongs in the view, given its identity.</param>
    /// <returns>A view holding the items which pass.</returns>
    /// <remarks>
    ///     The same membership <c>Filter</c> gives for the same answers, and cheaper to keep. A
    ///     state edit cannot move a key into this filter or out of it, so the stage neither
    ///     re-tests the predicate nor asks whether the key was already in — it forwards the update
    ///     and is done.
    ///     Nothing is being promised on trust: the predicate is handed the identity and not the
    ///     state, so it cannot read what it says it does not.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> FilterByIdentity<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, bool> predicate)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.FilterByIdentityImpl(upstream, predicate);

    /// <summary>
    ///     Reorders the view by a value projected from each item's immutable half — its identity —
    ///     which a state edit cannot change.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="selector">Projects the sort value from an item's identity.</param>
    /// <returns>A view ordered by that value.</returns>
    /// <remarks>
    ///     The same ordering <c>SortBy</c> gives for the same values, and cheaper to keep. A state
    ///     edit cannot move a key under this order, so a stage skips re-filing one it is told
    ///     merely changed, and building the stage never reads the state map at all.
    ///     Nothing is being promised on trust here: the selector is handed the identity and not
    ///     the state, so it cannot read what it says it does not.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortByIdentity<TKey, TIdentity, TState, TSortKey>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TSortKey> selector)
        where TKey : notnull
        where TIdentity : notnull =>
        upstream.SortByIdentity(selector, Comparer<TSortKey>.Default, Comparer<TKey>.Default, false);

    /// <summary>
    ///     Reorders the view, descending, by a value projected from each item's identity.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="selector">Projects the sort value from an item's identity.</param>
    /// <returns>A view ordered by that value, descending.</returns>
    /// <remarks>See <see cref="SortByIdentity{TKey,TIdentity,TState,TSortKey}(ReactiveCollection{TKey,TIdentity,TState},Func{TIdentity,TSortKey})" />.</remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortByIdentityDescending<TKey, TIdentity, TState, TSortKey>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TSortKey> selector)
        where TKey : notnull
        where TIdentity : notnull =>
        upstream.SortByIdentity(selector, Comparer<TSortKey>.Default, Comparer<TKey>.Default, true);

    /// <summary>
    ///     Reorders the view by a value projected from each item's identity, with explicit
    ///     comparers.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <typeparam name="TSortKey">The type of the projected sort value.</typeparam>
    /// <param name="upstream">The collection or view to reorder.</param>
    /// <param name="selector">Projects the sort value from an item's identity.</param>
    /// <param name="sortComparer">Compares two projected sort values.</param>
    /// <param name="keyComparer">Breaks ties, so that the order is total.</param>
    /// <param name="descending">Whether to reverse the sort comparison.</param>
    /// <returns>A view in that order.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> SortByIdentity<TKey, TIdentity, TState, TSortKey>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Func<TIdentity, TSortKey> selector,
        IComparer<TSortKey> sortComparer,
        IComparer<TKey> keyComparer,
        bool descending)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.SortByIdentityImpl(upstream, selector, sortComparer, keyComparer, descending);

    /// <summary>
    ///     The first <paramref name="limit" /> keys of the upstream — the top-n of whatever ordering
    ///     and filtering precedes it.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="upstream">The collection or view to window.</param>
    /// <param name="limit">How many keys to keep.</param>
    /// <returns>A view of that window.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Take<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        int limit)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.TakeImpl(upstream, Cell.Constant(limit));

    /// <summary>The first however many keys of the upstream, where that count can itself change.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="upstream">The collection or view to window.</param>
    /// <param name="limitCell">How many keys to keep.</param>
    /// <returns>A view of that window.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Take<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<int> limitCell)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.TakeImpl(upstream, limitCell);

    /// <summary>
    ///     <paramref name="limit" /> keys of the upstream starting at <paramref name="offset" /> —
    ///     a page of whatever ordering and filtering precedes it.
    /// </summary>
    /// <remarks>
    ///     There is no <c>Skip</c> to pair with <c>Take</c>, and this is why: a window with both
    ///     ends is bounded, so this stage stays O(limit) per transaction, where a skip alone would
    ///     yield a view whose size follows the collection. Paging wants both ends anyway.
    /// </remarks>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="upstream">The collection or view to window.</param>
    /// <param name="offset">How many keys to pass over before the window begins.</param>
    /// <param name="limit">How many keys to keep.</param>
    /// <returns>A view of that window.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Slice<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        int offset,
        int limit)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.SliceImpl(upstream, Cell.Constant(offset), Cell.Constant(limit));

    /// <summary>
    ///     A page of the upstream where either end can itself change — send a new offset to turn
    ///     the page.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="upstream">The collection or view to window.</param>
    /// <param name="offsetCell">How many keys to pass over before the window begins.</param>
    /// <param name="limitCell">How many keys to keep.</param>
    /// <returns>A view of that window.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Slice<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> upstream,
        Cell<int> offsetCell,
        Cell<int> limitCell)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.SliceImpl(upstream, offsetCell, limitCell);

    /// <summary>
    ///     Follows whichever view the cell currently holds — the way to switch between sorts whose
    ///     sort keys are different types, as clickable column headers need.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="source">The collection the views are derived from, which owns the store.</param>
    /// <param name="viewCell">The view in force.</param>
    /// <returns>A view following whichever view <paramref name="viewCell" /> holds.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReactiveCollection<TKey, TIdentity, TState> Switch<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> source,
        Cell<ReactiveCollection<TKey, TIdentity, TState>> viewCell)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionViewUtility.SwitchImpl(source, viewCell);
}
