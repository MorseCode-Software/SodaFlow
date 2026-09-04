module SodaFlow.Tests.Internal.Node

open NUnit.Framework
open SodaFlow

[<TestFixture>]
type ``Node Tests``() =

    [<Test>]
    member _.``Test Node``() =
        let a = Node<int>()
        let b = Node<int>()

        TransactionInternal.Apply(fun trans _ ->
            a.Link(trans, (fun _ _ -> ()), b) |> ignore
            trans.Prioritized(a, (fun _ -> ())))

        Assert.That(a.Rank, Is.LessThan b.Rank)
