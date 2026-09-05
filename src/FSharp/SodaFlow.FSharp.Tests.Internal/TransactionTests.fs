module SodaFlow.Tests.Internal.Transaction

open System
open System.Threading
open SodaFlow
open SodaFlow.Tests
open TUnit.Core

type ``Transaction Tests``() =

    [<Test>]
    member _.``Post See Outside``() =
        task {
            use re = new AutoResetEvent false

            let! actual =
                async {
                    use cts = new CancellationTokenSource()

                    let! a =
                        async {
                            Transaction.post (fun () ->
                                re.Set() |> ignore
                                Thread.Sleep 5000
                                cts.Token.ThrowIfCancellationRequested())
                        }
                        |> Async.StartChild

                    re.WaitOne() |> ignore
                    cts.Cancel()

                    try
                        do! a
                        return None
                    with :? OperationCanceledException as e ->
                        return Some e
                }

            do! Expect.True(Option.isSome actual)
        }
