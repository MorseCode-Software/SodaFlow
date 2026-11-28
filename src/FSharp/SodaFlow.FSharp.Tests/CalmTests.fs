module SodaFlow.Tests.Calm

open System
open System.Collections.Generic
open SodaFlow
open TUnit.Core

#if NET
// A struct record. A calm that is not inline compares with the generic =, which boxes the two
// values of each comparison for such a type.
[<Struct>]
type private Point = { X: int; Y: int }

// The bytes that one call of send allocates, after the first calls make the spare queues of the
// transaction and compile the code. The reference assemblies of net472 do not have the read of
// the bytes that a thread allocated.
let private bytesForEachCall (send: unit -> unit) =
    let calls = 1000

    for _ in 1..calls do
        send ()

    let before = GC.GetAllocatedBytesForCurrentThread()

    for _ in 1..calls do
        send ()

    (GC.GetAllocatedBytesForCurrentThread() - before) / int64 calls

// Each send gives a new point. Thus, calm lets each one through.
let private bytesThroughAStream (build: Stream<Point> -> Stream<Point>) =
    let s = sinkS ()
    let l = build s |> listenS ignore
    let x = ref 0

    let bytes =
        bytesForEachCall (fun () ->
            x.Value <- x.Value + 1
            s |> sendS { X = x.Value; Y = 0 })

    l |> unlistenL
    bytes

let private bytesThroughACell (build: Cell<Point> -> Cell<Point>) =
    let s = sinkS ()
    let l = s |> holdS { X = 0; Y = 0 } |> build |> listenC ignore
    let x = ref 0

    let bytes =
        bytesForEachCall (fun () ->
            x.Value <- x.Value + 1
            s |> sendS { X = x.Value; Y = 0 })

    l |> unlistenL
    bytes
#endif

type ``Calm Tests``() =
#if NET
    // Each pair is the same calm node, one with the comparer of the type, which does not box.
    // The two costs are equal unless calm boxes at each comparison. A map is not the reference: the
    // calm of a cell is more than one node.
    [<Test>]
    member _.``Calm On A Struct Record Allocates What The Comparer Of Its Type Allocates``() =
        task {
            do!
                Expect.Equal(
                    bytesThroughAStream (calmWithEqualityComparerS EqualityComparer<Point>.Default),
                    bytesThroughAStream calmS
                )

            do!
                Expect.Equal(
                    bytesThroughACell (calmWithEqualityComparerC EqualityComparer<Point>.Default),
                    bytesThroughACell calmC
                )
        }
#endif

    // The equality of F# gives false for NaN against NaN. Thus, calm lets each NaN through. The
    // default comparer of .NET gives true, and stops the second one.
    [<Test>]
    member _.``Calm Lets Each NaN Through``() =
        task {
            let s = sinkS ()
            let out = List<float>()
            let l = s |> calmS |> listenS out.Add
            s |> sendS nan
            s |> sendS nan
            l |> unlistenL
            do! Expect.Equal(2, out.Count)

            let c = sinkS ()
            let cellOut = List<float>()
            let cl = c |> holdS 0.0 |> calmC |> updatesC |> listenS cellOut.Add
            c |> sendS nan
            c |> sendS nan
            cl |> unlistenL
            do! Expect.Equal(2, cellOut.Count)
        }
