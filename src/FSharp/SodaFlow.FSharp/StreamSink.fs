/// <summary>
///     Creating stream sinks, and pushing values into them.
/// </summary>
/// <remarks>
///     A stream sink puts an event from other code into the FRP graph. Use these only to connect
///     I/O to FRP. <c>send</c> throws an exception in a listener callback, because a sink is not
///     for the definition of a new primitive.
/// </remarks>
module SodaFlow.StreamSink

open System
open System.Runtime.CompilerServices

/// <summary>
///     Creates a stream sink. A second <c>send</c> in one transaction throws an exception.
/// </summary>
/// <typeparam name="'a">The type of the values the stream sink fires.</typeparam>
/// <returns>A new stream sink.</returns>
/// <remarks>
///     Two sends in one transaction are usually an error and not an intention. Thus, the sink
///     reports this, and does not resolve it without a message. Use <c>createWithCoalesce</c> where
///     the second send is correct.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let create<'a> () = StreamInternal.CreateSinkImpl<'a>()

/// <summary>
///     Creates a stream sink that combines the values of more than one <c>send</c> in one transaction.
/// </summary>
/// <param name="coalesce">
///     Puts two values from the same transaction together. It receives the value from before this
///     send and the new value, in that sequence.
/// </param>
/// <returns>A new stream sink.</returns>
/// <remarks>
///     A stream fires one time or no times in each transaction, and this keeps that rule. The sink
///     folds all that a caller sends in one transaction into the one value that fires.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let createWithCoalesce coalesce =
    StreamInternal.CreateSinkImpl(Func<_, _, _> coalesce)

/// <summary>
///     Sends a value, firing the stream sink.
/// </summary>
/// <param name="a">The value to send.</param>
/// <param name="streamSink">The stream sink to send it to.</param>
/// <remarks>
///     A call from a listener callback throws an exception. Sinks are for getting
///     I/O into FRP, not for building new primitives out of.
///
///     Two sends in one transaction throw an exception, unless <c>createWithCoalesce</c> made the
///     sink.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let send a (streamSink: StreamSink<'T>) = streamSink.SendImpl a
