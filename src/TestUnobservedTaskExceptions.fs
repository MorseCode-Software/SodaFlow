// The F# copy of TestUnobservedTaskExceptions.cs, linked into each F# test project. That file gives
// the cause and the method. The two must stay the same.
namespace SodaFlow.Tests.Support

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open TUnit.Core

type UnobservedTaskExceptions private () =

    static let seen = ConcurrentQueue<exn>()

    // The name of the test that runs now. The finalizer thread reads it.
    [<VolatileField>]
    static let mutable currentTest: string = null

    static do
        TaskScheduler.UnobservedTaskException.Add(fun e ->
            Console.Error.WriteLine($"Unobserved task exception after {currentTest}: {e.Exception}")
            seen.Enqueue(e.Exception))

    [<BeforeEvery(HookType.Test)>]
    static member Start(context: TestContext) =
        currentTest <- context.Metadata.DisplayName

    [<AfterEvery(HookType.Test)>]
    static member Check(context: TestContext) =
        GC.Collect()
        GC.WaitForPendingFinalizers()
        GC.Collect()

        let found = ResizeArray<exn>()
        let mutable error = Unchecked.defaultof<exn>

        while seen.TryDequeue(&error) do
            found.Add error

        if found.Count > 0 then
            raise (
                AggregateException(
                    $"{context.Metadata.DisplayName} left a faulted task that no code observed.",
                    found
                )
            )
