/// Scaffolding, to be deleted along with this file once the reactive collections come over and
/// bring real tests with them.
///
/// It exists rather than the project simply starting out empty because Microsoft.Testing.Platform
/// treats a run that discovered no tests as a failure, and build.cake runs one solution-wide
/// `dotnet test` — so an empty test project would take the whole build down with it. What it
/// asserts is the only thing there is to assert yet: that this project's reference chain resolves
/// and a graph can be built and torn down through it.
module SodaFlow.Collections.Tests.ScaffoldTests

open System.Collections.Generic
open SodaFlow
open SodaFlow.Tests
open TUnit.Core

type ``Scaffold Tests``() =

    [<Test>]
    member _.``the reference chain resolves``() =
        task {
            let source = sinkS<int> ()
            let received = List<int>()
            let l = source |> listenStrongS received.Add

            source |> sendS 1
            l |> unlistenL
            source |> sendS 2

            do! Expect.Sequence([ 1 ], received)
        }
