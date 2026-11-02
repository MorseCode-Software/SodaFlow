module SodaFlow.Tests.Listener

open System.Collections.Generic
open SodaFlow
open TUnit.Core

// A mutable listener owns the listener it points at. Thus, the listener it replaces stops, and so
// does the one it clears.
type ``Listener Tests``() =

    [<Test>]
    member _.``setListener stops the listener it replaces``() =
        task {
            let s = sinkS ()
            let out = List<string>()
            let m = MutableListener()

            m |> MutableListener.setListener (s |> listenStrongS (fun v -> out.Add("first:" + string v)))
            m |> MutableListener.setListener (s |> listenStrongS (fun v -> out.Add("second:" + string v)))
            s |> sendS 1
            m |> MutableListener.unlisten

            do! Expect.Sequence([ "second:1" ], out)
        }

    [<Test>]
    member _.``clearListener stops the current listener``() =
        task {
            let s = sinkS ()
            let out = List<int>()
            let m = MutableListener()

            m |> MutableListener.setListener (s |> listenStrongS out.Add)
            s |> sendS 1
            m |> MutableListener.clearListener
            s |> sendS 2

            do! Expect.Sequence([ 1 ], out)
        }

    [<Test>]
    member _.``the mutable listener stays usable after clearListener``() =
        task {
            let s = sinkS ()
            let out = List<int>()
            let m = MutableListener()

            m |> MutableListener.setListener (s |> listenStrongS out.Add)
            m |> MutableListener.clearListener
            m |> MutableListener.setListener (s |> listenStrongS (fun v -> out.Add(v * 10)))
            s |> sendS 1
            m |> MutableListener.unlisten

            do! Expect.Sequence([ 10 ], out)
        }

    // A set of the listener that is already the target must not stop it.
    [<Test>]
    member _.``setListener with the same listener keeps it``() =
        task {
            let s = sinkS ()
            let out = List<int>()
            let m = MutableListener()
            let l = s |> listenStrongS out.Add

            m |> MutableListener.setListener l
            m |> MutableListener.setListener l
            s |> sendS 1
            m |> MutableListener.unlisten

            do! Expect.Sequence([ 1 ], out)
        }
