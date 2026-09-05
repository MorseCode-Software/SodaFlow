module SodaFlow.Tests.Common

open System.Collections.Generic
open SodaFlow
open TUnit.Core

type ``Common Tests``() =

    [<Test>]
    member _.``Test Base Send 1``() =
        task {
            let s = sinkS ()
            let out = List<_>()
            let l = s |> listenStrongS out.Add
            s |> sendS "a"
            s |> sendS "b"
            l |> unlistenL
            do! Expect.Sequence([ "a"; "b" ], out)
        }

    [<Test>]
    member _.``Test Operational Split``() =
        task {
            let a = sinkS ()
            let b = a |> Operational.split
            let out = List<_>()
            let l = b |> listenStrongS out.Add
            a |> sendS [| "a"; "b" |]
            l |> unlistenL
            do! Expect.Sequence([ "a"; "b" ], out)
        }

    [<Test>]
    member _.``Test Operational Defer 1``() =
        task {
            let a = sinkS ()
            let b = a |> Operational.defer
            let out = List<_>()
            let l = b |> listenStrongS out.Add
            a |> sendS "a"
            l |> unlistenL
            do! Expect.Sequence([ "a" ], out)
            let out = List<_>()
            let l = b |> listenStrongS out.Add
            a |> sendS "b"
            l |> unlistenL
            do! Expect.Sequence([ "b" ], out)
        }

    [<Test>]
    member _.``Test Operational Defer 2``() =
        task {
            let a = sinkS ()
            let b = sinkS ()
            let c = (a |> Operational.defer, b) |> orElseS
            let out = List<_>()
            let l = c |> listenStrongS out.Add
            a |> sendS "a"
            l |> unlistenL
            do! Expect.Sequence([ "a" ], out)
            let out = List<_>()
            let l = c |> listenStrongS out.Add

            runT (fun () ->
                a |> sendS "b"
                b |> sendS "B")

            l |> unlistenL
            do! Expect.Sequence([ "B"; "b" ], out)
        }

    [<Test>]
    member _.``Test Stream OrElse 1``() =
        task {
            let a = sinkS ()
            let b = sinkS ()
            let c = (a, b) |> orElseS
            let out = List<_>()
            let l = c |> listenStrongS out.Add
            a |> sendS 0
            l |> unlistenL
            do! Expect.Sequence([ 0 ], out)
            let out = List<_>()
            let l = c |> listenStrongS out.Add
            b |> sendS 10
            l |> unlistenL
            do! Expect.Sequence([ 10 ], out)
            let out = List<_>()
            let l = c |> listenStrongS out.Add

            runT (fun () ->
                a |> sendS 2
                b |> sendS 20)

            l |> unlistenL
            do! Expect.Sequence([ 2 ], out)
            let out = List<_>()
            let l = c |> listenStrongS out.Add
            b |> sendS 30
            l |> unlistenL
            do! Expect.Sequence([ 30 ], out)
        }

    [<Test>]
    member _.``Test Operational Defer Simultaneous``() =
        task {
            let a = sinkS ()
            let b = sinkS ()
            let c = (a |> Operational.defer, b |> Operational.defer) |> orElseS
            let out = List<_>()
            let l = c |> listenStrongS out.Add
            a |> sendS "A"
            l |> unlistenL
            do! Expect.Sequence([ "A" ], out)
            let out = List<_>()
            let l = c |> listenStrongS out.Add

            runT (fun () ->
                a |> sendS "b"
                b |> sendS "B")

            l |> unlistenL
            do! Expect.Sequence([ "b" ], out)
        }
