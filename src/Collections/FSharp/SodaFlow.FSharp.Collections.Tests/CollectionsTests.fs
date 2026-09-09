module SodaFlow.Collections.Tests.CollectionsTests

open System.Collections.Generic
open SodaFlow
open SodaFlow.Collections
open SodaFlow.Tests
open TUnit.Core

/// The immutable portion of a test item. The key is its Number.
type ItemId = { Number: int; Code: string }

/// The mutable portion of a test item.
type ItemState = { Name: string; Score: int }

let private keyOf (identity: ItemId) = identity.Number

let private item number name score =
    entry { Number = number; Code = sprintf "C%d" number } { Name = name; Score = score }

let private keysOf (view: IReactiveCollection<int, ItemId, ItemState>) =
    List<int>(view |> keysCell |> sampleC)

type ``Collections Tests``() =

    [<Test>]
    member _.``create holds its initial entries``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemId, ItemState>> ()

            let collection =
                create keyOf [ item 1 "one" 10; item 2 "two" 20 ] [ edits ]

            let snapshot = collection |> snapshotCell |> sampleC

            do! Expect.Equal(2, snapshot.Count)

            // option rather than Maybe: the core answers in TryGets so that each language surface
            // can put its own optional type on top.
            do! Expect.Equal(
                    Some "one",
                    snapshot |> lookup 1 |> Option.map (fun e -> e.State.Name))
        }

    [<Test>]
    member _.``an add edit reaches the shape cell and the state cell``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemId, ItemState>> ()
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
                        | None -> "gone"))

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

            let collection =
                create keyOf [ item 1 "one" 10 ] [ fromUpdates scores ]

            scores |> sendS (1, (fun state -> { state with Score = 99 }))

            let snapshot = collection |> snapshotCell |> sampleC

            do! Expect.Equal(
                    Some 99,
                    snapshot |> lookup 1 |> Option.map (fun e -> e.State.Score))
        }

    [<Test>]
    member _.``the root is ordered by key``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemId, ItemState>> ()

            let collection =
                create keyOf [ item 3 "three" 30; item 1 "one" 10; item 2 "two" 20 ] [ edits ]

            do! Expect.Sequence([ 1; 2; 3 ], keysOf collection)
        }

    [<Test>]
    member _.``a chain runs in the order it is written``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemId, ItemState>> ()

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
    member _.``sortBy re-files an item whose sort value moved``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemId, ItemState>> ()

            let collection =
                create keyOf [ item 1 "one" 30; item 2 "two" 10; item 3 "three" 20 ] [ edits ]

            let byScore = collection |> sortBy (fun _ state -> state.Score)

            do! Expect.Sequence([ 2; 3; 1 ], keysOf byScore)

            edits |> sendS (updateEdit 1 (fun state -> { state with Score = 5 }))

            do! Expect.Sequence([ 1; 2; 3 ], keysOf byScore)
        }

    [<Test>]
    member _.``a view shares the store with its root``() =
        task {
            let edits = sinkS<CollectionEdit<int, ItemId, ItemState>> ()

            let collection =
                create keyOf [ item 1 "one" 10; item 2 "two" 20 ] [ edits ]

            let passing = collection |> filter (fun _ state -> state.Score >= 20)

            do! Expect.Same(collection |> stateCell 1, passing |> stateCell 1)
            do! Expect.False((passing |> keysCell |> sampleC).Contains 1)

            // The store answers for a key the view filtered out, which is the seam the unification
            // leaves; membership questions belong to the keys.
            do! Expect.Equal(
                    Some "one",
                    collection |> stateCell 1 |> sampleC |> Option.map (fun s -> s.Name))
        }
