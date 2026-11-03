module SodaFlow.Tests.Loop

open System
open System.Collections.Generic
open SodaFlow
open TUnit.Core

type ``Loop Tests``() =

    [<Test>]
    member _.``Functional Stream Loop``() =
        task {
            let s = sinkS ()
            let result = loopWithNoCapturesS (fun r -> s |> snapshotC (r |> holdS 0) (+))
            let out = List<_>()
            let l = result |> listenStrongS out.Add
            s |> sendS 1
            s |> sendS 2
            s |> sendS 3
            l |> unlistenL
            do! Expect.Sequence([ 1; 3; 6 ], out)
        }

    [<Test>]
    member _.``Functional Stream Loop With Captures``() =
        task {
            let s = sinkS ()

            let struct (result, s2) =
                loopS (fun r -> struct (s |> snapshotC (r |> holdS 0) (+), s |> mapS ((*) 2)))

            let out = List<_>()
            let out2 = List<_>()

            let _ =
                (use _l = result |> listenStrongS out.Add
                 use _l = s2 |> listenStrongS out2.Add
                 s |> sendS 1
                 s |> sendS 2
                 s |> sendS 3)

            do! Expect.Sequence([ 1; 3; 6 ], out)
            do! Expect.Sequence([ 2; 4; 6 ], out2)
        }

    [<Test>]
    member _.``Functional Behavior Loop``() =
        task {
            let s = sinkB 0

            let result =
                loopWithNoCapturesB (fun r -> s |> Operational.updates |> snapshotB r (+) |> holdS 0 |> asBehaviorC)

            let out = List<_>()
            let l = runT (fun () -> result |> Operational.value |> listenStrongS out.Add)
            s |> sendB 1
            s |> sendB 2
            s |> sendB 3
            l |> unlistenL
            do! Expect.Sequence([ 0; 1; 3; 6 ], out)
        }

    [<Test>]
    member _.``Functional Behavior Loop With Captures``() =
        task {
            let s = sinkB 0

            let struct (result, s2) =
                loopB (fun r ->
                    struct (s |> Operational.updates |> snapshotB r (+) |> holdS 0 |> asBehaviorC, s |> mapB ((*) 2)))

            let out = List<_>()
            let out2 = List<_>()

            let _ =
                (use _l = runT (fun () -> result |> Operational.value |> listenStrongS out.Add)
                 use _l = runT (fun () -> s2 |> Operational.value |> listenStrongS out2.Add)
                 s |> sendB 1
                 s |> sendB 2
                 s |> sendB 3)

            do! Expect.Sequence([ 0; 1; 3; 6 ], out)
            do! Expect.Sequence([ 0; 2; 4; 6 ], out2)
        }

    [<Test>]
    member _.``Functional Cell Loop``() =
        task {
            let s = sinkC 0

            let result =
                loopWithNoCapturesC (fun r -> s |> updatesC |> snapshotC r (+) |> holdS 0)

            let out = List<_>()
            let l = runT (fun () -> result |> listenStrongC out.Add)
            s |> sendC 1
            s |> sendC 2
            s |> sendC 3
            l |> unlistenL
            do! Expect.Sequence([ 0; 1; 3; 6 ], out)
        }

    [<Test>]
    member _.``Functional Cell Loop With Captures``() =
        task {
            let s = sinkC 0

            let struct (result, s2) =
                loopC (fun r -> struct (s |> updatesC |> snapshotC r (+) |> holdS 0, s |> mapC ((*) 2)))

            let out = List<_>()
            let out2 = List<_>()

            let _ =
                (use _l = runT (fun () -> result |> listenStrongC out.Add)
                 use _l = s2 |> listenStrongC out2.Add
                 s |> sendC 1
                 s |> sendC 2
                 s |> sendC 3)

            do! Expect.Sequence([ 0; 1; 3; 6 ], out)
            do! Expect.Sequence([ 0; 2; 4; 6 ], out2)
        }

    [<Test>]
    member _.``The Closer Gives A Looped Capture As A Cell``() =
        task {
            let s = sinkC 0

            // The capture reads the placeholder, thus it is a looped cell. The second function runs
            // when the loop is closed, and the closer it gets gives the capture back as a cell.
            let struct (result, plusOne) =
                loopWithLoopedCapturesC
                    (fun l -> struct (s |> updatesC |> snapshotC l (+) |> holdS 0, l |> mapLC ((+) 1)))
                    (fun plusOne closer -> plusOne |> closeLC closer)

            s |> sendC 5

            do! Expect.Equal(6, plusOne |> sampleC)
            do! Expect.Equal(5, result |> sampleC)
        }

    [<Test>]
    member _.``The Close Function Runs After The Loop Is Closed``() =
        task {
            let s = sinkC 3

            // A read of the converted capture in the close function has an answer, which shows that
            // the loop is closed when the function runs.
            let struct (result, readInClose) =
                loopWithLoopedCapturesC
                    (fun l -> struct (s |> updatesC |> snapshotC l (+) |> holdS 0, l |> mapLC ((+) 1)))
                    (fun plusOne closer -> plusOne |> closeLC closer |> sampleC)

            do! Expect.Equal(1, readInClose)
            do! Expect.Equal(0, result |> sampleC)
        }

    [<Test>]
    member _.``As Cell Read Before The Close Throws``() =
        task {
            do!
                Expect.Throws<InvalidOperationException>(fun () ->
                    loopWithNoCapturesC (fun l -> constantC (l |> asCellLC |> sampleC)) |> ignore)
        }

    [<Test>]
    member _.``As Cell Read After The Close Gives The Value``() =
        task {
            let struct (result, asserted) = loopC (fun l -> struct (constantC 4, l |> asCellLC))

            do! Expect.Equal(4, asserted |> sampleC)
            do! Expect.Equal(4, result |> sampleC)
        }
