module SodaFlow.Tests.Transaction

open System
open System.Threading
open SodaFlow
open TUnit.Core

type ``Transaction Tests``() =

    [<Test>]
    member _.``postWithFailure runs the release where the transaction fails``() =
        task {
            let mutable ran = false
            let mutable cause: exn option = None

            let sink = sinkS<int> ()

            let listener =
                sink |> listenStrongS (fun _ -> raise (InvalidOperationException "listener"))

            let thrown =
                try
                    runT (fun () ->
                        Transaction.postWithFailure (fun e -> cause <- Some e) (fun () -> ran <- true)
                        sink |> sendS 1)

                    None
                with e ->
                    Some e

            listener |> unlistenL

            do! Expect.False ran
            do! Expect.True thrown.IsSome
            do! Expect.True cause.IsSome
            do! Expect.Same(thrown.Value, cause.Value)
        }

    [<Test>]
    member _.``postWithFailure runs no release where the action completes``() =
        task {
            let mutable ran = false
            let mutable released = false

            runT (fun () ->
                Transaction.postWithFailure (fun _ -> released <- true) (fun () -> ran <- true))

            do! Expect.True ran
            do! Expect.False released
        }

    [<Test>]
    member _.Post() =
        task {
            let cell =
                runT (fun () ->
                    let s = sinkS ()
                    s |> sendS 2
                    s |> holdS 1)

            let mutable value = 0
            Transaction.post (fun () -> value <- cell |> sampleC)
            do! Expect.Equal(2, value)
        }

    [<Test>]
    member _.``Nested Post``() =
        task {
            let cell =
                runT (fun () ->
                    let s = sinkS ()
                    s |> sendS 2

                    Transaction.post (fun () ->
                        s |> sendS 3
                        Transaction.post (fun () -> s |> sendS 5))

                    Transaction.post (fun () -> s |> sendS 4)
                    s |> holdS 1)

            do! Expect.Equal(5, cell |> sampleC)
        }

    [<Test>]
    member _.``Post In Transaction``() =
        task {
            let mutable value = 0
            let mutable valueInsideTransaction = -1

            runT (fun () ->
                let s = sinkS ()
                s |> sendS 2
                let c = s |> holdS 1
                Transaction.post (fun () -> value <- c |> sampleC)
                valueInsideTransaction <- value)

            do! Expect.Equal(0, valueInsideTransaction)
            do! Expect.Equal(2, value)
        }

    [<Test>]
    member _.``Post In Nested Transaction``() =
        task {
            let mutable value = 0
            let mutable valueInsideTransaction = -1

            runT (fun () ->
                let s = sinkS ()
                s |> sendS 2

                runT (fun () ->
                    let c = s |> holdS 1
                    Transaction.post (fun () -> value <- c |> sampleC))

                valueInsideTransaction <- value)

            do! Expect.Equal(0, valueInsideTransaction)
            do! Expect.Equal(2, value)
        }

    [<Test>]
    member _.``Is Active``() =
        task {
            let isActive = runT Transaction.isActive
            do! Expect.True(isActive)
        }

    [<Test>]
    member _.``Is Not Active``() =
        task {
            let isActive = Transaction.isActive ()
            do! Expect.False(isActive)
        }

    [<Test>]
    member _.``Is Not Active Separate Thread``() =
        task {
            let mutable threadIsActive1 = None
            let mutable threadIsActive2 = None
            let mutable threadIsActive3 = None
            let mutable threadIsActive4 = None
            let mutable threadIsActive5 = None

            Thread(fun () ->
                threadIsActive1 <- Some(Transaction.isActive ())
                Thread.Sleep 500
                threadIsActive2 <- Some(Transaction.isActive ())

                runT (fun () ->
                    threadIsActive3 <- Some(Transaction.isActive ())
                    Thread.Sleep 500
                    threadIsActive4 <- Some(Transaction.isActive ()))

                threadIsActive5 <- Some(Transaction.isActive ()))
                .Start()

            Thread.Sleep 250
            let isActive1 = Transaction.isActive ()
            Thread.Sleep 500
            let isActive2 = Transaction.isActive ()
            Thread.Sleep 500
            let isActive3 = Transaction.isActive ()

            do! Expect.False(isActive1)
            do! Expect.False(isActive2)
            do! Expect.False(isActive3)

            let getAssertIsFalseValue =
                function
                | Some v -> v
                | None -> true

            let getAssertIsTrueValue =
                function
                | Some v -> v
                | None -> false

            do! Expect.False(getAssertIsFalseValue threadIsActive1)
            do! Expect.False(getAssertIsFalseValue threadIsActive2)
            do! Expect.True(getAssertIsTrueValue threadIsActive3)
            do! Expect.True(getAssertIsTrueValue threadIsActive4)
            do! Expect.False(getAssertIsFalseValue threadIsActive5)
        }
