module SodaFlow.Tests.Transaction

open NUnit.Framework
open SodaFlow
open System.Threading

[<TestFixture>]
type ``Transaction Tests``() =

    [<Test>]
    member _.Post() =
        let cell =
            runT (fun () ->
                let s = sinkS ()
                s |> sendS 2
                s |> holdS 1)

        let mutable value = 0
        Transaction.post (fun () -> value <- cell |> sampleC)
        Assert.AreEqual(2, value)

    [<Test>]
    member _.``Nested Post``() =
        let cell =
            runT (fun () ->
                let s = sinkS ()
                s |> sendS 2

                Transaction.post (fun () ->
                    s |> sendS 3
                    Transaction.post (fun () -> s |> sendS 5))

                Transaction.post (fun () -> s |> sendS 4)
                s |> holdS 1)

        Assert.AreEqual(5, cell |> sampleC)

    [<Test>]
    member _.``Post In Transaction``() =
        let mutable value = 0

        runT (fun () ->
            let s = sinkS ()
            s |> sendS 2
            let c = s |> holdS 1
            Transaction.post (fun () -> value <- c |> sampleC)
            Assert.AreEqual(0, value))

        Assert.AreEqual(2, value)

    [<Test>]
    member _.``Post In Nested Transaction``() =
        let mutable value = 0

        runT (fun () ->
            let s = sinkS ()
            s |> sendS 2

            runT (fun () ->
                let c = s |> holdS 1
                Transaction.post (fun () -> value <- c |> sampleC))

            Assert.AreEqual(0, value))

        Assert.AreEqual(2, value)

    [<Test>]
    member _.``Is Active``() =
        let isActive = runT Transaction.isActive
        Assert.IsTrue isActive

    [<Test>]
    member _.``Is Not Active``() =
        let isActive = Transaction.isActive ()
        Assert.IsFalse isActive

    [<Test>]
    member _.``Is Not Active Separate Thread``() =
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

        Assert.IsFalse isActive1
        Assert.IsFalse isActive2
        Assert.IsFalse isActive3

        let getAssertIsFalseValue =
            function
            | Some v -> v
            | None -> true

        let getAssertIsTrueValue =
            function
            | Some v -> v
            | None -> false

        Assert.IsFalse(getAssertIsFalseValue threadIsActive1)
        Assert.IsFalse(getAssertIsFalseValue threadIsActive2)
        Assert.IsTrue(getAssertIsTrueValue threadIsActive3)
        Assert.IsTrue(getAssertIsTrueValue threadIsActive4)
        Assert.IsFalse(getAssertIsFalseValue threadIsActive5)
