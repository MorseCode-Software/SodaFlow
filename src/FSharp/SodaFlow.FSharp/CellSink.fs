/// <summary>
///     Creating cell sinks, and pushing values into them.
/// </summary>
/// <remarks>
///     A cell sink is how a value from outside the FRP graph gets into it. These are for
///     interfacing I/O to FRP only: <c>send</c> throws if called from inside a listener callback.
/// </remarks>
module SodaFlow.CellSink

open System
open System.Runtime.CompilerServices

/// <summary>
///     Creates a cell sink which keeps the last value sent when <c>send</c> is called more than once
///     in a single transaction.
/// </summary>
/// <param name="initialValue">The value the cell holds until something is sent.</param>
/// <returns>A new cell sink.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let create initialValue =
    CellInternal.CreateSinkImpl initialValue

/// <summary>
///     Creates a cell sink which combines values when <c>send</c> is called more than once in a
///     single transaction.
/// </summary>
/// <param name="initialValue">The value the cell holds until something is sent.</param>
/// <param name="coalesce">
///     Combines two values sent in the same transaction. Called with the value already
///     accumulated and the value just sent, in that order.
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
///     Must not be called from inside a listener callback; doing so throws.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let send a (cellSink: CellSink<'T>) = cellSink.SendImpl a
