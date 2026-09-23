/// <summary>
///     Creating behavior sinks, and pushing values into them.
/// </summary>
/// <remarks>
///     A behavior sink puts a value from other code into the FRP graph. Use these only to connect
///     I/O to FRP. <c>send</c> throws an exception in a listener callback.
/// </remarks>
module SodaFlow.BehaviorSink

open System.Runtime.CompilerServices

/// <summary>
///     Creates a behavior sink that keeps the last value of more than one <c>send</c> in one
///     transaction.
/// </summary>
/// <param name="initialValue">The value the behavior holds until something is sent.</param>
/// <returns>A new behavior sink.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let create initialValue =
    BehaviorInternal.CreateSinkImpl initialValue

/// <summary>
/// Creates a behavior sink that combines the values of more than one <c>send</c> in one transaction.
/// </summary>
/// <param name="initialValue">The value the behavior holds until something is sent.</param>
/// <param name="coalesce">
/// Puts two values from the same transaction together. It receives the value from before this
/// send and the new value, in that sequence.
/// </param>
/// <returns>A new behavior sink.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let createWithCoalesce initialValue coalesce =
    BehaviorInternal.CreateSinkImpl(initialValue, coalesce)

/// <summary>
///     Sends a value, changing what the behavior holds.
/// </summary>
/// <param name="a">The value to send.</param>
/// <param name="behaviorSink">The behavior sink to send it to.</param>
/// <remarks>
///     A call from a listener callback throws an exception.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let send a (behaviorSink: BehaviorSink<'T>) = behaviorSink.SendImpl a
