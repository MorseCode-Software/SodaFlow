module SodaFlow.Tests.ForwardReference

open System
open System.Collections.Generic
open SodaFlow
open TUnit.Core

type Node(child: Child) =
    member _.Child = child

and Child(parent: Cell<Node>) =
    member _.Parent = parent

/// Builds the node whose child holds a reference back to it, which is the knot under test.
let private nodeHolding reference = Node(Child reference)

type ``Forward Reference Tests``() =

    [<Test>]
    member _.``Test Create With No Captures Resolves The Reference``() =
        task {
            let node = forwardReferenceWithNoCaptures nodeHolding

            do! Expect.Same(node, node.Child.Parent |> sampleC)
        }

    [<Test>]
    member _.``Test Create With No Captures Returns What The Function Produced``() =
        task {
            let produced = obj ()

            let result = forwardReferenceWithNoCaptures (fun _ -> produced)

            do! Expect.Same(produced, result)
        }

    [<Test>]
    member _.``Test Create With No Captures Runs The Function Once``() =
        task {
            let mutable calls = 0

            forwardReferenceWithNoCaptures (fun _ ->
                calls <- calls + 1
                1)
            |> ignore

            do! Expect.Equal(1, calls)
        }

    [<Test>]
    member _.``Test Create With No Captures Reference Never Changes``() =
        task {
            // The single-valued case of a cell loop: the reference resolves once and stays there.
            let node = forwardReferenceWithNoCaptures nodeHolding
            let out = List<_>()
            let l = node.Child.Parent |> listenStrongC out.Add
            l |> unlistenL

            do! Expect.Sequence([ node ], out)
        }

    [<Test>]
    member _.``Test Create Resolves The Reference And Returns The Captures``() =
        task {
            let struct (node, sink) =
                forwardReference (fun reference -> struct (nodeHolding reference, sinkS ()))

            do! Expect.Same(node, node.Child.Parent |> sampleC)
            do! Expect.NotNull(sink)
        }

    [<Test>]
    member _.``Test Create Runs The Function Once``() =
        task {
            let mutable calls = 0

            forwardReference (fun _ ->
                calls <- calls + 1
                struct (1, 2))
            |> ignore

            do! Expect.Equal(1, calls)
        }

    [<Test>]
    member _.``Test Two Objects Can Refer To Each Other``() =
        task {
            // Neither exists when the other is constructed, which is the knot this unties.
            let struct (node, child) =
                forwardReference (fun reference -> struct (nodeHolding reference, Child reference))

            do! Expect.Same(node, child.Parent |> sampleC)
            do! Expect.Same(node, node.Child.Parent |> sampleC)
        }

    [<Test>]
    member _.``Test Works Inside An Existing Transaction``() =
        task {
            let node = runT (fun () -> forwardReferenceWithNoCaptures nodeHolding)

            do! Expect.Same(node, node.Child.Parent |> sampleC)
        }

    [<Test>]
    member _.``Test Reference Cannot Be Read During Construction``() =
        task {
            // The reference is a promise about what the value will be, not the value, so asking for
            // it before the constructing function has returned has no answer.
            do! Expect.Throws<InvalidOperationException>(fun () -> forwardReferenceWithNoCaptures sampleC |> ignore)
        }
