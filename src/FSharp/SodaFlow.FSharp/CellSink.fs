/// <summary>
///     Creating cell sinks, and pushing values into them.
/// </summary>
/// <remarks>
///     A cell sink puts a value from other code into the FRP graph. Use these only to connect I/O
///     to FRP. <c>send</c> throws an exception in a listener callback.
/// </remarks>
module SodaFlow.CellSink

open System
open System.Runtime.CompilerServices

/// <summary>
/// Creates a cell sink that keeps the last value of more than one <c>send</c> in one transaction.
/// </summary>
/// <param name="initialValue">The value the cell holds until something is sent.</param>
/// <returns>A new cell sink.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let create initialValue =
    CellInternal.CreateSinkImpl initialValue

/// <summary>
/// Creates a cell sink that combines the values of more than one <c>send</c> in one transaction.
/// </summary>
/// <param name="initialValue">The value the cell holds until something is sent.</param>
/// <param name="coalesce">
/// Puts two values from the same transaction together. It receives the value from before this
/// send and the new value, in that sequence.
/// </param>
/// <returns>A new cell sink.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let createWithCoalesce initialValue coalesce =
    CellInternal.CreateSinkImpl(initialValue, Func<_, _, _> coalesce)

/// <summary>
///     Sends a value, changing what the cell holds.
/// </summary>
/// <param name="a">The value to send.</param>
/// <param name="cellSink">The cell sink to send it to.</param>
/// <remarks>
///     A call from a listener callback throws an exception.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let send a (cellSink: CellSink<'T>) = cellSink.SendImpl a
