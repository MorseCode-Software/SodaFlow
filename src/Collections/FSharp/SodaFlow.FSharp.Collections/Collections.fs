/// <summary>
///     The F# surface over SodaFlow's reactive collections: a large keyed collection whose
///     per-item observation costs one hash lookup per observer per transaction, and the chain of
///     <c>filter</c>, <c>sortBy</c> and <c>take</c> views derived from it.
/// </summary>
/// <remarks>
///     <para>
///         This module is the F# equivalent of the C# extension methods in SodaFlow.Collections,
///         with the arguments in the sequence of a pipeline. The collection is last, thus
///         <c>collection |> filter p |> take 10</c> reads in the sequence of its operations. A
///         predicate and a selector are F# functions and not a <c>Func</c>, and this boundary
///         changes them.
///     </para>
///     <para>
///         An optional value is the F# <c>option</c>. The core answers with a <c>TryGet</c> and
///         with an integer, and not with an optional type. Thus each language surface can add its
///         own optional type above the core. This module never uses SodaFlow.Functional, and code
///         that installs this package does not use it.
///     </para>
/// </remarks>
module SodaFlow.Collections

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open SodaFlow
// The types of this module are in the namespace SodaFlow.Collections, and this module has the
// same name and thus hides that namespace. The module in SodaFlow.FSharp.Async does the same above
// SodaFlow.Async.
open SodaFlow.Collections

// --- construction -------------------------------------------------------------------------

/// <summary>
///     Defines a collection from its initial contents and each stream that edits it. There is no
///     imperative entry point. This construction sets the code that can change the collection.
/// </summary>
/// <param name="keySelector">Makes the key of an item from its immutable part.</param>
/// <param name="initialEntries">The collection's initial contents.</param>
/// <param name="editStreams">Each stream that edits the collection.</param>
/// <returns>The collection.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let create
    (keySelector: 'TIdentity -> 'TKey)
    (initialEntries: seq<Item<'TIdentity, 'TState>>)
    (editStreams: seq<Stream<CollectionEdit<'TKey, 'TIdentity, 'TState>>>)
    =
    ReactiveCollection<'TKey, 'TIdentity, 'TState>
        .Create(Func<_, _> keySelector, initialEntries, Array.ofSeq editStreams)

/// <summary>
///     Defines a collection whose identities hold their own key, thus a selector is not
///     necessary.
/// </summary>
/// <param name="initialEntries">The collection's initial contents.</param>
/// <param name="editStreams">Each stream that edits the collection.</param>
/// <returns>The collection.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let createByIdentity
    (initialEntries: seq<Item<'TIdentity, 'TState>>)
    (editStreams: seq<Stream<CollectionEdit<'TKey, 'TIdentity, 'TState>>>)
    =
    ReactiveCollection.Create<'TKey, 'TIdentity, 'TState>(initialEntries, Array.ofSeq editStreams)

/// <summary>An item, from its immutable part and its mutable part.</summary>
/// <param name="identity">The immutable part, which gives the key.</param>
/// <param name="state">The mutable part.</param>
/// <returns>The item.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let item (identity: 'TIdentity) (state: 'TState) =
    Item<'TIdentity, 'TState>(identity, state)

// --- edits --------------------------------------------------------------------------------

/// <summary>An edit applying one transform to one key.</summary>
/// <param name="key">The key to transform.</param>
/// <param name="transform">The transform to apply to that key's state.</param>
/// <returns>The edit.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let updateEdit (key: 'TKey) (transform: 'TState -> 'TState) : CollectionEdit<'TKey, 'TIdentity, 'TState> =
    CollectionEdit<'TKey, 'TIdentity, 'TState>.Update(key, Func<_, _> transform)

/// <summary>An edit adding items.</summary>
/// <param name="items">The items to add.</param>
/// <returns>The edit.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let addEdit (items: seq<Item<'TIdentity, 'TState>>) : CollectionEdit<'TKey, 'TIdentity, 'TState> =
    CollectionEdit<'TKey, 'TIdentity, 'TState>.Add(Array.ofSeq items)

/// <summary>An edit removing keys.</summary>
/// <param name="keys">The keys to remove.</param>
/// <returns>The edit.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let removeEdit (keys: seq<'TKey>) : CollectionEdit<'TKey, 'TIdentity, 'TState> =
    CollectionEdit<'TKey, 'TIdentity, 'TState>.Remove(Array.ofSeq keys)

/// <summary>Lifts a stream of keyed transforms into edits.</summary>
/// <param name="updatesStream">The stream of keyed transforms.</param>
/// <returns>The stream of edits.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let fromUpdates
    (updatesStream: Stream<'TKey * ('TState -> 'TState)>)
    : Stream<CollectionEdit<'TKey, 'TIdentity, 'TState>> =
    updatesStream
    |> mapS (fun (key, transform) -> CollectionEdit<'TKey, 'TIdentity, 'TState>.Update(key, Func<_, _> transform))

/// <summary>Lifts a stream of transforms for one fixed key into edits.</summary>
/// <param name="key">The key the transforms apply to.</param>
/// <param name="transformsStream">The stream of transforms.</param>
/// <returns>The stream of edits.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let fromUpdatesFor
    (key: 'TKey)
    (transformsStream: Stream<'TState -> 'TState>)
    : Stream<CollectionEdit<'TKey, 'TIdentity, 'TState>> =
    transformsStream
    |> mapS (fun transform -> CollectionEdit<'TKey, 'TIdentity, 'TState>.Update(key, Func<_, _> transform))

/// <summary>Lifts a stream of new states for one fixed key into edits.</summary>
/// <param name="key">The key the states apply to.</param>
/// <param name="statesStream">The stream of new states.</param>
/// <returns>The stream of edits.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let fromStates (key: 'TKey) (statesStream: Stream<'TState>) : Stream<CollectionEdit<'TKey, 'TIdentity, 'TState>> =
    statesStream
    |> mapS (fun state -> CollectionEdit<'TKey, 'TIdentity, 'TState>.Update(key, Func<_, _>(fun _ -> state)))

/// <summary>Lifts a stream of items into edits which add them.</summary>
/// <param name="addsStream">The stream of items to add.</param>
/// <returns>The stream of edits.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let fromAdds (addsStream: Stream<Item<'TIdentity, 'TState>>) : Stream<CollectionEdit<'TKey, 'TIdentity, 'TState>> =
    addsStream
    |> mapS (fun e -> CollectionEdit<'TKey, 'TIdentity, 'TState>.Add [| e |])

/// <summary>Lifts a stream of keys into edits which remove them.</summary>
/// <param name="removesStream">The stream of keys to remove.</param>
/// <returns>The stream of edits.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let fromRemoves (removesStream: Stream<'TKey>) : Stream<CollectionEdit<'TKey, 'TIdentity, 'TState>> =
    removesStream
    |> mapS (fun key -> CollectionEdit<'TKey, 'TIdentity, 'TState>.Remove [| key |])

// --- observation --------------------------------------------------------------------------

/// <summary>
///     The mutable part of the item. It is <c>None</c> while the store has no such key.
/// </summary>
/// <param name="key">The key to monitor.</param>
/// <param name="collection">The collection or view to read.</param>
/// <returns>A cell tracking that key's state.</returns>
/// <remarks>
///     The cost is low, thus code can make one for each bound row. It filters on one hash lookup
///     and does not read the other items. A weak cache holds one for each key, thus N observers of
///     one key share a node, and two views of the same root give the same cell.
///     The key can be missing now. A removal sends <c>None</c>, and a subsequent add with the same
///     key sends <c>Some</c> again. Thus a view that binds to a key can continue after the item.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let stateCell (key: 'TKey) (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    collection.StateCellImpl(key, Func<_, _> Some, Func<_>(fun () -> None))

/// <summary>The immutable part of the item. It is <c>None</c> while the key is missing.</summary>
/// <param name="key">The key to monitor.</param>
/// <param name="collection">The collection or view to read.</param>
/// <returns>A cell tracking that key's identity.</returns>
/// <remarks>It sends a value only at a structural change, thus it costs almost nothing to
/// hold.</remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let identityCell (key: 'TKey) (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    collection.IdentityCellImpl(key, Func<_, _> Some, Func<_>(fun () -> None))

/// <summary>The two parts of the item for a key, when there is one.</summary>
/// <param name="key">The key to look up.</param>
/// <param name="snapshot">The snapshot to look in.</param>
/// <returns>The item, or <c>None</c> when the key is missing.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lookup (key: 'TKey) (snapshot: CollectionSnapshot<'TKey, 'TIdentity, 'TState>) =
    match snapshot.TryGetItem key with
    | true, item -> Some item
    | _ -> None

/// <summary>The state for a key, when there is one.</summary>
/// <param name="key">The key to look up.</param>
/// <param name="states">The state map to look in.</param>
/// <returns>The state, or <c>None</c> when the key is missing.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lookupState (key: 'TKey) (states: StateMap<'TKey, 'TState>) =
    match states.TryGetState key with
    | true, state -> Some state
    | _ -> None

/// <summary>
///     The result of a change on one key. The two levels are deliberate and they give
///     different data. The outer <c>option</c> gives if the key changed, and <c>None</c> means no
///     event for this observer. The inner <c>option</c> gives if the key is in the collection
///     after the change, thus a removal comes as <c>Some None</c>.
/// </summary>
/// <param name="key">The key to read.</param>
/// <param name="change">The change to read.</param>
/// <returns>The change to that key, when there is one.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let changeFor (key: 'TKey) (change: ItemChange<'TKey, 'TIdentity, 'TState>) =
    match change.TryGetNewState key with
    | true, state -> Some(Some state)
    | _ -> if change.WasChanged key then Some None else None

/// <summary>The position of a key in an ordered set, when the set has that key.</summary>
/// <param name="key">The key to look for.</param>
/// <param name="keys">The ordered set to look in.</param>
/// <returns>Its position, or <c>None</c> when the key is missing.</returns>
/// <remarks>
///     The core answers -1 for a missing key, which is the convention of each other
///     <c>IndexOf</c> in the framework, and it keeps that answer private. This is the only
///     <c>IndexOf</c> in the public F# API, and it answers with an F# type.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let indexOf (key: 'TKey) (keys: OrderedKeys<'TKey, 'TIdentity, 'TState>) =
    match keys.IndexOfInternal key with
    | index when index >= 0 -> Some index
    | _ -> None

/// <summary>The shared item store. Each view of the same root has it.</summary>
/// <param name="collection">The collection or view to read.</param>
/// <returns>A cell that holds the full store.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshotCell (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) = collection.SnapshotCell

/// <summary>This collection's keys, in order.</summary>
/// <param name="collection">The collection or view to read.</param>
/// <returns>A cell holding the ordered keys.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let keysCell (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) = collection.KeysCell

/// <summary>
///     The change to the keys of this view: the keys that entered, the keys that left, the keys
///     that moved, and their new positions. This stream has positions and no states, and
///     <c>itemChangesStream</c> has states and no positions.
/// </summary>
/// <param name="collection">The collection or view to read.</param>
/// <returns>The stream of changes.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let keyChangesStream (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) = collection.KeyChangesStream

/// <summary>The outer view. It sends a value only at a change of the item count or a change of a
/// key.</summary>
/// <param name="collection">The collection to read.</param>
/// <returns>A cell holding the identity map.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let shapeCell (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) = collection.ShapeCell

/// <summary>
///     The change to the store: the keys of the items that an edit added, removed, or changed,
///     with the new state of each one. This stream has states and no positions, and
///     <c>keyChangesStream</c> has positions and no states.
///     It is on the root only, because it gives the shared store and not one view. For a total
///     across a filtered view, fold <c>keyChangesStream</c>.
/// </summary>
/// <param name="collection">The root collection to read.</param>
/// <returns>The stream of keyed changes.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let itemChangesStream (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) = collection.ItemChangesStream

// --- views --------------------------------------------------------------------------------

/// <summary>Reorders by key, over any stage.</summary>
/// <param name="keyComparer">The comparer to order keys by.</param>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view ordered by key.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortByKey (keyComparer: IComparer<'TKey>) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.SortByKeyImpl(upstream, keyComparer)

/// <summary>Reorders by arrival, which is the order of the collection. It is available above
/// each stage.</summary>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view in the order its items arrived.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortByArrival (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.SortByArrivalImpl upstream

/// <summary>Narrows the view and keeps the upstream order.</summary>
/// <param name="predicate">True when an item is in the view.</param>
/// <param name="upstream">The collection or view to narrow.</param>
/// <returns>A view with the items that the predicate accepts.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let filter (predicate: 'TIdentity -> 'TState -> bool) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.FilterImpl(upstream, CellInternal.ConstantImpl(Func<_, _, _> predicate))

/// <summary>
///     Narrows the view with a predicate that can change. Each change to the predicate builds
///     this stage again, at a cost of <c>O(m log m)</c> in the size of the upstream. Thus a
///     criteria from a keystroke needs a Calm stage above this one.
/// </summary>
/// <param name="predicateCell">The predicate in force.</param>
/// <param name="upstream">The collection or view to narrow.</param>
/// <returns>A view with the items that the predicate accepts.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let filterC
    (predicateCell: Cell<'TIdentity -> 'TState -> bool>)
    (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>)
    =
    CollectionViewUtility.FilterImpl(upstream, predicateCell |> mapC (fun predicate -> Func<_, _, _> predicate))

/// <summary>Reorders the view by a value projected from each item.</summary>
/// <param name="selector">Projects the sort value from an item.</param>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view ordered by that value.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortBy (selector: 'TIdentity -> 'TState -> 'TSortKey) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.SortByImpl(
        upstream,
        Func<_, _, _> selector,
        Comparer<'TSortKey>.Default,
        Comparer<'TKey>.Default,
        false
    )

/// <summary>Reorders the view, descending, by a value projected from each item.</summary>
/// <param name="selector">Projects the sort value from an item.</param>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view ordered by that value, descending.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortByDescending
    (selector: 'TIdentity -> 'TState -> 'TSortKey)
    (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>)
    =
    CollectionViewUtility.SortByImpl(
        upstream,
        Func<_, _, _> selector,
        Comparer<'TSortKey>.Default,
        Comparer<'TKey>.Default,
        true
    )

/// <summary>
///     Narrows the view with a predicate on the immutable part of each item, which is its
///     identity. A state edit cannot change that part.
/// </summary>
/// <param name="predicate">True when an item is in the view, from its identity.</param>
/// <param name="upstream">The collection or view to narrow.</param>
/// <returns>A view with the items that the predicate accepts.</returns>
/// <remarks>
///     This gives the same members as <c>filter</c> for the same answers, and it costs less to
///     keep. A state edit cannot move a key into this filter or out of it, thus the stage does not
///     test the predicate again and does not read the previous membership of the key. The
///     predicate receives the identity and not the state, thus it cannot read the state that it
///     says it does not read.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let filterByIdentity (predicate: 'TIdentity -> bool) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.FilterByIdentityImpl(upstream, Func<_, _> predicate)

/// <summary>
///     Reorders the view by a value from the immutable part of each item, which is its identity.
///     A state edit cannot change that part.
/// </summary>
/// <param name="selector">Projects the sort value from an item's identity.</param>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view ordered by that value.</returns>
/// <remarks>
///     This gives the same order as <c>sortBy</c> for the same values, and it costs less to keep.
///     A state edit cannot move a key in this order, thus a stage does not sort a key again when
///     it hears only that the key changed, and the construction of the stage never reads the state
///     map. The selector receives the identity and not the state, thus it cannot read the state
///     that it says it does not read.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortByIdentity (selector: 'TIdentity -> 'TSortKey) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.SortByIdentityImpl(
        upstream,
        Func<_, _> selector,
        Comparer<'TSortKey>.Default,
        Comparer<'TKey>.Default,
        false
    )

/// <summary>
///     Reorders the view, descending, by a value projected from each item's identity.
/// </summary>
/// <param name="selector">Projects the sort value from an item's identity.</param>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view ordered by that value, descending.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortByIdentityDescending
    (selector: 'TIdentity -> 'TSortKey)
    (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>)
    =
    CollectionViewUtility.SortByIdentityImpl(
        upstream,
        Func<_, _> selector,
        Comparer<'TSortKey>.Default,
        Comparer<'TKey>.Default,
        true
    )

/// <summary>
///     Reorders the view by a value projected from each item's identity, with explicit comparers.
/// </summary>
/// <param name="selector">Projects the sort value from an item's identity.</param>
/// <param name="sortComparer">Compares two projected sort values.</param>
/// <param name="keyComparer">Compares two keys with equal sort values, thus the order is total.</param>
/// <param name="isDescending">True when the sort uses the opposite direction.</param>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view in that order.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortByIdentityWith
    (selector: 'TIdentity -> 'TSortKey)
    (sortComparer: IComparer<'TSortKey>)
    (keyComparer: IComparer<'TKey>)
    (isDescending: bool)
    (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>)
    =
    CollectionViewUtility.SortByIdentityImpl(upstream, Func<_, _> selector, sortComparer, keyComparer, isDescending)

/// <summary>
///     Reorders the view with explicit comparers. The sort key type stays a generic parameter to
///     the comparer, thus this code compares each sort value as its own type and never boxes
///     it.
/// </summary>
/// <param name="selector">Projects the sort value from an item.</param>
/// <param name="sortComparer">Compares two projected sort values.</param>
/// <param name="keyComparer">Compares two keys with equal sort values, thus the order is total.</param>
/// <param name="isDescending">True when the sort uses the opposite direction.</param>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view in that order.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortByWith
    (selector: 'TIdentity -> 'TState -> 'TSortKey)
    (sortComparer: IComparer<'TSortKey>)
    (keyComparer: IComparer<'TKey>)
    (isDescending: bool)
    (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>)
    =
    CollectionViewUtility.SortByImpl(upstream, Func<_, _, _> selector, sortComparer, keyComparer, isDescending)

/// <summary>Reorders the view by an order that does not change.</summary>
/// <param name="order">The order to sort by.</param>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view in that order.</returns>
/// <remarks>
///     This is the shape of a sort with more than one level whose levels do not change. Build the
///     order with <c>orderBy</c> and <c>thenBy</c>, and send the collection here. An order that
///     changes goes in a cell. See <c>sortByOrderC</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortByOrder
    (order: KeyOrder<'TKey, 'TIdentity, 'TState>)
    (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>)
    =
    CollectionViewUtility.SortByImpl(upstream, CellInternal.ConstantImpl order)

/// <summary>Reorders the view by whichever order the cell currently holds.</summary>
/// <param name="orderCell">The order for the sort. It can change.</param>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view in whichever order that cell holds.</returns>
/// <remarks>
///     This stage is the base of the other sorts, and the order of each one does not change. This
///     stage is also the shape of a column header that a user can click. Build the orders with
///     <c>orderBy</c> and the functions below it, which are the equivalent of those sorts. An
///     order holds its own sort value type, thus one cell can hold orders that sort on values of
///     different types.
///     A new order is a change of criteria, as each other criteria is. It builds this stage again
///     and reports a reset. A stage below sorts again in the new order and no code tells it to,
///     because a filter builds from the current order of its upstream. It is always a reset, also
///     when the new order is the previous order in the opposite direction. A change of order is
///     never a set of moves, because a move is for a key that a change of value moved.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortByOrderC
    (orderCell: Cell<KeyOrder<'TKey, 'TIdentity, 'TState>>)
    (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>)
    =
    CollectionViewUtility.SortByImpl(upstream, orderCell)

/// <summary>An order by a value projected from each item.</summary>
/// <param name="selector">Projects the sort value from an item.</param>
/// <returns>The order.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let orderBy (selector: 'TIdentity -> 'TState -> 'TSortKey) : KeyOrder<'TKey, 'TIdentity, 'TState> =
    KeyOrder<'TKey, 'TIdentity, 'TState>
        .By(Func<_, _, _> selector, Comparer<'TSortKey>.Default, Comparer<'TKey>.Default, false)

/// <summary>An order, descending, by a value projected from each item.</summary>
/// <param name="selector">Projects the sort value from an item.</param>
/// <returns>The order.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let orderByDescending (selector: 'TIdentity -> 'TState -> 'TSortKey) : KeyOrder<'TKey, 'TIdentity, 'TState> =
    KeyOrder<'TKey, 'TIdentity, 'TState>
        .By(Func<_, _, _> selector, Comparer<'TSortKey>.Default, Comparer<'TKey>.Default, true)

/// <summary>
///     An order by a value from each item, with explicit comparers. The sort key type stays a
///     generic parameter to the comparer, thus this code compares each sort value as its own type
///     and never boxes it.
/// </summary>
/// <param name="selector">Projects the sort value from an item.</param>
/// <param name="sortComparer">Compares two projected sort values.</param>
/// <param name="keyComparer">Compares two keys with equal sort values, thus the order is total.</param>
/// <param name="isDescending">True when the sort uses the opposite direction.</param>
/// <returns>The order.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let orderByWith
    (selector: 'TIdentity -> 'TState -> 'TSortKey)
    (sortComparer: IComparer<'TSortKey>)
    (keyComparer: IComparer<'TKey>)
    (isDescending: bool)
    : KeyOrder<'TKey, 'TIdentity, 'TState> =
    KeyOrder<'TKey, 'TIdentity, 'TState>.By(Func<_, _, _> selector, sortComparer, keyComparer, isDescending)

/// <summary>
///     An order by a value from the immutable part of each item only. A state edit cannot change
///     that part.
/// </summary>
/// <param name="selector">Projects the sort value from an identity.</param>
/// <returns>The order.</returns>
/// <remarks>
///     This costs less to keep than <c>orderBy</c> for the same values. A stage in this order
///     does not sort a key again when it hears only that the key changed, and the construction of
///     a stage never reads the state map. The selector does not receive the state, thus it cannot
///     read the state that it says it does not read.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let orderByIdentity (selector: 'TIdentity -> 'TSortKey) : KeyOrder<'TKey, 'TIdentity, 'TState> =
    KeyOrder<'TKey, 'TIdentity, 'TState>
        .ByIdentity(Func<_, _> selector, Comparer<'TSortKey>.Default, Comparer<'TKey>.Default, false)

/// <summary>
///     An order, descending, by a value from the immutable part of each item only.
/// </summary>
/// <param name="selector">Projects the sort value from an identity.</param>
/// <returns>The order.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let orderByIdentityDescending (selector: 'TIdentity -> 'TSortKey) : KeyOrder<'TKey, 'TIdentity, 'TState> =
    KeyOrder<'TKey, 'TIdentity, 'TState>
        .ByIdentity(Func<_, _> selector, Comparer<'TSortKey>.Default, Comparer<'TKey>.Default, true)

/// <summary>
///     An order by a value from the immutable part of each item only, with explicit
///     comparers.
/// </summary>
/// <param name="selector">Projects the sort value from an identity.</param>
/// <param name="sortComparer">Compares two projected sort values.</param>
/// <param name="keyComparer">Compares two keys with equal sort values, thus the order is total.</param>
/// <param name="isDescending">True when the sort uses the opposite direction.</param>
/// <returns>The order.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let orderByIdentityWith
    (selector: 'TIdentity -> 'TSortKey)
    (sortComparer: IComparer<'TSortKey>)
    (keyComparer: IComparer<'TKey>)
    (isDescending: bool)
    : KeyOrder<'TKey, 'TIdentity, 'TState> =
    KeyOrder<'TKey, 'TIdentity, 'TState>.ByIdentity(Func<_, _> selector, sortComparer, keyComparer, isDescending)

/// <summary>An order by key, over any stage.</summary>
/// <param name="keyComparer">The comparer to order keys by.</param>
/// <returns>The order.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let orderByKey (keyComparer: IComparer<'TKey>) : KeyOrder<'TKey, 'TIdentity, 'TState> =
    KeyOrder<'TKey, 'TIdentity, 'TState>.ByKey keyComparer

/// <summary>An order by arrival, which is the order of the collection. It is available above
/// each stage.</summary>
/// <returns>The order.</returns>
/// <remarks>
///     With a sort, this is the path from a cell back to no sort. It is the third state of a
///     column header that moves between ascending, descending, and off.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let orderByArrival () : KeyOrder<'TKey, 'TIdentity, 'TState> =
    KeyOrder<'TKey, 'TIdentity, 'TState>.ByArrival()

/// <summary>An order with a second level, by a value from each item, for keys with equal sort
/// values.</summary>
/// <param name="selector">Projects the next level's sort value from an item.</param>
/// <param name="order">The order to refine. It does not change.</param>
/// <returns>The refined order.</returns>
/// <remarks>
///     This is the equivalent of <c>ThenBy</c> after <c>OrderBy</c> in LINQ. The new level
///     selects only between keys that the order ranks equal. Each level keeps its own sort value
///     type to its comparer, thus this code boxes nothing at each count of levels, and the key is
///     the last level in each order.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let thenBy
    (selector: 'TIdentity -> 'TState -> 'TSortKey)
    (order: KeyOrder<'TKey, 'TIdentity, 'TState>)
    : KeyOrder<'TKey, 'TIdentity, 'TState> =
    order.ThenBy(Func<_, _, _> selector, Comparer<'TSortKey>.Default, false)

/// <summary>An order with a second level, descending, by a value from each item, for keys with
/// equal sort values.</summary>
/// <param name="selector">Projects the next level's sort value from an item.</param>
/// <param name="order">The order to refine. It does not change.</param>
/// <returns>The refined order.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let thenByDescending
    (selector: 'TIdentity -> 'TState -> 'TSortKey)
    (order: KeyOrder<'TKey, 'TIdentity, 'TState>)
    : KeyOrder<'TKey, 'TIdentity, 'TState> =
    order.ThenBy(Func<_, _, _> selector, Comparer<'TSortKey>.Default, true)

/// <summary>An order with a second level, by a value from each item, for keys with equal sort
/// values, with a comparer.</summary>
/// <param name="selector">Projects the next level's sort value from an item.</param>
/// <param name="sortComparer">Compares two of the next level's sort values.</param>
/// <param name="isDescending">
///     True when this level uses the opposite direction, at each direction of the levels above it.
/// </param>
/// <param name="order">The order to refine. It does not change.</param>
/// <returns>The refined order.</returns>
/// <remarks>
///     There is no key comparer for this call. The key is the last level in each order, and the
///     construction of the first level selected the comparer for it.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let thenByWith
    (selector: 'TIdentity -> 'TState -> 'TSortKey)
    (sortComparer: IComparer<'TSortKey>)
    (isDescending: bool)
    (order: KeyOrder<'TKey, 'TIdentity, 'TState>)
    : KeyOrder<'TKey, 'TIdentity, 'TState> =
    order.ThenBy(Func<_, _, _> selector, sortComparer, isDescending)

/// <summary>
///     An order with a second level, by a value from the immutable part of each item only, for
///     keys with equal sort values.
/// </summary>
/// <param name="selector">Projects the next level's sort value from an identity.</param>
/// <param name="order">The order to refine. It does not change.</param>
/// <returns>The refined order.</returns>
/// <remarks>
///     The refined order uses the identity only when the order that it refines also uses the
///     identity only. One level that reads the state is sufficient to let a state edit move a
///     key.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let thenByIdentity
    (selector: 'TIdentity -> 'TSortKey)
    (order: KeyOrder<'TKey, 'TIdentity, 'TState>)
    : KeyOrder<'TKey, 'TIdentity, 'TState> =
    order.ThenByIdentity(Func<_, _> selector, Comparer<'TSortKey>.Default, false)

/// <summary>
///     An order with a second level, descending, by a value from the immutable part of each item
///     only, for keys with equal sort values.
/// </summary>
/// <param name="selector">Projects the next level's sort value from an identity.</param>
/// <param name="order">The order to refine. It does not change.</param>
/// <returns>The refined order.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let thenByIdentityDescending
    (selector: 'TIdentity -> 'TSortKey)
    (order: KeyOrder<'TKey, 'TIdentity, 'TState>)
    : KeyOrder<'TKey, 'TIdentity, 'TState> =
    order.ThenByIdentity(Func<_, _> selector, Comparer<'TSortKey>.Default, true)

/// <summary>
///     An order with a second level, by a value from the immutable part of each item only, for
///     keys with equal sort values, with a comparer.
/// </summary>
/// <param name="selector">Projects the next level's sort value from an identity.</param>
/// <param name="sortComparer">Compares two of the next level's sort values.</param>
/// <param name="isDescending">
///     True when this level uses the opposite direction, at each direction of the levels above it.
/// </param>
/// <param name="order">The order to refine. It does not change.</param>
/// <returns>The refined order.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let thenByIdentityWith
    (selector: 'TIdentity -> 'TSortKey)
    (sortComparer: IComparer<'TSortKey>)
    (isDescending: bool)
    (order: KeyOrder<'TKey, 'TIdentity, 'TState>)
    : KeyOrder<'TKey, 'TIdentity, 'TState> =
    order.ThenByIdentity(Func<_, _> selector, sortComparer, isDescending)

/// <summary>
///     The first <c>limit</c> keys of the upstream — the top-n of whatever ordering and filtering
///     precedes it.
/// </summary>
/// <param name="limit">The number of keys to keep.</param>
/// <param name="upstream">The collection or view to window.</param>
/// <returns>A view of that window.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let take (limit: int) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.TakeImpl(upstream, CellInternal.ConstantImpl limit)

/// <summary>The first keys of the upstream, and that count can change.</summary>
/// <param name="limitCell">The number of keys to keep.</param>
/// <param name="upstream">The collection or view to window.</param>
/// <returns>A view of that window.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let takeC (limitCell: Cell<int>) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.TakeImpl(upstream, limitCell)

/// <summary>
///     <c>limit</c> keys of the upstream starting at <c>offset</c> — a page of whatever ordering
///     and filtering precedes it.
/// </summary>
/// <remarks>
///     There is no <c>skip</c> for a pair with <c>take</c>, for this cause. A window with the two
///     ends has a limit, thus this stage stays <c>O(limit)</c> for each transaction. A skip alone
///     gives a view whose size follows the collection. A page also needs the two ends.
/// </remarks>
/// <param name="offset">The number of keys before the start of the window.</param>
/// <param name="limit">The number of keys to keep.</param>
/// <param name="upstream">The collection or view to window.</param>
/// <returns>A view of that window.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let slice (offset: int) (limit: int) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.SliceImpl(upstream, CellInternal.ConstantImpl offset, CellInternal.ConstantImpl limit)

/// <summary>
///     A page of the upstream where each end can change. Send a new offset to move to a different
///     page.
/// </summary>
/// <param name="offsetCell">The number of keys before the start of the window.</param>
/// <param name="limitCell">The number of keys to keep.</param>
/// <param name="upstream">The collection or view to window.</param>
/// <returns>A view of that window.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sliceC (offsetCell: Cell<int>) (limitCell: Cell<int>) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.SliceImpl(upstream, offsetCell, limitCell)

/// <summary>
///     One object per key, in this collection's order, so a list can bind to something stable.
/// </summary>
/// <remarks>
///     This is the end of a chain and not a stage in one. The result is objects, and an object
///     has no identity and no state for a subsequent stage. <c>project</c> runs one time for each
///     key and this code keeps the object. Thus a collection whose items changed, and whose
///     members and order did not change, gives the same objects in the same sequence. Build each
///     object from <c>stateCell</c> and <c>identityCell</c>, and the object then follows its own
///     item.
///     <para />
///     <c>MappedItems.DefaultRetainedBeyondTheView</c> limits the objects that this code keeps.
///     Use <c>mapWith</c> to set that limit, or to get a message at each removal.
/// </remarks>
/// <param name="project">Builds the object for one key.</param>
/// <param name="collection">The collection or view to project.</param>
/// <returns>The projected objects, and the means to release them.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let map (project: 'TKey -> 'TResult) (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.MapImpl(collection, Func<_, _> project, MappedItems.DefaultRetainedBeyondTheView, null)

/// <summary>
///     The same, choosing how much to keep and hearing about what is dropped.
/// </summary>
/// <remarks>
///     The limit counts the keys that <i>left</i>. This code keeps each key that is here now, at
///     each value of the limit, thus a limit below the size of the collection cannot remove an
///     object from the screen. <c>onEvicted</c> is the position to release the resources of a
///     projected object. A disposal of the result releases the objects that this code holds, and
///     an eviction never reaches those.
/// </remarks>
/// <param name="retainedBeyondTheView">The number of keys that left to keep objects for.</param>
/// <param name="onEvicted">This receives an object whose key this code removed.</param>
/// <param name="project">Builds the object for one key.</param>
/// <param name="collection">The collection or view to project.</param>
/// <returns>The projected objects, and the means to release them.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let mapWith
    (retainedBeyondTheView: int)
    (onEvicted: 'TResult -> unit)
    (project: 'TKey -> 'TResult)
    (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>)
    =
    CollectionViewUtility.MapImpl(collection, Func<_, _> project, retainedBeyondTheView, Action<_> onEvicted)
