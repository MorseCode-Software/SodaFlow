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
/// <param name="sample">The first sample.</param>
/// <param name="sample2">The second sample.</param>
/// <returns>A lazy sample of the result.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift2 (f: 'T1 -> 'T2 -> 'TResult) (sample: LazySample<'T1>) (sample2: LazySample<'T2>) =
    LazySampleInternal.LiftImpl(sample, sample2, Func<_, _, _> f)

/// <summary>
///     Combines three lazy samples into one.
/// </summary>
/// <param name="f">Makes the result from the three values.</param>
/// <param name="sample">The first sample.</param>
/// <param name="sample2">The second sample.</param>
/// <param name="sample3">The third sample.</param>
/// <returns>A lazy sample of the result.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift3
    (f: 'T1 -> 'T2 -> 'T3 -> 'TResult)
    (sample: LazySample<'T1>)
    (sample2: LazySample<'T2>)
    (sample3: LazySample<'T3>)
    =
    LazySampleInternal.LiftImpl(sample, sample2, sample3, Func<_, _, _, _> f)

/// <summary>
///     Combines four lazy samples into one.
/// </summary>
/// <param name="f">Makes the result from the four values.</param>
/// <param name="sample">The first sample.</param>
/// <param name="sample2">The second sample.</param>
/// <param name="sample3">The third sample.</param>
/// <param name="sample4">The fourth sample.</param>
/// <returns>A lazy sample of the result.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift4
    (f: 'T1 -> 'T2 -> 'T3 -> 'T4 -> 'TResult)
    (sample: LazySample<'T1>)
    (sample2: LazySample<'T2>)
    (sample3: LazySample<'T3>)
    (sample4: LazySample<'T4>)
    =
    LazySampleInternal.LiftImpl(sample, sample2, sample3, sample4, Func<_, _, _, _, _> f)

/// <summary>
///     Combines five lazy samples into one.
/// </summary>
/// <param name="f">Makes the result from the five values.</param>
/// <param name="sample">The first sample.</param>
/// <param name="sample2">The second sample.</param>
/// <param name="sample3">The third sample.</param>
/// <param name="sample4">The fourth sample.</param>
/// <param name="sample5">The fifth sample.</param>
/// <returns>A lazy sample of the result.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift5
    (f: 'T1 -> 'T2 -> 'T3 -> 'T4 -> 'T5 -> 'TResult)
    (sample: LazySample<'T1>)
    (sample2: LazySample<'T2>)
    (sample3: LazySample<'T3>)
    (sample4: LazySample<'T4>)
    (sample5: LazySample<'T5>)
    =
    LazySampleInternal.LiftImpl(sample, sample2, sample3, sample4, sample5, Func<_, _, _, _, _, _> f)

/// <summary>
///     Combines six lazy samples into one.
/// </summary>
/// <param name="f">Makes the result from the six values.</param>
/// <param name="sample">The first sample.</param>
/// <param name="sample2">The second sample.</param>
/// <param name="sample3">The third sample.</param>
/// <param name="sample4">The fourth sample.</param>
/// <param name="sample5">The fifth sample.</param>
/// <param name="sample6">The sixth sample.</param>
/// <returns>A lazy sample of the result.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift6
    (f: 'T1 -> 'T2 -> 'T3 -> 'T4 -> 'T5 -> 'T6 -> 'TResult)
    (sample: LazySample<'T1>)
    (sample2: LazySample<'T2>)
    (sample3: LazySample<'T3>)
    (sample4: LazySample<'T4>)
    (sample5: LazySample<'T5>)
    (sample6: LazySample<'T6>)
    =
    LazySampleInternal.LiftImpl(sample, sample2, sample3, sample4, sample5, sample6, Func<_, _, _, _, _, _, _> f)

/// <summary>
///     Combines seven lazy samples into one.
/// </summary>
/// <param name="f">Makes the result from the seven values.</param>
/// <param name="sample">The first sample.</param>
/// <param name="sample2">The second sample.</param>
/// <param name="sample3">The third sample.</param>
/// <param name="sample4">The fourth sample.</param>
/// <param name="sample5">The fifth sample.</param>
/// <param name="sample6">The sixth sample.</param>
/// <param name="sample7">The seventh sample.</param>
/// <returns>A lazy sample of the result.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift7
    (f: 'T1 -> 'T2 -> 'T3 -> 'T4 -> 'T5 -> 'T6 -> 'T7 -> 'TResult)
    (sample: LazySample<'T1>)
    (sample2: LazySample<'T2>)
    (sample3: LazySample<'T3>)
    (sample4: LazySample<'T4>)
    (sample5: LazySample<'T5>)
    (sample6: LazySample<'T6>)
    (sample7: LazySample<'T7>)
    =
    LazySampleInternal.LiftImpl(
        sample,
        sample2,
        sample3,
        sample4,
        sample5,
        sample6,
        sample7,
        Func<_, _, _, _, _, _, _, _> f
    )

/// <summary>
///     Combines eight lazy samples into one.
/// </summary>
/// <param name="f">Makes the result from the eight values.</param>
/// <param name="sample">The first sample.</param>
/// <param name="sample2">The second sample.</param>
/// <param name="sample3">The third sample.</param>
/// <param name="sample4">The fourth sample.</param>
/// <param name="sample5">The fifth sample.</param>
/// <param name="sample6">The sixth sample.</param>
/// <param name="sample7">The seventh sample.</param>
/// <param name="sample8">The eighth sample.</param>
/// <returns>A lazy sample of the result.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift8
    (f: 'T1 -> 'T2 -> 'T3 -> 'T4 -> 'T5 -> 'T6 -> 'T7 -> 'T8 -> 'TResult)
    (sample: LazySample<'T1>)
    (sample2: LazySample<'T2>)
    (sample3: LazySample<'T3>)
    (sample4: LazySample<'T4>)
    (sample5: LazySample<'T5>)
    (sample6: LazySample<'T6>)
    (sample7: LazySample<'T7>)
    (sample8: LazySample<'T8>)
    =
    LazySampleInternal.LiftImpl(
        sample,
        sample2,
        sample3,
        sample4,
        sample5,
        sample6,
        sample7,
        sample8,
        Func<_, _, _, _, _, _, _, _, _> f
    )
