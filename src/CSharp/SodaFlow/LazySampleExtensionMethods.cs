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
    /// <param name="a">The first sample.</param>
    /// <param name="b">The second sample.</param>
    /// <param name="f">
    ///     Makes the result from the two values. It can run more than one time, thus
    ///     it must be pure, and it must not send into the graph.
    /// </param>
    /// <returns>A lazy sample of the result.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LazySample<TResult> Lift<T1, T2, TResult>(
        this LazySample<T1> a,
        LazySample<T2> b,
        Func<T1, T2, TResult> f) =>
        LazySampleInternal.LiftImpl(a: a, b: b, f: f);

    /// <summary>
    ///     Combines three lazy samples into one.
    /// </summary>
    /// <typeparam name="T1">The type of the first sample.</typeparam>
    /// <typeparam name="T2">The type of the second sample.</typeparam>
    /// <typeparam name="T3">The type of the third sample.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="a">The first sample.</param>
    /// <param name="b">The second sample.</param>
    /// <param name="c">The third sample.</param>
    /// <param name="f">
    ///     Makes the result from the three values. It can run more than one time,
    ///     thus it must be pure, and it must not send into the graph.
    /// </param>
    /// <returns>A lazy sample of the result.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LazySample<TResult> Lift<T1, T2, T3, TResult>(
        this LazySample<T1> a,
        LazySample<T2> b,
        LazySample<T3> c,
        Func<T1, T2, T3, TResult> f) =>
        LazySampleInternal.LiftImpl(a: a, b: b, c: c, f: f);

    /// <summary>
    ///     Combines four lazy samples into one.
    /// </summary>
    /// <typeparam name="T1">The type of the first sample.</typeparam>
    /// <typeparam name="T2">The type of the second sample.</typeparam>
    /// <typeparam name="T3">The type of the third sample.</typeparam>
    /// <typeparam name="T4">The type of the fourth sample.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="a">The first sample.</param>
    /// <param name="b">The second sample.</param>
    /// <param name="c">The third sample.</param>
    /// <param name="d">The fourth sample.</param>
    /// <param name="f">
    ///     Makes the result from the four values. It can run more than one time,
    ///     thus it must be pure, and it must not send into the graph.
    /// </param>
    /// <returns>A lazy sample of the result.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LazySample<TResult> Lift<T1, T2, T3, T4, TResult>(
        this LazySample<T1> a,
        LazySample<T2> b,
        LazySample<T3> c,
        LazySample<T4> d,
        Func<T1, T2, T3, T4, TResult> f) =>
        LazySampleInternal.LiftImpl(a: a, b: b, c: c, d: d, f: f);
}
