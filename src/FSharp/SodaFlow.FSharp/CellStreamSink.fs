/// <summary>
///     Creating cell stream sinks - stream sinks which may be held to make a cell.
/// </summary>
/// <remarks>
///     A plain stream sink cannot be held into a cell, because a cell must have a value from the
///     moment it exists and a stream sink has none until something is sent. A cell stream sink
///     exists to be held, supplying the initial value at that point.
/// </remarks>
module SodaFlow.CellStreamSink

open System
open System.Runtime.CompilerServices

/// <summary>
///     Creates a cell stream sink which throws if <c>StreamSink.send</c> is called more than once in
///     a transaction.
/// </summary>
/// <typeparam name="'a">The type of the values the cell stream sink fires.</typeparam>
/// <returns>A new cell stream sink.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let create<'a> () = CellInternal.CreateStreamSinkImpl<'a> ()

/// <summary>
///     Creates a cell stream sink which combines values when <c>StreamSink.send</c> is called more
///     than once in a single transaction.
/// </summary>
/// <param name="coalesce">
///     Combines two values sent in the same transaction. Called with the value already
///     accumulated and the value just sent, in that order.
/// </param>
/// <returns>A new cell stream sink.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let createWithCoalesce coalesce = CellInternal.CreateStreamSinkImpl (Func<_,_,_> coalesce)