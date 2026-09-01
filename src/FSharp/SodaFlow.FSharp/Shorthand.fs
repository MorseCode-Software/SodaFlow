/// <summary>
///     Short names for the whole library, opened automatically.
/// </summary>
/// <remarks>
///     Every binding here is an alias for one in <c>Stream</c>, <c>Cell</c>, <c>Behavior</c> or one
///     of their sinks, with a suffix naming which: <c>S</c> for stream, <c>C</c> for cell, <c>B</c>
///     for behavior, <c>T</c> for transaction and <c>L</c> for listener. The suffix is what
///     disambiguates operations that exist on more than one of them - <c>mapS</c>, <c>mapC</c> and
///     <c>mapB</c> - and lets all of them be used unqualified in the same scope.
///
///     Where an operation takes one kind of value but produces another the suffix names the
///     argument, not the result: <c>snapshotC</c> samples a cell, <c>holdS</c> holds a stream into
///     a cell, and <c>switchSB</c> takes a behavior of streams. The module is <c>AutoOpen</c>, so
///     opening <c>SodaFlow</c> is enough to bring these into scope.
/// </remarks>
[<AutoOpen>]
module SodaFlow.Shorthand

/// <summary>
/// Returns whether a transaction is currently running on this thread.
/// </summary>
/// <returns><c>true</c> if there is a current transaction, and <c>false</c> otherwise.</returns>
/// <remarks>
/// Shorthand for <c>Transaction.isActive</c>; see it for the full contract.
/// </remarks>
let inline isActiveT () = Transaction.isActive ()
/// <summary>
/// Runs a function inside a single transaction and returns its result.
/// </summary>
/// <param name="f">The function to run.</param>
/// <returns>Whatever <paramref name="f" /> returned.</returns>
/// <remarks>
/// Shorthand for <c>Transaction.run</c>; see it for the full contract.
///
/// Rarely needed for a single operation, since every primitive opens a transaction of its own
/// where it needs one. It is for making several operations atomic together.
///
/// Build the graph inside one of these so that no first firing is missed - particularly where
/// <c>Cell.values</c> is involved, which always fires immediately. It is also required for
/// <c>Stream.loop</c>, <c>Cell.loop</c> and <c>Behavior.loop</c>, which must be created and
/// closed within one transaction.
/// </remarks>
let inline runT f = Transaction.run f
/// <summary>
/// Registers an action to run whenever a transaction starts.
/// </summary>
/// <param name="a">The action to run at the start of every transaction.</param>
/// <remarks>
/// Shorthand for <c>Transaction.onStart</c>; see it for the full contract.
///
/// The action may start transactions itself without the hooks running recursively. This exists
/// for implementing a timer system - it is how <c>SodaFlow.Time</c> delivers alarms - and is
/// rarely what application code wants.
/// </remarks>
let inline onStartT a = Transaction.onStart a
/// <summary>
/// Runs an action once the current transaction has closed, or immediately if none is running.
/// </summary>
/// <param name="a">The action to run.</param>
/// <remarks>
/// Shorthand for <c>Transaction.post</c>; see it for the full contract.
///
/// The action still runs under the transaction lock, while the transaction is closing, so it
/// is subject to the same guidance as a listener callback: return promptly.
/// </remarks>
let inline postT a = Transaction.post a

/// <summary>
/// Stops listening.
/// </summary>
/// <param name="listener">The listener to stop.</param>
/// <remarks>
/// Shorthand for <c>Listener.unlisten</c>; see it for the full contract.
///
/// Safe to call more than once; later calls do nothing.
/// </remarks>
let inline unlistenL listener = Listener.unlisten listener
/// <summary>
/// Stops listening.
/// </summary>
/// <param name="listener">The listener to stop.</param>
/// <remarks>
/// Shorthand for <c>WeakListener.unlisten</c>; see it for the full contract.
///
/// Safe to call more than once; later calls do nothing.
/// </remarks>
let inline unlistenWeakL listener = WeakListener.unlisten listener
/// <summary>
/// Stops listening.
/// </summary>
/// <param name="listener">The listener to stop.</param>
/// <remarks>
/// Shorthand for <c>StrongListener.unlisten</c>; see it for the full contract.
///
/// Safe to call more than once; later calls do nothing. Disposing the listener does the same
/// thing.
/// </remarks>
let inline unlistenStrongL listener = StrongListener.unlisten listener

/// <summary>
/// Creates a stream which never fires.
/// </summary>
/// <typeparam name="'a">The type the stream would fire, were it ever to fire.</typeparam>
/// <returns>A stream that never fires.</returns>
/// <remarks>
/// Shorthand for <c>Stream.never</c>; see it for the full contract.
///
/// The identity for <c>orElse</c>, and what to return from a branch which has nothing to fire.
/// </remarks>
let inline neverS<'a> () = Stream.never<'a> ()
/// <summary>
/// Creates a stream sink which throws if <c>send</c> is called more than once in a transaction.
/// </summary>
/// <typeparam name="'a">The type of the values the stream sink fires.</typeparam>
/// <returns>A new stream sink.</returns>
/// <remarks>
/// Shorthand for <c>StreamSink.create</c>; see it for the full contract.
///
/// Two sends in one transaction is usually a mistake rather than an intent, so it is reported
/// rather than silently resolved. Use <c>createWithCoalesce</c> where it is intended.
/// </remarks>
let inline sinkS<'a> () = StreamSink.create<'a> ()
/// <summary>
/// Creates a stream sink which combines values when <c>send</c> is called more than once in a
/// single transaction.
/// </summary>
/// <param name="coalesce">
/// Combines two values sent in the same transaction. Called with the value already
/// accumulated and the value just sent, in that order.
/// </param>
/// <returns>A new stream sink.</returns>
/// <remarks>
/// Shorthand for <c>StreamSink.createWithCoalesce</c>; see it for the full contract.
///
/// A stream fires at most once per transaction, which is what this preserves: whatever is sent
/// within one transaction is folded down to the single value that fires.
/// </remarks>
let inline sinkWithCoalesceS coalesce = StreamSink.createWithCoalesce coalesce
/// <summary>
/// Creates a cell stream sink which throws if <c>StreamSink.send</c> is called more than once in
/// a transaction.
/// </summary>
/// <typeparam name="'a">The type of the values the cell stream sink fires.</typeparam>
/// <returns>A new cell stream sink.</returns>
/// <remarks>
/// Shorthand for <c>CellStreamSink.create</c>; see it for the full contract.
/// </remarks>
let inline sinkCS<'a> () = CellStreamSink.create<'a> ()
/// <summary>
/// Creates a cell stream sink which combines values when <c>StreamSink.send</c> is called more
/// than once in a single transaction.
/// </summary>
/// <param name="coalesce">
/// Combines two values sent in the same transaction. Called with the value already
/// accumulated and the value just sent, in that order.
/// </param>
/// <returns>A new cell stream sink.</returns>
/// <remarks>
/// Shorthand for <c>CellStreamSink.createWithCoalesce</c>; see it for the full contract.
/// </remarks>
let inline sinkWithCoalesceCS coalesce = CellStreamSink.createWithCoalesce coalesce
/// <summary>
/// Sends a value, firing the stream sink.
/// </summary>
/// <param name="a">The value to send.</param>
/// <param name="streamSink">The stream sink to send it to.</param>
/// <remarks>
/// Shorthand for <c>StreamSink.send</c>; see it for the full contract.
///
/// Must not be called from inside a listener callback; doing so throws. Sinks are for getting
/// I/O into FRP, not for building new primitives out of.
///
/// Sending twice in one transaction throws unless the sink was created with
/// <c>createWithCoalesce</c>.
/// </remarks>
let inline sendS a streamSink = StreamSink.send a streamSink
/// <summary>
/// Builds a stream which refers to itself, closing the loop within one transaction.
/// </summary>
/// <param name="f">
/// Given the forward reference, returns a struct tuple of the stream it stands for and
/// anything else the caller wants back out.
/// </param>
/// <returns>
/// A struct tuple of the stream the forward reference was closed with, and whatever
/// <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.loop</c>; see it for the full contract.
///
/// A stream defined in terms of itself needs a forward reference to exist before the stream it
/// refers to does. Both the reference and its resolution must happen in a single transaction,
/// which this opens if none is running.
///
/// Use <c>loopWithNoCaptures</c> where nothing but the stream itself is needed.
/// </remarks>
let inline loopS f = Stream.loop f
/// <summary>
/// Builds a self-referential stream where nothing but the stream itself is wanted back.
/// </summary>
/// <param name="f">Given the forward reference, returns the stream it stands for.</param>
/// <returns>The stream the forward reference was closed with.</returns>
/// <remarks>
/// Shorthand for <c>Stream.loopWithNoCaptures</c>; see it for the full contract.
///
/// <c>loop</c> where something more than the stream needs to escape the loop.
/// </remarks>
let inline loopWithNoCapturesS f = Stream.loopWithNoCaptures f
/// <summary>
/// Listens for firings without keeping the stream alive.
/// </summary>
/// <param name="handler">Run with each fired value.</param>
/// <param name="stream">The stream to listen to.</param>
/// <returns>A weak listener, which may be stopped with <c>WeakListener.unlisten</c>.</returns>
/// <remarks>
/// Shorthand for <c>Stream.listen</c>; see it for the full contract.
///
/// The listener stops on its own once the stream is collected, which makes this the right
/// choice where there is no clean moment to stop listening: hold the returned handle as a field
/// of the object doing the listening, and the two go away together. Where the stream should be
/// kept alive for as long as something is listening, use <c>listenStrong</c>.
/// </remarks>
let inline listenS handler stream = Stream.listen handler stream
/// <summary>
/// Listens for firings, keeping the stream alive while the listener lives.
/// </summary>
/// <param name="handler">Run with each fired value.</param>
/// <param name="stream">The stream to listen to.</param>
/// <returns>
/// A strong listener, which may be stopped with <c>StrongListener.unlisten</c> or disposed.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.listenStrong</c>; see it for the full contract.
///
/// The listener roots the stream, so the graph behind it stays alive for as long as the
/// returned handle is reachable. Keep the handle and stop it when finished, or use
/// <c>listen</c> where there is no good moment to do that.
///
/// The handler runs under the transaction lock, so it should return promptly; hand
/// long-running or blocking work to another thread.
/// </remarks>
let inline listenStrongS handler stream = Stream.listenStrong handler stream
/// <summary>
/// Ties a listener to the lifetime of a stream, so the listener lives while the stream does.
/// </summary>
/// <param name="listener">The listener to attach.</param>
/// <param name="stream">The stream to attach it to.</param>
/// <returns>The same stream, now keeping <paramref name="listener" /> alive.</returns>
/// <remarks>
/// Shorthand for <c>Stream.attachListener</c>; see it for the full contract.
///
/// For building a primitive whose returned stream depends on internal wiring that nothing else
/// holds a reference to - the timer system does exactly this with the listener that watches
/// its alarm cell. Without the attachment the wiring is collected and the returned stream
/// quietly stops firing.
/// </remarks>
let inline attachListenerS listener stream = Stream.attachListener listener stream
/// <summary>
/// Listens for the next firing only, then stops.
/// </summary>
/// <param name="handler">Run with the first fired value.</param>
/// <param name="stream">The stream to listen to.</param>
/// <returns>
/// A strong listener, which may be stopped before that first firing arrives if it is no longer
/// wanted.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.listenOnce</c>; see it for the full contract.
/// </remarks>
let inline listenOnceS handler stream = Stream.listenOnce handler stream
/// <summary>
/// Waits asynchronously for the next firing.
/// </summary>
/// <param name="stream">The stream to wait on.</param>
/// <returns>An async which produces the next value the stream fires.</returns>
/// <remarks>
/// Shorthand for <c>Stream.listenOnceAsync</c>; see it for the full contract.
///
/// The listener is attached at once, before the async is run, so a firing between this call
/// and the await is not missed.
///
/// Canceling the async stops listening and cancels the result. The value is produced on a
/// thread other than the one that fired it, so awaiting this does not run continuations under
/// the transaction lock.
/// </remarks>
let inline listenOnceAsyncS stream = Stream.listenOnceAsync stream
/// <summary>
/// Transforms each fired value with a function.
/// </summary>
/// <param name="f">Transforms the fired value.</param>
/// <param name="stream">The stream to transform.</param>
/// <returns>A stream firing <paramref name="f" /> applied to each value the input fires.</returns>
/// <remarks>
/// Shorthand for <c>Stream.map</c>; see it for the full contract.
///
/// <paramref name="f" /> may construct FRP logic or sample behaviors and cells; apart from
/// that it must be pure.
/// </remarks>
let inline mapS f stream = Stream.map f stream
/// <summary>
/// Replaces every fired value with a constant.
/// </summary>
/// <param name="value">The value to fire instead.</param>
/// <param name="stream">The stream to transform.</param>
/// <returns>A stream firing <paramref name="value" /> whenever the input fires.</returns>
/// <remarks>
/// Shorthand for <c>Stream.mapTo</c>; see it for the full contract.
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
/// Shorthand for <c>Stream.hold</c>; see it for the full contract.
///
/// The cell's new value is visible to anything sampling it after the transaction in which the
/// firing happened, not within it. That delay is what makes a loop through a cell well defined
/// rather than circular.
/// </remarks>
let inline holdS initialValue stream = Stream.hold initialValue stream
/// <summary>
/// Holds the most recently fired value in a cell, with an initial value computed on first use.
/// </summary>
/// <param name="initialValue">The lazy value the cell holds until the stream first fires.</param>
/// <param name="stream">The stream to hold.</param>
/// <returns>A cell holding the last value fired, or <paramref name="initialValue" /> before any.</returns>
/// <remarks>
/// Shorthand for <c>Stream.holdLazy</c>; see it for the full contract.
///
/// This is the form that closes a loop: inside <c>Cell.loop</c> the initial value comes from
/// the very cell being defined, so it cannot be forced yet - <c>Cell.sampleLazy</c> produces
/// exactly what this takes.
/// </remarks>
let inline holdLazyS initialValue stream = Stream.holdLazy initialValue stream
/// <summary>
/// Samples a behavior when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <param name="f">Combines the fired value with the sampled value.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the sampled
/// value.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshotB</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshotB behavior f stream = Stream.snapshotB behavior f stream
/// <summary>
/// Samples a cell when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <param name="f">Combines the fired value with the sampled value.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the sampled
/// value.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshotC cell f stream = Stream.snapshot cell f stream
/// <summary>
/// Samples a behavior when the stream fires, and fires the behavior's value, discarding the
/// stream's own.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>A stream firing the behavior's value at each firing of the input.</returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshotAndTakeB</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshotAndTakeB behavior stream = Stream.snapshotAndTakeB behavior stream
/// <summary>
/// Samples a cell when the stream fires, and fires the cell's value, discarding the stream's own.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>A stream firing the cell's value at each firing of the input.</returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshotAndTake</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshotAndTakeC cell stream = Stream.snapshotAndTake cell stream
/// <summary>
/// Samples two behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="f">Combines the fired value with the two sampled values.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the two sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot2B</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshot2B behavior1 behavior2 f stream = Stream.snapshot2B behavior1 behavior2 f stream
/// <summary>
/// Samples two cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="f">Combines the fired value with the two sampled values.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the two sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot2</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshot2C cell1 cell2 f stream = Stream.snapshot2 cell1 cell2 f stream
/// <summary>
/// Samples three behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="behavior3">The third behavior to sample.</param>
/// <param name="f">Combines the fired value with the three sampled values.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the three sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot3B</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshot3B behavior1 behavior2 behavior3 f stream = Stream.snapshot3B behavior1 behavior2 behavior3 f stream
/// <summary>
/// Samples three cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="cell3">The third cell to sample.</param>
/// <param name="f">Combines the fired value with the three sampled values.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the three sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot3</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshot3C cell1 cell2 cell3 f stream = Stream.snapshot3 cell1 cell2 cell3 f stream
/// <summary>
/// Samples four behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="behavior3">The third behavior to sample.</param>
/// <param name="behavior4">The fourth behavior to sample.</param>
/// <param name="f">Combines the fired value with the four sampled values.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the four sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot4B</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshot4B behavior1 behavior2 behavior3 behavior4 f stream = Stream.snapshot4B behavior1 behavior2 behavior3 behavior4 f stream
/// <summary>
/// Samples four cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="cell3">The third cell to sample.</param>
/// <param name="cell4">The fourth cell to sample.</param>
/// <param name="f">Combines the fired value with the four sampled values.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the four sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot4</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshot4C cell1 cell2 cell3 cell4 f stream = Stream.snapshot4 cell1 cell2 cell3 cell4 f stream
/// <summary>
/// Samples five behaviors when the stream fires, and fires the combination.
/// </summary>
/// <param name="behavior1">The first behavior to sample.</param>
/// <param name="behavior2">The second behavior to sample.</param>
/// <param name="behavior3">The third behavior to sample.</param>
/// <param name="behavior4">The fourth behavior to sample.</param>
/// <param name="behavior5">The fifth behavior to sample.</param>
/// <param name="f">Combines the fired value with the five sampled values.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the five sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot5B</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshot5B behavior1 behavior2 behavior3 behavior4 behavior5 f stream = Stream.snapshot5B behavior1 behavior2 behavior3 behavior4 behavior5 f stream
/// <summary>
/// Samples five cells when the stream fires, and fires the combination.
/// </summary>
/// <param name="cell1">The first cell to sample.</param>
/// <param name="cell2">The second cell to sample.</param>
/// <param name="cell3">The third cell to sample.</param>
/// <param name="cell4">The fourth cell to sample.</param>
/// <param name="cell5">The fifth cell to sample.</param>
/// <param name="f">Combines the fired value with the five sampled values.</param>
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the five sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot5</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshot5C cell1 cell2 cell3 cell4 cell5 f stream = Stream.snapshot5 cell1 cell2 cell3 cell4 cell5 f stream
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the six sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot6B</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshot6B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 f stream = Stream.snapshot6B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 f stream
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the six sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot6</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshot6C cell1 cell2 cell3 cell4 cell5 cell6 f stream = Stream.snapshot6 cell1 cell2 cell3 cell4 cell5 cell6 f stream
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the seven sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot7B</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshot7B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 behavior7 f stream = Stream.snapshot7B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 behavior7 f stream
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the seven sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot7</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshot7C cell1 cell2 cell3 cell4 cell5 cell6 cell7 f stream = Stream.snapshot7 cell1 cell2 cell3 cell4 cell5 cell6 cell7 f stream
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the eight sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot8B</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshot8B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 behavior7 behavior8 f stream = Stream.snapshot8B behavior1 behavior2 behavior3 behavior4 behavior5 behavior6 behavior7 behavior8 f stream
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
/// <param name="stream">The stream whose firings drive the result.</param>
/// <returns>
/// A stream firing <paramref name="f" /> applied to the fired value and the eight sampled
/// values.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.snapshot8</c>; see it for the full contract.
///
/// Sampling rather than merging: only the stream drives the firing, and the sampled values are the
/// ones held at the start of the transaction the firing belongs to. A cell updated in that same
/// transaction is therefore seen with its old value, which is what keeps the result independent of
/// the order the graph happens to be evaluated in.
/// </remarks>
let inline snapshot8C cell1 cell2 cell3 cell4 cell5 cell6 cell7 cell8 f stream = Stream.snapshot8 cell1 cell2 cell3 cell4 cell5 cell6 cell7 cell8 f stream
/// <summary>
/// Merges two streams, combining the values where both fire in one transaction.
/// </summary>
/// <param name="f">
/// Combines two simultaneous values. The value from the first stream is the left
/// argument and the value from the second is the right.
/// </param>
/// <param name="stream">The first stream.</param>
/// <param name="stream2">The second stream.</param>
/// <returns>A stream firing whenever either input does, at most once per transaction.</returns>
/// <remarks>
/// Shorthand for <c>Stream.merge</c>; see it for the full contract.
///
/// A stream fires at most once per transaction, so simultaneous firings must be resolved
/// rather than both delivered - that is what <paramref name="f" /> is for. Use <c>orElse</c>
/// to take the first instead of combining.
/// </remarks>
let inline mergeS f (stream, stream2) = Stream.merge f (stream, stream2)
/// <summary>
/// Merges two streams, preferring the first where both fire in one transaction.
/// </summary>
/// <param name="stream">The stream to prefer.</param>
/// <param name="stream2">The stream to fall back to.</param>
/// <returns>A stream firing whenever either input does, at most once per transaction.</returns>
/// <remarks>
/// Shorthand for <c>Stream.orElse</c>; see it for the full contract.
///
/// <c>merge</c> with a function that keeps the left value and drops the right. The dropped
/// value is gone, not deferred - use <c>merge</c> where both matter.
/// </remarks>
let inline orElseS (stream, stream2) = Stream.orElse (stream, stream2)
/// <summary>
/// Keeps only the firings whose value satisfies a predicate.
/// </summary>
/// <param name="predicate">Returns whether to keep the fired value.</param>
/// <param name="stream">The stream to filter.</param>
/// <returns>A stream firing only the values <paramref name="predicate" /> accepted.</returns>
/// <remarks>
/// Shorthand for <c>Stream.filter</c>; see it for the full contract.
/// </remarks>
let inline filterS predicate stream = Stream.filter predicate stream
/// <summary>
/// Keeps only the firings which carried <c>Some</c>, and unwraps them.
/// </summary>
/// <param name="stream">The stream of options to filter.</param>
/// <returns>A stream firing the value inside each <c>Some</c>, and not firing for <c>None</c>.</returns>
/// <remarks>
/// Shorthand for <c>Stream.filterSome</c>; see it for the full contract.
/// </remarks>
let inline filterSomeS stream = Stream.filterSome stream
/// <summary>
/// Transforms the firings with a function which may produce no value, and fires only the values it
/// produced.
/// </summary>
/// <param name="f">Applied to each fired value; the firings it returns <c>None</c> for are dropped.</param>
/// <param name="stream">The stream to transform.</param>
/// <returns>
/// A stream firing the value inside each <c>Some</c> that <paramref name="f" /> returned, and not
/// firing for the values it returned <c>None</c> for.
/// </returns>
/// <remarks>
/// Shorthand for <c>Stream.choose</c>; see it for the full contract.
/// </remarks>
let inline chooseS f stream = Stream.choose f stream
/// <summary>
/// Lets firings through only while a behavior holds true.
/// </summary>
/// <param name="behavior">The behavior deciding whether firings pass.</param>
/// <param name="stream">The stream to gate.</param>
/// <returns>A stream firing only when the behavior held true at the time of the firing.</returns>
/// <remarks>
/// Shorthand for <c>Stream.gateB</c>; see it for the full contract.
///
/// The behavior is sampled the way <c>snapshotB</c> samples it: the value read is the one held
/// at the start of the transaction the firing belongs to.
/// </remarks>
let inline gateB behavior stream = Stream.gateB behavior stream
/// <summary>
/// Lets firings through only while a cell holds true.
/// </summary>
/// <param name="cell">The cell deciding whether firings pass.</param>
/// <param name="stream">The stream to gate.</param>
/// <returns>A stream firing only when the cell held true at the time of the firing.</returns>
/// <remarks>
/// Shorthand for <c>Stream.gate</c>; see it for the full contract.
///
/// The cell is sampled the way <c>snapshot</c> samples it: the value read is the one held at
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
/// to fire and the state to carry forward.
/// </param>
/// <param name="stream">The stream to fold over.</param>
/// <returns>A stream firing the value <paramref name="f" /> returned for each input firing.</returns>
/// <remarks>
/// Shorthand for <c>Stream.collectLazy</c>; see it for the full contract.
///
/// The state is committed at the end of the transaction rather than in place, so a second
/// firing within one transaction does not see the first one's state, and a transaction that
/// throws leaves the state as though nothing had happened.
///
/// This is the lazy form, for closing a loop where the initial state is not yet available.
/// </remarks>
let inline collectLazyS initialState f stream = Stream.collectLazy initialState f stream
/// <summary>
/// Folds state across firings, firing a value derived from each step.
/// </summary>
/// <param name="initialState">The state to start from.</param>
/// <param name="f">
/// Given the fired value and the current state, returns a struct tuple of the value
/// to fire and the state to carry forward.
/// </param>
/// <param name="stream">The stream to fold over.</param>
/// <returns>A stream firing the value <paramref name="f" /> returned for each input firing.</returns>
/// <remarks>
/// Shorthand for <c>Stream.collect</c>; see it for the full contract.
///
/// The state is committed at the end of the transaction rather than in place, so a second
/// firing within one transaction does not see the first one's state, and a transaction that
/// throws leaves the state as though nothing had happened.
///
/// Use <c>accum</c> where the state itself is what should be published.
/// </remarks>
let inline collectS initialState f stream = Stream.collect initialState f stream
/// <summary>
/// Suppresses firings whose value the given comparison considers equal to the last one that got
/// through.
/// </summary>
/// <param name="compare">Returns whether two values are to be treated as equal.</param>
/// <param name="stream">The stream to calm.</param>
/// <returns>A stream firing only when the value actually changed.</returns>
/// <remarks>
/// Shorthand for <c>Stream.calmWithCompare</c>; see it for the full contract.
///
/// Suppressing a firing is not the same as it not happening: the next comparison is made against
/// the value that was suppressed, not against the last one that got through.
/// </remarks>
let inline calmWithCompareS compare stream = Stream.calmWithCompare compare stream
/// <summary>
/// Suppresses firings whose value the given comparer considers equal to the last one that got
/// through.
/// </summary>
/// <param name="equalityComparer">Decides whether two values are equal.</param>
/// <param name="stream">The stream to calm.</param>
/// <returns>A stream firing only when the value actually changed.</returns>
/// <remarks>
/// Shorthand for <c>Stream.calmWithEqualityComparer</c>; see it for the full contract.
///
/// Suppressing a firing is not the same as it not happening: the next comparison is made against
/// the value that was suppressed, not against the last one that got through.
/// </remarks>
let inline calmWithEqualityComparerS equalityComparer stream = Stream.calmWithEqualityComparer equalityComparer stream
/// <summary>
/// Suppresses firings equal, by F#'s structural equality, to the last one that got through.
/// </summary>
/// <param name="stream">The stream to calm.</param>
/// <returns>A stream firing only when the value actually changed.</returns>
/// <remarks>
/// Shorthand for <c>Stream.calm</c>; see it for the full contract.
///
/// Suppressing a firing is not the same as it not happening: the next comparison is made against
/// the value that was suppressed, not against the last one that got through.
///
/// Uses <c>=</c>, so for a type without meaningful structural equality use
/// <c>calmWithCompare</c> instead.
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
/// Shorthand for <c>Stream.accumLazy</c>; see it for the full contract.
///
/// This is the lazy form, for closing a loop where the initial state is not yet available.
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
/// Shorthand for <c>Stream.accum</c>; see it for the full contract.
///
/// A running total, a counter, anything where the state itself is what is wanted. Use
/// <c>collect</c> where the published value differs from the state carried forward.
/// </remarks>
let inline accumS initialState f stream = Stream.accum initialState f stream
/// <summary>
/// Keeps only the first firing.
/// </summary>
/// <param name="stream">The stream to take from.</param>
/// <returns>A stream firing the first value the input fires, and never again.</returns>
/// <remarks>
/// Shorthand for <c>Stream.once</c>; see it for the full contract.
/// </remarks>
let inline onceS stream = Stream.once stream
/// <summary>
/// Merges any number of streams, combining the values where several fire in one transaction.
/// </summary>
/// <param name="f">
/// Combines two simultaneous values. The value from the stream earlier in the sequence
/// is the left argument.
/// </param>
/// <param name="streams">The streams to merge.</param>
/// <returns>A stream firing whenever any input does, at most once per transaction.</returns>
/// <remarks>
/// Shorthand for <c>Stream.mergeAll</c>; see it for the full contract.
/// </remarks>
let inline mergeAllS f streams = Stream.mergeAll f streams
/// <summary>
/// Merges any number of streams, preferring the earliest where several fire in one transaction.
/// </summary>
/// <param name="streams">The streams to merge, in order of preference.</param>
/// <returns>A stream firing whenever any input does, at most once per transaction.</returns>
/// <remarks>
/// Shorthand for <c>Stream.orElseAll</c>; see it for the full contract.
///
/// <c>mergeAll</c> with a function that keeps the left value; the values from the later
/// streams are dropped rather than deferred.
/// </remarks>
let inline orElseAllS streams = Stream.orElseAll streams

/// <summary>
/// Creates a behavior with a value that never changes.
/// </summary>
/// <param name="value">The value the behavior always has.</param>
/// <returns>A behavior whose value is always <paramref name="value" />.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.constant</c>; see it for the full contract.
/// </remarks>
let inline constantB value = Behavior.constant value
/// <summary>
/// Creates a behavior with a value that never changes, computed on first use.
/// </summary>
/// <param name="value">The lazy value the behavior always has.</param>
/// <returns>A behavior whose value is always the value of <paramref name="value" />.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.constantLazy</c>; see it for the full contract.
///
/// For a constant that is expensive to produce, or that is not yet available when the graph
/// is being built - the value is forced only when the behavior is first sampled.
/// </remarks>
let inline constantLazyB value = Behavior.constantLazy value
/// <summary>
/// Creates a behavior sink which keeps the last value sent when <c>send</c> is called more than
/// once in a single transaction.
/// </summary>
/// <param name="initialValue">The value the behavior holds until something is sent.</param>
/// <returns>A new behavior sink.</returns>
/// <remarks>
/// Shorthand for <c>BehaviorSink.create</c>; see it for the full contract.
/// </remarks>
let inline sinkB initialValue = BehaviorSink.create initialValue
/// <summary>
/// Creates a behavior sink which combines values when <c>send</c> is called more than once in a
/// single transaction.
/// </summary>
/// <param name="initialValue">The value the behavior holds until something is sent.</param>
/// <param name="coalesce">
/// Combines two values sent in the same transaction. Called with the value already
/// accumulated and the value just sent, in that order.
/// </param>
/// <returns>A new behavior sink.</returns>
/// <remarks>
/// Shorthand for <c>BehaviorSink.createWithCoalesce</c>; see it for the full contract.
/// </remarks>
let inline sinkWithCoalesceB initialValue coalesce = BehaviorSink.createWithCoalesce initialValue coalesce
/// <summary>
/// Sends a value, changing what the behavior holds.
/// </summary>
/// <param name="a">The value to send.</param>
/// <param name="behaviorSink">The behavior sink to send it to.</param>
/// <remarks>
/// Shorthand for <c>BehaviorSink.send</c>; see it for the full contract.
///
/// Must not be called from inside a listener callback; doing so throws.
/// </remarks>
let inline sendB a behaviorSink = BehaviorSink.send a behaviorSink
/// <summary>
/// Builds a behavior which refers to itself, closing the loop within one transaction.
/// </summary>
/// <param name="f">
/// Given the forward reference, returns a struct tuple of the behavior it stands for and
/// anything else the caller wants back out.
/// </param>
/// <returns>
/// A struct tuple of the behavior the forward reference was closed with, and whatever
/// <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.loop</c>; see it for the full contract.
///
/// A behavior defined in terms of itself needs a forward reference to exist before the value
/// it refers to does. Both the reference and its resolution must happen in a single
/// transaction, which this opens if none is running.
///
/// Use <c>loopWithNoCaptures</c> where nothing but the behavior itself is needed.
/// </remarks>
let inline loopB f = Behavior.loop f
/// <summary>
/// Builds a self-referential behavior where nothing but the behavior itself is wanted back.
/// </summary>
/// <param name="f">Given the forward reference, returns the behavior it stands for.</param>
/// <returns>The behavior the forward reference was closed with.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.loopWithNoCaptures</c>; see it for the full contract.
///
/// <c>loop</c> where something more than the behavior needs to escape the loop.
/// </remarks>
let inline loopWithNoCapturesB f = Behavior.loopWithNoCaptures f
/// <summary>
/// Gets a behavior's current value.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <returns>The value the behavior has at this moment.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.sample</c>; see it for the full contract.
///
/// May be used inside the functions passed to the primitives that apply them to streams, where
/// it means the same as snapshotting. Outside a transaction it opens one of its own, so the
/// value read is never a half-updated one.
/// </remarks>
let inline sampleB behavior = Behavior.sample behavior
/// <summary>
/// Gets a behavior's value as of now, without forcing it yet.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <returns>A lazy value which yields what the behavior held at the moment of this call.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.sampleLazy</c>; see it for the full contract.
///
/// The value is pinned now and computed later. This is what <c>Stream.holdLazy</c> and the
/// looping constructs need: at the point a loop is being closed the value is not yet known,
/// but which moment it is to be taken from already is.
/// </remarks>
let inline sampleLazyB behavior = Behavior.sampleLazy behavior
/// <summary>
/// Applies a behavior of functions to a behavior of values.
/// </summary>
/// <param name="f">The behavior holding the function to apply.</param>
/// <param name="behavior">The behavior holding the value to apply it to.</param>
/// <returns>
/// A behavior whose value is the current function in <paramref name="f" /> applied to the
/// input behavior's current value.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.apply</c>; see it for the full contract.
///
/// The primitive all of the <c>lift</c> functions are built from. Reach for <c>lift2</c> and
/// its siblings first; this is for the cases they do not cover.
/// </remarks>
let inline applyB f behavior = Behavior.apply f behavior
/// <summary>
/// Transforms a behavior with a function.
/// </summary>
/// <param name="f">Transforms the value.</param>
/// <param name="behavior">The behavior to transform.</param>
/// <returns>
/// A behavior whose value is <paramref name="f" /> applied to the input behavior's current
/// value.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.map</c>; see it for the full contract.
///
/// <paramref name="f" /> may construct FRP logic or sample behaviors and cells; apart from
/// that it must be pure, since it may be called more than once for one input.
/// </remarks>
let inline mapB f behavior = Behavior.map f behavior
/// <summary>
/// Combines two behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the two current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <returns>
/// A behavior whose value is <paramref name="f" /> applied to the current values of the two
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.lift2</c>; see it for the full contract.
///
/// Glitch-free: when several of the inputs change in one transaction, the result updates
/// once, with all of the new values, rather than once per input.
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
/// A behavior whose value is <paramref name="f" /> applied to the current values of the three
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.lift3</c>; see it for the full contract.
///
/// Glitch-free: when several of the inputs change in one transaction, the result updates
/// once, with all of the new values, rather than once per input.
/// </remarks>
let inline lift3B f (behavior, behavior2, behavior3) = Behavior.lift3 f (behavior, behavior2, behavior3)
/// <summary>
/// Combines four behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the four current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <returns>
/// A behavior whose value is <paramref name="f" /> applied to the current values of the four
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.lift4</c>; see it for the full contract.
///
/// Glitch-free: when several of the inputs change in one transaction, the result updates
/// once, with all of the new values, rather than once per input.
/// </remarks>
let inline lift4B f (behavior, behavior2, behavior3, behavior4) = Behavior.lift4 f (behavior, behavior2, behavior3, behavior4)
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
/// A behavior whose value is <paramref name="f" /> applied to the current values of the five
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.lift5</c>; see it for the full contract.
///
/// Glitch-free: when several of the inputs change in one transaction, the result updates
/// once, with all of the new values, rather than once per input.
/// </remarks>
let inline lift5B f (behavior, behavior2, behavior3, behavior4, behavior5) = Behavior.lift5 f (behavior, behavior2, behavior3, behavior4, behavior5)
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
/// A behavior whose value is <paramref name="f" /> applied to the current values of the six
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.lift6</c>; see it for the full contract.
///
/// Glitch-free: when several of the inputs change in one transaction, the result updates
/// once, with all of the new values, rather than once per input.
/// </remarks>
let inline lift6B f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6) = Behavior.lift6 f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6)
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
/// A behavior whose value is <paramref name="f" /> applied to the current values of the seven
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.lift7</c>; see it for the full contract.
///
/// Glitch-free: when several of the inputs change in one transaction, the result updates
/// once, with all of the new values, rather than once per input.
/// </remarks>
let inline lift7B f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6, behavior7) = Behavior.lift7 f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6, behavior7)
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
/// A behavior whose value is <paramref name="f" /> applied to the current values of the eight
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Behavior.lift8</c>; see it for the full contract.
///
/// Glitch-free: when several of the inputs change in one transaction, the result updates
/// once, with all of the new values, rather than once per input.
/// </remarks>
let inline lift8B f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6, behavior7, behavior8) = Behavior.lift8 f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6, behavior7, behavior8)
/// <summary>
/// Combines any number of behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the current values, given in the order the behaviors were supplied.</param>
/// <param name="behaviors">The behaviors to combine.</param>
/// <returns>A behavior whose value is <paramref name="f" /> applied to all of the current values.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.liftAll</c>; see it for the full contract.
///
/// The <c>lift</c> family where the number of inputs is not known until run time. Glitch-free
/// in the same way: however many of the inputs change in one transaction, the result updates
/// once.
/// </remarks>
let inline liftAllB f behaviors = Behavior.liftAll f behaviors
/// <summary>
/// Unwraps a behavior of behaviors into a behavior which follows whichever one is current.
/// </summary>
/// <param name="behavior">The behavior holding another behavior.</param>
/// <returns>A behavior whose value is the current value of the currently held behavior.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.switchB</c>; see it for the full contract.
///
/// This is how a graph changes shape at run time: the outer behavior chooses which inner one
/// is being followed.
/// </remarks>
let inline switchBB behavior = Behavior.switchB behavior
/// <summary>
/// Unwraps a behavior of cells into a cell which follows whichever one is current.
/// </summary>
/// <param name="behavior">The behavior holding a cell.</param>
/// <returns>A cell whose value is the current value of the currently held cell.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.switchC</c>; see it for the full contract.
/// </remarks>
let inline switchCB behavior = Behavior.switchC behavior
/// <summary>
/// Unwraps a behavior of streams into a stream which fires whatever the current one fires.
/// </summary>
/// <param name="behavior">The behavior holding a stream.</param>
/// <returns>A stream firing the firings of the currently held stream.</returns>
/// <remarks>
/// Shorthand for <c>Behavior.switchS</c>; see it for the full contract.
///
/// On the transaction where the behavior changes, the firing taken is the one from the stream
/// held at the start of that transaction, not the newly selected one.
/// </remarks>
let inline switchSB behavior = Behavior.switchS behavior

/// <summary>
/// Creates a cell with a value that never changes.
/// </summary>
/// <param name="value">The value the cell always has.</param>
/// <returns>A cell whose value is always <paramref name="value" />.</returns>
/// <remarks>
/// Shorthand for <c>Cell.constant</c>; see it for the full contract.
/// </remarks>
let inline constantC value = Cell.constant value
/// <summary>
/// Creates a cell with a value that never changes, computed on first use.
/// </summary>
/// <param name="value">The lazy value the cell always has.</param>
/// <returns>A cell whose value is always the value of <paramref name="value" />.</returns>
/// <remarks>
/// Shorthand for <c>Cell.constantLazy</c>; see it for the full contract.
///
/// For a constant that is expensive to produce, or that is not yet available when the graph is
/// being built - the value is forced only when the cell is first sampled.
/// </remarks>
let inline constantLazyC value = Cell.constantLazy value
/// <summary>
/// Creates a cell sink which keeps the last value sent when <c>send</c> is called more than once
/// in a single transaction.
/// </summary>
/// <param name="initialValue">The value the cell holds until something is sent.</param>
/// <returns>A new cell sink.</returns>
/// <remarks>
/// Shorthand for <c>CellSink.create</c>; see it for the full contract.
/// </remarks>
let inline sinkC initialValue = CellSink.create initialValue
/// <summary>
/// Creates a cell sink which combines values when <c>send</c> is called more than once in a
/// single transaction.
/// </summary>
/// <param name="initialValue">The value the cell holds until something is sent.</param>
/// <param name="coalesce">
/// Combines two values sent in the same transaction. Called with the value already
/// accumulated and the value just sent, in that order.
/// </param>
/// <returns>A new cell sink.</returns>
/// <remarks>
/// Shorthand for <c>CellSink.createWithCoalesce</c>; see it for the full contract.
/// </remarks>
let inline sinkWithCoalesceC initialValue coalesce = CellSink.createWithCoalesce initialValue coalesce
/// <summary>
/// Sends a value, changing what the cell holds.
/// </summary>
/// <param name="a">The value to send.</param>
/// <param name="cellSink">The cell sink to send it to.</param>
/// <remarks>
/// Shorthand for <c>CellSink.send</c>; see it for the full contract.
///
/// Must not be called from inside a listener callback; doing so throws.
/// </remarks>
let inline sendC a cellSink = CellSink.send a cellSink
/// <summary>
/// Builds a cell which refers to itself, closing the loop within one transaction.
/// </summary>
/// <param name="f">
/// Given the forward reference, returns a struct tuple of the cell it stands for and
/// anything else the caller wants back out.
/// </param>
/// <returns>
/// A struct tuple of the cell the forward reference was closed with, and whatever
/// <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.loop</c>; see it for the full contract.
///
/// A cell defined in terms of itself needs a forward reference to exist before the value it
/// refers to does. Both the reference and its resolution must happen in a single transaction,
/// which this opens if none is running.
///
/// Use <c>loopWithNoCaptures</c> where nothing but the cell itself is needed.
/// </remarks>
let inline loopC f = Cell.loop f
/// <summary>
/// Builds a self-referential cell where nothing but the cell itself is wanted back.
/// </summary>
/// <param name="f">Given the forward reference, returns the cell it stands for.</param>
/// <returns>The cell the forward reference was closed with.</returns>
/// <remarks>
/// Shorthand for <c>Cell.loopWithNoCaptures</c>; see it for the full contract.
///
/// <c>loop</c> where something more than the cell needs to escape the loop.
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
/// A struct tuple of the value the forward reference was closed with, and whatever
/// <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
/// Shorthand for <c>ForwardReference.create</c>; see it for the full contract.
///
/// The single-valued case of a cell loop: the loop handed to <paramref name="f" /> is closed with
/// a constant cell, so the reference resolves to the value produced and never changes.
///
/// Use <c>forwardReferenceWithNoCaptures</c> where nothing but the value itself is needed.
/// </remarks>
let inline forwardReference f = ForwardReference.create f
/// <summary>
/// Builds a value which can refer to itself, where nothing but the value itself is wanted back.
/// </summary>
/// <param name="f">Given the forward reference, returns the value it stands for.</param>
/// <returns>The value the forward reference was closed with.</returns>
/// <remarks>
/// Shorthand for <c>ForwardReference.createWithNoCaptures</c>; see it for the full contract.
///
/// <c>forwardReference</c> where something more than the value needs to escape the construction.
/// </remarks>
let inline forwardReferenceWithNoCaptures f = ForwardReference.createWithNoCaptures f
/// <summary>
/// Gets a cell's current value.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <returns>The value the cell has at this moment.</returns>
/// <remarks>
/// Shorthand for <c>Cell.sample</c>; see it for the full contract.
///
/// May be used inside the functions passed to the primitives that apply them to streams, where
/// it means the same as snapshotting. Outside a transaction it opens one of its own, so the
/// value read is never a half-updated one.
/// </remarks>
let inline sampleC cell = Cell.sample cell
/// <summary>
/// Gets a cell's value as of now, without forcing it yet.
/// </summary>
/// <param name="cell">The cell to sample.</param>
/// <returns>A lazy value which yields what the cell held at the moment of this call.</returns>
/// <remarks>
/// Shorthand for <c>Cell.sampleLazy</c>; see it for the full contract.
///
/// The value is pinned now and computed later, which is what the looping constructs need: at
/// the point a loop is being closed the value is not yet known, but which moment it is to be
/// taken from already is.
/// </remarks>
let inline sampleLazyC cell = Cell.sampleLazy cell
/// <summary>
/// Gets a stream firing the new value of a cell each time it changes.
/// </summary>
/// <param name="cell">The cell to observe.</param>
/// <returns>A stream firing the updated value, in the transaction the update happened in.</returns>
/// <remarks>
/// Shorthand for <c>Cell.updates</c>; see it for the full contract.
///
/// Does not fire for the value the cell starts with - only for changes. Use <c>values</c> to
/// get that initial value as a firing too.
/// </remarks>
let inline updatesC cell = Cell.updates cell
/// <summary>
/// Gets a stream firing the cell's current value at once, and its new value on every change.
/// </summary>
/// <param name="cell">The cell to observe.</param>
/// <returns>
/// A stream which fires the current value in the transaction this is called in, and then the
/// updated value on every change.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.values</c>; see it for the full contract.
///
/// The immediate firing happens in the transaction this is called in, so this must be called
/// inside <c>Transaction.run</c> if that firing is to be observed at all - a listener attached
/// afterward, in a later transaction, has already missed it. This is the single most common
/// reason to wrap graph construction in a transaction.
/// </remarks>
let inline valuesC cell = Cell.values cell
/// <summary>
/// Views a cell as a behavior.
/// </summary>
/// <param name="cell">The cell to view.</param>
/// <returns>The same value, seen as a behavior, without the stream of its changes.</returns>
/// <remarks>
/// Shorthand for <c>Cell.asBehavior</c>; see it for the full contract.
///
/// Nothing is created or converted; a cell already is a behavior with updates attached. This
/// is for passing one to something written against <c>Behavior</c>.
/// </remarks>
let inline asBehaviorC cell = Cell.asBehavior cell
/// <summary>
/// Listens for changes without keeping the cell alive.
/// </summary>
/// <param name="handler">Run with each new value.</param>
/// <param name="cell">The cell to listen to.</param>
/// <returns>A weak listener, which may be stopped with <c>WeakListener.unlisten</c>.</returns>
/// <remarks>
/// Shorthand for <c>Cell.listen</c>; see it for the full contract.
///
/// The listener stops on its own once the cell is collected, which makes this the right choice
/// where there is no clean moment to stop listening: hold the returned handle as a field of the
/// object doing the listening, and the two go away together. Where the cell should be kept
/// alive for as long as something is listening, use <c>listenStrong</c>.
///
/// Fires the current value immediately, in the transaction this is called in.
/// </remarks>
let inline listenC handler cell = Cell.listen handler cell
/// <summary>
/// Listens for changes, keeping the cell alive while the listener lives.
/// </summary>
/// <param name="handler">Run with each new value.</param>
/// <param name="cell">The cell to listen to.</param>
/// <returns>
/// A strong listener, which may be stopped with <c>StrongListener.unlisten</c> or disposed.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.listenStrong</c>; see it for the full contract.
///
/// The listener roots the cell, so the graph behind it stays alive for as long as the returned
/// handle is reachable. Keep the handle and stop it when finished, or use <c>listen</c>
/// where there is no good moment to do that.
///
/// Fires the current value immediately, in the transaction this is called in. The handler runs
/// under the transaction lock, so it should return promptly.
/// </remarks>
let inline listenStrongC handler cell = Cell.listenStrong handler cell
/// <summary>
/// Applies a cell of functions to a cell of values.
/// </summary>
/// <param name="f">The cell holding the function to apply.</param>
/// <param name="cell">The cell holding the value to apply it to.</param>
/// <returns>
/// A cell whose value is the current function in <paramref name="f" /> applied to the input
/// cell's current value.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.apply</c>; see it for the full contract.
///
/// The primitive all of the <c>lift</c> functions are built from. Reach for <c>lift2</c> and
/// its siblings first; this is for the cases they do not cover.
/// </remarks>
let inline applyC f cell = Cell.apply f cell
/// <summary>
/// Transforms a cell with a function.
/// </summary>
/// <param name="f">Transforms the value.</param>
/// <param name="cell">The cell to transform.</param>
/// <returns>A cell whose value is <paramref name="f" /> applied to the input cell's current value.</returns>
/// <remarks>
/// Shorthand for <c>Cell.map</c>; see it for the full contract.
///
/// <paramref name="f" /> may construct FRP logic or sample behaviors and cells; apart from
/// that it must be pure, since it may be called more than once for one input.
/// </remarks>
let inline mapC f cell = Cell.map f cell
/// <summary>
/// Combines two cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the two current values into the result.</param>
/// <param name="cell">The first cell.</param>
/// <param name="cell2">The second cell.</param>
/// <returns>
/// A cell whose value is <paramref name="f" /> applied to the current values of the two
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.lift2</c>; see it for the full contract.
///
/// Glitch-free: when several of the inputs change in one transaction, the result updates
/// once, with all of the new values, rather than once per input.
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
/// A cell whose value is <paramref name="f" /> applied to the current values of the three
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.lift3</c>; see it for the full contract.
///
/// Glitch-free: when several of the inputs change in one transaction, the result updates
/// once, with all of the new values, rather than once per input.
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
/// A cell whose value is <paramref name="f" /> applied to the current values of the four
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.lift4</c>; see it for the full contract.
///
/// Glitch-free: when several of the inputs change in one transaction, the result updates
/// once, with all of the new values, rather than once per input.
/// </remarks>
let inline lift4C f (cell, cell2, cell3, cell4) = Cell.lift4 f (cell, cell2, cell3, cell4)
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
/// A cell whose value is <paramref name="f" /> applied to the current values of the five
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.lift5</c>; see it for the full contract.
///
/// Glitch-free: when several of the inputs change in one transaction, the result updates
/// once, with all of the new values, rather than once per input.
/// </remarks>
let inline lift5C f (cell, cell2, cell3, cell4, cell5) = Cell.lift5 f (cell, cell2, cell3, cell4, cell5)
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
/// A cell whose value is <paramref name="f" /> applied to the current values of the six
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.lift6</c>; see it for the full contract.
///
/// Glitch-free: when several of the inputs change in one transaction, the result updates
/// once, with all of the new values, rather than once per input.
/// </remarks>
let inline lift6C f (cell, cell2, cell3, cell4, cell5, cell6) = Cell.lift6 f (cell, cell2, cell3, cell4, cell5, cell6)
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
/// A cell whose value is <paramref name="f" /> applied to the current values of the seven
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.lift7</c>; see it for the full contract.
///
/// Glitch-free: when several of the inputs change in one transaction, the result updates
/// once, with all of the new values, rather than once per input.
/// </remarks>
let inline lift7C f (cell, cell2, cell3, cell4, cell5, cell6, cell7) = Cell.lift7 f (cell, cell2, cell3, cell4, cell5, cell6, cell7)
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
/// A cell whose value is <paramref name="f" /> applied to the current values of the eight
/// inputs.
/// </returns>
/// <remarks>
/// Shorthand for <c>Cell.lift8</c>; see it for the full contract.
///
/// Glitch-free: when several of the inputs change in one transaction, the result updates
/// once, with all of the new values, rather than once per input.
/// </remarks>
let inline lift8C f (cell, cell2, cell3, cell4, cell5, cell6, cell7, cell8) = Cell.lift8 f (cell, cell2, cell3, cell4, cell5, cell6, cell7, cell8)
/// <summary>
/// Suppresses updates whose value the given comparison considers equal to the last one that got
/// through.
/// </summary>
/// <param name="compare">Returns whether two values are to be treated as equal.</param>
/// <param name="cell">The cell to calm.</param>
/// <returns>A cell which updates only when the value actually changed.</returns>
/// <remarks>
/// Shorthand for <c>Cell.calmWithCompare</c>; see it for the full contract.
///
/// Suppressing an update is not the same as it not happening: the cell still takes the new value,
/// and the next comparison is made against the last value that got through.
/// </remarks>
let inline calmWithCompareC compare cell = Cell.calmWithCompare compare cell
/// <summary>
/// Suppresses updates whose value the given comparer considers equal to the last one that got
/// through.
/// </summary>
/// <param name="equalityComparer">Decides whether two values are equal.</param>
/// <param name="cell">The cell to calm.</param>
/// <returns>A cell which updates only when the value actually changed.</returns>
/// <remarks>
/// Shorthand for <c>Cell.calmWithEqualityComparer</c>; see it for the full contract.
///
/// Suppressing an update is not the same as it not happening: the cell still takes the new value,
/// and the next comparison is made against the last value that got through.
/// </remarks>
let inline calmWithEqualityComparerC equalityComparer cell = Cell.calmWithEqualityComparer equalityComparer cell
/// <summary>
/// Suppresses updates equal, by F#'s structural equality, to the last one that got through.
/// </summary>
/// <param name="cell">The cell to calm.</param>
/// <returns>A cell which updates only when the value actually changed.</returns>
/// <remarks>
/// Shorthand for <c>Cell.calm</c>; see it for the full contract.
///
/// Suppressing an update is not the same as it not happening: the cell still takes the new value,
/// and the next comparison is made against the last value that got through.
///
/// Uses <c>=</c>, so for a type without meaningful structural equality use
/// <c>calmWithCompare</c> instead.
/// </remarks>
let inline calmC cell = Cell.calm cell
/// <summary>
/// Combines any number of cells into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the current values, given in the order the cells were supplied.</param>
/// <param name="cells">The cells to combine.</param>
/// <returns>A cell whose value is <paramref name="f" /> applied to all of the current values.</returns>
/// <remarks>
/// Shorthand for <c>Cell.liftAll</c>; see it for the full contract.
///
/// The <c>lift</c> family where the number of inputs is not known until run time. Glitch-free
/// in the same way: however many of the inputs change in one transaction, the result updates
/// once.
/// </remarks>
let inline liftAllC f cells = Cell.liftAll f cells
/// <summary>
/// Unwraps a cell of behaviors into a behavior which follows whichever one is current.
/// </summary>
/// <param name="cell">The cell holding a behavior.</param>
/// <returns>A behavior whose value is the current value of the currently held behavior.</returns>
/// <remarks>
/// Shorthand for <c>Cell.switchB</c>; see it for the full contract.
/// </remarks>
let inline switchB cell = Cell.switchB cell
/// <summary>
/// Unwraps a cell of cells into a cell which follows whichever one is current.
/// </summary>
/// <param name="cell">The cell holding another cell.</param>
/// <returns>A cell whose value is the current value of the currently held cell.</returns>
/// <remarks>
/// Shorthand for <c>Cell.switchC</c>; see it for the full contract.
///
/// This is how a graph changes shape at run time: the outer cell chooses which inner one is
/// being followed.
/// </remarks>
let inline switchC cell = Cell.switchC cell
/// <summary>
/// Unwraps a cell of streams into a stream which fires whatever the current one fires.
/// </summary>
/// <param name="cell">The cell holding a stream.</param>
/// <returns>A stream firing the firings of the currently held stream.</returns>
/// <remarks>
/// Shorthand for <c>Cell.switchS</c>; see it for the full contract.
///
/// On the transaction where the cell changes, the firing taken is the one from the stream held
/// at the start of that transaction, not the newly selected one.
/// </remarks>
let inline switchS cell = Cell.switchS cell
