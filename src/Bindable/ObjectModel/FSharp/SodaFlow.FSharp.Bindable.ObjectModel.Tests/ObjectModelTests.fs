module SodaFlow.Bindable.ObjectModel.Tests.ObjectModelTests

open System.Collections.Generic
open System.Windows.Input
open SodaFlow
open SodaFlow.Bindable.ObjectModel
open SodaFlow.Tests
open TUnit.Core

// Covers the F# surface rather than the behavior underneath it, which the Core tests already
// pin. What is worth checking here is that each member of the factory reaches the implementation
// it names and carries its scheduler, because the factory is the only way to build a bindable.

let private immediate =
    BindableFactory(BindingScheduler.Immediate) :> IBindableFactory

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

/// Calls the command and makes the cell false in one transaction. The send runs after that
/// transaction closes, thus it finds the cell false.
let private executeThenDisable (command: ICommand) (parameter: obj) (enabled: CellSink<bool>) =
    Transaction.run (fun () ->
        command.Execute parameter
        enabled |> sendC false)

/// Makes the cell true, and then calls the command.
let private enableThenExecute (command: ICommand) (parameter: obj) (enabled: CellSink<bool>) =
    enabled |> sendC true
    command.Execute parameter

type ``Object Model Tests``() =

    [<Test>]
    member _.``one way follows its cell``() =
        task {
            let c = sinkC 0
            use b = immediate.ToOneWay c
            c |> sendC 7
            do! Expect.Equal(7, b.Value)
        }

    [<Test>]
    member _.``two way pushes writes into the graph``() =
        task {
            let c = sinkC 0
            use b = immediate.ToTwoWay c
            b.Value <- 5
            do! Expect.Equal(5, c |> sampleC)
            do! Expect.Equal(5, b.Value)
        }

    [<Test>]
    member _.``a parameterless action fires unit and ignores its parameter``() =
        task {
            let sink = sinkS<unit> ()
            let fired = List<unit>()
            use a = immediate.ToBindableAction sink
            use _ = a.FiringsStream |> listenStrongS fired.Add
            a.Execute "whatever the XAML author bound"
            do! Expect.Equal(1, fired.Count)
        }

    [<Test>]
    member _.``an action with a value carries its parameter``() =
        task {
            let sink = sinkS<int> ()
            let fired = List<int>()

            use a = immediate.ToBindableAction sink

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

    [<Test>]
    member _.``an option action takes a value, an option, or null``() =
        task {
            let sink = sinkS<int option> ()
            let fired = List<int option>()
            use a = immediate.ToBindableOptionAction sink
            use _ = a.FiringsStream |> listenStrongS fired.Add
            a.Execute 1
            a.Execute(Some 2)
            a.Execute null
            do! Expect.Sequence([ Some 1; Some 2; None ], fired)
        }

    // Each type of command from the factory gets Execute from the core command. That Execute
    // reads the enablement cell when it sends. The three tests below show that each type sends
    // nothing when the cell is false at the send. Each type sends its parameter when the cell is
    // true.
    [<Test>]
    member _.``a parameterless action sends nothing when its cell is false at the send``() =
        task {
            let enabled = sinkC true
            let fired = List<unit>()
            use a = immediate.ToBindableAction(sinkS<unit> (), enabled)
            use _ = a.FiringsStream |> listenStrongS fired.Add
            executeThenDisable a null enabled
            enableThenExecute a null enabled
            do! Expect.Equal(1, fired.Count)
        }

    [<Test>]
    member _.``an action with a value sends nothing when its cell is false at the send``() =
        task {
            let enabled = sinkC true
            let fired = List<int>()
            use a = immediate.ToBindableAction(sinkS<int> (), enabled)
            use _ = a.FiringsStream |> listenStrongS fired.Add
            executeThenDisable a 1 enabled
            enableThenExecute a 2 enabled
            do! Expect.Sequence([ 2 ], fired)
        }

    [<Test>]
    member _.``an option action sends nothing when its cell is false at the send``() =
        task {
            let enabled = sinkC true
            let fired = List<int option>()
            use a = immediate.ToBindableOptionAction(sinkS<int option> (), enabled)
            use _ = a.FiringsStream |> listenStrongS fired.Add
            executeThenDisable a null enabled
            enableThenExecute a 2 enabled
            do! Expect.Sequence([ Some 2 ], fired)
        }

    [<Test>]
    member _.``the factory rejects a null scheduler``() =
        task {
            do!
                Expect.Throws<System.ArgumentNullException>(fun () ->
                    BindableFactory(Unchecked.defaultof<IBindingScheduler>) |> ignore)
        }
