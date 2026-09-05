module SodaFlow.Tests.Internal.Node

open SodaFlow
open SodaFlow.Tests
open TUnit.Core

type ``Node Tests``() =

    [<Test>]
    member _.``Test Node``() =
        task {
            let a = Node<int>()
            let b = Node<int>()

            TransactionInternal.Apply(fun trans _ ->
                a.Link(trans, (fun _ _ -> ()), b) |> ignore
                trans.Prioritized(a, (fun _ -> ())))

            do! Expect.LessThan(b.Rank, a.Rank)
        }
