/// <summary>
///     Short names for the full library, opened automatically.
/// </summary>
/// <remarks>
///     Each binding here is an alias for one in <c>Stream</c>, <c>Cell</c>, <c>Behavior</c> or one
///     of their sinks. A suffix gives the module: <c>S</c> for stream, <c>C</c> for cell, <c>B</c>
///     for behavior, <c>T</c> for transaction, and <c>L</c> for listener. The suffix
///     differentiates the operations that are on more than one of them: <c>mapS</c>, <c>mapC</c>,
///     and <c>mapB</c>. Thus, code can use all of them with no qualification in the same scope.
///
///     Where an operation takes one type of value and gives a second type, the suffix names the
///     argument, and not the result. <c>snapshotC</c> samples a cell, <c>holdS</c> holds a stream
///     into a cell, and <c>switchSB</c> takes a behavior of streams. The module is <c>AutoOpen</c>,
///     thus an open of <c>SodaFlow</c> puts these in scope.
/// </remarks>
[<AutoOpen>]
module SodaFlow.Shorthand

/// <summary>
/// Gives true when a transaction is open on this thread.
/// </summary>
/// <returns><c>true</c> if there is a current transaction, and <c>false</c> otherwise.</returns>
/// <remarks>
/// Shorthand for <c>Transaction.isActive</c>. See it for the full contract.
/// </remarks>
let inline isActiveT () = Transaction.isActive ()

/// <summary>
/// Runs a function in a single transaction and returns its result.
/// </summary>
/// <param name="f">The function to run.</param>
/// <returns>Whatever <paramref name="f" /> returned.</returns>
/// <remarks>
/// Shorthand for <c>Transaction.run</c>. See it for the full contract.
///
/// Rarely needed for a single operation, since each primitive opens a transaction of its own
/// where it needs one. It is for making some operations atomic together.
///
/// Build the graph in one of these, and the graph then keeps the first firing. That is most
/// important with <c>Cell.values</c>, which always fires immediately. It is also necessary for
/// <c>Stream.loop</c>, <c>Cell.loop</c> and <c>Behavior.loop</c>. The code must make and close
/// each of these in one transaction.
/// </remarks>
let inline runT f = Transaction.run f

/// <summary>
/// Registers an action to run when a transaction starts.
/// </summary>
/// <param name="a">The action to run at the start of each transaction.</param>
/// <remarks>
/// Shorthand for <c>Transaction.onStart</c>. See it for the full contract.
///
/// The action can start its own transactions, and the hooks do not then run again. This exists
/// for implementing a timer system - it is how <c>SodaFlow.Time</c> delivers alarms - and is
/// rarely what calling code needs.
/// </remarks>
let inline onStartT a = Transaction.onStart a

/// <summary>
/// Runs an action after the current transaction closes, or immediately when no transaction is open.
/// </summary>
/// <param name="a">The action to run.</param>
/// <remarks>
/// Shorthand for <c>Transaction.post</c>. See it for the full contract.
///
/// The action runs with the transaction lock held, while the transaction closes. Thus, the rule
/// for a listener callback also applies: return quickly.
/// </remarks>
let inline postT a = Transaction.post a

/// <summary>
/// Stops listening.
/// </summary>
/// <param name="listener">The listener to stop.</param>
/// <remarks>
/// Shorthand for <c>Listener.unlisten</c>. See it for the full contract.
///
/// A second call is safe. Each call after the first does nothing.
/// </remarks>
let inline unlistenL listener = Listener.unlisten listener

/// <summary>
/// Stops listening.
/// </summary>
/// <param name="listener">The listener to stop.</param>
/// <remarks>
/// Shorthand for <c>WeakListener.unlisten</c>. See it for the full contract.
///
/// A second call is safe. Each call after the first does nothing.
/// </remarks>
let inline unlistenWeakL listener = WeakListener.unlisten listener

/// <summary>
/// Stops listening.
/// </summary>
/// <param name="listener">The listener to stop.</param>
/// <remarks>
/// Shorthand for <c>StrongListener.unlisten</c>. See it for the full contract.
///
/// A second call is safe and does nothing. A disposal of the listener has the same result.
/// </remarks>
let inline unlistenStrongL listener = StrongListener.unlisten listener

/// <summary>
/// Creates a stream which never fires.
/// </summary>
/// <typeparam name="'a">The type that the stream fires, if it fires.</typeparam>
/// <returns>A stream that never fires.</returns>
/// <remarks>
/// Shorthand for <c>Stream.never</c>. See it for the full contract.
///
/// The identity for <c>orElse</c>, and what to return from a branch which has nothing to fire.
/// </remarks>
let inline neverS<'a> () = Stream.never<'a> ()

/// <summary>
/// Creates a stream sink. A second <c>send</c> in one transaction throws an exception.
/// </summary>
/// <typeparam name="'a">The type of the values the stream sink fires.</typeparam>
/// <returns>A new stream sink.</returns>
/// <remarks>
/// Shorthand for <c>StreamSink.create</c>. See it for the full contract.
///
/// Two sends in one transaction are usually an error and not an intention. Thus, the sink
/// reports this, and does not resolve it without a message. Use <c>createWithCoalesce</c> where
/// the second send is correct.
/// </remarks>
let inline sinkS<'a> () = StreamSink.create<'a> ()

/// <summary>
/// Creates a stream sink that combines the values of more than one <c>send</c> in one transaction.
/// </summary>
/// <param name="coalesce">
/// Puts two values from the same transaction together. It receives the value from before this
/// send and the new value, in that sequence.
/// </param>
/// <returns>A new stream sink.</returns>
/// <remarks>
/// Shorthand for <c>StreamSink.createWithCoalesce</c>. See it for the full contract.
///
/// A stream fires one time or no times in each transaction, and this keeps that rule. The sink
/// folds all that a caller sends in one transaction into the one value that fires.
/// </remarks>
let inline sinkWithCoalesceS coalesce = StreamSink.createWithCoalesce coalesce

/// <summary>
/// Creates a cell stream sink. A second <c>StreamSink.send</c> in one transaction throws an exception.
/// </summary>
/// <typeparam name="'a">The type of the values the cell stream sink fires.</typeparam>
/// <returns>A new cell stream sink.</returns>
/// <remarks>
/// Shorthand for <c>CellStreamSink.create</c>. See it for the full contract.
/// </remarks>
let inline sinkCS<'a> () = CellStreamSink.create<'a> ()

/// <summary>
/// Creates a cell stream sink that combines the values of more than one
/// <c>StreamSink.send</c> in one transaction.
/// </summary>
/// <param name="coalesce">
/// Puts two values from the same transaction together. It receives the value from before this
/// send and the new value, in that sequence.
/// </param>
/// <returns>A new cell stream sink.</returns>
/// <remarks>
/// Shorthand for <c>CellStreamSink.createWithCoalesce</c>. See it for the full contract.
/// </remarks>
let inline sinkWithCoalesceCS coalesce =
    CellStreamSink.createWithCoalesce coalesce

/// <summary>
/// Sends a value, firing the stream sink.
/// </summary>
/// <param name="a">The value to send.</param>
/// <param name="streamSink">The stream sink to send it to.</param>
/// <remarks>
/// Shorthand for <c>StreamSink.send</c>. See it for the full contract.
///
/// A call from a listener callback throws an exception. Sinks are for getting
/// I/O into FRP, not for building new primitives out of.
///
/// Two sends in one transaction throw an exception, unless <c>createWithCoalesce</c> made the
/// sink.
/// </remarks>
let inline sendS a streamSink = StreamSink.send a streamSink

/// <summary>
/// Builds a stream which refers to itself, closing the loop in one transaction.
/// </summary>
/// <param name="f">
/// Given the forward reference, returns a struct tuple of the stream it stands for and
/// anything else the caller wants back out.
/// </param>
/// <returns>
/// A struct tuple of the stream that closed the forward reference, and whatever
/// <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.loop</c>. See it for the full contract.
///
/// A stream that refers to itself needs a forward reference. The code makes the reference
/// before the stream that it refers to. The reference and its resolution must occur in one
/// transaction, which this opens when no transaction is open.
///
/// Use <c>loopWithNoCaptures</c> where the caller needs only the stream.
/// </remarks>
let inline loopS f = Stream.loop f

/// <summary>
/// Builds a self-referential stream where the caller needs only the stream.
/// </summary>
/// <param name="f">Given the forward reference, returns the stream it stands for.</param>
/// <returns>The stream that closed the forward reference.</returns>
/// <remarks>
/// Shorthand for <c>Stream.loopWithNoCaptures</c>. See it for the full contract.
///
/// <c>loop</c> where the caller needs more than the stream from the loop.
/// </remarks>
let inline loopWithNoCapturesS f = Stream.loopWithNoCaptures f

/// <summary>
/// Listens for firings without keeping the stream alive.
/// </summary>
/// <param name="handler">Run with each fired value.</param>
/// <param name="stream">The stream to listen to.</param>
/// <returns>A weak listener. <c>WeakListener.unlisten</c> stops it.</returns>
/// <remarks>
/// Shorthand for <c>Stream.listen</c>. See it for the full contract.
///
/// The listener stops when a GC collects the stream, thus this is the correct selection where there is no
/// clear moment to stop the listener. Keep the handle from this call in a field of the object doing the
/// listening, and the two go away together. Where other code must keep the stream kept alive for as long
/// as something is listening, use <c>listenStrong</c>.
/// </remarks>
let inline listenS handler stream = Stream.listen handler stream

/// <summary>
/// Listens for firings, keeping the stream alive while the listener lives.
/// </summary>
/// <param name="handler">Run with each fired value.</param>
/// <param name="stream">The stream to listen to.</param>
/// <returns>
/// A strong listener. <c>StrongListener.unlisten</c> stops it, and a disposal also stops it.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.listenStrong</c>. See it for the full contract.
///
/// The listener roots the stream, so the graph behind it stays alive for as long as the
/// returned handle is reachable. Keep the handle and stop it when finished, or use
/// <c>listen</c> where there is no good moment to do that.
///
/// The handler runs with the transaction lock held, thus it must return quickly. Give work
/// that takes a long time, and work that blocks, to a different thread.
/// </remarks>
let inline listenStrongS handler stream = Stream.listenStrong handler stream

/// <summary>
/// Ties a listener to the lifetime of a stream, so the listener lives while the stream does.
/// </summary>
/// <param name="listener">The listener to attach.</param>
/// <param name="stream">The stream to attach it to.</param>
/// <returns>The same stream, now keeping <paramref name="listener" /> alive.</returns>
/// <remarks>
/// Shorthand for <c>Stream.attachListener</c>. See it for the full contract.
///
/// Use this to make a primitive whose stream depends on internal wiring that no other code
/// references. The timer system does this with the listener that monitors its alarm cell. With
/// no attached listener a GC collects the wiring, and the stream from this call stops with no
/// message.
/// </remarks>
let inline attachListenerS listener stream = Stream.attachListener listener stream

/// <summary>
/// Listens for the next firing only, then stops.
/// </summary>
/// <param name="handler">Run with the first fired value.</param>
/// <param name="stream">The stream to listen to.</param>
/// <returns>
/// A strong listener, that a caller can stop before that first firing, when the caller no longer
/// wanted.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.listenOnce</c>. See it for the full contract.
/// </remarks>
let inline listenOnceS handler stream = Stream.listenOnce handler stream

/// <summary>
/// Waits asynchronously for the next firing.
/// </summary>
/// <param name="stream">The stream to wait on.</param>
/// <returns>An async which produces the next value the stream fires.</returns>
/// <remarks>
/// Shorthand for <c>Stream.listenOnceAsync</c>. See it for the full contract.
///
/// The listener attaches immediately, before the async runs, so a firing between this call
/// and the await is not missed.
///
/// A cancellation of the async stops the listener and cancels the result. A thread that is not
/// the thread that fired the value gives the value. Thus, an await of this does not run a
/// continuation with the transaction lock.
/// </remarks>
let inline listenOnceAsyncS stream = Stream.listenOnceAsync stream

/// <summary>
/// Transforms each fired value with a function.
/// </summary>
/// <param name="f">Transforms the fired value.</param>
/// <param name="stream">The stream to transform.</param>
/// <returns>A stream firing <paramref name="f" /> applied to each value the input fires.</returns>
/// <remarks>
/// Shorthand for <c>Stream.map</c>. See it for the full contract.
///
/// <paramref name="f" /> can make FRP logic, and it can sample a behavior and a cell. In each
/// other operation it must be pure.
/// </remarks>
let inline mapS f stream = Stream.map f stream

/// <summary>
/// Replaces each fired value with a constant.
/// </summary>
/// <param name="value">The replacement value to fire.</param>
/// <param name="stream">The stream to transform.</param>
/// <returns>A stream that fires <paramref name="value" /> at each firing of the input.</returns>
/// <remarks>
/// Shorthand for <c>Stream.mapTo</c>. See it for the full contract.
///
/// For when only the fact that something happened matters, not what it carried.
/// </remarks>
let inline mapToS value stream = Stream.mapTo value stream

/// <summary>
/// Holds the most recently fired value in a cell.
/// </summary>
/// <param name="initialValue">The value the cell holds until the stream first fires.</param>
/// <param name="stream">The stream to hold.</param>
/// <returns>A cell holding the last value fired, or <paramref name="initialValue" /> before any.</returns>
/// <remarks>
/// Shorthand for <c>Stream.hold</c>. See it for the full contract.
///
/// A sample after the transaction of the firing gives the new value of the cell, and a sample
/// in that transaction does not. That interval is what makes a loop through a cell correct,
/// and not circular.
/// </remarks>
let inline holdS initialValue stream = Stream.hold initialValue stream

/// <summary>
/// Holds the most recently fired value in a cell, with an initial value computed on first use.
/// </summary>
/// <param name="initialValue">The lazy value the cell holds until the stream first fires.</param>
/// <param name="stream">The stream to hold.</param>
/// <returns>A cell holding the last value fired, or <paramref name="initialValue" /> before any.</returns>
/// <remarks>
/// Shorthand for <c>Stream.holdLazy</c>. See it for the full contract.
///
/// This is the version that closes a loop. In <c>Cell.loop</c> the initial value comes from the
/// same cell, thus no code can force it now. <c>Cell.sampleLazy</c> gives what this takes.
/// </remarks>
let inline holdLazyS initialValue stream = Stream.holdLazy initialValue stream

/// <summary>
/// Samples a behavior when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <param name="f">Combines the fired value with the sampled value.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the sampled
/// value.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshotB</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshotB behavior f stream = Stream.snapshotB behavior f stream

/// <summary>
/// Samples a cell when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <param name="f">Combines the fired value with the sampled value.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the sampled
/// value.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshotC cell f stream = Stream.snapshot cell f stream

/// <summary>
/// Samples a behavior when the stream fires, and fires the behavior's value, discarding the
/// stream's own.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>A stream firing the behavior's value at each firing of the input.</returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshotAndTakeB</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshotAndTakeB behavior stream = Stream.snapshotAndTakeB behavior stream

/// <summary>
/// Samples a cell when the stream fires, and fires the cell's value, discarding the stream's own.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>A stream firing the cell's value at each firing of the input.</returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshotAndTake</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshotAndTakeC cell stream = Stream.snapshotAndTake cell stream

/// <summary>
/// Samples two behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="f">Combines the fired value with the two sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the two sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot2B</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshot2B behavior1 behavior2 f stream =
    Stream.snapshot2B behavior1 behavior2 f stream

/// <summary>
/// Samples two cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="f">Combines the fired value with the two sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the two sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot2</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshot2C cell1 cell2 f stream = Stream.snapshot2 cell1 cell2 f stream

/// <summary>
/// Samples three behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="behavior3">The third behavior to sample.</param>
/// <param name="f">Combines the fired value with the three sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the three sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot3B</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshot3B behavior1 behavior2 behavior3 f stream =
    Stream.snapshot3B behavior1 behavior2 behavior3 f stream

/// <summary>
/// Samples three cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="cell3">The third cell to sample.</param>
/// <param name="f">Combines the fired value with the three sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the three sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot3</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshot3C cell1 cell2 cell3 f stream =
    Stream.snapshot3 cell1 cell2 cell3 f stream

/// <summary>
/// Samples four behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="behavior3">The third behavior to sample.</param>
/// <param name="behavior4">The fourth behavior to sample.</param>
/// <param name="f">Combines the fired value with the four sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the four sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot4B</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshot4B behavior1 behavior2 behavior3 behavior4 f stream =
    Stream.snapshot4B behavior1 behavior2 behavior3 behavior4 f stream

/// <summary>
/// Samples four cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="cell3">The third cell to sample.</param>
/// <param name="cell4">The fourth cell to sample.</param>
/// <param name="f">Combines the fired value with the four sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the four sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot4</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshot4C cell1 cell2 cell3 cell4 f stream =
    Stream.snapshot4 cell1 cell2 cell3 cell4 f stream

/// <summary>
/// Samples five behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="behavior3">The third behavior to sample.</param>
/// <param name="behavior4">The fourth behavior to sample.</param>
/// <param name="behavior5">The fifth behavior to sample.</param>
/// <param name="f">Combines the fired value with the five sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the five sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot5B</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshot5B behavior1 behavior2 behavior3 behavior4 behavior5 f stream =
    Stream.snapshot5B behavior1 behavior2 behavior3 behavior4 behavior5 f stream

/// <summary>
/// Samples five cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="cell3">The third cell to sample.</param>
/// <param name="cell4">The fourth cell to sample.</param>
/// <param name="cell5">The fifth cell to sample.</param>
/// <param name="f">Combines the fired value with the five sampled values.</param>
/// <param name="stream">The stream that causes each firing of the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the five sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot5</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshot5C cell1 cell2 cell3 cell4 cell5 f stream =
    Stream.snapshot5 cell1 cell2 cell3 cell4 cell5 f stream

/// <summary>
/// Samples six behaviors when the stream fires, and fires the combination.
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
/// A stream firing <paramref name="f" /> applied to the fired value and the six sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot6B</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshot6B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 f stream =
    Stream.snapshot6B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 f stream

/// <summary>
/// Samples six cells when the stream fires, and fires the combination.
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
/// A stream firing <paramref name="f" /> applied to the fired value and the six sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot6</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshot6C cell1 cell2 cell3 cell4 cell5 cell6 f stream =
    Stream.snapshot6 cell1 cell2 cell3 cell4 cell5 cell6 f stream

/// <summary>
/// Samples seven behaviors when the stream fires, and fires the combination.
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
/// A stream firing <paramref name="f" /> applied to the fired value and the seven sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot7B</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshot7B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 behavior7 f stream =
    Stream.snapshot7B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 behavior7 f stream

/// <summary>
/// Samples seven cells when the stream fires, and fires the combination.
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
/// A stream firing <paramref name="f" /> applied to the fired value and the seven sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot7</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshot7C cell1 cell2 cell3 cell4 cell5 cell6 cell7 f stream =
    Stream.snapshot7 cell1 cell2 cell3 cell4 cell5 cell6 cell7 f stream

/// <summary>
/// Samples eight behaviors when the stream fires, and fires the combination.
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
/// A stream firing <paramref name="f" /> applied to the fired value and the eight sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot8B</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshot8B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 behavior7 behavior8 f stream =
    Stream.snapshot8B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 behavior7 behavior8 f stream

/// <summary>
/// Samples eight cells when the stream fires, and fires the combination.
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
/// A stream firing <paramref name="f" /> applied to the fired value and the eight sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot8</c>. See it for the full contract.
///
/// This samples and does not merge. Only the stream causes the firing, and each sampled value is
/// the value at the start of the transaction of that firing. Thus, a cell that the same
/// transaction updates gives its previous value. The result does not change when the graph
/// operates in a different sequence.
/// </remarks>
let inline snapshot8C cell1 cell2 cell3 cell4 cell5 cell6 cell7 cell8 f stream =
    Stream.snapshot8 cell1 cell2 cell3 cell4 cell5 cell6 cell7 cell8 f stream

/// <summary>
/// Merges two streams, combining the values where the two fire in one transaction.
/// </summary>
/// <param name="f">
/// Combines two simultaneous values. The value from the first stream is the left
/// argument and the value from the second is the right.
/// </param>
/// <param name="stream">The first stream.</param>
/// <param name="stream2">The second stream.</param>
/// <returns>A stream that fires at each firing of an input, one time or no times in a transaction.</returns>
/// <remarks>
/// Shorthand for <c>Stream.merge</c>. See it for the full contract.
///
/// A stream fires one time or no times in each transaction, thus the result must give one value
/// for two firings at the same time. <paramref name="f" /> gives that value. Use <c>orElse</c>
/// to use the first value, and not the result of <paramref name="f" />.
/// </remarks>
let inline mergeS f (stream, stream2) = Stream.merge f (stream, stream2)

/// <summary>
/// Merges two streams, preferring the first where the two fire in one transaction.
/// </summary>
/// <param name="stream">The stream to prefer.</param>
/// <param name="stream2">The alternative stream.</param>
/// <returns>A stream that fires at each firing of an input, one time or no times in a transaction.</returns>
/// <remarks>
/// Shorthand for <c>Stream.orElse</c>. See it for the full contract.
///
/// <c>merge</c> with a function that keeps the left value and drops the right. The dropped
/// value is gone, not deferred - use <c>merge</c> where the two are necessary.
/// </remarks>
let inline orElseS (stream, stream2) = Stream.orElse (stream, stream2)

/// <summary>
/// Keeps only the firings whose value satisfies a predicate.
/// </summary>
/// <param name="predicate">Gives true when the result keeps the value.</param>
/// <param name="stream">The stream to filter.</param>
/// <returns>A stream firing only the values <paramref name="predicate" /> accepted.</returns>
/// <remarks>
/// Shorthand for <c>Stream.filter</c>. See it for the full contract.
/// </remarks>
let inline filterS predicate stream = Stream.filter predicate stream

/// <summary>
/// Keeps only the firings which carried <c>Some</c>, and unwraps them.
/// </summary>
/// <param name="stream">The stream of options to filter.</param>
/// <returns>A stream firing the value in each <c>Some</c>, and not firing for <c>None</c>.</returns>
/// <remarks>
/// Shorthand for <c>Stream.filterSome</c>. See it for the full contract.
/// </remarks>
let inline filterSomeS stream = Stream.filterSome stream

/// <summary>
/// Transforms the firings with a function that can give no value, and fires only the values it
/// produced.
/// </summary>
/// <param name="f">Runs on each fired value. The result drops the firings that give <c>None</c>.</param>
/// <param name="stream">The stream to transform.</param>
/// <returns>
/// A stream firing the value in each <c>Some</c> that <paramref name="f" /> returned, and not
/// firing for the values it returned <c>None</c> for.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.choose</c>. See it for the full contract.
/// </remarks>
let inline chooseS f stream = Stream.choose f stream

/// <summary>
/// Lets firings through only while a behavior holds true.
/// </summary>
/// <param name="behavior">The behavior that gives true for the firings to keep.</param>
/// <param name="stream">The stream to gate.</param>
/// <returns>A stream firing only when the behavior held true at the time of the firing.</returns>
/// <remarks>
/// Shorthand for <c>Stream.gateB</c>. See it for the full contract.
///
/// This samples the behavior as <c>snapshotB</c> samples it: the value read is the one held
/// at the start of the transaction the firing belongs to.
/// </remarks>
let inline gateB behavior stream = Stream.gateB behavior stream

/// <summary>
/// Lets firings through only while a cell holds true.
/// </summary>
/// <param name="cell">The cell that gives true for the firings to keep.</param>
/// <param name="stream">The stream to gate.</param>
/// <returns>A stream firing only when the cell held true at the time of the firing.</returns>
/// <remarks>
/// Shorthand for <c>Stream.gate</c>. See it for the full contract.
///
/// This samples the cell as <c>snapshot</c> samples it: the value read is the one held at
/// the start of the transaction the firing belongs to.
/// </remarks>
let inline gateC cell stream = Stream.gate cell stream

/// <summary>
/// Folds state across firings, firing a value derived from each step, with an initial state
/// computed on first use.
/// </summary>
/// <param name="initialState">The lazy state to start from.</param>
/// <param name="f">
/// Given the fired value and the current state, returns a struct tuple of the value
/// to fire and the state for the next firing.
/// </param>
/// <param name="stream">The stream to fold over.</param>
/// <returns>A stream firing the value <paramref name="f" /> returned for each input firing.</returns>
/// <remarks>
/// Shorthand for <c>Stream.collectLazy</c>. See it for the full contract.
///
/// The transaction commits the state at its end, and not immediately. Thus, a second firing in one
/// transaction does not see the state of the first firing. A transaction that throws an exception
/// leaves the state with no change.
///
/// This is the lazy version, to close a loop where the initial state is not available now.
/// </remarks>
let inline collectLazyS initialState f stream =
    Stream.collectLazy initialState f stream

/// <summary>
/// Folds state across firings, firing a value derived from each step.
/// </summary>
/// <param name="initialState">The state to start from.</param>
/// <param name="f">
/// Given the fired value and the current state, returns a struct tuple of the value
/// to fire and the state for the next firing.
/// </param>
/// <param name="stream">The stream to fold over.</param>
/// <returns>A stream firing the value <paramref name="f" /> returned for each input firing.</returns>
/// <remarks>
/// Shorthand for <c>Stream.collect</c>. See it for the full contract.
///
/// The transaction commits the state at its end, and not immediately. Thus, a second firing in one
/// transaction does not see the state of the first firing. A transaction that throws an exception
/// leaves the state with no change.
///
/// Use <c>accum</c> where the pipeline publishes the state itself.
/// </remarks>
let inline collectS initialState f stream = Stream.collect initialState f stream

/// <summary>
/// Suppresses firings whose value the given function considers equal to the last one that got
/// through.
/// </summary>
/// <param name="compare">Gives true when this code must read two values as equal.</param>
/// <param name="stream">The stream to calm.</param>
/// <returns>A stream firing only when the value actually changed.</returns>
/// <remarks>
/// Shorthand for <c>Stream.calmWithCompare</c>. See it for the full contract.
///
/// A suppressed firing is not a missing firing. The next comparer call reads the value that this
/// code suppressed, and not the last value that the stream published.
/// </remarks>
let inline calmWithCompareS compare stream = Stream.calmWithCompare compare stream

/// <summary>
/// Suppresses firings whose value the given comparer considers equal to the last one that got
/// through.
/// </summary>
/// <param name="equalityComparer">Gives true when two values are equal.</param>
/// <param name="stream">The stream to calm.</param>
/// <returns>A stream firing only when the value actually changed.</returns>
/// <remarks>
/// Shorthand for <c>Stream.calmWithEqualityComparer</c>. See it for the full contract.
///
/// A suppressed firing is not a missing firing. The next comparer call reads the value that this
/// code suppressed, and not the last value that the stream published.
/// </remarks>
let inline calmWithEqualityComparerS equalityComparer stream =
    Stream.calmWithEqualityComparer equalityComparer stream

/// <summary>
/// Suppresses firings equal, by F#'s structural equality, to the last one that got through.
/// </summary>
/// <param name="stream">The stream to calm.</param>
/// <returns>A stream firing only when the value actually changed.</returns>
/// <remarks>
/// Shorthand for <c>Stream.calm</c>. See it for the full contract.
///
/// A suppressed firing is not a missing firing. The next comparer call reads the value that this
/// code suppressed, and not the last value that the stream published.
///
/// Uses <c>=</c>, so for a type without meaningful structural equality use
/// the alternative <c>calmWithCompare</c>.
/// </remarks>
let inline calmS stream = Stream.calm stream

/// <summary>
/// Folds state across firings into a cell, with an initial state computed on first use.
/// </summary>
/// <param name="initialState">The lazy state to start from.</param>
/// <param name="f">Given the fired value and the current state, returns the new state.</param>
/// <param name="stream">The stream to fold over.</param>
/// <returns>A cell holding the accumulated state.</returns>
/// <remarks>
/// Shorthand for <c>Stream.accumLazy</c>. See it for the full contract.
///
/// This is the lazy version, to close a loop where the initial state is not available now.
/// </remarks>
let inline accumLazyS initialState f stream = Stream.accumLazy initialState f stream

/// <summary>
/// Folds state across firings into a cell.
/// </summary>
/// <param name="initialState">The state to start from.</param>
/// <param name="f">Given the fired value and the current state, returns the new state.</param>
/// <param name="stream">The stream to fold over.</param>
/// <returns>A cell holding the accumulated state.</returns>
/// <remarks>
/// Shorthand for <c>Stream.accum</c>. See it for the full contract.
///
/// A total, a counter, or a different value where the state itself is the result. Use
/// <c>collect</c> where the published value differs from the state carried forward.
/// </remarks>
let inline accumS initialState f stream = Stream.accum initialState f stream

/// <summary>
/// Keeps only the first firing.
/// </summary>
/// <param name="stream">The stream to read.</param>
/// <returns>A stream firing the first value the input fires, and never again.</returns>
/// <remarks>
/// Shorthand for <c>Stream.once</c>. See it for the full contract.
/// </remarks>
let inline onceS stream = Stream.once stream

/// <summary>
/// Merges any number of streams, combining the values where some fire in one transaction.
/// </summary>
/// <param name="f">
/// Combines two simultaneous values. The value from the stream earlier in the sequence
/// is the left argument.
/// </param>
/// <param name="streams">The streams to merge.</param>
/// <returns>A stream that fires at each firing of an input, one time or no times in a transaction.</returns>
/// <remarks>
/// Shorthand for <c>Stream.mergeAll</c>. See it for the full contract.
/// </remarks>
let inline mergeAllS f streams = Stream.mergeAll f streams

/// <summary>
/// Merges any number of streams, preferring the earliest where some fire in one transaction.
/// </summary>
/// <param name="streams">The streams to merge, in order of preference.</param>
/// <returns>A stream that fires at each firing of an input, one time or no times in a transaction.</returns>
/// <remarks>
/// Shorthand for <c>Stream.orElseAll</c>. See it for the full contract.
///
/// This is <c>mergeAll</c> with a function that keeps the left value. The result drops the values
/// from the subsequent streams, and does not defer them.
/// </remarks>
let inline orElseAllS streams = Stream.orElseAll streams

/// <summary>
/// Creates a behavior with a value that never changes.
/// </summary>
/// <param name="value">The value the behavior always has.</param>
/// <returns>A behavior whose value is always <paramref name="value" />.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.constant</c>. See it for the full contract.
/// </remarks>
let inline constantB value = Behavior.constant value

/// <summary>
/// Creates a behavior with a value that never changes, computed on first use.
/// </summary>
/// <param name="value">The lazy value the behavior always has.</param>
/// <returns>A behavior whose value is always the value of <paramref name="value" />.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.constantLazy</c>. See it for the full contract.
///
/// For a constant with a high cost, or that is not available when the code builds the graph.
/// The code forces the value only at the first sample of the behavior.
/// </remarks>
let inline constantLazyB value = Behavior.constantLazy value

/// <summary>
/// Creates a behavior sink that keeps the last value of more than one <c>send</c> in one
/// transaction.
/// </summary>
/// <param name="initialValue">The value the behavior holds until something is sent.</param>
/// <returns>A new behavior sink.</returns>
/// <remarks>
/// Shorthand for <c>BehaviorSink.create</c>. See it for the full contract.
/// </remarks>
let inline sinkB initialValue = BehaviorSink.create initialValue

/// <summary>
/// Creates a behavior sink that combines the values of more than one <c>send</c> in one transaction.
/// </summary>
/// <param name="initialValue">The value the behavior holds until something is sent.</param>
/// <param name="coalesce">
/// Puts two values from the same transaction together. It receives the value from before this
/// send and the new value, in that sequence.
/// </param>
/// <returns>A new behavior sink.</returns>
/// <remarks>
/// Shorthand for <c>BehaviorSink.createWithCoalesce</c>. See it for the full contract.
/// </remarks>
let inline sinkWithCoalesceB initialValue coalesce =
    BehaviorSink.createWithCoalesce initialValue coalesce

/// <summary>
/// Sends a value, changing what the behavior holds.
/// </summary>
/// <param name="a">The value to send.</param>
/// <param name="behaviorSink">The behavior sink to send it to.</param>
/// <remarks>
/// Shorthand for <c>BehaviorSink.send</c>. See it for the full contract.
///
/// A call from a listener callback throws an exception.
/// </remarks>
let inline sendB a behaviorSink = BehaviorSink.send a behaviorSink

/// <summary>
/// Builds a behavior which refers to itself, closing the loop in one transaction.
/// </summary>
/// <param name="f">
/// Given the forward reference, returns a struct tuple of the behavior it stands for and
/// anything else the caller wants back out.
/// </param>
/// <returns>
/// A struct tuple of the behavior that closed the forward reference, and whatever
/// <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.loop</c>. See it for the full contract.
///
/// A behavior that refers to itself needs a forward reference. The code makes the reference
/// before the value that it refers to. The reference and its resolution must occur in one
/// transaction, which this opens when no transaction is open.
///
/// Use <c>loopWithNoCaptures</c> where the caller needs only the behavior.
/// </remarks>
let inline loopB f = Behavior.loop f

/// <summary>
/// Builds a self-referential behavior where the caller needs only the behavior.
/// </summary>
/// <param name="f">Given the forward reference, returns the behavior it stands for.</param>
/// <returns>The behavior that closed the forward reference.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.loopWithNoCaptures</c>. See it for the full contract.
///
/// <c>loop</c> where the caller needs more than the behavior from the loop.
/// </remarks>
let inline loopWithNoCapturesB f = Behavior.loopWithNoCaptures f

/// <summary>
/// Gets a behavior's current value.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <returns>The value the behavior has at this moment.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.sample</c>. See it for the full contract.
///
/// The functions that the primitives give to a stream can call this, and there it is the same as
/// a snapshot. With no transaction it opens its own transaction, thus a read never gives a value
/// in the middle of an update.
/// </remarks>
let inline sampleB behavior = Behavior.sample behavior

/// <summary>
/// Gets the current value of a behavior, and does not force it.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <returns>A lazy value which yields what the behavior held at the moment of this call.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.sampleLazy</c>. See it for the full contract.
///
/// This code sets the value now and calculates it after this. This is necessary for
/// <c>Stream.holdLazy</c> and the loop constructs. At the moment that the code closes a loop,
/// the value is not known, but the moment of the value is known.
/// </remarks>
let inline sampleLazyB behavior = Behavior.sampleLazy behavior

/// <summary>
/// Applies a behavior of functions to a behavior of values.
/// </summary>
/// <param name="f">The behavior holding the function to apply.</param>
/// <param name="behavior">The behavior holding the value to apply it to.</param>
/// <returns>
/// A behavior whose value is the result of the current function in <paramref name="f" /> on the
/// input behavior's current value.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.apply</c>. See it for the full contract.
///
/// This is the primitive of all the <c>lift</c> functions. Use <c>lift2</c> and the other
/// <c>lift</c> functions first. Use this function only for the conditions that they do not cover.
/// </remarks>
let inline applyB f behavior = Behavior.apply f behavior

/// <summary>
/// Transforms a behavior with a function.
/// </summary>
/// <param name="f">Transforms the value.</param>
/// <param name="behavior">The behavior to transform.</param>
/// <returns>
/// A behavior whose value is the result of <paramref name="f" /> on the input behavior's current
/// value.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.map</c>. See it for the full contract.
///
/// <paramref name="f" /> can build FRP logic, and it can sample a behavior and a cell. It must
/// be pure in each other operation, because this code can call it more than one time for one
/// input.
/// </remarks>
let inline mapB f behavior = Behavior.map f behavior

/// <summary>
/// Combines two behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the two current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <returns>
/// A behavior whose value is the result of <paramref name="f" /> on the current values of the two
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.lift2</c>. See it for the full contract.
///
/// There is no glitch. When some of the inputs change in one transaction, the result updates one
/// time, with each new value, and not one time for each input.
/// </remarks>
let inline lift2B f (behavior, behavior2) = Behavior.lift2 f (behavior, behavior2)

/// <summary>
/// Combines three behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the three current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <returns>
/// A behavior whose value is the result of <paramref name="f" /> on the current values of the three
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.lift3</c>. See it for the full contract.
///
/// There is no glitch. When some of the inputs change in one transaction, the result updates one
/// time, with each new value, and not one time for each input.
/// </remarks>
let inline lift3B f (behavior, behavior2, behavior3) =
    Behavior.lift3 f (behavior, behavior2, behavior3)

/// <summary>
/// Combines four behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the four current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <returns>
/// A behavior whose value is the result of <paramref name="f" /> on the current values of the four
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.lift4</c>. See it for the full contract.
///
/// There is no glitch. When some of the inputs change in one transaction, the result updates one
/// time, with each new value, and not one time for each input.
/// </remarks>
let inline lift4B f (behavior, behavior2, behavior3, behavior4) =
    Behavior.lift4 f (behavior, behavior2, behavior3, behavior4)

/// <summary>
/// Combines five behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the five current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <param name="behavior5">The fifth behavior.</param>
/// <returns>
/// A behavior whose value is the result of <paramref name="f" /> on the current values of the five
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.lift5</c>. See it for the full contract.
///
/// There is no glitch. When some of the inputs change in one transaction, the result updates one
/// time, with each new value, and not one time for each input.
/// </remarks>
let inline lift5B f (behavior, behavior2, behavior3, behavior4, behavior5) =
    Behavior.lift5 f (behavior, behavior2, behavior3, behavior4, behavior5)

/// <summary>
/// Combines six behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the six current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <param name="behavior5">The fifth behavior.</param>
/// <param name="behavior6">The sixth behavior.</param>
/// <returns>
/// A behavior whose value is the result of <paramref name="f" /> on the current values of the six
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.lift6</c>. See it for the full contract.
///
/// There is no glitch. When some of the inputs change in one transaction, the result updates one
/// time, with each new value, and not one time for each input.
/// </remarks>
let inline lift6B f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6) =
    Behavior.lift6 f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6)

/// <summary>
/// Combines seven behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the seven current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <param name="behavior5">The fifth behavior.</param>
/// <param name="behavior6">The sixth behavior.</param>
/// <param name="behavior7">The seventh behavior.</param>
/// <returns>
/// A behavior whose value is the result of <paramref name="f" /> on the current values of the seven
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.lift7</c>. See it for the full contract.
///
/// There is no glitch. When some of the inputs change in one transaction, the result updates one
/// time, with each new value, and not one time for each input.
/// </remarks>
let inline lift7B f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6, behavior7) =
    Behavior.lift7 f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6, behavior7)

/// <summary>
/// Combines eight behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the eight current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <param name="behavior5">The fifth behavior.</param>
/// <param name="behavior6">The sixth behavior.</param>
/// <param name="behavior7">The seventh behavior.</param>
/// <param name="behavior8">The eighth behavior.</param>
/// <returns>
/// A behavior whose value is the result of <paramref name="f" /> on the current values of the eight
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.lift8</c>. See it for the full contract.
///
/// There is no glitch. When some of the inputs change in one transaction, the result updates one
/// time, with each new value, and not one time for each input.
/// </remarks>
let inline lift8B f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6, behavior7, behavior8) =
    Behavior.lift8 f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6, behavior7, behavior8)

/// <summary>
/// Combines any number of behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the current values, in the sequence of the behaviors.</param>
/// <param name="behaviors">The behaviors to put together.</param>
/// <returns>A behavior whose value is the result of <paramref name="f" /> on all the current values.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.liftAll</c>. See it for the full contract.
///
/// The <c>lift</c> family where the number of inputs is not known until run time. Glitch-free
/// in the same manner. At each count of the inputs that change in one transaction, the result
/// updates one time.
/// </remarks>
let inline liftAllB f behaviors = Behavior.liftAll f behaviors

/// <summary>
/// Unwraps a behavior of behaviors into a behavior which follows whichever one is current.
/// </summary>
/// <param name="behavior">The behavior that holds a second behavior.</param>
/// <returns>A behavior whose value is the current value of the currently held behavior.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.switchB</c>. See it for the full contract.
///
/// This is how a graph changes its shape at run time. The external behavior selects the
/// internal behavior to follow.
/// </remarks>
let inline switchBB behavior = Behavior.switchB behavior

/// <summary>
/// Unwraps a behavior of cells into a cell which follows whichever one is current.
/// </summary>
/// <param name="behavior">The behavior holding a cell.</param>
/// <returns>A cell whose value is the current value of the currently held cell.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.switchC</c>. See it for the full contract.
/// </remarks>
let inline switchCB behavior = Behavior.switchC behavior

/// <summary>
/// Unwraps a behavior of streams into a stream which fires whatever the current one fires.
/// </summary>
/// <param name="behavior">The behavior holding a stream.</param>
/// <returns>A stream firing the firings of the currently held stream.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.switchS</c>. See it for the full contract.
///
/// In the transaction where the behavior changes, the result takes the firing from the stream
/// at the start of that transaction. It does not use the firing from the new stream.
/// </remarks>
let inline switchSB behavior = Behavior.switchS behavior

/// <summary>
/// Creates a cell with a value that never changes.
/// </summary>
/// <param name="value">The value the cell always has.</param>
/// <returns>A cell whose value is always <paramref name="value" />.</returns>
/// <remarks>
/// Shorthand for <c>Cell.constant</c>. See it for the full contract.
/// </remarks>
let inline constantC value = Cell.constant value

/// <summary>
/// Creates a cell with a value that never changes, computed on first use.
/// </summary>
/// <param name="value">The lazy value the cell always has.</param>
/// <returns>A cell whose value is always the value of <paramref name="value" />.</returns>
/// <remarks>
/// Shorthand for <c>Cell.constantLazy</c>. See it for the full contract.
///
/// For a constant with a high cost, or that is not available when the code builds the graph.
/// The code forces the value only at the first sample of the cell.
/// </remarks>
let inline constantLazyC value = Cell.constantLazy value

/// <summary>
/// Creates a cell sink that keeps the last value of more than one <c>send</c> in one transaction.
/// </summary>
/// <param name="initialValue">The value the cell holds until something is sent.</param>
/// <returns>A new cell sink.</returns>
/// <remarks>
/// Shorthand for <c>CellSink.create</c>. See it for the full contract.
/// </remarks>
let inline sinkC initialValue = CellSink.create initialValue

/// <summary>
/// Creates a cell sink that combines the values of more than one <c>send</c> in one transaction.
/// </summary>
/// <param name="initialValue">The value the cell holds until something is sent.</param>
/// <param name="coalesce">
/// Puts two values from the same transaction together. It receives the value from before this
/// send and the new value, in that sequence.
/// </param>
/// <returns>A new cell sink.</returns>
/// <remarks>
/// Shorthand for <c>CellSink.createWithCoalesce</c>. See it for the full contract.
/// </remarks>
let inline sinkWithCoalesceC initialValue coalesce =
    CellSink.createWithCoalesce initialValue coalesce

/// <summary>
/// Sends a value, changing what the cell holds.
/// </summary>
/// <param name="a">The value to send.</param>
/// <param name="cellSink">The cell sink to send it to.</param>
/// <remarks>
/// Shorthand for <c>CellSink.send</c>. See it for the full contract.
///
/// A call from a listener callback throws an exception.
/// </remarks>
let inline sendC a cellSink = CellSink.send a cellSink

/// <summary>
/// Builds a cell which refers to itself, closing the loop in one transaction.
/// </summary>
/// <param name="f">
/// Given the forward reference, returns a struct tuple of the cell it stands for and
/// anything else the caller wants back out.
/// </param>
/// <returns>
/// A struct tuple of the cell that closed the forward reference, and whatever
/// <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.loop</c>. See it for the full contract.
///
/// A cell that refers to itself needs a forward reference. The code makes the reference before
/// the value that it refers to. The reference and its resolution must occur in one transaction,
/// which this opens when no transaction is open.
///
/// Use <c>loopWithNoCaptures</c> where the caller needs only the cell.
/// </remarks>
let inline loopC f = Cell.loop f

/// <summary>
/// Builds a self-referential cell where the caller needs only the cell.
/// </summary>
/// <param name="f">Given the forward reference, returns the cell it stands for.</param>
/// <returns>The cell that closed the forward reference.</returns>
/// <remarks>
/// Shorthand for <c>Cell.loopWithNoCaptures</c>. See it for the full contract.
///
/// <c>loop</c> where the caller needs more than the cell from the loop.
/// </remarks>
let inline loopWithNoCapturesC f = Cell.loopWithNoCaptures f

/// <summary>
/// Builds a value which can refer to itself, along with anything else worth keeping from its
/// construction.
/// </summary>
/// <param name="f">
/// Given the forward reference, returns a struct tuple of the value it stands for and anything
/// else the caller wants back out.
/// </param>
/// <returns>
/// A struct tuple of the value that closed the forward reference, and whatever
/// <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
/// Shorthand for <c>ForwardReference.create</c>. See it for the full contract.
///
/// The cell loop with one value. A constant cell closes the loop that <paramref name="f" />
/// gets, thus the reference resolves to that value and never changes.
///
/// Use <c>forwardReferenceWithNoCaptures</c> where the caller needs only the value.
/// </remarks>
let inline forwardReference f = ForwardReference.create f

/// <summary>
/// Builds a value which can refer to itself, where the caller needs only the value.
/// </summary>
/// <param name="f">Given the forward reference, returns the value it stands for.</param>
/// <returns>The value that closed the forward reference.</returns>
/// <remarks>
/// Shorthand for <c>ForwardReference.createWithNoCaptures</c>. See it for the full contract.
///
/// <c>forwardReference</c> where the caller needs more than the value from the construction.
/// </remarks>
let inline forwardReferenceWithNoCaptures f = ForwardReference.createWithNoCaptures f

/// <summary>
/// Gets a cell's current value.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <returns>The value the cell has at this moment.</returns>
/// <remarks>
/// Shorthand for <c>Cell.sample</c>. See it for the full contract.
///
/// The functions that the primitives give to a stream can call this, and there it is the same as
/// a snapshot. With no transaction it opens its own transaction, thus a read never gives a value
/// in the middle of an update.
/// </remarks>
let inline sampleC cell = Cell.sample cell

/// <summary>
/// Gets the current value of a cell, and does not force it.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <returns>A lazy value which yields what the cell held at the moment of this call.</returns>
/// <remarks>
/// Shorthand for <c>Cell.sampleLazy</c>. See it for the full contract.
///
/// This code sets the value now and calculates it after this. This is necessary for the loop
/// constructs. At the moment that the code closes a loop, the value is not known, but the
/// moment of the value is known.
/// </remarks>
let inline sampleLazyC cell = Cell.sampleLazy cell

/// <summary>
/// Gets a stream firing the new value of a cell each time it changes.
/// </summary>
/// <param name="cell">The cell to monitor.</param>
/// <returns>A stream firing the updated value, in the transaction the update happened in.</returns>
/// <remarks>
/// Shorthand for <c>Cell.updates</c>. See it for the full contract.
///
/// Does not fire for the value the cell starts with - only for changes. Use <c>values</c> to
/// get that initial value as a firing too.
/// </remarks>
let inline updatesC cell = Cell.updates cell

/// <summary>
/// Gets a stream firing the current value of the cell immediately, and its new value on each change.
/// </summary>
/// <param name="cell">The cell to monitor.</param>
/// <returns>
/// A stream which fires the current value in the transaction of this call, and then the
/// updated value on each change.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.values</c>. See it for the full contract.
///
/// The first firing occurs in the transaction of this call. Thus, a caller must call this in
/// <c>Transaction.run</c> for a listener to see that firing. A listener that attaches after that,
/// in a subsequent transaction, does not get it. This is why most code puts the construction of a
/// graph in a transaction.
/// </remarks>
let inline valuesC cell = Cell.values cell

/// <summary>
/// Views a cell as a behavior.
/// </summary>
/// <param name="cell">The cell to view.</param>
/// <returns>The same value, seen as a behavior, without the stream of its changes.</returns>
/// <remarks>
/// Shorthand for <c>Cell.asBehavior</c>. See it for the full contract.
///
/// This creates nothing and changes nothing. A cell is a behavior with updates attached. Use
/// this to give a cell to code that takes a <c>Behavior</c>.
/// </remarks>
let inline asBehaviorC cell = Cell.asBehavior cell

/// <summary>
/// Listens for changes without keeping the cell alive.
/// </summary>
/// <param name="handler">Run with each new value.</param>
/// <param name="cell">The cell to listen to.</param>
/// <returns>A weak listener. <c>WeakListener.unlisten</c> stops it.</returns>
/// <remarks>
/// Shorthand for <c>Cell.listen</c>. See it for the full contract.
///
/// The listener stops when a GC collects the cell, thus this is the correct selection where there
/// is no clear moment to stop the listener. Keep the handle from this call in a field of the
/// object doing the listening, and the two go away together. Where other code must keep the cell
/// alive for as long as something is listening, use <c>listenStrong</c>.
///
/// Fires the current value immediately, in the transaction of this call.
/// </remarks>
let inline listenC handler cell = Cell.listen handler cell

/// <summary>
/// Listens for changes, keeping the cell alive while the listener lives.
/// </summary>
/// <param name="handler">Run with each new value.</param>
/// <param name="cell">The cell to listen to.</param>
/// <returns>
/// A strong listener. <c>StrongListener.unlisten</c> stops it, and a disposal also stops it.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.listenStrong</c>. See it for the full contract.
///
/// The listener roots the cell, so the graph behind it stays alive for as long as the returned
/// handle is reachable. Keep the handle and stop it when finished, or use <c>listen</c>
/// where there is no good moment to do that.
///
/// Fires the current value immediately, in the transaction of this call. The handler runs
/// with the transaction lock held, thus it must return quickly.
/// </remarks>
let inline listenStrongC handler cell = Cell.listenStrong handler cell

/// <summary>
/// Applies a cell of functions to a cell of values.
/// </summary>
/// <param name="f">The cell holding the function to apply.</param>
/// <param name="cell">The cell holding the value to apply it to.</param>
/// <returns>
/// A cell whose value is the result of the current function in <paramref name="f" /> on the input
/// cell's current value.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.apply</c>. See it for the full contract.
///
/// This is the primitive of all the <c>lift</c> functions. Use <c>lift2</c> and the other
/// <c>lift</c> functions first. Use this function only for the conditions that they do not cover.
/// </remarks>
let inline applyC f cell = Cell.apply f cell

/// <summary>
/// Transforms a cell with a function.
/// </summary>
/// <param name="f">Transforms the value.</param>
/// <param name="cell">The cell to transform.</param>
/// <returns>A cell whose value is the result of <paramref name="f" /> on the input cell's current value.</returns>
/// <remarks>
/// Shorthand for <c>Cell.map</c>. See it for the full contract.
///
/// <paramref name="f" /> can build FRP logic, and it can sample a behavior and a cell. It must
/// be pure in each other operation, because this code can call it more than one time for one
/// input.
/// </remarks>
let inline mapC f cell = Cell.map f cell

/// <summary>
/// Combines two cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the two current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <returns>
/// A cell whose value is the result of <paramref name="f" /> on the current values of the two
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.lift2</c>. See it for the full contract.
///
/// There is no glitch. When some of the inputs change in one transaction, the result updates one
/// time, with each new value, and not one time for each input.
/// </remarks>
let inline lift2C f (cell, cell2) = Cell.lift2 f (cell, cell2)

/// <summary>
/// Combines three cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the three current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <returns>
/// A cell whose value is the result of <paramref name="f" /> on the current values of the three
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.lift3</c>. See it for the full contract.
///
/// There is no glitch. When some of the inputs change in one transaction, the result updates one
/// time, with each new value, and not one time for each input.
/// </remarks>
let inline lift3C f (cell, cell2, cell3) = Cell.lift3 f (cell, cell2, cell3)

/// <summary>
/// Combines four cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the four current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <param name="cell4">The fourth cell.</param>
/// <returns>
/// A cell whose value is the result of <paramref name="f" /> on the current values of the four
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.lift4</c>. See it for the full contract.
///
/// There is no glitch. When some of the inputs change in one transaction, the result updates one
/// time, with each new value, and not one time for each input.
/// </remarks>
let inline lift4C f (cell, cell2, cell3, cell4) =
    Cell.lift4 f (cell, cell2, cell3, cell4)

/// <summary>
/// Combines five cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the five current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <param name="cell4">The fourth cell.</param>
/// <param name="cell5">The fifth cell.</param>
/// <returns>
/// A cell whose value is the result of <paramref name="f" /> on the current values of the five
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.lift5</c>. See it for the full contract.
///
/// There is no glitch. When some of the inputs change in one transaction, the result updates one
/// time, with each new value, and not one time for each input.
/// </remarks>
let inline lift5C f (cell, cell2, cell3, cell4, cell5) =
    Cell.lift5 f (cell, cell2, cell3, cell4, cell5)

/// <summary>
/// Combines six cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the six current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <param name="cell4">The fourth cell.</param>
/// <param name="cell5">The fifth cell.</param>
/// <param name="cell6">The sixth cell.</param>
/// <returns>
/// A cell whose value is the result of <paramref name="f" /> on the current values of the six
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.lift6</c>. See it for the full contract.
///
/// There is no glitch. When some of the inputs change in one transaction, the result updates one
/// time, with each new value, and not one time for each input.
/// </remarks>
let inline lift6C f (cell, cell2, cell3, cell4, cell5, cell6) =
    Cell.lift6 f (cell, cell2, cell3, cell4, cell5, cell6)

/// <summary>
/// Combines seven cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the seven current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <param name="cell4">The fourth cell.</param>
/// <param name="cell5">The fifth cell.</param>
/// <param name="cell6">The sixth cell.</param>
/// <param name="cell7">The seventh cell.</param>
/// <returns>
/// A cell whose value is the result of <paramref name="f" /> on the current values of the seven
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.lift7</c>. See it for the full contract.
///
/// There is no glitch. When some of the inputs change in one transaction, the result updates one
/// time, with each new value, and not one time for each input.
/// </remarks>
let inline lift7C f (cell, cell2, cell3, cell4, cell5, cell6, cell7) =
    Cell.lift7 f (cell, cell2, cell3, cell4, cell5, cell6, cell7)

/// <summary>
/// Combines eight cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the eight current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <param name="cell3">The third cell.</param>
/// <param name="cell4">The fourth cell.</param>
/// <param name="cell5">The fifth cell.</param>
/// <param name="cell6">The sixth cell.</param>
/// <param name="cell7">The seventh cell.</param>
/// <param name="cell8">The eighth cell.</param>
/// <returns>
/// A cell whose value is the result of <paramref name="f" /> on the current values of the eight
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.lift8</c>. See it for the full contract.
///
/// There is no glitch. When some of the inputs change in one transaction, the result updates one
/// time, with each new value, and not one time for each input.
/// </remarks>
let inline lift8C f (cell, cell2, cell3, cell4, cell5, cell6, cell7, cell8) =
    Cell.lift8 f (cell, cell2, cell3, cell4, cell5, cell6, cell7, cell8)

/// <summary>
/// Suppresses updates whose value the given function considers equal to the last one that got
/// through.
/// </summary>
/// <param name="compare">Gives true when this code must read two values as equal.</param>
/// <param name="cell">The cell to calm.</param>
/// <returns>A cell which updates only when the value actually changed.</returns>
/// <remarks>
/// Shorthand for <c>Cell.calmWithCompare</c>. See it for the full contract.
///
/// A suppressed update is not a missing update. The cell takes the new value, and the next
/// comparer call reads the last value that the cell published.
/// </remarks>
let inline calmWithCompareC compare cell = Cell.calmWithCompare compare cell

/// <summary>
/// Suppresses updates whose value the given comparer considers equal to the last one that got
/// through.
/// </summary>
/// <param name="equalityComparer">Gives true when two values are equal.</param>
/// <param name="cell">The cell to calm.</param>
/// <returns>A cell which updates only when the value actually changed.</returns>
/// <remarks>
/// Shorthand for <c>Cell.calmWithEqualityComparer</c>. See it for the full contract.
///
/// A suppressed update is not a missing update. The cell takes the new value, and the next
/// comparer call reads the last value that the cell published.
/// </remarks>
let inline calmWithEqualityComparerC equalityComparer cell =
    Cell.calmWithEqualityComparer equalityComparer cell

/// <summary>
/// Suppresses updates equal, by F#'s structural equality, to the last one that got through.
/// </summary>
/// <param name="cell">The cell to calm.</param>
/// <returns>A cell which updates only when the value actually changed.</returns>
/// <remarks>
/// Shorthand for <c>Cell.calm</c>. See it for the full contract.
///
/// A suppressed update is not a missing update. The cell takes the new value, and the next
/// comparer call reads the last value that the cell published.
///
/// Uses <c>=</c>, so for a type without meaningful structural equality use
/// the alternative <c>calmWithCompare</c>.
/// </remarks>
let inline calmC cell = Cell.calm cell

/// <summary>
/// Combines any number of cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the current values, in the sequence of the cells.</param>
/// <param name="cells">The cells to put together.</param>
/// <returns>A cell whose value is the result of <paramref name="f" /> on all the current values.</returns>
/// <remarks>
/// Shorthand for <c>Cell.liftAll</c>. See it for the full contract.
///
/// The <c>lift</c> family where the number of inputs is not known until run time. Glitch-free
/// in the same manner. At each count of the inputs that change in one transaction, the result
/// updates one time.
/// </remarks>
let inline liftAllC f cells = Cell.liftAll f cells

/// <summary>
/// Unwraps a cell of behaviors into a behavior which follows whichever one is current.
/// </summary>
/// <param name="cell">The cell holding a behavior.</param>
/// <returns>A behavior whose value is the current value of the currently held behavior.</returns>
/// <remarks>
/// Shorthand for <c>Cell.switchB</c>. See it for the full contract.
/// </remarks>
let inline switchB cell = Cell.switchB cell

/// <summary>
/// Unwraps a cell of cells into a cell which follows whichever one is current.
/// </summary>
/// <param name="cell">The cell that holds a second cell.</param>
/// <returns>A cell whose value is the current value of the currently held cell.</returns>
/// <remarks>
/// Shorthand for <c>Cell.switchC</c>. See it for the full contract.
///
/// This is how a graph changes its shape at run time. The external cell selects the internal
/// cell to follow.
/// </remarks>
let inline switchC cell = Cell.switchC cell

/// <summary>
/// Unwraps a cell of streams into a stream which fires whatever the current one fires.
/// </summary>
/// <param name="cell">The cell holding a stream.</param>
/// <returns>A stream firing the firings of the currently held stream.</returns>
/// <remarks>
/// Shorthand for <c>Cell.switchS</c>. See it for the full contract.
///
/// In the transaction where the cell changes, the result takes the firing from the stream at
/// the start of that transaction. It does not use the firing from the new stream.
/// </remarks>
let inline switchS cell = Cell.switchS cell
