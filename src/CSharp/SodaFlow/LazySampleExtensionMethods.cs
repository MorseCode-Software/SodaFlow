using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     Transforms and combines lazy samples. Each result is a lazy sample too,
///     which only SodaFlow reads.
/// </summary>
[PublicAPI]
public static class LazySampleExtensionMethods
{
    /// <summary>
    ///     Transforms the value of a lazy sample.
    /// </summary>
    /// <typeparam name="T">The type of the sample.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="sample">The sample.</param>
    /// <param name="f">
    ///     Makes the result from the value. It can run more than one time, thus it
    ///     must be pure, and it must not send into the graph.
    /// </param>
    /// <returns>A lazy sample of the result.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LazySample<TResult> Map<T, TResult>(this LazySample<T> sample, Func<T, TResult> f) =>
        sample.MapImpl(f);

    /// <summary>
    ///     Combines two lazy samples into one.
    /// </summary>
    /// <typeparam name="T1">The type of the first sample.</typeparam>
    /// <typeparam name="T2">The type of the second sample.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="sample1">The first sample.</param>
    /// <param name="sample2">The second sample.</param>
    /// <param name="f">
    ///     Makes the result from the two values. It can run more than one time,
    ///     thus it must be pure, and it must not send into the graph.
    /// </param>
    /// <returns>A lazy sample of the result.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LazySample<TResult> Lift<T1, T2, TResult>(
        this LazySample<T1> sample1,
        LazySample<T2> sample2,
        Func<T1, T2, TResult> f) =>
        LazySampleInternal.LiftImpl(sample1: sample1, sample2: sample2, f: f);

    /// <summary>
    ///     Combines three lazy samples into one.
    /// </summary>
    /// <typeparam name="T1">The type of the first sample.</typeparam>
    /// <typeparam name="T2">The type of the second sample.</typeparam>
    /// <typeparam name="T3">The type of the third sample.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="sample1">The first sample.</param>
    /// <param name="sample2">The second sample.</param>
    /// <param name="sample3">The third sample.</param>
    /// <param name="f">
    ///     Makes the result from the three values. It can run more than one time,
    ///     thus it must be pure, and it must not send into the graph.
    /// </param>
    /// <returns>A lazy sample of the result.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LazySample<TResult> Lift<T1, T2, T3, TResult>(
        this LazySample<T1> sample1,
        LazySample<T2> sample2,
        LazySample<T3> sample3,
        Func<T1, T2, T3, TResult> f) =>
        LazySampleInternal.LiftImpl(sample1: sample1, sample2: sample2, sample3: sample3, f: f);

    /// <summary>
    ///     Combines four lazy samples into one.
    /// </summary>
    /// <typeparam name="T1">The type of the first sample.</typeparam>
    /// <typeparam name="T2">The type of the second sample.</typeparam>
    /// <typeparam name="T3">The type of the third sample.</typeparam>
    /// <typeparam name="T4">The type of the fourth sample.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="sample1">The first sample.</param>
    /// <param name="sample2">The second sample.</param>
    /// <param name="sample3">The third sample.</param>
    /// <param name="sample4">The fourth sample.</param>
    /// <param name="f">
    ///     Makes the result from the four values. It can run more than one time,
    ///     thus it must be pure, and it must not send into the graph.
    /// </param>
    /// <returns>A lazy sample of the result.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LazySample<TResult> Lift<T1, T2, T3, T4, TResult>(
        this LazySample<T1> sample1,
        LazySample<T2> sample2,
        LazySample<T3> sample3,
        LazySample<T4> sample4,
        Func<T1, T2, T3, T4, TResult> f) =>
        LazySampleInternal.LiftImpl(sample1: sample1, sample2: sample2, sample3: sample3, sample4: sample4, f: f);

    /// <summary>
    ///     Combines five lazy samples into one.
    /// </summary>
    /// <typeparam name="T1">The type of the first sample.</typeparam>
    /// <typeparam name="T2">The type of the second sample.</typeparam>
    /// <typeparam name="T3">The type of the third sample.</typeparam>
    /// <typeparam name="T4">The type of the fourth sample.</typeparam>
    /// <typeparam name="T5">The type of the fifth sample.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="sample1">The first sample.</param>
    /// <param name="sample2">The second sample.</param>
    /// <param name="sample3">The third sample.</param>
    /// <param name="sample4">The fourth sample.</param>
    /// <param name="sample5">The fifth sample.</param>
    /// <param name="f">
    ///     Makes the result from the five values. It can run more than one time,
    ///     thus it must be pure, and it must not send into the graph.
    /// </param>
    /// <returns>A lazy sample of the result.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LazySample<TResult> Lift<T1, T2, T3, T4, T5, TResult>(
        this LazySample<T1> sample1,
        LazySample<T2> sample2,
        LazySample<T3> sample3,
        LazySample<T4> sample4,
        LazySample<T5> sample5,
        Func<T1, T2, T3, T4, T5, TResult> f) =>
        LazySampleInternal.LiftImpl(
            sample1: sample1,
            sample2: sample2,
            sample3: sample3,
            sample4: sample4,
            sample5: sample5,
            f: f);

    /// <summary>
    ///     Combines six lazy samples into one.
    /// </summary>
    /// <typeparam name="T1">The type of the first sample.</typeparam>
    /// <typeparam name="T2">The type of the second sample.</typeparam>
    /// <typeparam name="T3">The type of the third sample.</typeparam>
    /// <typeparam name="T4">The type of the fourth sample.</typeparam>
    /// <typeparam name="T5">The type of the fifth sample.</typeparam>
    /// <typeparam name="T6">The type of the sixth sample.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="sample1">The first sample.</param>
    /// <param name="sample2">The second sample.</param>
    /// <param name="sample3">The third sample.</param>
    /// <param name="sample4">The fourth sample.</param>
    /// <param name="sample5">The fifth sample.</param>
    /// <param name="sample6">The sixth sample.</param>
    /// <param name="f">
    ///     Makes the result from the six values. It can run more than one time,
    ///     thus it must be pure, and it must not send into the graph.
    /// </param>
    /// <returns>A lazy sample of the result.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LazySample<TResult> Lift<T1, T2, T3, T4, T5, T6, TResult>(
        this LazySample<T1> sample1,
        LazySample<T2> sample2,
        LazySample<T3> sample3,
        LazySample<T4> sample4,
        LazySample<T5> sample5,
        LazySample<T6> sample6,
        Func<T1, T2, T3, T4, T5, T6, TResult> f) =>
        LazySampleInternal.LiftImpl(
            sample1: sample1,
            sample2: sample2,
            sample3: sample3,
            sample4: sample4,
            sample5: sample5,
            sample6: sample6,
            f: f);

    /// <summary>
    ///     Combines seven lazy samples into one.
    /// </summary>
    /// <typeparam name="T1">The type of the first sample.</typeparam>
    /// <typeparam name="T2">The type of the second sample.</typeparam>
    /// <typeparam name="T3">The type of the third sample.</typeparam>
    /// <typeparam name="T4">The type of the fourth sample.</typeparam>
    /// <typeparam name="T5">The type of the fifth sample.</typeparam>
    /// <typeparam name="T6">The type of the sixth sample.</typeparam>
    /// <typeparam name="T7">The type of the seventh sample.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="sample1">The first sample.</param>
    /// <param name="sample2">The second sample.</param>
    /// <param name="sample3">The third sample.</param>
    /// <param name="sample4">The fourth sample.</param>
    /// <param name="sample5">The fifth sample.</param>
    /// <param name="sample6">The sixth sample.</param>
    /// <param name="sample7">The seventh sample.</param>
    /// <param name="f">
    ///     Makes the result from the seven values. It can run more than one time,
    ///     thus it must be pure, and it must not send into the graph.
    /// </param>
    /// <returns>A lazy sample of the result.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LazySample<TResult> Lift<T1, T2, T3, T4, T5, T6, T7, TResult>(
        this LazySample<T1> sample1,
        LazySample<T2> sample2,
        LazySample<T3> sample3,
        LazySample<T4> sample4,
        LazySample<T5> sample5,
        LazySample<T6> sample6,
        LazySample<T7> sample7,
        Func<T1, T2, T3, T4, T5, T6, T7, TResult> f) =>
        LazySampleInternal.LiftImpl(
            sample1: sample1,
            sample2: sample2,
            sample3: sample3,
            sample4: sample4,
            sample5: sample5,
            sample6: sample6,
            sample7: sample7,
            f: f);

    /// <summary>
    ///     Combines eight lazy samples into one.
    /// </summary>
    /// <typeparam name="T1">The type of the first sample.</typeparam>
    /// <typeparam name="T2">The type of the second sample.</typeparam>
    /// <typeparam name="T3">The type of the third sample.</typeparam>
    /// <typeparam name="T4">The type of the fourth sample.</typeparam>
    /// <typeparam name="T5">The type of the fifth sample.</typeparam>
    /// <typeparam name="T6">The type of the sixth sample.</typeparam>
    /// <typeparam name="T7">The type of the seventh sample.</typeparam>
    /// <typeparam name="T8">The type of the eighth sample.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="sample1">The first sample.</param>
    /// <param name="sample2">The second sample.</param>
    /// <param name="sample3">The third sample.</param>
    /// <param name="sample4">The fourth sample.</param>
    /// <param name="sample5">The fifth sample.</param>
    /// <param name="sample6">The sixth sample.</param>
    /// <param name="sample7">The seventh sample.</param>
    /// <param name="sample8">The eighth sample.</param>
    /// <param name="f">
    ///     Makes the result from the eight values. It can run more than one time,
    ///     thus it must be pure, and it must not send into the graph.
    /// </param>
    /// <returns>A lazy sample of the result.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LazySample<TResult> Lift<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(
        this LazySample<T1> sample1,
        LazySample<T2> sample2,
        LazySample<T3> sample3,
        LazySample<T4> sample4,
        LazySample<T5> sample5,
        LazySample<T6> sample6,
        LazySample<T7> sample7,
        LazySample<T8> sample8,
        Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult> f) =>
        LazySampleInternal.LiftImpl(
            sample1: sample1,
            sample2: sample2,
            sample3: sample3,
            sample4: sample4,
            sample5: sample5,
            sample6: sample6,
            sample7: sample7,
            sample8: sample8,
            f: f);
}
