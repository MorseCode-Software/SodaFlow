/// <summary>
///     The F# surface over SodaFlow's reactive collections: a large keyed collection whose
///     per-item observation costs one hash lookup per observer per transaction, and the chain of
///     <c>filter</c>, <c>sortBy</c> and <c>take</c> views derived from it.
/// </summary>
/// <remarks>
///     <para>
///         The F# equivalent of SodaFlow.Collections' C# extension methods, with the arguments
///         ordered for the pipeline: the collection comes last, so <c>collection |> filter p |>
///         take 10</c> reads in the order it runs. Predicates and selectors are F# functions rather
///         than <c>Func</c>, converted at this boundary.
///     </para>
///     <para>
///         Optionality is F#'s own <c>option</c>. The core answers in <c>TryGet</c>s and
///         integers rather than in any optional type, precisely so that each language surface can
///         put its own back on top: this module never sees SodaFlow.Functional, and neither does
///         anything that installs this package.
///     </para>
/// </remarks>
module SodaFlow.Collections

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open SodaFlow
// The types this module wraps live in the namespace SodaFlow.Collections, which this module
// shadows by having the same name - as SodaFlow.FSharp.Async's module does over SodaFlow.Async.
open SodaFlow.Collections

// --- construction -------------------------------------------------------------------------

/// <summary>
///     Defines a collection from its initial contents and every stream that will ever edit it.
///     There is no imperative entry point: what can change the collection is fixed here.
/// </summary>
/// <param name="keySelector">Derives an item's key from its immutable portion.</param>
/// <param name="initialEntries">The collection's initial contents.</param>
/// <param name="editStreams">Every stream that will ever edit the collection.</param>
/// <returns>The collection.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let create
    (keySelector: 'TIdentity -> 'TKey)
    (initialEntries: seq<Item<'TIdentity, 'TState>>)
    (editStreams: seq<Stream<CollectionEdit<'TKey, 'TIdentity, 'TState>>>)
    =
    ReactiveCollection<'TKey, 'TIdentity, 'TState>.Create(Func<_, _> keySelector, initialEntries, Array.ofSeq editStreams)

/// <summary>
///     Defines a collection whose identities carry their own key, so no selector is needed.
/// </summary>
/// <param name="initialEntries">The collection's initial contents.</param>
/// <param name="editStreams">Every stream that will ever edit the collection.</param>
/// <returns>The collection.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let createByIdentity
    (initialEntries: seq<Item<'TIdentity, 'TState>>)
    (editStreams: seq<Stream<CollectionEdit<'TKey, 'TIdentity, 'TState>>>)
    =
    ReactiveCollection.Create<'TKey, 'TIdentity, 'TState>(initialEntries, Array.ofSeq editStreams)

/// <summary>An item, from its immutable and mutable portions.</summary>
/// <param name="identity">The immutable portion, which the key is derived from.</param>
/// <param name="state">The mutable portion.</param>
/// <returns>The item.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let item (identity: 'TIdentity) (state: 'TState) = Item<'TIdentity, 'TState>(identity, state)

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
    |> mapS (fun (key, transform) ->
        CollectionEdit<'TKey, 'TIdentity, 'TState>.Update(key, Func<_, _> transform))

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
let fromStates
    (key: 'TKey)
    (statesStream: Stream<'TState>)
    : Stream<CollectionEdit<'TKey, 'TIdentity, 'TState>> =
    statesStream
    |> mapS (fun state ->
        CollectionEdit<'TKey, 'TIdentity, 'TState>.Update(key, Func<_, _>(fun _ -> state)))

/// <summary>Lifts a stream of items into edits which add them.</summary>
/// <param name="addsStream">The stream of items to add.</param>
/// <returns>The stream of edits.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let fromAdds
    (addsStream: Stream<Item<'TIdentity, 'TState>>)
    : Stream<CollectionEdit<'TKey, 'TIdentity, 'TState>> =
    addsStream |> mapS (fun e -> CollectionEdit<'TKey, 'TIdentity, 'TState>.Add [| e |])

/// <summary>Lifts a stream of keys into edits which remove them.</summary>
/// <param name="removesStream">The stream of keys to remove.</param>
/// <returns>The stream of edits.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let fromRemoves
    (removesStream: Stream<'TKey>)
    : Stream<CollectionEdit<'TKey, 'TIdentity, 'TState>> =
    removesStream |> mapS (fun key -> CollectionEdit<'TKey, 'TIdentity, 'TState>.Remove [| key |])

// --- observation --------------------------------------------------------------------------

/// <summary>
///     The item's mutable portion, <c>None</c> while the key is absent from the store.
/// </summary>
/// <param name="key">The key to observe.</param>
/// <param name="collection">The collection or view to ask.</param>
/// <returns>A cell tracking that key's state.</returns>
/// <remarks>
///     Cheap enough to create per bound row: it filters on a single hash lookup and never touches
///     the rest of the collection. Cached weakly per key, so N observers of one key share a node,
///     and asking two views of the same root gives the same cell.
///     The key need not exist yet. A removal fires <c>None</c> and a later add under the same key
///     fires <c>Some</c> again, so a view bound to a key can outlive the item.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let stateCell (key: 'TKey) (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    collection.StateCellImpl(key, Func<_, _> Some, Func<_>(fun () -> None))

/// <summary>The item's immutable portion, <c>None</c> while the key is absent.</summary>
/// <param name="key">The key to observe.</param>
/// <param name="collection">The collection or view to ask.</param>
/// <returns>A cell tracking that key's identity.</returns>
/// <remarks>Fires only on structural change, so it is near-free to hold.</remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let identityCell (key: 'TKey) (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    collection.IdentityCellImpl(key, Func<_, _> Some, Func<_>(fun () -> None))

/// <summary>Both halves of the item stored under a key, if there is one.</summary>
/// <param name="key">The key to look up.</param>
/// <param name="snapshot">The snapshot to look in.</param>
/// <returns>The item, or <c>None</c> if the key is absent.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lookup (key: 'TKey) (snapshot: CollectionSnapshot<'TKey, 'TIdentity, 'TState>) =
    match snapshot.TryGetItem key with
    | true, item -> Some item
    | _ -> None

/// <summary>The state stored under a key, if there is one.</summary>
/// <param name="key">The key to look up.</param>
/// <param name="states">The state map to look in.</param>
/// <returns>The state, or <c>None</c> if the key is absent.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lookupState (key: 'TKey) (states: StateMap<'TKey, 'TState>) =
    match states.TryGetState key with
    | true, state -> Some state
    | _ -> None

/// <summary>
///     What a change did to one key. The nesting is deliberate and the two levels mean different
///     things: the outer <c>option</c> is whether the key moved at all - <c>None</c> meaning no
///     event for this observer - and the inner one is whether the key is present afterwards, so a
///     removal arrives as <c>Some None</c>.
/// </summary>
/// <param name="key">The key to ask about.</param>
/// <param name="change">The change to ask about.</param>
/// <returns>What happened to that key, if anything.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let changeFor (key: 'TKey) (change: ItemChange<'TKey, 'TIdentity, 'TState>) =
    match change.TryGetNewState key with
    | true, state -> Some(Some state)
    | _ -> if change.WasChanged key then Some None else None

/// <summary>The position of a key in an ordered set, if it is present.</summary>
/// <param name="key">The key to look for.</param>
/// <param name="keys">The ordered set to look in.</param>
/// <returns>Its position, or <c>None</c> if the key is absent.</returns>
/// <remarks>
///     <c>IOrderedKeys.IndexOf</c> itself answers -1, following the convention every other
///     <c>IndexOf</c> in the framework does. This is the same question asked the F# way.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let indexOf (key: 'TKey) (keys: IOrderedKeys<'TKey, 'TIdentity, 'TState>) =
    match keys.IndexOf key with
    | index when index >= 0 -> Some index
    | _ -> None

/// <summary>The shared item store, spanning every view of the same root.</summary>
/// <param name="collection">The collection or view to ask.</param>
/// <returns>A cell holding the whole store.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshotCell (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) = collection.SnapshotCell

/// <summary>This collection's keys, in order.</summary>
/// <param name="collection">The collection or view to ask.</param>
/// <returns>A cell holding the ordered keys.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let keysCell (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) = collection.KeysCell

/// <summary>
///     How this view's keys changed: which entered, which left, which moved, and to what position.
///     Positions and no states, where <c>itemChangesStream</c> carries states and no positions.
/// </summary>
/// <param name="collection">The collection or view to ask.</param>
/// <returns>The stream of changes.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let keyChangesStream (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    collection.KeyChangesStream

/// <summary>The outer view: fires only when the item count changes or a key changes.</summary>
/// <param name="collection">The collection to ask.</param>
/// <returns>A cell holding the identity map.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let shapeCell (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) = collection.ShapeCell

/// <summary>
///     How the store changed: the keys whose items were added, removed or altered, carrying the new
///     state of each. States and no positions, where <c>keyChangesStream</c> carries positions and
///     no states.
///     On the root only, because it reports the shared store rather than any one view. For a total
///     over a filtered view, fold <c>keyChangesStream</c> instead.
/// </summary>
/// <param name="collection">The root collection to ask.</param>
/// <returns>The stream of keyed changes.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let itemChangesStream (collection: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    collection.ItemChangesStream

// --- views --------------------------------------------------------------------------------

/// <summary>Reorders by key — the root's own order, available over any stage.</summary>
/// <param name="keyComparer">The comparer to order keys by.</param>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view ordered by key.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortByKey (keyComparer: IComparer<'TKey>) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.SortByKeyImpl(upstream, keyComparer)

/// <summary>Narrows the view, preserving the upstream order.</summary>
/// <param name="predicate">Whether an item belongs in the view.</param>
/// <param name="upstream">The collection or view to narrow.</param>
/// <returns>A view holding the items which pass.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let filter (predicate: 'TIdentity -> 'TState -> bool) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.FilterImpl(upstream, CellInternal.ConstantImpl(Func<_, _, _> predicate))

/// <summary>
///     Narrows the view by a predicate which can itself change. Each change to it rebuilds this
///     stage, which is O(m log m) in the upstream size, so keystroke-driven criteria are worth
///     debouncing upstream.
/// </summary>
/// <param name="predicateCell">The predicate in force.</param>
/// <param name="upstream">The collection or view to narrow.</param>
/// <returns>A view holding the items which pass.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let filterC
    (predicateCell: Cell<'TIdentity -> 'TState -> bool>)
    (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>)
    =
    CollectionViewUtility.FilterImpl(
        upstream,
        predicateCell |> mapC (fun predicate -> Func<_, _, _> predicate))

/// <summary>Reorders the view by a value projected from each item.</summary>
/// <param name="selector">Projects the sort value from an item.</param>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view ordered by that value.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortBy
    (selector: 'TIdentity -> 'TState -> 'TSortKey)
    (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>)
    =
    CollectionViewUtility.SortByImpl(
        upstream,
        Func<_, _, _> selector,
        Comparer<'TSortKey>.Default,
        Comparer<'TKey>.Default,
        false)

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
        true)

/// <summary>
///     Narrows the view by a predicate over each item's immutable half - its identity - which a
///     state edit cannot change.
/// </summary>
/// <param name="predicate">Whether an item belongs in the view, given its identity.</param>
/// <param name="upstream">The collection or view to narrow.</param>
/// <returns>A view holding the items which pass.</returns>
/// <remarks>
///     The same membership <c>filter</c> gives for the same answers, and cheaper to keep: a state
///     edit cannot move a key into this filter or out of it, so the stage neither re-tests the
///     predicate nor asks whether the key was already in. The predicate is handed the identity and
///     not the state, so it cannot read what it says it does not.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let filterByIdentity (predicate: 'TIdentity -> bool) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.FilterByIdentityImpl(upstream, Func<_, _> predicate)

/// <summary>
///     Reorders the view by a value projected from each item's immutable half - its identity -
///     which a state edit cannot change.
/// </summary>
/// <param name="selector">Projects the sort value from an item's identity.</param>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view ordered by that value.</returns>
/// <remarks>
///     The same ordering <c>sortBy</c> gives for the same values, and cheaper to keep: a state
///     edit cannot move a key under this order, so a stage skips re-filing one it is told merely
///     changed, and building the stage never reads the state map. The selector is handed the
///     identity and not the state, so it cannot read what it says it does not.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortByIdentity (selector: 'TIdentity -> 'TSortKey) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.SortByIdentityImpl(
        upstream,
        Func<_, _> selector,
        Comparer<'TSortKey>.Default,
        Comparer<'TKey>.Default,
        false)

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
        true)

/// <summary>
///     Reorders the view by a value projected from each item's identity, with explicit comparers.
/// </summary>
/// <param name="selector">Projects the sort value from an item's identity.</param>
/// <param name="sortComparer">Compares two projected sort values.</param>
/// <param name="keyComparer">Breaks ties, so that the order is total.</param>
/// <param name="descending">Whether to reverse the sort comparison.</param>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view in that order.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortByIdentityWith
    (selector: 'TIdentity -> 'TSortKey)
    (sortComparer: IComparer<'TSortKey>)
    (keyComparer: IComparer<'TKey>)
    (descending: bool)
    (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>)
    =
    CollectionViewUtility.SortByIdentityImpl(
        upstream,
        Func<_, _> selector,
        sortComparer,
        keyComparer,
        descending)

/// <summary>
///     Reorders the view with explicit comparers. The sort key type stays a real generic
///     parameter all the way down to the comparer, so sort values are compared as themselves and
///     never boxed.
/// </summary>
/// <param name="selector">Projects the sort value from an item.</param>
/// <param name="sortComparer">Compares two projected sort values.</param>
/// <param name="keyComparer">Breaks ties, so that the order is total.</param>
/// <param name="descending">Whether to reverse the sort comparison.</param>
/// <param name="upstream">The collection or view to reorder.</param>
/// <returns>A view in that order.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortByWith
    (selector: 'TIdentity -> 'TState -> 'TSortKey)
    (sortComparer: IComparer<'TSortKey>)
    (keyComparer: IComparer<'TKey>)
    (descending: bool)
    (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>)
    =
    CollectionViewUtility.SortByImpl(
        upstream,
        Func<_, _, _> selector,
        sortComparer,
        keyComparer,
        descending)

/// <summary>
///     The first <c>limit</c> keys of the upstream — the top-n of whatever ordering and filtering
///     precedes it.
/// </summary>
/// <param name="limit">How many keys to keep.</param>
/// <param name="upstream">The collection or view to window.</param>
/// <returns>A view of that window.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let take (limit: int) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.TakeImpl(upstream, CellInternal.ConstantImpl limit)

/// <summary>The first however many keys of the upstream, where that count can itself change.</summary>
/// <param name="limitCell">How many keys to keep.</param>
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
///     There is no <c>skip</c> to pair with <c>take</c>, and this is why: a window with both ends
///     is bounded, so this stage stays O(limit) per transaction, where a skip alone would yield a
///     view whose size follows the collection. Paging wants both ends anyway.
/// </remarks>
/// <param name="offset">How many keys to pass over before the window begins.</param>
/// <param name="limit">How many keys to keep.</param>
/// <param name="upstream">The collection or view to window.</param>
/// <returns>A view of that window.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let slice (offset: int) (limit: int) (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>) =
    CollectionViewUtility.SliceImpl(
        upstream,
        CellInternal.ConstantImpl offset,
        CellInternal.ConstantImpl limit
    )

/// <summary>
///     A page of the upstream where either end can itself change — send a new offset to turn the
///     page.
/// </summary>
/// <param name="offsetCell">How many keys to pass over before the window begins.</param>
/// <param name="limitCell">How many keys to keep.</param>
/// <param name="upstream">The collection or view to window.</param>
/// <returns>A view of that window.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sliceC
    (offsetCell: Cell<int>)
    (limitCell: Cell<int>)
    (upstream: ReactiveCollection<'TKey, 'TIdentity, 'TState>)
    =
    CollectionViewUtility.SliceImpl(upstream, offsetCell, limitCell)

/// <summary>
///     Follows whichever view the cell currently holds — the way to switch between sorts whose
///     sort keys are different types, as clickable column headers need.
/// </summary>
/// <param name="viewCell">The view in force.</param>
/// <param name="source">The collection the views are derived from, which owns the store.</param>
/// <returns>A view following whichever view the cell holds.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchView
    (viewCell: Cell<ReactiveCollection<'TKey, 'TIdentity, 'TState>>)
    (source: ReactiveCollection<'TKey, 'TIdentity, 'TState>)
    =
    CollectionViewUtility.SwitchImpl(source, viewCell)
