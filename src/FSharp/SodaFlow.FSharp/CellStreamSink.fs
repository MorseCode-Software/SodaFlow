/// <summary>
///     Creating cell stream sinks - stream sinks that a caller can hold to make a cell.
/// </summary>
/// <remarks>
///     A hold of a plain stream sink into a cell is not possible. A cell must have a value from the
///     moment of its construction, and a stream sink has no value before the first send. A cell
///     stream sink is for a hold, and it gives the initial value at that moment.
/// </remarks>
module SodaFlow.CellStreamSink

open System
open System.Runtime.CompilerServices

/// <summary>
/// Creates a cell stream sink. A second <c>StreamSink.send</c> in one transaction throws an exception.
/// </summary>
/// <typeparam name="'a">The type of the values the cell stream sink fires.</typeparam>
/// <returns>A new cell stream sink.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let create<'a> () = CellInternal.CreateStreamSinkImpl<'a>()

/// <summary>
///     Creates a cell stream sink that combines the values of more than one
///     <c>StreamSink.send</c> in one transaction.
/// </summary>
/// <param name="coalesce">
///     Puts two values from the same transaction together. It receives the value from before this
///     send and the new value, in that sequence.
/// </param>
/// <returns>A new cell stream sink.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let createWithCoalesce coalesce =
    CellInternal.CreateStreamSinkImpl(Func<_, _, _> coalesce)
