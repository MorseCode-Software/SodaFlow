module SodaFlow.Collections.Tests.CollectionsTests

open System.Collections.Generic
open SodaFlow
open SodaFlow.Collections
open SodaFlow.Tests
open TUnit.Core

/// The immutable portion of a test item. The key is its Number.
type ItemIdentity = { Number: int; Code: string }

/// The mutable portion of a test item.
type ItemState = { Name: string; Score: int }

/// The same identity, carrying its own key, for the create overloads that take no selector.
/// The fields are named apart from ItemIdentity's deliberately: F# resolves a record expression by its
/// field names, last declaration winning, so reusing Number and Code here would silently re-point
/// every { Number = _; Code = _ } in this file at this type.
type SelfKeyedItemIdentity =
    { SelfNumber: int
      SelfCode: string }

    interface IIdentity<int> with
        member this.Key = this.SelfNumber

let private keyOf (identity: ItemIdentity) = identity.Number

/// The library's item constructor, bound under another name because the helper below wants to be
/// called `item` too and would otherwise shadow it. Eta-expanded so it generalizes.
let private ofHalves identity state = item identity state

let private item number name score =
    ofHalves
        { Number = number
          Code = sprintf "C%d" number }
        { Name = name; Score = score }

let private selfKeyedItem number name score : Item<SelfKeyedItemIdentity, ItemState> =
    ofHalves
        { SelfNumber = number
          SelfCode = sprintf "C%d" number }
        { Name = name; Score = score }

let private keysOf (view: ReactiveCollection<int, 'TIdentity, ItemState>) = List<int>(view |> keysCell |> sampleC)

type ``Collections Tests``() =

    [<Test>]
    member _.``create holds its initial items``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()

            let collection = create keyOf [ item 1 "one" 10; item 2 "two" 20 ] [ edits ]

            let snapshot = collection |> snapshotCell |> sampleC

            do! Expect.Equal(2, snapshot.Count)

            // option rather than Maybe: the core answers in TryGets so that each language surface
            // can put its own optional type on top.
            do! Expect.Equal(Some "one", snapshot |> lookup 1 |> Option.map (fun e -> e.State.Name))
        }

    [<Test>]
    member _.``an add edit reaches the shape cell and the state cell``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()
            let collection = create keyOf [] [ edits ]

            // Built before the key exists: a bound view can be created before its item and outlive
            // it, because this is a cell of option rather than something that throws.
            let seven = collection |> stateCell 7
            let seen = List<string>()

            let l =
                seven
                |> updatesC
                |> listenStrongS (fun state ->
                    seen.Add(
                        match state with
                        | Some s -> s.Name
                        | None -> "gone"
                    ))

            edits |> sendS (addEdit [ item 7 "seven" 70 ])
            edits |> sendS (removeEdit [ 7 ])

            l |> unlistenL

            do! Expect.Sequence([ "seven"; "gone" ], seen)
            do! Expect.Equal(0, (collection |> shapeCell |> sampleC).Count)
        }

    [<Test>]
    member _.``fromUpdates lifts a domain stream into edits``() =
        task {
            let scores = sinkS<int * (ItemState -> ItemState)> ()

            let collection = create keyOf [ item 1 "one" 10 ] [ fromUpdates scores ]

            scores |> sendS (1, (fun state -> { state with Score = 99 }))

            let snapshot = collection |> snapshotCell |> sampleC

            do! Expect.Equal(Some 99, snapshot |> lookup 1 |> Option.map (fun e -> e.State.Score))
        }

    [<Test>]
    member _.``the root is ordered by key``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()

            let collection =
                create keyOf [ item 3 "three" 30; item 1 "one" 10; item 2 "two" 20 ] [ edits ]

            do! Expect.Sequence([ 1; 2; 3 ], keysOf collection)
        }

    [<Test>]
    member _.``a chain runs in the order it is written``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()

            let collection =
                create
                    keyOf
                    [ item 1 "one" 50
                      item 2 "two" 40
                      item 3 "three" 30
                      item 4 "four" 20
                      item 5 "five" 10 ]
                    [ edits ]

            let topTwoOfTheEvens =
                collection
                |> sortByDescending (fun _ state -> state.Score)
                |> filter (fun identity _ -> identity.Number % 2 = 0)
                |> take 2

            do! Expect.Sequence([ 2; 4 ], keysOf topTwoOfTheEvens)

            // The filter sits above the window, so an odd item scoring highest changes nothing.
            edits |> sendS (updateEdit 1 (fun state -> { state with Score = 99 }))

            do! Expect.Sequence([ 2; 4 ], keysOf topTwoOfTheEvens)
        }

    [<Test>]
    member _.``createByIdentity takes the key from the identity``() =
        task {
            let edits = sinkS<CollectionEdit<int, SelfKeyedItemIdentity, ItemState>> ()

            // No keyOf: the identity implements IIdentity<int>.
            let collection =
                createByIdentity [ selfKeyedItem 1 "one" 10; selfKeyedItem 2 "two" 20 ] [ edits ]

            do! Expect.Sequence([ 1; 2 ], keysOf collection)

            // The derived selector is used for later adds too, not only the initial contents.
            edits |> sendS (addEdit [ selfKeyedItem 3 "three" 30 ])

            do! Expect.Sequence([ 1; 2; 3 ], keysOf collection)
        }

    [<Test>]
    member _.``map keeps an object per key and releases them all``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()

            let collection = create keyOf [ item 1 "one" 10; item 2 "two" 20 ] [ edits ]

            let projections = ref 0
            let released = ResizeArray<string>()

            let mapped =
                collection
                |> mapWith 0 released.Add (fun key ->
                    incr projections
                    sprintf "row %d" key)

            do! Expect.Sequence([ "row 1"; "row 2" ], List<string>(mapped.Items |> sampleC))
            do! Expect.Equal(2, projections.Value)

            // An edit that moves no key projects nothing new.
            edits |> sendS (updateEdit 1 (fun state -> { state with Name = "renamed" }))

            do! Expect.Equal(2, projections.Value)
            do! Expect.Equal(0, released.Count)

            // Removing one drops it, because it has left and this bound keeps none.
            edits |> sendS (removeEdit [ 2 ])

            do! Expect.Sequence([ "row 2" ], released)

            // Disposal releases what never left, which eviction never reaches.
            (mapped :> System.IDisposable).Dispose()

            do! Expect.Sequence([ "row 2"; "row 1" ], released)
        }

    [<Test>]
    member _.``slice windows the middle of the upstream``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()

            let collection =
                create
                    keyOf
                    [ item 1 "one" 10
                      item 2 "two" 20
                      item 3 "three" 30
                      item 4 "four" 40
                      item 5 "five" 50 ]
                    [ edits ]

            let page = collection |> slice 1 2

            do! Expect.Sequence([ 2; 3 ], keysOf page)

            // A key below the window shifts everything down one, so the window holds different
            // items without its bounds having changed.
            edits |> sendS (addEdit [ item 0 "zero" 5 ])

            do! Expect.Sequence([ 1; 2 ], keysOf page)
        }

    [<Test>]
    member _.``sliceC turns the page when the offset changes``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()
            let offset = sinkC 0

            let collection =
                create
                    keyOf
                    [ item 1 "one" 10
                      item 2 "two" 20
                      item 3 "three" 30
                      item 4 "four" 40
                      item 5 "five" 50 ]
                    [ edits ]

            let page = collection |> sliceC offset (constantC 2)

            do! Expect.Sequence([ 1; 2 ], keysOf page)

            offset |> sendC 2

            do! Expect.Sequence([ 3; 4 ], keysOf page)

            // The last page is short rather than padded, and an offset past the end is empty.
            offset |> sendC 4

            do! Expect.Sequence([ 5 ], keysOf page)

            offset |> sendC 99

            do! Expect.Sequence([], keysOf page)
        }

    [<Test>]
    member _.``sortBy re-files an item whose sort value moved``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()

            let collection =
                create keyOf [ item 1 "one" 30; item 2 "two" 10; item 3 "three" 20 ] [ edits ]

            let byScore = collection |> sortBy (fun _ state -> state.Score)

            do! Expect.Sequence([ 2; 3; 1 ], keysOf byScore)

            edits |> sendS (updateEdit 1 (fun state -> { state with Score = 5 }))

            do! Expect.Sequence([ 1; 2; 3 ], keysOf byScore)
        }

    [<Test>]
    member _.``sortByOrder follows whichever order the cell holds``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()

            let collection =
                create keyOf [ item 1 "one" 30; item 2 "two" 10; item 3 "three" 20 ] [ edits ]

            // One projects an int and the other a string, and the same cell holds both: an order
            // carries its own sort value type inside itself rather than in its own type.
            let byScore = orderBy (fun _ state -> state.Score)
            let byName = orderBy (fun _ (state: ItemState) -> state.Name)

            let order = sinkC byScore
            let sorted = collection |> sortByOrder order

            do! Expect.Sequence([ 2; 3; 1 ], keysOf sorted)

            order |> sendC byName

            do! Expect.Sequence([ 1; 3; 2 ], keysOf sorted)
        }

    [<Test>]
    member _.``a view answers for itself, not for the store behind it``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()

            let collection = create keyOf [ item 1 "one" 10; item 2 "two" 20 ] [ edits ]

            let passing = collection |> filter (fun _ state -> state.Score >= 20)

            do! Expect.False((passing |> keysCell |> sampleC).Contains 1)

            // The view has no value for a key it does not hold; the collection still does. This
            // asserted that they were the same cell until a view became a collection in its own
            // right.
            do! Expect.Equal(None, passing |> stateCell 1 |> sampleC |> Option.map (fun s -> s.Name))

            do! Expect.Equal(Some "one", collection |> stateCell 1 |> sampleC |> Option.map (fun s -> s.Name))

            // Sharing still falls out of never copying, within the one view where it means
            // something.
            do! Expect.Same(passing |> stateCell 2, passing |> stateCell 2)
        }
