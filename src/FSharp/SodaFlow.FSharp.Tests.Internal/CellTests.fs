module SodaFlow.Tests.Internal.Cell

open System.Collections.Generic
open SodaFlow
open SodaFlow.Tests
open TUnit.Core

type ``Cell Tests``() =

    [<Test>]
    member _.``Test Transaction``() =
        task {
            let mutable calledBack = false
            TransactionInternal.Apply(fun trans _ -> trans.Prioritized(Node<unit>.Null, fun trans -> calledBack <- true))
            do! Expect.True calledBack
        }

    [<Test>]
    member _.``Test Regen``() =
        task {
            let out = List<_>()

            TransactionInternal.Apply(fun trans _ ->
                let setNeedsRegeneratingAndPrioritized a =
                    trans.Prioritized(Node<unit>(), (fun _ -> a ()))

                setNeedsRegeneratingAndPrioritized (fun () -> out.Add 1)
                setNeedsRegeneratingAndPrioritized (fun () -> setNeedsRegeneratingAndPrioritized (fun () -> out.Add 4))
                setNeedsRegeneratingAndPrioritized (fun () -> out.Add 2)

                setNeedsRegeneratingAndPrioritized (fun () ->
                    setNeedsRegeneratingAndPrioritized (fun () ->
                        setNeedsRegeneratingAndPrioritized (fun () -> out.Add 6)))

                setNeedsRegeneratingAndPrioritized (fun () -> setNeedsRegeneratingAndPrioritized (fun () -> out.Add 5))
                trans.Prioritized(Node<unit>(), fun _ -> out.Add 3))

            do! Expect.Sequence([ 1; 2; 3; 4; 5; 6 ], out)
        }
