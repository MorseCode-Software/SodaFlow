module SodaFlow.Bindable.ObjectModel.Tests.ObjectModelTests

open System.Collections.Generic
open SodaFlow
open SodaFlow.Bindable.ObjectModel
open SodaFlow.Tests
open TUnit.Core

// Covers the F# surface rather than the behavior underneath it, which the Core tests already
// pin. What is worth checking here is that each binding reaches the implementation it names and
// carries its scheduler - the module is a wall of near-identical one-liners, so a copy-and-paste
// slip between two of them would otherwise go unnoticed.

/// Records that it was asked, then behaves like the immediate scheduler.
type private RecordingScheduler() =
    let mutable posts = 0
    member _.Posts = posts

    interface IBindingScheduler with
        member _.Post action =
            posts <- posts + 1
            BindingScheduler.Immediate.Post action

        /// True throughout, as for the immediate scheduler this delegates to: it runs work on
        /// whichever thread hands it over, so every thread is its binding thread.
        member _.CheckAccess() = true

type ``Object Model Tests``() =

    [<Test>]
    member _.``one way follows its cell``() =
        task {
            let c = sinkC 0
            use b = Bindable.oneWayWithScheduler c BindingScheduler.Immediate
            c |> sendC 7
            do! Expect.Equal(7, b.Value)
        }

    [<Test>]
    member _.``two way pushes writes into the graph``() =
        task {
            let c = sinkC 0
            use b = Bindable.twoWayCSWithScheduler c BindingScheduler.Immediate
            b.Value <- 5
            do! Expect.Equal(5, c |> sampleC)
            do! Expect.Equal(5, b.Value)
        }

    [<Test>]
    member _.``one way to source pushes writes into the graph``() =
        task {
            let c = sinkC 0
            use b = Bindable.oneWayToSourceCS c
            b.Value <- 3
            do! Expect.Equal(3, c |> sampleC)
        }

    [<Test>]
    member _.``a parameterless action fires unit and ignores its parameter``() =
        task {
            let sink = sinkS<unit> ()
            let fired = List<unit>()
            use a = Bindable.toBindableActionAndScheduler sink BindingScheduler.Immediate
            use _ = a.FiringsStream |> listenStrongS fired.Add
            a.Execute "whatever the XAML author bound"
            do! Expect.Equal(1, fired.Count)
        }

    [<Test>]
    member _.``an action with a value carries its parameter``() =
        task {
            let sink = sinkS<int> ()
            let fired = List<int>()

            use a =
                Bindable.toBindableActionWithValueAndScheduler sink BindingScheduler.Immediate

            use _ = a.FiringsStream |> listenStrongS fired.Add
            a.Execute 42
            do! Expect.Sequence([ 42 ], fired)
        }

    [<Test>]
    member _.``the factory carries its scheduler into a command``() =
        task {
            let scheduler = RecordingScheduler()
            let enabled = sinkC false
            let factory = BindableFactory(scheduler) :> IBindableFactory
            use a = factory.ToBindableAction(sinkS<int> (), enabled)
            enabled |> sendC true
            do! Expect.True(a.CanExecute null)
            do! Expect.Equal(1, scheduler.Posts)
        }

    [<Test>]
    member _.``the factory carries its scheduler into a value``() =
        task {
            let scheduler = RecordingScheduler()
            let c = sinkC 0
            let factory = BindableFactory(scheduler) :> IBindableFactory
            use b = factory.ToOneWay c
            c |> sendC 1
            do! Expect.Equal(1, b.Value)
            do! Expect.Equal(1, scheduler.Posts)
        }
