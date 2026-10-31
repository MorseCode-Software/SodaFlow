/// <summary>
///     Making and transforming lazy samples. <c>Cell.sampleLazy</c> gives one, and
///     <c>Stream.holdLazy</c>, <c>Stream.accumLazy</c>, <c>Stream.collectLazy</c>, and
///     <c>Cell.constantLazy</c> take one.
/// </summary>
/// <remarks>
///     Only SodaFlow reads a lazy sample, at the end of the transaction that asked for it. Thus, a
///     transaction that fails can forget the value that the sample took. A cell that starts from
///     the sample then reads the committed value. A function given to <c>defer</c>,
///     <c>map</c>, or one of the <c>lift</c> functions can run more than one time. Thus, it must
///     be pure, and it must not send into the graph.
/// </remarks>
module SodaFlow.LazySample

open System
open System.Runtime.CompilerServices

/// <summary>
///     Makes a lazy sample from a function. SodaFlow runs the function when it reads the sample.
/// </summary>
/// <param name="f">Gives the value.</param>
/// <returns>The lazy sample.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let defer (f: unit -> 'T) = LazySampleInternal.DeferImpl(Func<'T> f)

/// <summary>
///     Transforms the value of a lazy sample.
/// </summary>
/// <param name="f">Makes the result from the value.</param>
/// <param name="sample">The sample.</param>
/// <returns>A lazy sample of the result.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let map (f: 'T -> 'TResult) (sample: LazySample<'T>) = sample.MapImpl(Func<'T, 'TResult> f)

/// <summary>
///     Combines two lazy samples into one.
/// </summary>
/// <param name="f">Makes the result from the two values.</param>
/// <param name="a">The first sample.</param>
/// <param name="b">The second sample.</param>
/// <returns>A lazy sample of the result.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift2 (f: 'T1 -> 'T2 -> 'TResult) (a: LazySample<'T1>) (b: LazySample<'T2>) =
    LazySampleInternal.LiftImpl(a, b, Func<'T1, 'T2, 'TResult> f)

/// <summary>
///     Combines three lazy samples into one.
/// </summary>
/// <param name="f">Makes the result from the three values.</param>
/// <param name="a">The first sample.</param>
/// <param name="b">The second sample.</param>
/// <param name="c">The third sample.</param>
/// <returns>A lazy sample of the result.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift3 (f: 'T1 -> 'T2 -> 'T3 -> 'TResult) (a: LazySample<'T1>) (b: LazySample<'T2>) (c: LazySample<'T3>) =
    LazySampleInternal.LiftImpl(a, b, c, Func<'T1, 'T2, 'T3, 'TResult> f)

/// <summary>
///     Combines four lazy samples into one.
/// </summary>
/// <param name="f">Makes the result from the four values.</param>
/// <param name="a">The first sample.</param>
/// <param name="b">The second sample.</param>
/// <param name="c">The third sample.</param>
/// <param name="d">The fourth sample.</param>
/// <returns>A lazy sample of the result.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift4
    (f: 'T1 -> 'T2 -> 'T3 -> 'T4 -> 'TResult)
    (a: LazySample<'T1>)
    (b: LazySample<'T2>)
    (c: LazySample<'T3>)
    (d: LazySample<'T4>)
    =
    LazySampleInternal.LiftImpl(a, b, c, d, Func<'T1, 'T2, 'T3, 'T4, 'TResult> f)
