module SodaFlow.Async.Tests.TestUtil

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open SodaFlow.Async

/// Reads `condition` until it is true, or fails the test at a timeout.
let waitUntil (condition: unit -> bool) =
    let sw = Stopwatch.StartNew()

    while not (condition ()) do
        if sw.ElapsedMilliseconds > 5000L then
            raise (TimeoutException("Condition was not met within the timeout."))

        Thread.Sleep(10)

/// An async operation, with the input as its key, whose end a test controls with Release and
/// Fail. Thus, a test does not race the true clock. It also records the inputs that the pipeline
/// called. Thus, a test can show that an operation has the Running status, and not only an
/// admission, before the release.
type ControlledOperation<'TInput, 'TResult when 'TInput: equality>() =
    let gates = ConcurrentDictionary<'TInput, TaskCompletionSource<'TResult>>()
    let started = ConcurrentDictionary<'TInput, bool>()

    let gateFor input =
        gates.GetOrAdd(input, fun _ -> TaskCompletionSource<'TResult>())

    member _.HasStarted(input: 'TInput) = started.ContainsKey(input)

    member _.Release(input: 'TInput, result: 'TResult) =
        (gateFor input).TrySetResult(result) |> ignore

    member _.Fail(input: 'TInput, error: exn) =
        (gateFor input).TrySetException(error) |> ignore

    member _.Operation
        : 'TInput -> ResultFactory<'TResult> -> CancellationToken -> Task<MapAsyncResult<'TResult>> =
        fun input resultFactory token ->
            started[input] <- true
            let tcs = gateFor input
            token.Register(fun () -> tcs.TrySetCanceled(token) |> ignore) |> ignore
            tcs.Task.ContinueWith(fun (t: Task<'TResult>) -> resultFactory.FromValue(t.Result))
