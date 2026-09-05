module SodaFlow.Tests.Stream

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Threading
open SodaFlow
open TUnit.Core

// These two run outside the tests that assert on them, and not by preference. Each turns on a weak
// listener being collected once the local holding it is dropped, and neither a task block nor an
// inner `let f () = ...` gives it a frame that ends: the state machine holds every local for the
// life of the method, and F# inlines a local function used once. So the part that has to go out of
// scope is its own function, kept out of its caller, which is what the C# tests do too.

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private listenAndSend (s: StreamSink<int>) (out: List<int>) (values: int list) =
    let _l = s |> listenS out.Add
    values |> List.iter (fun v -> s |> sendS v)

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private listenAndSendMapped (s: StreamSink<int>) (s2: Stream<int>) (out: List<int>) (values: int list) =
    let _l = s2 |> listenS out.Add
    values |> List.iter (fun v -> s |> sendS v)

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private listenAndDrop () =
    let s = sinkS ()
    let out = List<_>()

    listenAndSend s out [ 1; 2 ]

    GC.Collect(0, GCCollectionMode.Forced)
    GC.Collect(0, GCCollectionMode.Forced)
    s |> sendS 3
    s |> sendS 4
    out

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private listenAndDropInner (s: StreamSink<int>) (out: List<int>) =
    let s2 = s |> mapS ((+) 1)

    listenAndSend s out [ 1; 2 ]

    GC.Collect(0, GCCollectionMode.Forced)
    GC.Collect(0, GCCollectionMode.Forced)

    listenAndSendMapped s s2 out [ 3; 4; 5 ]

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private listenAndDropWithMap () =
    let s = sinkS ()
    let out = List<_>()

    listenAndDropInner s out

    GC.Collect(0, GCCollectionMode.Forced)
    GC.Collect(0, GCCollectionMode.Forced)
    s |> sendS 6
    s |> sendS 7
    out

type ``Stream Tests``() =

    [<Test>]
    member _.``Test Stream Send``() =
        task {
            let s = sinkS ()
            let out = List<_>()
            let l = s |> listenStrongS out.Add
            s |> sendS 5
            l |> unlistenL
            do! Expect.Sequence([ 5 ], out)
            s |> sendS 6
            do! Expect.Sequence([ 5 ], out)
        }

    [<Test>]
    member _.``Test Stream Send In Callback Throws Exception``() =
        task {
            let s = sinkS ()
            let s2 = sinkS ()

            let actual =
                (use _let = s |> listenStrongS (s2 |> flip sendS)

                 try
                     s |> sendS 5
                     None
                 with :? InvalidOperationException as e ->
                     Some e)

            do!
                actual
                |> assertExceptionExists (fun e -> Expect.Equal("Send may not be called inside a callback.", e.Message))
        }

    [<Test>]
    member _.``Test Stream Send In Map Throws Exception``() =
        task {
            let s = sinkS ()
            let s2 = sinkS ()

            let actual =
                (use _let = s |> mapS (s2 |> flip sendS) |> listenStrongS id

                 try
                     s |> sendS 5
                     None
                 with :? InvalidOperationException as e ->
                     Some e)

            do!
                actual
                |> assertExceptionExists (fun e -> Expect.Equal("Send may not be called inside a callback.", e.Message))
        }

    [<Test>]
    member _.``Test Stream Send In Cell Map Throws Exception``() =
        task {
            let c = constantC 5
            let s2 = sinkS ()

            let actual =
                (try
                    use _let = c |> mapC (s2 |> flip sendS) |> listenStrongC id
                    None
                 with :? InvalidOperationException as e ->
                     Some e)

            do!
                actual
                |> assertExceptionExists (fun e -> Expect.Equal("Send may not be called inside a callback.", e.Message))
        }

    [<Test>]
    member _.``Test Stream Send In Cell Lift Throws Exception``() =
        task {
            let c = constantC 5
            let c2 = constantC 7
            let s2 = sinkS ()

            let actual =
                (try
                    use _let = (c, c2) |> lift2C (fun _ _ -> s2 |> sendS 5) |> listenStrongC id
                    None
                 with :? InvalidOperationException as e ->
                     Some e)

            do!
                actual
                |> assertExceptionExists (fun e -> Expect.Equal("Send may not be called inside a callback.", e.Message))
        }

    [<Test>]
    member _.``Test Stream Send In Cell Apply Throws Exception``() =
        task {
            let c = constantC 5
            let s2 = sinkS ()
            let c2 = constantC (fun _ -> s2 |> sendS 5)

            let actual =
                (try
                    use _let = c |> applyC c2 |> listenStrongC id
                    None
                 with :? InvalidOperationException as e ->
                     Some e)

            do!
                actual
                |> assertExceptionExists (fun e -> Expect.Equal("Send may not be called inside a callback.", e.Message))
        }

    [<Test>]
    member _.``Test Map``() =
        task {
            let s = sinkS ()
            let m = s |> mapS ((+) 2 >> string)
            let out = List<_>()
            let l = m |> listenStrongS out.Add
            s |> sendS 5
            s |> sendS 3
            l |> unlistenL
            do! Expect.Sequence([ "7"; "5" ], out)
        }

    [<Test>]
    member _.``Test OrElse Non-Simultaneous``() =
        task {
            let s1 = sinkS ()
            let s2 = sinkS ()
            let out = List<_>()
            let l = (s1, s2) |> orElseS |> listenStrongS out.Add
            s1 |> sendS 7
            s1 |> sendS 9
            s1 |> sendS 8
            l |> unlistenL
            do! Expect.Sequence([ 7; 9; 8 ], out)
        }

    [<Test>]
    member _.``Test OrElse Simultaneous 1``() =
        task {
            let s1 = sinkWithCoalesceS (fun _ r -> r)
            let s2 = sinkWithCoalesceS (fun _ r -> r)
            let out = List<_>()
            let l = (s2, s1) |> orElseS |> listenStrongS out.Add

            runT (fun () ->
                s1 |> sendS 7
                s2 |> sendS 60)

            runT (fun () -> s1 |> sendS 9)

            runT (fun () ->
                s1 |> sendS 7
                s1 |> sendS 60
                s2 |> sendS 8
                s2 |> sendS 90)

            runT (fun () ->
                s2 |> sendS 8
                s2 |> sendS 90
                s1 |> sendS 7
                s1 |> sendS 60)

            runT (fun () ->
                s2 |> sendS 8
                s1 |> sendS 7
                s2 |> sendS 90
                s1 |> sendS 60)

            l |> unlistenL
            do! Expect.Sequence([ 60; 9; 90; 90; 90 ], out)
        }

    [<Test>]
    member _.``Test OrElse Simultaneous 2``() =
        task {
            let s = sinkS ()
            let s2 = s |> mapS ((*) 2)
            let out = List<_>()
            let l = (s, s2) |> orElseS |> listenStrongS out.Add
            s |> sendS 7
            s |> sendS 9
            l |> unlistenL
            do! Expect.Sequence([ 7; 9 ], out)
        }

    [<Test>]
    member _.``Test OrElse Left Bias``() =
        task {
            let s = sinkS ()
            let s2 = s |> mapS ((*) 2)
            let out = List<_>()
            let l = (s2, s) |> orElseS |> listenStrongS out.Add
            s |> sendS 7
            s |> sendS 9
            l |> unlistenL
            do! Expect.Sequence([ 14; 18 ], out)
        }

    [<Test>]
    member _.``Test Merge Non-Simultaneous``() =
        task {
            let s1 = sinkS ()
            let s2 = sinkS ()
            let out = List<_>()
            let l = (s1, s2) |> mergeS (+) |> listenStrongS out.Add
            s1 |> sendS 7
            s1 |> sendS 9
            s1 |> sendS 8
            l |> unlistenL
            do! Expect.Sequence([ 7; 9; 8 ], out)
        }

    [<Test>]
    member _.``Test Merge Simultaneous``() =
        task {
            let s = sinkS ()
            let s2 = s |> mapS ((*) 2)
            let out = List<_>()
            let l = (s, s2) |> mergeS (+) |> listenStrongS out.Add
            s |> sendS 7
            s |> sendS 9
            l |> unlistenL
            do! Expect.Sequence([ 21; 27 ], out)
        }

    [<Test>]
    member _.``Test Coalesce``() =
        task {
            let s = sinkWithCoalesceS (+)
            let out = List<_>()
            let l = s |> listenStrongS out.Add
            runT (fun () -> s |> sendS 2)

            runT (fun () ->
                s |> sendS 8
                s |> sendS 40)

            l |> unlistenL
            do! Expect.Sequence([ 2; 48 ], out)
        }

    [<Test>]
    member _.``Test Coalesce 2``() =
        task {
            let s = sinkWithCoalesceS (+)
            let out = List<_>()
            let l = s |> listenStrongS out.Add
            runT (fun () -> Seq.init 5 ((+) 1) |> Seq.iter (s |> flip sendS))
            runT (fun () -> Seq.init 5 ((+) 6) |> Seq.iter (s |> flip sendS))
            l |> unlistenL
            do! Expect.Sequence([ 15; 40 ], out)
        }

    [<Test>]
    member _.``Test Filter``() =
        task {
            let s = sinkS ()
            let out = List<_>()
            let l = s |> filterS Char.IsUpper |> listenStrongS out.Add
            s |> sendS 'H'
            s |> sendS 'o'
            s |> sendS 'I'
            l |> unlistenL
            do! Expect.Sequence([ 'H'; 'I' ], out)
        }

    [<Test>]
    member _.``Test Filter Some``() =
        task {
            let s = sinkS ()
            let out = List<_>()
            let l = s |> filterSomeS |> listenStrongS out.Add
            s |> sendS (Some "tomato")
            s |> sendS None
            s |> sendS (Some "peach")
            s |> sendS None
            s |> sendS (Some "pear")
            l |> unlistenL
            do! Expect.Sequence([ "tomato"; "peach"; "pear" ], out)
        }

    [<Test>]
    member _.``Test Choose``() =
        task {
            let s = sinkS ()
            let out = List<_>()

            let l =
                s
                |> chooseS (fun (v: string) -> if v.Length > 4 then Some v.Length else None)
                |> listenStrongS out.Add

            s |> sendS "tomato"
            s |> sendS "fig"
            s |> sendS "peach"
            s |> sendS "yam"
            s |> sendS "pear"
            l |> unlistenL
            do! Expect.Sequence([ 6; 5 ], out)
        }

    [<Test>]
    member _.``Test Choose Matches Map Then Filter Some``() =
        task {
            let s = sinkS ()
            let chosen = List<_>()
            let mapped = List<_>()

            let f (v: string) =
                if v.Length > 4 then Some v.Length else None

            let l1 = s |> chooseS f |> listenStrongS chosen.Add
            let l2 = s |> mapS f |> filterSomeS |> listenStrongS mapped.Add
            s |> sendS "tomato"
            s |> sendS "fig"
            s |> sendS "peach"
            l1 |> unlistenL
            l2 |> unlistenL
            do! Expect.Sequence(mapped, chosen)
            do! Expect.Sequence([ 6; 5 ], chosen)
        }

    [<Test>]
    member _.``Test Choose None Fires Nothing``() =
        task {
            let s = sinkS ()
            let out = List<_>()
            let l = s |> chooseS (fun (_: string) -> Option<int>.None) |> listenStrongS out.Add
            s |> sendS "tomato"
            s |> sendS "peach"
            l |> unlistenL
            do! Expect.Sequence(List<int>(), out)
        }

    [<Test>]
    member _.``Test Loop Stream``() =
        task {
            let sa = sinkS ()

            let struct (sb, sc) =
                loopS (fun sb ->
                    let sc = (sa |> mapS (flip (%) 10), sb) |> mergeS (*)
                    let sb = sa |> mapS (flip (/) 10) |> filterS ((<>) 0)
                    struct (sb, sc))

            let out = List<_>()
            let out2 = List<_>()
            let l = sb |> listenStrongS out.Add
            let l2 = sc |> listenStrongS out2.Add
            sa |> sendS 2
            sa |> sendS 52
            l2 |> unlistenL
            l |> unlistenL
            do! Expect.Sequence([ 5 ], out)
            do! Expect.Sequence([ 2; 10 ], out2)
        }

    [<Test>]
    member _.``Test Loop Cell``() =
        task {
            let ca = sinkC 22

            let struct (cb, cc) =
                loopC (fun cb ->
                    let cc = (ca |> mapC (flip (%) 10), cb) |> lift2C (*)
                    let cb = ca |> mapC (flip (/) 10)
                    struct (cb, cc))

            let out = List<_>()
            let out2 = List<_>()
            let l = cb |> listenStrongC out.Add
            let l2 = cc |> listenStrongC out2.Add
            ca |> sendC 2
            ca |> sendC 52
            l2 |> unlistenL
            l |> unlistenL
            do! Expect.Sequence([ 2; 0; 5 ], out)
            do! Expect.Sequence([ 4; 0; 10 ], out2)
        }

    [<Test>]
    member _.``Test Gate``() =
        task {
            let sc = sinkS ()
            let cGate = sinkB true
            let out = List<_>()
            let l = sc |> gateB cGate |> listenStrongS out.Add
            sc |> sendS 'H'
            cGate |> sendB false
            sc |> sendS 'O'
            cGate |> sendB true
            sc |> sendS 'I'
            l |> unlistenL
            do! Expect.Sequence([ 'H'; 'I' ], out)
        }

    [<Test>]
    member _.``Test Calm``() =
        task {
            let s = sinkS ()
            let out = List<_>()
            let l = s |> calmS |> listenStrongS out.Add
            s |> sendS 2
            s |> sendS 2
            s |> sendS 2
            s |> sendS 4
            s |> sendS 2
            s |> sendS 4
            s |> sendS 4
            s |> sendS 2
            s |> sendS 2
            s |> sendS 2
            s |> sendS 2
            s |> sendS 2
            s |> sendS 4
            s |> sendS 2
            s |> sendS 4
            s |> sendS 4
            s |> sendS 2
            s |> sendS 2
            s |> sendS 2
            s |> sendS 2
            s |> sendS 2
            s |> sendS 4
            s |> sendS 2
            s |> sendS 4
            s |> sendS 4
            s |> sendS 2
            s |> sendS 2
            s |> sendS 2
            s |> sendS 2
            s |> sendS 2
            s |> sendS 4
            s |> sendS 2
            s |> sendS 4
            s |> sendS 4
            s |> sendS 2
            s |> sendS 2
            s |> sendS 2
            s |> sendS 2
            s |> sendS 2
            s |> sendS 4
            s |> sendS 2
            s |> sendS 4
            s |> sendS 4
            s |> sendS 2
            s |> sendS 2
            l |> unlistenL
            do! Expect.Sequence([ 2; 4; 2; 4; 2; 4; 2; 4; 2; 4; 2; 4; 2; 4; 2; 4; 2; 4; 2; 4; 2 ], out)
        }

    [<Test>]
    member _.``Test Calm 2``() =
        task {
            let s = sinkS ()
            let out = List<_>()
            let l = s |> calmS |> listenStrongS out.Add
            s |> sendS 2
            s |> sendS 4
            s |> sendS 2
            s |> sendS 4
            s |> sendS 4
            s |> sendS 2
            s |> sendS 2
            l |> unlistenL
            do! Expect.Sequence([ 2; 4; 2; 4; 2 ], out)
        }

    [<Test>]
    member _.``Test Collect``() =
        task {
            let sa = sinkS ()
            let out = List<_>()

            let sum =
                sa
                |> collectS struct (100, true) (fun a struct (value, test) ->
                    let outputValue = value + if test then a * 3 else a
                    struct (outputValue, struct (outputValue, outputValue % 2 = 0)))

            let l = sum |> listenStrongS out.Add
            sa |> sendS 5
            sa |> sendS 7
            sa |> sendS 1
            sa |> sendS 2
            sa |> sendS 3
            l |> unlistenL
            do! Expect.Sequence([ 115; 122; 125; 127; 130 ], out)
        }

    [<Test>]
    member _.``Test Accum``() =
        task {
            let sa = sinkS ()
            let out = List<_>()
            let sum = sa |> accumS 100 (+)
            let l = sum |> listenStrongC out.Add
            sa |> sendS 5
            sa |> sendS 7
            sa |> sendS 1
            sa |> sendS 2
            sa |> sendS 3
            l |> unlistenL
            do! Expect.Sequence([ 100; 105; 112; 113; 115; 118 ], out)
        }

    [<Test>]
    member _.``Test Once``() =
        task {
            let s = sinkS ()
            let out = List<_>()
            let l = s |> onceS |> listenStrongS out.Add
            s |> sendS 'A'
            s |> sendS 'B'
            s |> sendS 'C'
            l |> unlistenL
            do! Expect.Sequence([ 'A' ], out)
        }

    [<Test>]
    member _.``Test Hold``() =
        task {
            let s = sinkS ()
            let c = s |> holdS ' '
            let out = List<_>()
            let l = c |> listenStrongC out.Add
            s |> sendS 'C'
            s |> sendS 'B'
            s |> sendS 'A'
            l |> unlistenL
            do! Expect.Sequence([ ' '; 'C'; 'B'; 'A' ], out)
        }

    [<Test>]
    member _.``Test Hold Implicit Delay``() =
        task {
            let s = sinkS ()
            let c = s |> holdS ' '
            let out = List<_>()
            let l = s |> snapshotAndTakeC c |> listenStrongS out.Add
            s |> sendS 'C'
            s |> sendS 'B'
            s |> sendS 'A'
            l |> unlistenL
            do! Expect.Sequence([ ' '; 'C'; 'B' ], out)
        }

    [<Test>]
    member _.``Test Defer``() =
        task {
            let s = sinkS ()
            let c = s |> holdS ' '
            let out = List<_>()
            let l = s |> Operational.defer |> snapshotAndTakeC c |> listenStrongS out.Add
            s |> sendS 'C'
            s |> sendS 'B'
            s |> sendS 'A'
            l |> unlistenL
            do! Expect.Sequence([ 'C'; 'B'; 'A' ], out)
        }

    [<Test>]
    member _.``Test Listen``() =
        task {
            let out = listenAndDrop ()
            do! Expect.Equal(2, out.Count)
        }

    [<Test>]
    member _.``Test Listen With Map``() =
        task {
            let out = listenAndDropWithMap ()
            do! Expect.Equal(5, out.Count)
        }

    [<Test>]
    member _.``Test Unlisten``() =
        task {
            let s = sinkS ()
            let out = List<_>()

            let a () =
                let l = s |> listenStrongS out.Add
                s |> sendS 1
                l |> unlistenL
                s |> sendS 2

            a ()
            s |> sendS 3
            s |> sendS 4
            do! Expect.Equal(1, out.Count)
        }

    [<Test>]
    member _.``Test Unlisten Weak``() =
        task {
            let s = sinkS ()
            let out = List<_>()

            let a () =
                let l = s |> listenS out.Add
                s |> sendS 1
                l |> unlistenL
                s |> sendS 2

            a ()
            s |> sendS 3
            s |> sendS 4
            do! Expect.Equal(1, out.Count)
        }

    [<Test>]
    member _.``Test Multiple Unlisten``() =
        task {
            let s = sinkS ()
            let out = List<_>()

            let a () =
                let l = s |> listenStrongS out.Add
                s |> sendS 1
                l |> unlistenL
                l |> unlistenL
                s |> sendS 2
                l |> unlistenL

            a ()
            s |> sendS 3
            s |> sendS 4
            do! Expect.Equal(1, out.Count)
        }

    [<Test>]
    member _.``Test Multiple Unlisten Weak``() =
        task {
            let s = sinkS ()
            let out = List<_>()

            let a () =
                let l = s |> listenS out.Add
                s |> sendS 1
                l |> unlistenL
                l |> unlistenL
                s |> sendS 2
                l |> unlistenL

            a ()
            s |> sendS 3
            s |> sendS 4
            do! Expect.Equal(1, out.Count)
        }

    [<Test>]
    member _.``Test ListenOnce``() =
        task {
            let s = sinkS ()
            let out = List<_>()
            let l = s |> listenOnceS out.Add
            s |> sendS 'A'
            s |> sendS 'B'
            s |> sendS 'C'
            l |> unlistenL
            do! Expect.Sequence([ 'A' ], out)
        }

    [<Test>]
    member _.``Test ListenOnceAsync``() =
        task {
            let s = sinkS ()

            Thread(fun () ->
                Thread.Sleep 250
                s |> sendS 'A'
                s |> sendS 'B'
                s |> sendS 'C')
                .Start()

            let! r = s |> listenOnceAsyncS
            do! Expect.Equal('A', r)
        }

    [<Test>]
    member _.``Test ListenOnceAsync Same Thread``() =
        task {
            let s = sinkS ()
            let r' = s |> listenOnceAsyncS
            s |> sendS 'A'
            s |> sendS 'B'
            s |> sendS 'C'
            let! r = r'
            do! Expect.Equal('A', r)
        }

    [<Test>]
    member _.``Test ListenStrong Async``() =
        task {
            let a = sinkC 1
            let a1 = a |> mapC ((+) 1)
            let a2 = a |> mapC ((*) 2)

            let struct (called, struct (_, l)) =
                loopC (fun calledLoop ->
                    let result = (a1, a2) |> lift2C (+)
                    let incrementStream = result |> valuesC |> mapToS ()
                    let decrementStream = sinkS ()

                    let called =
                        (incrementStream |> mapToS 1, decrementStream |> mapToS -1)
                        |> mergeS (+)
                        |> snapshotC calledLoop (+)
                        |> holdS 0

                    let results = List<_>()

                    let l =
                        result
                        |> listenStrongC (fun v ->
                            async {
                                do! Async.Sleep 900
                                results.Add v
                                decrementStream |> sendS ()
                            }
                            |> Async.Start)

                    struct (called, struct (results, l)))

            let calledResults = List<_>()
            let l2 = called |> listenStrongC calledResults.Add
            do! Async.Sleep 500
            a |> sendC 2
            do! Async.Sleep 500
            a |> sendC 3
            do! Async.Sleep 2500
            l2 |> unlistenL
            l |> unlistenL
        }

    [<Test>]
    member _.``Test Stream Loop``() =
        task {
            let streamSink = sinkS ()

            let s =
                loopWithNoCapturesS (fun sl ->
                    let c = sl |> mapS ((+) 2) |> holdS 0
                    streamSink |> snapshotC c (+))

            let out = List<_>()
            let l = s |> listenStrongS out.Add
            streamSink |> sendS 3
            streamSink |> sendS 4
            streamSink |> sendS 7
            streamSink |> sendS 8
            l |> unlistenL
            do! Expect.Sequence([ 3; 9; 18; 28 ], out)
        }

    [<Test>]
    member _.``Test Stream Loop Defer``() =
        task {
            let streamSink = sinkS ()

            let stream =
                loopWithNoCapturesS (fun streamLoop ->
                    (streamSink, streamLoop)
                    |> orElseS
                    |> filterS (flip (<) 5)
                    |> mapS ((+) 1)
                    |> Operational.defer)

            let out = List<_>()
            let l = stream |> listenStrongS out.Add
            streamSink |> sendS 2
            l |> unlistenL
            do! Expect.Sequence([ 3; 4; 5 ], out)
        }
