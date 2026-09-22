module SodaFlow.Collections.Tests.CollectionsTests

open System.Collections.Generic
open SodaFlow
open SodaFlow.Collections
open SodaFlow.Tests
open TUnit.Core

/// The immutable part of a test item. The key is its Number.
type ItemIdentity = { Number: int; Code: string }

/// The mutable part of a test item.
type ItemState = { Name: string; Score: int }

/// The same identity, with its own key, for the `create` overloads with no selector.
/// The names of these fields are different from the names in ItemIdentity, and that is deliberate.
/// F# resolves a record expression by its field names and uses the last declaration. Thus the names
/// Number and Code here move each `{ Number = _; Code = _ }` in this file to this type, with no
/// message.
type SelfKeyedItemIdentity =
    { SelfNumber: int
      SelfCode: string }

    interface IIdentity<int> with
        member this.Key = this.SelfNumber

let private keyOf (identity: ItemIdentity) = identity.Number

/// The item constructor of the library, with a different name because the helper below also has
/// the name `item` and thus hides it. This code writes the parameters, and that makes it
/// generic.
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

            // This is an `option` and not a Maybe. The core answers with a TryGet, thus each
            // language surface can add its own optional type above it.
            do! Expect.Equal(Some "one", snapshot |> lookup 1 |> Option.map (fun e -> e.State.Name))
        }

    [<Test>]
    member _.``an add edit reaches the shape cell and the state cell``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()
            let collection = create keyOf [] [ edits ]

            // This code builds the cell before the key is in the collection. Code can make a bound
            // view before its item, and that view can continue after the item, because this is a
            // cell of `option` and not a value that throws an exception.
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
    member _.``the root keeps the order items arrived in``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()

            let collection =
                create keyOf [ item 3 "three" 30; item 1 "one" 10; item 2 "two" 20 ] [ edits ]

            // This is the sequence of the enumeration of the initial items, and not the sequence
            // of their keys.
            do! Expect.Sequence([ 3; 1; 2 ], keysOf collection)

            edits |> sendS (addEdit [ item 0 "zero" 0 ])

            do! Expect.Sequence([ 3; 1; 2; 0 ], keysOf collection)

            // An update is not an arrival.
            edits |> sendS (updateEdit 3 (fun state -> { state with Score = 99 }))

            do! Expect.Sequence([ 3; 1; 2; 0 ], keysOf collection)
        }

    [<Test>]
    member _.``orderByArrival takes a sort back to the order items arrived in``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()

            let collection =
                create keyOf [ item 3 "three" 30; item 1 "one" 10; item 2 "two" 20 ] [ edits ]

            let order = sinkC (orderBy (fun _ (state: ItemState) -> state.Score))
            let sorted = collection |> sortByOrderC order

            do! Expect.Sequence([ 1; 2; 3 ], keysOf sorted)

            order |> sendC (orderByArrival ())

            do! Expect.Sequence([ 3; 1; 2 ], keysOf sorted)

            let sortedThenUnsorted =
                collection |> sortBy (fun _ (state: ItemState) -> state.Score) |> sortByArrival

            do! Expect.Sequence([ 3; 1; 2 ], keysOf sortedThenUnsorted)
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

            // The filter is above the window, thus an odd item with the highest score changes
            // nothing.
            edits |> sendS (updateEdit 1 (fun state -> { state with Score = 99 }))

            do! Expect.Sequence([ 2; 4 ], keysOf topTwoOfTheEvens)
        }

    [<Test>]
    member _.``createByIdentity takes the key from the identity``() =
        task {
            let edits = sinkS<CollectionEdit<int, SelfKeyedItemIdentity, ItemState>> ()

            // There is no keyOf, because the identity is an IIdentity<int>.
            let collection =
                createByIdentity [ selfKeyedItem 1 "one" 10; selfKeyedItem 2 "two" 20 ] [ edits ]

            do! Expect.Sequence([ 1; 2 ], keysOf collection)

            // A subsequent add also uses the derived selector, and not only the initial contents.
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

            // An edit that moves no key makes no new object.
            edits |> sendS (updateEdit 1 (fun state -> { state with Name = "renamed" }))

            do! Expect.Equal(2, projections.Value)
            do! Expect.Equal(0, released.Count)

            // A removal of one key removes its object, because the key left and this limit keeps
            // none.
            edits |> sendS (removeEdit [ 2 ])

            do! Expect.Sequence([ "row 2" ], released)

            // A disposal releases each object whose key stayed, and an eviction never reaches
            // those.
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

            // A removal of a key before the window moves each key after it one position earlier,
            // thus the window holds different items and its limits do not change.
            edits |> sendS (removeEdit [ 1 ])

            do! Expect.Sequence([ 3; 4 ], keysOf page)
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

            // The last page has fewer keys and no fill values, and an offset above the end gives an
            // empty page.
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
    member _.``thenBy breaks the ties the first level leaves``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()

            let collection =
                create keyOf [ item 1 "b" 10; item 2 "a" 10; item 3 "c" 5; item 4 "a" 5 ] [ edits ]

            let sorted =
                collection
                |> sortByOrder (
                    orderByDescending (fun _ (state: ItemState) -> state.Score)
                    |> thenBy (fun _ (state: ItemState) -> state.Name)
                )

            do! Expect.Sequence([ 2; 1; 4; 3 ], keysOf sorted)
        }

    [<Test>]
    member _.``sortByOrderC follows whichever order the cell holds``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemIdentity, ItemState>> ()

            let collection =
                create keyOf [ item 1 "one" 30; item 2 "two" 10; item 3 "three" 20 ] [ edits ]

            // One order gives an int and the other gives a string, and one cell holds the two. An
            // order holds its own sort value type, and that type is not in the type of the
            // order.
            let byScore = orderBy (fun _ state -> state.Score)
            let byName = orderBy (fun _ (state: ItemState) -> state.Name)

            let order = sinkC byScore
            let sorted = collection |> sortByOrderC order

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

            // The view has no value for a key that it does not hold, and the collection has one.
            // This test asserted that the two were the same cell before a view became a
            // collection.
            do! Expect.Equal(None, passing |> stateCell 1 |> sampleC |> Option.map (fun s -> s.Name))

            do! Expect.Equal(Some "one", collection |> stateCell 1 |> sampleC |> Option.map (fun s -> s.Name))

            // Two observers share a cell because this code never copies one, in one view, which is
            // where that result is important.
            do! Expect.Same(passing |> stateCell 2, passing |> stateCell 2)
        }
