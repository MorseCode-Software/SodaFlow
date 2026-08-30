module SodaFlow.Bindable.ObjectModel.Tests.ObjectModelTests

open System.Collections.Generic
open NUnit.Framework
open SodaFlow
open SodaFlow.Bindable.ObjectModel

// Covers the F# surface rather than the behaviour underneath it, which the Core tests already
// pin. What is worth checking here is that each binding reaches the implementation it names and
// carries its scheduler - the module is a wall of near-identical one-liners, so a copy-and-paste
// slip between two of them would otherwise go unnoticed.

/// Records that it was asked, then behaves like the immediate scheduler.
type private RecordingScheduler() =
    let mutable posts = 0
    member __.Posts = posts

    interface IBindingScheduler with
        member __.Post action =
            posts <- posts + 1
            BindingScheduler.Immediate.Post action

[<TestFixture>]
type ``Object Model Tests``() =

    [<Test>]
    member __.``one way follows its cell``() =
        let c = sinkC 0
        use b = Bindable.oneWayWithScheduler c BindingScheduler.Immediate
        c |> sendC 7
        Assert.AreEqual(7, b.Value)

    [<Test>]
    member __.``two way pushes writes into the graph``() =
        let c = sinkC 0
        use b = Bindable.twoWayCSWithScheduler c BindingScheduler.Immediate
        b.Value <- 5
        Assert.AreEqual(5, c |> sampleC)
        Assert.AreEqual(5, b.Value)

    [<Test>]
    member __.``one way to source pushes writes into the graph``() =
        let c = sinkC 0
        use b = Bindable.oneWayToSourceCS c
        b.Value <- 3
        Assert.AreEqual(3, c |> sampleC)

    [<Test>]
    member __.``a parameterless action fires unit and ignores its parameter``() =
        let sink = sinkS<unit> ()
        let fired = List<unit>()
        use a = Bindable.toBindableActionAndScheduler sink BindingScheduler.Immediate
        use _ = a.FiringsStream |> listenStrongS fired.Add
        a.Execute "whatever the XAML author bound"
        Assert.AreEqual(1, fired.Count)

    [<Test>]
    member __.``an action with a value carries its parameter``() =
        let sink = sinkS<int> ()
        let fired = List<int>()
        use a = Bindable.toBindableActionWithValueAndScheduler sink BindingScheduler.Immediate
        use _ = a.FiringsStream |> listenStrongS fired.Add
        a.Execute 42
        CollectionAssert.AreEqual([ 42 ], fired)

    [<Test>]
    member __.``the factory carries its scheduler into a command``() =
        let scheduler = RecordingScheduler()
        let enabled = sinkC false
        let factory = BindableFactory(scheduler) :> IBindableFactory
        use a = factory.ToBindableAction(sinkS<int> (), enabled)
        enabled |> sendC true
        Assert.IsTrue(a.CanExecute null)
        Assert.AreEqual(1, scheduler.Posts)

    [<Test>]
    member __.``the factory carries its scheduler into a value``() =
        let scheduler = RecordingScheduler()
        let c = sinkC 0
        let factory = BindableFactory(scheduler) :> IBindableFactory
        use b = factory.ToOneWay c
        c |> sendC 1
        Assert.AreEqual(1, b.Value)
        Assert.AreEqual(1, scheduler.Posts)
