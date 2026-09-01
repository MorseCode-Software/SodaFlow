/// <summary>
///     Creating streams and combining them.
/// </summary>
/// <remarks>
///     A stream is a sequence of discrete firings, each one belonging to the transaction it
///     happened in. A stream fires at most once per transaction; where two firings would land in
///     the same transaction, the combinators that can produce that say how the two are resolved.
///
///     Build the graph inside <c>Transaction.run</c> so that no first firing is missed. Everything
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
/// <typeparam name="'a">The type the stream would fire, were it ever to fire.</typeparam>
/// <returns>A stream that never fires.</returns>
/// <remarks>
///     The identity for <c>orElse</c>, and what to return from a branch which has nothing to fire.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let never<'a> () = StreamInternal.NeverImpl<'a>()

/// <summary>
///     Builds a stream which refers to itself, closing the loop within one transaction.
/// </summary>
/// <param name="f">
///     Given the forward reference, returns a struct tuple of the stream it stands for and
///     anything else the caller wants back out.
/// </param>
/// <returns>
///     A struct tuple of the stream the forward reference was closed with, and whatever
///     <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
///     A stream defined in terms of itself needs a forward reference to exist before the stream it
///     refers to does. Both the reference and its resolution must happen in a single transaction,
///     which this opens if none is running.
///
///     Use <c>loopWithNoCaptures</c> where nothing but the stream itself is needed.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let loop f =
    TransactionInternal.Apply(fun transaction _ ->
        let l = LoopedStream()
        let struct (s, r) = f l
        l.Loop(transaction, s)
        struct (s, r))

/// <summary>
///     Builds a self-referential stream where nothing but the stream itself is wanted back.
/// </summary>
/// <param name="f">Given the forward reference, returns the stream it stands for.</param>
/// <returns>The stream the forward reference was closed with.</returns>
/// <remarks>
///     <c>loop</c> where something more than the stream needs to escape the loop.
/// </remarks>
let loopWithNoCaptures f =
    let struct (l, _) = loop (fun s -> struct (f s, ()))
    l

/// <summary>
///     Listens for firings without keeping the stream alive.
/// </summary>
/// <param name="handler">Run with each fired value.</param>
/// <param name="stream">The stream to listen to.</param>
/// <returns>A weak listener, which may be stopped with <c>WeakListener.unlisten</c>.</returns>
/// <remarks>
///     The listener stops on its own once the stream is collected, which makes this the right
///     choice where there is no clean moment to stop listening: hold the returned handle as a field
///     of the object doing the listening, and the two go away together. Where the stream should be
///     kept alive for as long as something is listening, use <c>listenStrong</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let listen handler (stream: Stream<_>) = stream.ListenImpl(Action<_> handler)

/// <summary>
///     Listens for firings, keeping the stream alive while the listener lives.
/// </summary>
/// <param name="handler">Run with each fired value.</param>
/// <param name="stream">The stream to listen to.</param>
/// <returns>
///     A strong listener, which may be stopped with <c>StrongListener.unlisten</c> or disposed.
/// </returns>
/// <remarks>
///     The listener roots the stream, so the graph behind it stays alive for as long as the
///     returned handle is reachable. Keep the handle and stop it when finished, or use
///     <c>listen</c> where there is no good moment to do that.
///
///     The handler runs under the transaction lock, so it should return promptly; hand
///     long-running or blocking work to another thread.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let listenStrong handler (stream: Stream<_>) =
    stream.ListenStrongImpl(Action<_> handler)

/// <summary>
///     Ties a listener to the lifetime of a stream, so the listener lives while the stream does.
/// </summary>
/// <param name="listener">The listener to attach.</param>
/// <param name="stream">The stream to attach it to.</param>
/// <returns>The same stream, now keeping <paramref name="listener" /> alive.</returns>
/// <remarks>
///     For building a primitive whose returned stream depends on internal wiring that nothing else
///     holds a reference to - the timer system does exactly this with the listener that watches
///     its alarm cell. Without the attachment the wiring is collected and the returned stream
///     quietly stops firing.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let attachListener listener (stream: Stream<_>) = stream.AttachListenerImpl listener

/// <summary>
///     Listens for the next firing only, then stops.
/// </summary>
/// <param name="handler">Run with the first fired value.</param>
/// <param name="stream">The stream to listen to.</param>
/// <returns>
///     A strong listener, which may be stopped before that first firing arrives if it is no longer
///     wanted.
/// </returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let listenOnce handler (stream: Stream<_>) =
    stream.ListenOnceImpl(Action<_> handler)

/// <summary>
///     Waits asynchronously for the next firing.
/// </summary>
/// <param name="stream">The stream to wait on.</param>
/// <returns>An async which produces the next value the stream fires.</returns>
/// <remarks>
///     The listener is attached at once, before the async is run, so a firing between this call
///     and the await is not missed.
///
///     Canceling the async stops listening and cancels the result. The value is produced on a
///     thread other than the one that fired it, so awaiting this does not run continuations under
///     the transaction lock.
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
///     <paramref name="f" /> may construct FRP logic or sample behaviors and cells; apart from
///     that it must be pure.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let map f (stream: Stream<_>) = stream.MapImpl(Func<_, _> f)

/// <summary>
///     Replaces every fired value with a constant.
/// </summary>
/// <param name="value">The value to fire instead.</param>
/// <param name="stream">The stream to transform.</param>
/// <returns>A stream firing <paramref name="value" /> whenever the input fires.</returns>
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
///     The cell's new value is visible to anything sampling it after the transaction in which the
///     firing happened, not within it. That delay is what makes a loop through a cell well defined
///     rather than circular.
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
///     This is the form that closes a loop: inside <c>Cell.loop</c> the initial value comes from
///     the very cell being defined, so it cannot be forced yet - <c>Cell.sampleLazy</c> produces
///     exactly what this takes.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let holdLazy initialValue (stream: Stream<_>) = stream.HoldLazyImpl initialValue

/// <summary>
///     Samples a behavior when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <param name="f">Combines the fired value with the sampled value.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the sampled
///     value.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshotB (behavior: Behavior<_>) f (stream: Stream<_>) =
    stream.SnapshotImpl(behavior, (Func<_, _, _> f))

/// <summary>
///     Samples a cell when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <param name="f">Combines the fired value with the sampled value.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the sampled
///     value.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshot (cell: Cell<_>) f (stream: Stream<_>) =
    stream.SnapshotImpl(cell, (Func<_, _, _> f))

/// <summary>
///     Samples a behavior when the stream fires, and fires the behavior's value, discarding the
///     stream's own.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>A stream firing the behavior's value at each firing of the input.</returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshotAndTakeB (behavior: Behavior<_>) (stream: Stream<_>) = stream.SnapshotImpl behavior

/// <summary>
///     Samples a cell when the stream fires, and fires the cell's value, discarding the stream's own.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>A stream firing the cell's value at each firing of the input.</returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let snapshotAndTake (cell: Cell<_>) (stream: Stream<_>) = stream.SnapshotImpl cell

/// <summary>
///     Samples two behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="f">Combines the fired value with the two sampled values.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the two sampled
///     values.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the two sampled
///     values.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the three sampled
///     values.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the three sampled
///     values.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the four sampled
///     values.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the four sampled
///     values.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the five sampled
///     values.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
/// </remarks>
let snapshot5B behavior1 behavior2 behavior3 behavior4 behavior5 f stream =
    stream
    |> snapshot4B behavior1 behavior2 behavior3 behavior4 tuple5S
    |> snapshotB behavior5 (fun struct (a, b, c, d, e) f' -> f a b c d e f')

/// <summary>
///     Samples five cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="cell3">The third cell to sample.</param>
/// <param name="cell4">The fourth cell to sample.</param>
/// <param name="cell5">The fifth cell to sample.</param>
/// <param name="f">Combines the fired value with the five sampled values.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the five sampled
///     values.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the six sampled
///     values.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
/// </remarks>
let snapshot6B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 f stream =
    stream
    |> snapshot4B behavior1 behavior2 behavior3 behavior4 tuple5S
    |> snapshot2B behavior5 behavior6 (fun struct (a, b, c, d, e) f' g -> f a b c d e f' g)

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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the six sampled
///     values.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the seven sampled
///     values.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
/// </remarks>
let snapshot7B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 behavior7 f stream =
    stream
    |> snapshot4B behavior1 behavior2 behavior3 behavior4 tuple5S
    |> snapshot3B behavior5 behavior6 behavior7 (fun struct (a, b, c, d, e) f' g h -> f a b c d e f' g h)

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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the seven sampled
///     values.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the eight sampled
///     values.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
/// </remarks>
let snapshot8B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 behavior7 behavior8 f stream =
    stream
    |> snapshot4B behavior1 behavior2 behavior3 behavior4 tuple5S
    |> snapshot4B behavior5 behavior6 behavior7 behavior8 (fun struct (a, b, c, d, e) f' g h i -> f a b c d e f' g h i)

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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
///     A stream firing <paramref name="f" /> applied to the fired value and the eight sampled
///     values.
/// </returns>
/// <remarks>
///     Sampling rather than merging: only the stream drives the firing, and the sampled values are the
///     ones held at the start of the transaction the firing belongs to. A cell updated in that same
///     transaction is therefore seen with its old value, which is what keeps the result independent of
///     the order the graph happens to be evaluated in.
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
///     Merges two streams, combining the values where both fire in one transaction.
/// </summary>
/// <param name="f">
///     Combines two simultaneous values. The value from the first stream is the left
///     argument and the value from the second is the right.
/// </param>
/// <param name="stream">The first stream.</param>
/// <param name="stream2">The second stream.</param>
/// <returns>A stream firing whenever either input does, at most once per transaction.</returns>
/// <remarks>
///     A stream fires at most once per transaction, so simultaneous firings must be resolved
///     rather than both delivered - that is what <paramref name="f" /> is for. Use <c>orElse</c>
///     to take the first instead of combining.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let merge f (stream: Stream<_>, stream2) =
    stream.MergeImpl(stream2, (Func<_, _, _> f))

/// <summary>
///     Merges two streams, preferring the first where both fire in one transaction.
/// </summary>
/// <param name="stream">The stream to prefer.</param>
/// <param name="stream2">The stream to fall back to.</param>
/// <returns>A stream firing whenever either input does, at most once per transaction.</returns>
/// <remarks>
///     <c>merge</c> with a function that keeps the left value and drops the right. The dropped
///     value is gone, not deferred - use <c>merge</c> where both matter.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let orElse (stream: Stream<_>, stream2) = stream.OrElseImpl stream2

/// <summary>
///     Keeps only the firings whose value satisfies a predicate.
/// </summary>
/// <param name="predicate">Returns whether to keep the fired value.</param>
/// <param name="stream">The stream to filter.</param>
/// <returns>A stream firing only the values <paramref name="predicate" /> accepted.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let filter predicate (stream: Stream<_>) = stream.FilterImpl(Func<_, _> predicate)

/// <summary>
///     Keeps only the firings which carried <c>Some</c>, and unwraps them.
/// </summary>
/// <param name="stream">The stream of options to filter.</param>
/// <returns>A stream firing the value inside each <c>Some</c>, and not firing for <c>None</c>.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let filterSome (stream: Stream<_>) =
    StreamExtensionMethodsInternal.FilterSomeImpl(stream, (Action<_, _>(fun o a -> o |> Option.iter a.Invoke)))

/// <summary>
///     Transforms the firings with a function which may produce no value, and fires only the values
///     it produced.
/// </summary>
/// <param name="f">Applied to each fired value; the firings it returns <c>None</c> for are dropped.</param>
/// <param name="stream">The stream to transform.</param>
/// <returns>
///     A stream firing the value inside each <c>Some</c> that <paramref name="f" /> returned, and not
///     firing for the values it returned <c>None</c> for.
/// </returns>
/// <remarks>
///     Mapping and filtering in one step, for the common case where deciding whether a firing should
///     pass is the same work as producing the value to pass on. The same thing as
///     <c>map f >> filterSome</c>, and the counterpart of <c>List.choose</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let choose f (stream: Stream<_>) =
    StreamExtensionMethodsInternal.FilterSomeImpl(
        stream.MapImpl(Func<_, _> f),
        (Action<_, _>(fun o a -> o |> Option.iter a.Invoke))
    )

/// <summary>
///     Lets firings through only while a behavior holds true.
/// </summary>
/// <param name="behavior">The behavior deciding whether firings pass.</param>
/// <param name="stream">The stream to gate.</param>
/// <returns>A stream firing only when the behavior held true at the time of the firing.</returns>
/// <remarks>
///     The behavior is sampled the way <c>snapshotB</c> samples it: the value read is the one held
///     at the start of the transaction the firing belongs to.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let gateB (behavior: Behavior<_>) (stream: Stream<_>) = stream.GateImpl behavior

/// <summary>
///     Lets firings through only while a cell holds true.
/// </summary>
/// <param name="cell">The cell deciding whether firings pass.</param>
/// <param name="stream">The stream to gate.</param>
/// <returns>A stream firing only when the cell held true at the time of the firing.</returns>
/// <remarks>
///     The cell is sampled the way <c>snapshot</c> samples it: the value read is the one held at
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
///     to fire and the state to carry forward.
/// </param>
/// <param name="stream">The stream to fold over.</param>
/// <returns>A stream firing the value <paramref name="f" /> returned for each input firing.</returns>
/// <remarks>
///     The state is committed at the end of the transaction rather than in place, so a second
///     firing within one transaction does not see the first one's state, and a transaction that
///     throws leaves the state as though nothing had happened.
///
///     This is the lazy form, for closing a loop where the initial state is not yet available.
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
///     to fire and the state to carry forward.
/// </param>
/// <param name="stream">The stream to fold over.</param>
/// <returns>A stream firing the value <paramref name="f" /> returned for each input firing.</returns>
/// <remarks>
///     The state is committed at the end of the transaction rather than in place, so a second
///     firing within one transaction does not see the first one's state, and a transaction that
///     throws leaves the state as though nothing had happened.
///
///     Use <c>accum</c> where the state itself is what should be published.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let collect initialState (f: 'a -> 'TState -> struct ('b * 'TState)) (stream: Stream<_>) =
    stream.CollectImpl(initialState, (Func<_, _, _> f))

/// <summary>
///     Suppresses firings whose value the given comparison considers equal to the last one that got
///     through.
/// </summary>
/// <param name="compare">Returns whether two values are to be treated as equal.</param>
/// <param name="stream">The stream to calm.</param>
/// <returns>A stream firing only when the value actually changed.</returns>
/// <remarks>
///     Suppressing a firing is not the same as it not happening: the next comparison is made against
///     the value that was suppressed, not against the last one that got through.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let calmWithCompare compare (stream: Stream<_>) = stream.CalmImpl(Func<_, _, _> compare)

/// <summary>
///     Suppresses firings whose value the given comparer considers equal to the last one that got
///     through.
/// </summary>
/// <param name="equalityComparer">Decides whether two values are equal.</param>
/// <param name="stream">The stream to calm.</param>
/// <returns>A stream firing only when the value actually changed.</returns>
/// <remarks>
///     Suppressing a firing is not the same as it not happening: the next comparison is made against
///     the value that was suppressed, not against the last one that got through.
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
///     Suppressing a firing is not the same as it not happening: the next comparison is made against
///     the value that was suppressed, not against the last one that got through.
///
///     Uses <c>=</c>, so for a type without meaningful structural equality use
///     <c>calmWithCompare</c> instead.
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
///     This is the lazy form, for closing a loop where the initial state is not yet available.
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
///     A running total, a counter, anything where the state itself is what is wanted. Use
///     <c>collect</c> where the published value differs from the state carried forward.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let accum initialState f (stream: Stream<_>) =
    stream.AccumImpl(initialState, (Func<_, _, _> f))

/// <summary>
///     Keeps only the first firing.
/// </summary>
/// <param name="stream">The stream to take from.</param>
/// <returns>A stream firing the first value the input fires, and never again.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let once (stream: Stream<_>) = stream.OnceImpl()

/// <summary>
///     Merges any number of streams, combining the values where several fire in one transaction.
/// </summary>
/// <param name="f">
///     Combines two simultaneous values. The value from the stream earlier in the sequence
///     is the left argument.
/// </param>
/// <param name="streams">The streams to merge.</param>
/// <returns>A stream firing whenever any input does, at most once per transaction.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let mergeAll f (streams: seq<_>) =
    StreamExtensionMethodsInternal.MergeImpl(streams, (Func<_, _, _> f))

/// <summary>
///     Merges any number of streams, preferring the earliest where several fire in one transaction.
/// </summary>
/// <param name="streams">The streams to merge, in order of preference.</param>
/// <returns>A stream firing whenever any input does, at most once per transaction.</returns>
/// <remarks>
///     <c>mergeAll</c> with a function that keeps the left value; the values from the later
///     streams are dropped rather than deferred.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let orElseAll (streams: seq<_>) =
    StreamExtensionMethodsInternal.OrElseImpl streams
