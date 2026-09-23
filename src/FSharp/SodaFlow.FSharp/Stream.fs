/// <summary>
///     Creating streams and combining them.
/// </summary>
/// <remarks>
///     A stream is a sequence of discrete firings, each one belonging to the transaction it happened
///     in. A stream fires one time or no times in each transaction. Where two firings occur in the
///     same transaction, the combinator that causes this gives the rule for the two firings.
///
///     Build the graph in <c>Transaction.run</c> so the graph keeps the first firing. Everything
///     here takes the stream last so it composes with <c>|&gt;</c>.
/// </remarks>
module SodaFlow.Stream

open System
open System.Threading.Tasks
open System.Collections.Generic
open System.Runtime.CompilerServices

/// <summary>
///     Creates a stream which never fires.
/// </summary>
/// <typeparam name="'a">The type that the stream fires, if it fires.</typeparam>
/// <returns>A stream that never fires.</returns>
/// <remarks>
///     The identity for <c>orElse</c>, and what to return from a branch which has nothing to fire.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let never<'a> () = StreamInternal.NeverImpl<'a>()

/// <summary>
///     Builds a stream which refers to itself, closing the loop in one transaction.
/// </summary>
/// <param name="f">
///     Given the forward reference, returns a struct tuple of the stream it stands for and
///     anything else the caller wants back out.
/// </param>
/// <returns>
///     A struct tuple of the stream that closed the forward reference, and whatever
///     <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
///     A stream that refers to itself needs a forward reference. The code makes the reference
///     before the stream that it refers to. The reference and its resolution must occur in one
///     transaction, which this opens when no transaction is open.
///
///     Use <c>loopWithNoCaptures</c> where the caller needs only the stream.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let loop f =
    TransactionInternal.Apply(fun transaction _ ->
        let l = LoopedStream()
        let struct (s, r) = f l
        l.Loop(transaction, s)
        struct (s, r))

/// <summary>
///     Builds a self-referential stream where the caller needs only the stream.
/// </summary>
/// <param name="f">Given the forward reference, returns the stream it stands for.</param>
/// <returns>The stream that closed the forward reference.</returns>
/// <remarks>
///     <c>loop</c> where the caller needs more than the stream from the loop.
/// </remarks>
let loopWithNoCaptures f =
    let struct (l, _) = loop (fun s -> struct (f s, ()))
    l

/// <summary>
///     Listens for firings without keeping the stream alive.
/// </summary>
/// <param name="handler">Run with each fired value.</param>
/// <param name="stream">The stream to listen to.</param>
/// <returns>A weak listener. <c>WeakListener.unlisten</c> stops it.</returns>
/// <remarks>
///     The listener stops when a GC collects the stream, thus this is the correct selection where there is no
///     clear moment to stop the listener. Keep the handle from this call in a field of the object doing the
///     listening, and the two go away together. Where other code must keep the stream kept alive for as long
///     as something is listening, use <c>listenStrong</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let listen handler (stream: Stream<_>) = stream.ListenImpl(Action<_> handler)

/// <summary>
///     Listens for firings, keeping the stream alive while the listener lives.
/// </summary>
/// <param name="handler">Run with each fired value.</param>
/// <param name="stream">The stream to listen to.</param>
/// <returns>
///     A strong listener. <c>StrongListener.unlisten</c> stops it, and a disposal also stops it.
/// </returns>
/// <remarks>
///     The listener roots the stream, so the graph behind it stays alive for as long as the
///     returned handle is reachable. Keep the handle and stop it when finished, or use
///     <c>listen</c> where there is no good moment to do that.
///
///     The handler runs with the transaction lock held, thus it must return quickly. Give work
///     that takes a long time, and work that blocks, to a different thread.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let listenStrong handler (stream: Stream<_>) =
    stream.ListenStrongImpl(Action<_> handler)

/// <summary>
///     Listens for the next firing only, then stops, without keeping the stream alive.
/// </summary>
/// <param name="handler">Run with the first fired value.</param>
/// <param name="stream">The stream to listen to.</param>
/// <returns>A weak listener. <c>WeakListener.unlisten</c> stops it before that first firing.</returns>
/// <remarks>
///     The handle from this call is the only thing that keeps the handler alive, thus hold it until
///     that first firing arrives. A call that discards the handle compiles, and the handler then runs
///     or does not run according to when a GC collects it. Use <c>listenOnceStrong</c> where the
///     caller does not keep the handle.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let listenOnce handler (stream: Stream<_>) =
    stream.ListenOnceImpl(Action<_> handler)

/// <summary>
///     Listens for the next firing only, then stops, and keeps the stream alive until that firing.
/// </summary>
/// <param name="handler">Run with the first fired value.</param>
/// <param name="stream">The stream to listen to.</param>
/// <returns>
///     A strong listener. <c>StrongListener.unlisten</c> stops it before that first firing, and a
///     disposal also stops it.
/// </returns>
/// <remarks>
///     The listener roots the stream until that first firing, thus the handler runs when the caller
///     discards the handle. The root ends with that firing, or with an earlier stop. Use
///     <c>listenOnce</c> where the listener must not extend the lifetime of what it observes.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let listenOnceStrong handler (stream: Stream<_>) =
    stream.ListenOnceStrongImpl(Action<_> handler)

/// <summary>
///     Waits asynchronously for the next firing.
/// </summary>
/// <param name="stream">The stream to wait on.</param>
/// <returns>An async which produces the next value the stream fires.</returns>
/// <remarks>
///     The listener attaches immediately, before the async runs, so a firing between this call
///     and the await is not missed.
///
///     A cancellation of the async stops the listener and cancels the result. A thread that is not
///     the thread that fired the value gives the value. Thus, an await of this does not run a
///     continuation with the transaction lock.
/// </remarks>
let listenOnceAsync stream =
#if NETSTANDARD2_0_OR_GREATER || NET461_OR_GREATER || NET
    let tcs = TaskCompletionSource<_> TaskCreationOptions.RunContinuationsAsynchronously
#else
    let tcs = TaskCompletionSource<_>()
#endif
    let mutable listenerOption = None
    let mutable unlistenEarly = false

    let listener =
        stream
        |> listenStrong (fun a ->
            match listenerOption with
            | None -> unlistenEarly <- true
            | Some listener -> listener |> Listener.unlisten

            tcs.TrySetResult(a) |> ignore)

    listenerOption <- Some listener

    if unlistenEarly then
        listener |> Listener.unlisten

    async {
        let! ct = Async.CancellationToken

        ct.Register(fun () ->
            Listener.unlisten listener
            tcs.TrySetCanceled() |> ignore)
        |> ignore
#if NETSTANDARD2_0_OR_GREATER || NET461_OR_GREATER || NET
        return! Async.AwaitTask tcs.Task
#else
        let execute (tcs: TaskCompletionSource<_>) =
            async {
                let! result = Async.AwaitTask tcs.Task
                do! Utilities.Yield() |> Async.AwaitTask
                return result
            }

        return! execute tcs
#endif
    }

/// <summary>
///     Transforms each fired value with a function.
/// </summary>
/// <param name="f">Transforms the fired value.</param>
/// <param name="stream">The stream to transform.</param>
/// <returns>A stream firing <paramref name="f" /> applied to each value the input fires.</returns>
/// <remarks>
///     <paramref name="f" /> can make FRP logic, and it can sample a behavior and a cell. In each
///     other operation it must be pure.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let map f (stream: Stream<_>) = stream.MapImpl(Func<_, _> f)

/// <summary>
///     Replaces each fired value with a constant.
/// </summary>
/// <param name="value">The replacement value to fire.</param>
/// <param name="stream">The stream to transform.</param>
/// <returns>A stream that fires <paramref name="value" /> at each firing of the input.</returns>
/// <remarks>
///     For when only the fact that something happened matters, not what it carried.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let mapTo value (stream: Stream<_>) = stream.MapToImpl value

/// <summary>
///     Holds the most recently fired value in a cell.
/// </summary>
/// <param name="initialValue">The value the cell holds until the stream first fires.</param>
/// <param name="stream">The stream to hold.</param>
/// <returns>A cell holding the last value fired, or <paramref name="initialValue" /> before any.</returns>
/// <remarks>
///     A sample after the transaction of the firing gives the new value of the cell, and a sample
///     in that transaction does not. That interval is what makes a loop through a cell correct,
///     and not circular.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let hold initialValue (stream: Stream<_>) = stream.HoldImpl initialValue

/// <summary>
///     Holds the most recently fired value in a cell, with an initial value computed on first use.
/// </summary>
/// <param name="initialValue">The lazy value the cell holds until the stream first fires.</param>
/// <param name="stream">The stream to hold.</param>
/// <returns>A cell holding the last value fired, or <paramref name="initialValue" /> before any.</returns>
/// <remarks>
///     This is the version that closes a loop. In <c>Cell.loop</c> the initial value comes from the
///     same cell, thus no code can force it now. <c>Cell.sampleLazy</c> gives what this takes.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let holdLazy initialValue (stream: Stream<_>) = stream.HoldLazyImpl initialValue

/// <summary>
///     Samples a behavior when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <param name="f">Combines the fired value with the sampled value.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the sampled
///     value.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshotB (behavior: Behavior<_>) f (stream: Stream<_>) =
    stream.SnapshotImpl(behavior, (Func<_, _, _> f))

/// <summary>
///     Samples a cell when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <param name="f">Combines the fired value with the sampled value.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the sampled
///     value.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshot (cell: Cell<_>) f (stream: Stream<_>) =
    stream.SnapshotImpl(cell, (Func<_, _, _> f))

/// <summary>
///     Samples a behavior when the stream fires, and fires the behavior's value, discarding the
///     stream's own.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>A stream firing the behavior's value at each firing of the input.</returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshotAndTakeB (behavior: Behavior<_>) (stream: Stream<_>) = stream.SnapshotImpl behavior

/// <summary>
///     Samples a cell when the stream fires, and fires the cell's value, discarding the stream's own.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>A stream firing the cell's value at each firing of the input.</returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshotAndTake (cell: Cell<_>) (stream: Stream<_>) = stream.SnapshotImpl cell

/// <summary>
///     Samples two behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="f">Combines the fired value with the two sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the two sampled
///     values.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshot2B (behavior1: Behavior<_>) behavior2 f (stream: Stream<_>) =
    stream.SnapshotImpl(behavior1, behavior2, (Func<_, _, _, _> f))

/// <summary>
///     Samples two cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="f">Combines the fired value with the two sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the two sampled
///     values.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshot2 (cell1: Cell<_>) cell2 f (stream: Stream<_>) =
    stream.SnapshotImpl(cell1, cell2, (Func<_, _, _, _> f))

/// <summary>
///     Samples three behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="behavior3">The third behavior to sample.</param>
/// <param name="f">Combines the fired value with the three sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the three sampled
///     values.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshot3B (behavior1: Behavior<_>) behavior2 behavior3 f (stream: Stream<_>) =
    stream.SnapshotImpl(behavior1, behavior2, behavior3, (Func<_, _, _, _, _> f))

/// <summary>
///     Samples three cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="cell3">The third cell to sample.</param>
/// <param name="f">Combines the fired value with the three sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the three sampled
///     values.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshot3 (cell1: Cell<_>) cell2 cell3 f (stream: Stream<_>) =
    stream.SnapshotImpl(cell1, cell2, cell3, (Func<_, _, _, _, _> f))

/// <summary>
///     Samples four behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="behavior3">The third behavior to sample.</param>
/// <param name="behavior4">The fourth behavior to sample.</param>
/// <param name="f">Combines the fired value with the four sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the four sampled
///     values.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshot4B (behavior1: Behavior<_>) behavior2 behavior3 behavior4 f (stream: Stream<_>) =
    stream.SnapshotImpl(behavior1, behavior2, behavior3, behavior4, (Func<_, _, _, _, _, _> f))

/// <summary>
///     Samples four cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="cell3">The third cell to sample.</param>
/// <param name="cell4">The fourth cell to sample.</param>
/// <param name="f">Combines the fired value with the four sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the four sampled
///     values.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshot4 (cell1: Cell<_>) cell2 cell3 cell4 f (stream: Stream<_>) =
    stream.SnapshotImpl(cell1, cell2, cell3, cell4, (Func<_, _, _, _, _, _> f))

/// <summary>
///     Samples five behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="behavior3">The third behavior to sample.</param>
/// <param name="behavior4">The fourth behavior to sample.</param>
/// <param name="behavior5">The fifth behavior to sample.</param>
/// <param name="f">Combines the fired value with the five sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the five sampled
///     values.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
let snapshot5B behavior1 behavior2 behavior3 behavior4 behavior5 f stream =
    stream
    |> snapshot4B behavior1 behavior2 behavior3 behavior4 tuple5S
    |> snapshotB behavior5 (fun struct (a, b, c, d, e) -> f a b c d e)

/// <summary>
///     Samples five cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="cell3">The third cell to sample.</param>
/// <param name="cell4">The fourth cell to sample.</param>
/// <param name="cell5">The fifth cell to sample.</param>
/// <param name="f">Combines the fired value with the five sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the five sampled
///     values.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
let snapshot5 cell1 cell2 cell3 cell4 cell5 f stream =
    stream
    |> snapshot5B
        (cell1 |> Cell.asBehavior)
        (cell2 |> Cell.asBehavior)
        (cell3 |> Cell.asBehavior)
        (cell4 |> Cell.asBehavior)
        (cell5 |> Cell.asBehavior)
        f

/// <summary>
///     Samples six behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="behavior3">The third behavior to sample.</param>
/// <param name="behavior4">The fourth behavior to sample.</param>
/// <param name="behavior5">The fifth behavior to sample.</param>
/// <param name="behavior6">The sixth behavior to sample.</param>
/// <param name="f">Combines the fired value with the six sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the six sampled
///     values.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
let snapshot6B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 f stream =
    stream
    |> snapshot4B behavior1 behavior2 behavior3 behavior4 tuple5S
    |> snapshot2B behavior5 behavior6 (fun struct (a, b, c, d, e) -> f a b c d e)

/// <summary>
///     Samples six cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="cell3">The third cell to sample.</param>
/// <param name="cell4">The fourth cell to sample.</param>
/// <param name="cell5">The fifth cell to sample.</param>
/// <param name="cell6">The sixth cell to sample.</param>
/// <param name="f">Combines the fired value with the six sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the six sampled
///     values.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
let snapshot6 cell1 cell2 cell3 cell4 cell5 cell6 f stream =
    stream
    |> snapshot6B
        (cell1 |> Cell.asBehavior)
        (cell2 |> Cell.asBehavior)
        (cell3 |> Cell.asBehavior)
        (cell4 |> Cell.asBehavior)
        (cell5 |> Cell.asBehavior)
        (cell6 |> Cell.asBehavior)
        f

/// <summary>
///     Samples seven behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="behavior3">The third behavior to sample.</param>
/// <param name="behavior4">The fourth behavior to sample.</param>
/// <param name="behavior5">The fifth behavior to sample.</param>
/// <param name="behavior6">The sixth behavior to sample.</param>
/// <param name="behavior7">The seventh behavior to sample.</param>
/// <param name="f">Combines the fired value with the seven sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the seven sampled
///     values.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
let snapshot7B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 behavior7 f stream =
    stream
    |> snapshot4B behavior1 behavior2 behavior3 behavior4 tuple5S
    |> snapshot3B behavior5 behavior6 behavior7 (fun struct (a, b, c, d, e) -> f a b c d e)

/// <summary>
///     Samples seven cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="cell3">The third cell to sample.</param>
/// <param name="cell4">The fourth cell to sample.</param>
/// <param name="cell5">The fifth cell to sample.</param>
/// <param name="cell6">The sixth cell to sample.</param>
/// <param name="cell7">The seventh cell to sample.</param>
/// <param name="f">Combines the fired value with the seven sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the seven sampled
///     values.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
let snapshot7 cell1 cell2 cell3 cell4 cell5 cell6 cell7 f stream =
    stream
    |> snapshot7B
        (cell1 |> Cell.asBehavior)
        (cell2 |> Cell.asBehavior)
        (cell3 |> Cell.asBehavior)
        (cell4 |> Cell.asBehavior)
        (cell5 |> Cell.asBehavior)
        (cell6 |> Cell.asBehavior)
        (cell7 |> Cell.asBehavior)
        f

/// <summary>
///     Samples eight behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="behavior3">The third behavior to sample.</param>
/// <param name="behavior4">The fourth behavior to sample.</param>
/// <param name="behavior5">The fifth behavior to sample.</param>
/// <param name="behavior6">The sixth behavior to sample.</param>
/// <param name="behavior7">The seventh behavior to sample.</param>
/// <param name="behavior8">The eighth behavior to sample.</param>
/// <param name="f">Combines the fired value with the eight sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the eight sampled
///     values.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
let snapshot8B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 behavior7 behavior8 f stream =
    stream
    |> snapshot4B behavior1 behavior2 behavior3 behavior4 tuple5S
    |> snapshot4B behavior5 behavior6 behavior7 behavior8 (fun struct (a, b, c, d, e) -> f a b c d e)

/// <summary>
///     Samples eight cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="cell3">The third cell to sample.</param>
/// <param name="cell4">The fourth cell to sample.</param>
/// <param name="cell5">The fifth cell to sample.</param>
/// <param name="cell6">The sixth cell to sample.</param>
/// <param name="cell7">The seventh cell to sample.</param>
/// <param name="cell8">The eighth cell to sample.</param>
/// <param name="f">Combines the fired value with the eight sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the eight sampled
///     values.
/// </returns>
/// <remarks>
///     This samples and does not merge. Only the stream causes the firing, and each sampled value is
///     the value at the start of the transaction of that firing. Thus, a cell that the same
///     transaction updates gives its previous value. The result does not change when the graph
///     operates in a different sequence.
/// </remarks>
let snapshot8 cell1 cell2 cell3 cell4 cell5 cell6 cell7 cell8 f stream =
    stream
    |> snapshot8B
        (cell1 |> Cell.asBehavior)
        (cell2 |> Cell.asBehavior)
        (cell3 |> Cell.asBehavior)
        (cell4 |> Cell.asBehavior)
        (cell5 |> Cell.asBehavior)
        (cell6 |> Cell.asBehavior)
        (cell7 |> Cell.asBehavior)
        (cell8 |> Cell.asBehavior)
        f

/// <summary>
///     Merges two streams, combining the values where the two fire in one transaction.
/// </summary>
/// <param name="f">
///     Combines two simultaneous values. The value from the first stream is the left
///     argument and the value from the second is the right.
/// </param>
/// <param name="stream">The first stream.</param>
/// <param name="stream2">The second stream.</param>
/// <returns>A stream that fires at each firing of an input, one time or no times in a transaction.</returns>
/// <remarks>
///     A stream fires one time or no times in each transaction, thus the result must give one value
///     for two firings at the same time. <paramref name="f" /> gives that value. Use <c>orElse</c>
///     to use the first value, and not the result of <paramref name="f" />.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let merge f (stream: Stream<_>, stream2) =
    stream.MergeImpl(stream2, (Func<_, _, _> f))

/// <summary>
///     Merges two streams, preferring the first where the two fire in one transaction.
/// </summary>
/// <param name="stream">The stream to prefer.</param>
/// <param name="stream2">The alternative stream.</param>
/// <returns>A stream that fires at each firing of an input, one time or no times in a transaction.</returns>
/// <remarks>
///     <c>merge</c> with a function that keeps the left value and drops the right. The dropped
///     value is gone, not deferred - use <c>merge</c> where the two are necessary.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let orElse (stream: Stream<_>, stream2) = stream.OrElseImpl stream2

/// <summary>
///     Keeps only the firings whose value satisfies a predicate.
/// </summary>
/// <param name="predicate">Gives true when the result keeps the value.</param>
/// <param name="stream">The stream to filter.</param>
/// <returns>A stream firing only the values <paramref name="predicate" /> accepted.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let filter predicate (stream: Stream<_>) = stream.FilterImpl(Func<_, _> predicate)

/// <summary>
///     Keeps only the firings which carried <c>Some</c>, and unwraps them.
/// </summary>
/// <param name="stream">The stream of options to filter.</param>
/// <returns>A stream firing the value in each <c>Some</c>, and not firing for <c>None</c>.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let filterSome (stream: Stream<_>) =
    StreamExtensionMethodsInternal.FilterSomeImpl(stream, Action<_, _>(fun o a -> o |> Option.iter a.Invoke))

/// <summary>
///     Transforms the firings with a function that can give no value, and fires only the values
///     it produced.
/// </summary>
/// <param name="f">Runs on each fired value. The result drops the firings that give <c>None</c>.</param>
/// <param name="stream">The stream to transform.</param>
/// <returns>
///     A stream firing the value in each <c>Some</c> that <paramref name="f" /> returned, and not
///     firing for the values it returned <c>None</c> for.
/// </returns>
/// <remarks>
///     This maps and filters in one step. Use it for the usual condition where the test of a firing
///     is the same work as the value that the result fires. This is the same as
///     <c>map f >> filterSome</c>, and the equivalent of <c>List.choose</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let choose f (stream: Stream<_>) =
    StreamExtensionMethodsInternal.FilterSomeImpl(
        stream.MapImpl(Func<_, _> f),
        Action<_, _>(fun o a -> o |> Option.iter a.Invoke)
    )

/// <summary>
///     Lets firings through only while a behavior holds true.
/// </summary>
/// <param name="behavior">The behavior that gives true for the firings to keep.</param>
/// <param name="stream">The stream to gate.</param>
/// <returns>A stream firing only when the behavior held true at the time of the firing.</returns>
/// <remarks>
///     This samples the behavior as <c>snapshotB</c> samples it: the value read is the one held
///     at the start of the transaction the firing belongs to.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let gateB (behavior: Behavior<_>) (stream: Stream<_>) = stream.GateImpl behavior

/// <summary>
///     Lets firings through only while a cell holds true.
/// </summary>
/// <param name="cell">The cell that gives true for the firings to keep.</param>
/// <param name="stream">The stream to gate.</param>
/// <returns>A stream firing only when the cell held true at the time of the firing.</returns>
/// <remarks>
///     This samples the cell as <c>snapshot</c> samples it: the value read is the one held at
///     the start of the transaction the firing belongs to.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let gate (cell: Cell<_>) (stream: Stream<_>) = stream.GateImpl cell

/// <summary>
///     Folds state across firings, firing a value derived from each step, with an initial state
///     computed on first use.
/// </summary>
/// <param name="initialState">The lazy state to start from.</param>
/// <param name="f">
///     Given the fired value and the current state, returns a struct tuple of the value
///     to fire and the state for the next firing.
/// </param>
/// <param name="stream">The stream to fold over.</param>
/// <returns>A stream firing the value <paramref name="f" /> returned for each input firing.</returns>
/// <remarks>
///     The transaction commits the state at its end, and not immediately. Thus, a second firing in one
///     transaction does not see the state of the first firing. A transaction that throws an exception
///     leaves the state with no change.
///
///     This is the lazy version, to close a loop where the initial state is not available now.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let collectLazy initialState (f: 'a -> 'TState -> struct ('b * 'TState)) (stream: Stream<_>) =
    stream.CollectLazyImpl(initialState, (Func<_, _, _> f))

/// <summary>
///     Folds state across firings, firing a value derived from each step.
/// </summary>
/// <param name="initialState">The state to start from.</param>
/// <param name="f">
///     Given the fired value and the current state, returns a struct tuple of the value
///     to fire and the state for the next firing.
/// </param>
/// <param name="stream">The stream to fold over.</param>
/// <returns>A stream firing the value <paramref name="f" /> returned for each input firing.</returns>
/// <remarks>
///     The transaction commits the state at its end, and not immediately. Thus, a second firing in one
///     transaction does not see the state of the first firing. A transaction that throws an exception
///     leaves the state with no change.
///
///     Use <c>accum</c> where the pipeline publishes the state itself.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let collect initialState (f: 'a -> 'TState -> struct ('b * 'TState)) (stream: Stream<_>) =
    stream.CollectImpl(initialState, (Func<_, _, _> f))

/// <summary>
///     Suppresses firings whose value the given function considers equal to the last one that got
///     through.
/// </summary>
/// <param name="compare">Gives true when this code must read two values as equal.</param>
/// <param name="stream">The stream to calm.</param>
/// <returns>A stream firing only when the value actually changed.</returns>
/// <remarks>
///     A suppressed firing is not a missing firing. The next comparer call reads the value that this
///     code suppressed, and not the last value that the stream published.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let calmWithCompare compare (stream: Stream<_>) = stream.CalmImpl(Func<_, _, _> compare)

/// <summary>
///     Suppresses firings whose value the given comparer considers equal to the last one that got
///     through.
/// </summary>
/// <param name="equalityComparer">Gives true when two values are equal.</param>
/// <param name="stream">The stream to calm.</param>
/// <returns>A stream firing only when the value actually changed.</returns>
/// <remarks>
///     A suppressed firing is not a missing firing. The next comparer call reads the value that this
///     code suppressed, and not the last value that the stream published.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let calmWithEqualityComparer (equalityComparer: IEqualityComparer<_>) (stream: Stream<_>) =
    stream.CalmImpl(Func<_, _, _>(fun x y -> equalityComparer.Equals(x, y)))

/// <summary>
///     Suppresses firings equal, by F#'s structural equality, to the last one that got through.
/// </summary>
/// <param name="stream">The stream to calm.</param>
/// <returns>A stream firing only when the value actually changed.</returns>
/// <remarks>
///     A suppressed firing is not a missing firing. The next comparer call reads the value that this
///     code suppressed, and not the last value that the stream published.
///
///     Uses <c>=</c>, so for a type without meaningful structural equality use
///     the alternative <c>calmWithCompare</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let calm (stream: Stream<_>) = stream.CalmImpl(Func<_, _, _> (=))

/// <summary>
///     Folds state across firings into a cell, with an initial state computed on first use.
/// </summary>
/// <param name="initialState">The lazy state to start from.</param>
/// <param name="f">Given the fired value and the current state, returns the new state.</param>
/// <param name="stream">The stream to fold over.</param>
/// <returns>A cell holding the accumulated state.</returns>
/// <remarks>
///     This is the lazy version, to close a loop where the initial state is not available now.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let accumLazy initialState f (stream: Stream<_>) =
    stream.AccumLazyImpl(initialState, (Func<_, _, _> f))

/// <summary>
///     Folds state across firings into a cell.
/// </summary>
/// <param name="initialState">The state to start from.</param>
/// <param name="f">Given the fired value and the current state, returns the new state.</param>
/// <param name="stream">The stream to fold over.</param>
/// <returns>A cell holding the accumulated state.</returns>
/// <remarks>
///     A total, a counter, or a different value where the state itself is the result. Use
///     <c>collect</c> where the published value differs from the state carried forward.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let accum initialState f (stream: Stream<_>) =
    stream.AccumImpl(initialState, (Func<_, _, _> f))

/// <summary>
///     Keeps only the first firing.
/// </summary>
/// <param name="stream">The stream to read.</param>
/// <returns>A stream firing the first value the input fires, and never again.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let once (stream: Stream<_>) = stream.OnceImpl()

/// <summary>
///     Merges any number of streams, combining the values where some fire in one transaction.
/// </summary>
/// <param name="f">
///     Combines two simultaneous values. The value from the stream earlier in the sequence
///     is the left argument.
/// </param>
/// <param name="streams">The streams to merge.</param>
/// <returns>A stream that fires at each firing of an input, one time or no times in a transaction.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let mergeAll f (streams: seq<_>) =
    StreamExtensionMethodsInternal.MergeImpl(streams, (Func<_, _, _> f))

/// <summary>
///     Merges any number of streams, preferring the earliest where some fire in one transaction.
/// </summary>
/// <param name="streams">The streams to merge, in order of preference.</param>
/// <returns>A stream that fires at each firing of an input, one time or no times in a transaction.</returns>
/// <remarks>
///     This is <c>mergeAll</c> with a function that keeps the left value. The result drops the values
///     from the subsequent streams, and does not defer them.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let orElseAll (streams: seq<_>) =
    StreamExtensionMethodsInternal.OrElseImpl streams
