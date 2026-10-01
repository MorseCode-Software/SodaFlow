module SodaFlow.Async.Tests.MapAsyncTests

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open SodaFlow
open SodaFlow.Async
open SodaFlow.Async.Tests.TestUtil
open SodaFlow.Tests
open TUnit.Core

// The tests below use a custom strategy that this file writes. That strategy subclasses
// AsyncConcurrencyStrategy<'TInput,'TResult,'TState> and overrides Admit, OnCompleted,
// OnCanceled, and CreateState. A previous version of this file said that F# cannot do this. That
// was incorrect, and the causes were more simple than a limit of the compiler:
//   - This project had no ProjectReference to SodaFlow.Core.Async, and had only a
//     reference through SodaFlow.FSharp.Async. The accessibility test in F# for the
//     protected-internal types AsyncQueuedItem, AsyncToStart, AsyncOutcome, and
//     AsyncStrategyResult needs that reference, and a reference through a second project is not
//     sufficient.
//   - AsyncToStart<'T>, AsyncOutcome<'T>, and AsyncStrategyResult<'T> were readonly structs. Each
//     struct has an implicit constructor with no parameter that no code can remove, and F# can
//     call it with a bare default or with Unchecked.defaultof. That made an incorrect instance,
//     with a null item in particular, and did not run the constructor that tests the arguments.
//     The three are sealed classes now, and that removes the hole. There is no path to a default
//     construction around `new`.
//   - The short AsyncConcurrencyStrategy classes of the F# module, which are not generic, used
//     the short `type X = inherit Y` class syntax. That syntax did not always make a constructor
//     that code out of the module can call. The three now use an explicit
//     `type X() = inherit Y()`.
//   - One more cause came from the move of these tests to F#, and C# has no equivalent. F#
//     refuses the
//     construction of a protected-internal type directly in an array literal, such as
//     `[| Ctor(...) |]`, as if that literal were a closure. The correction is a `let` for the
//     instance first, and only a reference to that local in the array. See Admit below, in the
//     two strategies.

/// Starts each item immediately, as parallelStrategy does. It operates on each 'TStrategyInput,
/// and records the value at the admission and how each item ended. Thus, a test can show that a
/// converter ran, and not only that it compiled.
type private AlwaysStartStrategy<'TStrategyInput>() =
    inherit AsyncConcurrencyStrategy<'TStrategyInput, EmptyState>()

    let admittedValues = ResizeArray<'TStrategyInput>()
    let completions = ResizeArray<string>()

    member _.AdmittedValues = admittedValues
    member _.Completions = completions

    override _.CreateState() = EmptyState

    override _.Admit
        (
            _state: EmptyState,
            incoming: AsyncMapBase.AsyncQueuedItem<'TStrategyInput>,
            _tracked: IReadOnlyList<AsyncMapBase.AsyncTrackedItem<'TStrategyInput>>
        ) =
        // A closure cannot read the members of the protected-internal item. Read the value into a
        // local first, and then use that local in the closure.
        let v = incoming.Value
        lock admittedValues (fun () -> admittedValues.Add(v))
        let toStart = AsyncMapBase.AsyncToStart<'TStrategyInput>(incoming)
        [| toStart |] :> IReadOnlyList<_>

    override _.OnCompleted
        (
            _state: EmptyState,
            _item: AsyncMapBase.AsyncQueuedItem<'TStrategyInput>,
            completion: AsyncMapBase.AsyncCompletion,
            _tracked: IReadOnlyList<AsyncMapBase.AsyncTrackedItem<'TStrategyInput>>
        ) =
        let mutable how = ""

        completion.MatchVoid(Action(fun () -> how <- "succeeded"), Action<exn>(fun ex -> how <- "failed:" + ex.Message))

        lock completions (fun () -> completions.Add(how))

        AsyncMapBase.AsyncStrategyResult<'TStrategyInput>(true, AsyncMapBase.AsyncStrategyResult<'TStrategyInput>.None)

    override _.OnCanceled
        (
            _state: EmptyState,
            canceled: IReadOnlyList<AsyncMapBase.AsyncQueuedItem<'TStrategyInput>>,
            _tracked: IReadOnlyList<AsyncMapBase.AsyncTrackedItem<'TStrategyInput>>
        ) =
        // A for loop and not Seq.iter: F# refuses a protected member in a lambda.
        for _ in 0 .. canceled.Count - 1 do
            lock completions (fun () -> completions.Add("canceled"))

        AsyncMapBase.AsyncStrategyResult<'TStrategyInput>.None

/// A small custom strategy that uses EmptyState directly. The input type and the result type are
/// `unit`, through the short AsyncConcurrencyStrategy of the F# module, which is not generic. Each
/// value starts immediately, as parallelStrategy does, and this strategy also counts the
/// admissions.
type private CountingStrategy() =
    inherit AsyncConcurrencyStrategy()

    let mutable count = 0

    member _.AdmittedCount = count

    override _.CreateState() = EmptyState

    override _.Admit
        (
            _state: EmptyState,
            incoming: AsyncMapBase.AsyncQueuedItem<unit>,
            _tracked: IReadOnlyList<AsyncMapBase.AsyncTrackedItem<unit>>
        ) =
        count <- count + 1
        let toStart = AsyncMapBase.AsyncToStart<unit>(incoming)
        [| toStart |] :> IReadOnlyList<_>

    override _.OnCompleted
        (
            _state: EmptyState,
            _item: AsyncMapBase.AsyncQueuedItem<unit>,
            _completion: AsyncMapBase.AsyncCompletion,
            _tracked: IReadOnlyList<AsyncMapBase.AsyncTrackedItem<unit>>
        ) =
        AsyncMapBase.AsyncStrategyResult<unit>(true, AsyncMapBase.AsyncStrategyResult<unit>.None)

    override _.OnCanceled
        (
            _state: EmptyState,
            _canceled: IReadOnlyList<AsyncMapBase.AsyncQueuedItem<unit>>,
            _tracked: IReadOnlyList<AsyncMapBase.AsyncTrackedItem<unit>>
        ) =
        AsyncMapBase.AsyncStrategyResult<unit>.None

type ``MapAsync Tests``() =

    [<Test>]
    member _.``parallelStrategy starts both immediately and publishes in completion order``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let op = ControlledOperation<string, string>()
            let received = List<string>()
            let l = results |> listenStrongS received.Add

            let status =
                source
                |> mapAsync results errors op.Operation (parallelStrategy ()) None None true

            source |> sendS "a"
            source |> sendS "b"

            waitUntil (fun () -> op.HasStarted "a" && op.HasStarted "b")

            op.Release("b", "B")
            waitUntil (fun () -> received.Count = 1)
            op.Release("a", "A")
            waitUntil (fun () -> received.Count = 2)

            do! Expect.Sequence([ "B"; "A" ], received)

            status.Dispose()
            l |> unlistenL
        }

    [<Test>]
    member _.``queueStrategy runs one at a time in FIFO order``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let op = ControlledOperation<string, string>()
            let received = List<string>()
            let l = results |> listenStrongS received.Add

            let status =
                source |> mapAsync results errors op.Operation (queueStrategy ()) None None true

            source |> sendS "a"
            source |> sendS "b"

            waitUntil (fun () -> op.HasStarted "a")
            do! Expect.False(op.HasStarted "b")

            op.Release("a", "A")
            waitUntil (fun () -> op.HasStarted "b")
            op.Release("b", "B")

            waitUntil (fun () -> received.Count = 2)
            do! Expect.Sequence([ "A"; "B" ], received)

            status.Dispose()
            l |> unlistenL
        }

    [<Test>]
    member _.``switchLatestStrategy never publishes a superseded run``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let op = ControlledOperation<string, string>()
            let received = List<string>()
            let l = results |> listenStrongS received.Add

            let status =
                source
                |> mapAsync results errors op.Operation (switchLatestStrategy ()) None None true

            source |> sendS "a"
            waitUntil (fun () -> op.HasStarted "a")
            source |> sendS "b"
            waitUntil (fun () -> op.HasStarted "b")

            op.Release("a", "A")
            op.Release("b", "B")
            waitUntil (fun () -> received.Count = 1)

            Thread.Sleep(100)
            do! Expect.Sequence([ "B" ], received)

            status.Dispose()
            l |> unlistenL
        }

    [<Test>]
    member _.``queuePerGroupStrategy lets different groups run concurrently but serializes within a group``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let op = ControlledOperation<string, string>()
            let received = List<string>()
            let l = results |> listenStrongS received.Add

            let getGroup (v: string) = v.Split('-').[0]
            let strategy = queuePerGroupStrategy getGroup

            let status =
                source
                |> mapAsyncWithInputConverter results errors op.Operation strategy id None None true

            source |> sendS "g1-a"
            source |> sendS "g1-b"
            source |> sendS "g2-a"

            waitUntil (fun () -> op.HasStarted "g1-a" && op.HasStarted "g2-a")
            do! Expect.False(op.HasStarted "g1-b")

            op.Release("g1-a", "A1")
            waitUntil (fun () -> op.HasStarted "g1-b")

            op.Release("g1-b", "B1")
            op.Release("g2-a", "A2")
            waitUntil (fun () -> received.Count = 3)

            do! Expect.SameItems([ "A1"; "B1"; "A2" ], received)

            status.Dispose()
            l |> unlistenL
        }

    [<Test>]
    member _.``queuePerGroupStrategyWithComparer uses the supplied comparer for group keys``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let op = ControlledOperation<string, string>()
            let received = List<string>()
            let l = results |> listenStrongS received.Add

            // The group comparer is not sensitive to a capital letter, thus "A-1" and "a-2" have
            // one group.
            let getGroup (v: string) = v.Split('-').[0]

            let strategy =
                queuePerGroupStrategyWithComparer StringComparer.OrdinalIgnoreCase getGroup

            let status =
                source
                |> mapAsyncWithInputConverter results errors op.Operation strategy id None None true

            source |> sendS "A-1"
            source |> sendS "a-2"

            waitUntil (fun () -> op.HasStarted "A-1")
            do! Expect.False(op.HasStarted "a-2", "a-2 shares a group with A-1 under a case-insensitive comparer.")

            op.Release("A-1", "R1")
            waitUntil (fun () -> op.HasStarted "a-2")
            op.Release("a-2", "R2")

            waitUntil (fun () -> received.Count = 2)
            do! Expect.SameItems([ "R1"; "R2" ], received)

            status.Dispose()
            l |> unlistenL
        }

    [<Test>]
    member _.``a custom strategy using EmptyState works``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<unit> ()
            let errors = sinkS<exn> ()
            let received = List<unit>()
            let l = results |> listenStrongS received.Add
            let strategy = CountingStrategy()

            let operation (_: string) (resultFactory: ResultFactory<unit>) (_: CancellationToken) =
                Task.FromResult(resultFactory.FromValue(()))

            let status = source |> mapAsync results errors operation strategy None None true

            source |> sendS "a"
            source |> sendS "b"

            waitUntil (fun () -> received.Count = 2)
            do! Expect.Equal(2, strategy.AdmittedCount)

            status.Dispose()
            l |> unlistenL
        }

    [<Test>]
    member _.``cancelAll cancels every tracked operation``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let cancelAll = sinkS<unit> ()
            let op = ControlledOperation<string, string>()
            let received = List<string>()
            let l = results |> listenStrongS received.Add

            let status =
                source
                |> mapAsync results errors op.Operation (parallelStrategy ()) (Some cancelAll) None true

            source |> sendS "a"
            source |> sendS "b"
            waitUntil (fun () -> op.HasStarted "a" && op.HasStarted "b")

            cancelAll |> sendS ()

            Thread.Sleep(200)
            do! Expect.Equal(0, received.Count, "A canceled outcome must never be published.")

            status.Dispose()
            l |> unlistenL
        }

    [<Test>]
    member _.``cancelMatching cancels only tracked operations for matching input values``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let cancelMatching = sinkS<IReadOnlyCollection<string>> ()
            let op = ControlledOperation<string, string>()
            let received = List<string>()
            let l = results |> listenStrongS received.Add

            let status =
                source
                |> mapAsync results errors op.Operation (parallelStrategy ()) None (Some cancelMatching) true

            source |> sendS "a"
            source |> sendS "b"
            waitUntil (fun () -> op.HasStarted "a" && op.HasStarted "b")

            cancelMatching |> sendS ([| "a" |] :> IReadOnlyCollection<string>)

            op.Release("b", "B")
            waitUntil (fun () -> received.Count = 1)

            Thread.Sleep(100)
            do! Expect.Sequence([ "B" ], received)

            status.Dispose()
            l |> unlistenL
        }

    [<Test>]
    member _.``cancelOnDispose true cancels an in-flight item``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let op = ControlledOperation<string, string>()
            let received = List<string>()
            let l = results |> listenStrongS received.Add

            let status =
                source
                |> mapAsync results errors op.Operation (parallelStrategy ()) None None true

            source |> sendS "a"
            waitUntil (fun () -> op.HasStarted "a")

            status.Dispose()

            Thread.Sleep(200)
            do! Expect.Equal(0, received.Count)

            l |> unlistenL
        }

    [<Test>]
    member _.``cancelOnDispose false lets an in-flight item finish and publish``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let op = ControlledOperation<string, string>()
            let received = List<string>()
            let l = results |> listenStrongS received.Add

            let status =
                source
                |> mapAsync results errors op.Operation (parallelStrategy ()) None None false

            source |> sendS "a"
            waitUntil (fun () -> op.HasStarted "a")

            status.Dispose()
            op.Release("a", "A")

            waitUntil (fun () -> received.Count = 1)
            do! Expect.Sequence([ "A" ], received)

            l |> unlistenL
        }

    [<Test>]
    member _.``a failed operation publishes to errors``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let thrown = InvalidOperationException("boom")
            let received = List<exn>()
            let l = errors |> listenStrongS received.Add

            let operation (_: string) (_: ResultFactory<string>) (_: CancellationToken) : Task<MapAsyncResult<string>> =
                Task.FromException<MapAsyncResult<string>>(thrown)

            let status =
                source |> mapAsync results errors operation (parallelStrategy ()) None None true

            source |> sendS "hello"
            waitUntil (fun () -> received.Count = 1)
            do! Expect.Same(thrown, received[0])

            status.Dispose()
            l |> unlistenL
        }

    [<Test>]
    member _.``Items and IsRunning reflect queued and running status``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let op = ControlledOperation<string, string>()

            let status =
                source |> mapAsync results errors op.Operation (queueStrategy ()) None None true

            do! Expect.False(status.IsRunning |> sampleC)
            do! Expect.Equal(0, (status.Items |> sampleC).Count)

            source |> sendS "a"
            source |> sendS "b"
            waitUntil (fun () -> op.HasStarted "a")
            waitUntil (fun () -> status.IsRunning |> sampleC)

            let items = status.Items |> sampleC
            do! Expect.Equal(2, items.Count)

            op.Release("a", "A")
            waitUntil (fun () -> op.HasStarted "b")
            op.Release("b", "B")

            waitUntil (fun () -> (status.Items |> sampleC).Count = 0)
            do! Expect.False(status.IsRunning |> sampleC)

            status.Dispose()
        }

    [<Test>]
    member _.``Execute gives the result of one value and obeys the strategy``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let op = ControlledOperation<string, string>()

            let status =
                source |> mapAsync results errors op.Operation (queueStrategy ()) None None true

            let first = status.Execute "a"
            waitUntil (fun () -> op.HasStarted "a")

            let second = status.Execute "b"
            Thread.Sleep 100

            // queueStrategy holds the second value while the first one runs.
            do! Expect.False(op.HasStarted "b")

            op.Release("a", "A")
            waitUntil (fun () -> op.HasStarted "b")
            op.Release("b", "B")

            let! firstResult = first
            let! secondResult = second

            do! Expect.Equal("A", firstResult)
            do! Expect.Equal("B", secondResult)

            status.Dispose()
        }

    [<Test>]
    member _.``Execute cancels its task when a cancellation stops the value``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let cancelAll = sinkS<unit> ()
            let op = ControlledOperation<string, string>()

            let status =
                source
                |> mapAsync results errors op.Operation (parallelStrategy ()) (Some cancelAll) None true

            let task = status.Execute "a"
            waitUntil (fun () -> op.HasStarted "a")

            cancelAll |> sendS ()
            waitUntil (fun () -> task.IsCompleted)

            do! Expect.True(task.IsCanceled)

            status.Dispose()
        }

    [<Test>]
    member _.``Execute reads a cell in the transaction of the send``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let input = sinkC "a"
            let op = ControlledOperation<string, string>()

            let status =
                source
                |> mapAsync results errors op.Operation (parallelStrategy ()) None None true

            // The call that the documentation gives for F#, with no parentheses.
            let task = status.Execute input

            waitUntil (fun () -> op.HasStarted "a")

            // A new value of the cell does not go to the item that runs.
            input |> sendC "b"
            Thread.Sleep 100
            do! Expect.False(op.HasStarted "b")

            op.Release("a", "A")
            let! result = task
            do! Expect.Equal("A", result)

            status.Dispose()
        }

    [<Test>]
    member _.``Execute with an option cell gives Some with the result where the cell holds Some``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let input = sinkC (Some "a")
            let op = ControlledOperation<string, string>()

            let status =
                source
                |> mapAsync results errors op.Operation (parallelStrategy ()) None None true

            let task = status.Execute input

            waitUntil (fun () -> op.HasStarted "a")
            op.Release("a", "A")

            let! result = task
            do! Expect.Equal(Some "A", result)

            status.Dispose()
        }

    [<Test>]
    member _.``Execute with an option cell gives None and admits nothing where the cell holds None``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let input = sinkC (None: string option)
            let started = ConcurrentQueue<string>()

            let operation (v: string) (factory: ResultFactory<string>) (_: CancellationToken) =
                started.Enqueue v
                Task.FromResult(factory.FromValue(v.ToUpperInvariant()))

            let status =
                source |> mapAsync results errors operation (parallelStrategy ()) None None true

            let task = status.Execute input
            waitUntil (fun () -> task.IsCompleted)

            do! Expect.Equal(TaskStatus.RanToCompletion, task.Status)
            do! Expect.Equal(None, task.Result)
            do! Expect.True(started.IsEmpty, "A cell that holds None puts no value into the pipeline.")
            do! Expect.Equal(0, (status.Items |> sampleC).Count)

            status.Dispose()
        }

    [<Test>]
    member _.``Execute with an option cell reads the cell in the transaction of the send``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let input = sinkC (None: string option)
            let op = ControlledOperation<string, string>()

            let status =
                source
                |> mapAsync results errors op.Operation (parallelStrategy ()) None None true

            // A sample in this transaction gives None. The call defers, thus the read is in a new
            // transaction, where the cell holds Some.
            let task =
                runT (fun () ->
                    input |> sendC (Some "b")
                    status.Execute input)

            waitUntil (fun () -> op.HasStarted "b")
            op.Release("b", "B")

            let! result = task
            do! Expect.Equal(Some "B", result, "The read is in the transaction of the send.")

            status.Dispose()
        }

    [<Test>]
    member _.``Execute with an option cell tests for None in the transaction of the send``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let input = sinkC (Some "a")
            let op = ControlledOperation<string, string>()

            let status =
                source
                |> mapAsync results errors op.Operation (parallelStrategy ()) None None true

            // A sample in this transaction gives Some, but the cell holds None in the transaction of
            // the send. Thus, a test at the call admits "a", and a test at the send admits nothing.
            let task =
                runT (fun () ->
                    input |> sendC None
                    status.Execute input)

            // A wait with a time limit, because a test at the call admits "a", and no code releases
            // it. A bare await then waits permanently and does not fail.
            waitUntil (fun () -> task.IsCompleted)

            let! result = task
            do! Expect.Equal(None, result)
            do! Expect.False(op.HasStarted "a", "The test for None is in the transaction of the send.")

            status.Dispose()
        }

    [<Test>]
    member _.``Execute with an option cell is canceled and does not give None by a cancellation``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let cancelAll = sinkS<unit> ()
            let input = sinkC (Some "a")
            let op = ControlledOperation<string, string>()

            let status =
                source
                |> mapAsync results errors op.Operation (parallelStrategy ()) (Some cancelAll) None true

            let task = status.Execute input
            waitUntil (fun () -> op.HasStarted "a")

            cancelAll |> sendS ()
            waitUntil (fun () -> task.IsCompleted)

            do! Expect.True(task.IsCanceled)

            status.Dispose()
        }

    [<Test>]
    member _.``Execute with an option cell carries the exception of the operation``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let input = sinkC (Some "a")
            let op = ControlledOperation<string, string>()
            let thrown = InvalidOperationException "boom"

            let status =
                source
                |> mapAsync results errors op.Operation (parallelStrategy ()) None None true

            let task = status.Execute input
            waitUntil (fun () -> op.HasStarted "a")

            op.Fail("a", thrown)
            waitUntil (fun () -> task.IsCompleted)

            // ControlledOperation reads t.Result in a continuation, thus the operation itself throws
            // an AggregateException that contains `thrown`. The pipeline gives that exception
            // unchanged, and GetBaseException finds `thrown` in it.
            do! Expect.True(task.IsFaulted)
            do! Expect.Same(thrown, task.Exception.GetBaseException())

            status.Dispose()
        }

    [<Test>]
    member _.``Execute with an option cell throws for a null cell``() =
        task {
            let source = sinkS<string> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()
            let op = ControlledOperation<string, string>()

            let status =
                source
                |> mapAsync results errors op.Operation (parallelStrategy ()) None None true

            do!
                Expect.Throws<ArgumentNullException>(
                    (fun () -> status.Execute(null: Cell<string option>) |> ignore),
                    "A null cell has no value to read."
                )

            status.Dispose()
        }

    [<Test>]
    member _.``Execute where the input type is an option takes a nested option cell``() =
        task {
            let source = sinkS<string option> ()
            let results = sinkS<string> ()
            let errors = sinkS<exn> ()

            let operation (v: string option) (factory: ResultFactory<string>) (_: CancellationToken) =
                Task.FromResult(factory.FromValue(defaultArg v "none"))

            let status =
                source |> mapAsync results errors operation (parallelStrategy ()) None None true

            // A Cell<'TInput> goes to the intrinsic overload, which admits None as a value.
            let plain = status.Execute(sinkC (None: string option))

            // A Cell<'TInput option> goes to the overload for an option. Some None admits the value
            // None, and None admits nothing.
            let someNone = status.Execute(sinkC (Some(None: string option)))
            let none = status.Execute(sinkC (None: string option option))

            waitUntil (fun () -> plain.IsCompleted && someNone.IsCompleted && none.IsCompleted)

            do! Expect.Equal("none", plain.Result)
            do! Expect.Equal(Some "none", someNone.Result)
            do! Expect.Equal(None, none.Result)

            status.Dispose()
        }
