module SodaFlow.Tests.Cleanup

open SodaFlow
open TUnit.Core

// cleanupNow runs the action at the call, one time. The collected path is in the C# memory tests,
// which can run the sweep.
type ``Cleanup Tests``() =

    [<Test>]
    member _.``cleanupNow runs the action now``() =
        task {
            let runs = ref 0
            let cleanup = Cleanup(fun () -> runs.Value <- runs.Value + 1)

            cleanup |> Cleanup.cleanupNow

            do! Expect.Equal(1, runs.Value)
        }

    [<Test>]
    member _.``a second cleanupNow does nothing``() =
        task {
            let runs = ref 0
            let cleanup = Cleanup(fun () -> runs.Value <- runs.Value + 1)

            cleanup |> Cleanup.cleanupNow
            cleanup |> Cleanup.cleanupNow

            do! Expect.Equal(1, runs.Value)
        }
