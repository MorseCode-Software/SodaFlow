module SodaFlow.Tests.ForwardReference

open System
open System.Collections.Generic
open NUnit.Framework
open SodaFlow

type Node(child: Child) =
    member _.Child = child

and Child(parent: Cell<Node>) =
    member _.Parent = parent

/// Builds the node whose child holds a reference back to it, which is the knot under test.
let private nodeHolding reference = Node(Child reference)

[<TestFixture>]
type ``Forward Reference Tests``() =

    [<Test>]
    member _.``Test Create With No Captures Resolves The Reference``() =
        let node = forwardReferenceWithNoCaptures nodeHolding

        Assert.AreSame(node, node.Child.Parent |> sampleC)

    [<Test>]
    member _.``Test Create With No Captures Returns What The Function Produced``() =
        let produced = obj ()

        let result = forwardReferenceWithNoCaptures (fun _ -> produced)

        Assert.AreSame(produced, result)

    [<Test>]
    member _.``Test Create With No Captures Runs The Function Once``() =
        let mutable calls = 0

        forwardReferenceWithNoCaptures (fun _ ->
            calls <- calls + 1
            1)
        |> ignore

        Assert.AreEqual(1, calls)

    [<Test>]
    member _.``Test Create With No Captures Reference Never Changes``() =
        // The single-valued case of a cell loop: the reference resolves once and stays there.
        let node = forwardReferenceWithNoCaptures nodeHolding
        let out = List<_>()
        let l = node.Child.Parent |> listenStrongC out.Add
        l |> unlistenL

        CollectionAssert.AreEqual([ node ], out)

    [<Test>]
    member _.``Test Create Resolves The Reference And Returns The Captures``() =
        let struct (node, sink) =
            forwardReference (fun reference -> struct (nodeHolding reference, sinkS ()))

        Assert.AreSame(node, node.Child.Parent |> sampleC)
        Assert.IsNotNull sink

    [<Test>]
    member _.``Test Create Runs The Function Once``() =
        let mutable calls = 0

        forwardReference (fun _ ->
            calls <- calls + 1
            struct (1, 2))
        |> ignore

        Assert.AreEqual(1, calls)

    [<Test>]
    member _.``Test Two Objects Can Refer To Each Other``() =
        // Neither exists when the other is constructed, which is the knot this unties.
        let struct (node, child) =
            forwardReference (fun reference -> struct (nodeHolding reference, Child reference))

        Assert.AreSame(node, child.Parent |> sampleC)
        Assert.AreSame(node, node.Child.Parent |> sampleC)

    [<Test>]
    member _.``Test Works Inside An Existing Transaction``() =
        let node = runT (fun () -> forwardReferenceWithNoCaptures nodeHolding)

        Assert.AreSame(node, node.Child.Parent |> sampleC)

    [<Test>]
    member _.``Test Reference Cannot Be Read During Construction``() =
        // The reference is a promise about what the value will be, not the value, so asking for
        // it before the constructing function has returned has no answer.
        Assert.Throws<InvalidOperationException>(fun () -> forwardReferenceWithNoCaptures sampleC |> ignore)
        |> ignore
