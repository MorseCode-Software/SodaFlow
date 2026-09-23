using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     The operations available on a <see cref="Behavior{T}" />.
/// </summary>
/// <remarks>
///     A behavior is a continuously varying value. These are extension methods rather than instance
///     members so that the combinators live out of the small assembly that holds the FRP engine.
///     the effect at the call site is the same.
///     See <see cref="CellExtensionMethods" /> for the corresponding operations on cells, which are
///     behaviors that also give the stream of their changes.
/// </remarks>
[PublicAPI]
public static class BehaviorExtensionMethods
{
    /// <summary>
    ///     Sample the current value of the behavior.
    /// </summary>
    /// <typeparam name="T">The type of the behavior.</typeparam>
    /// <param name="b">The behavior.</param>
    /// <returns>The current value of the behavior.</returns>
    /// <remarks>
    ///     <para>
    ///         The functions that the primitives give to a stream can call this method. Those primitives are
    ///         <see cref="StreamExtensionMethods.Map{T, TResult}(Stream{T}, Func{T,TResult})" />, where a call
    ///         here is the same as a snapshot of the behavior,
    ///         <see cref=" StreamExtensionMethods.Snapshot{T, T2, TResult}(Stream{T}, Behavior{T2}, Func{T, T2, TResult})" />,
    ///         <see cref="StreamExtensionMethods.Filter{T}(Stream{T}, Func{T, bool})" />, and
    ///         <see cref="StreamExtensionMethods.Merge{T}(Stream{T}, Stream{T}, Func{T, T, T})" />.
    ///     </para>
    ///     <para>
    ///         It can be best to use this method in an explicit transaction (using
    ///         <see cref="Transaction.Run{T}(Func{T})" /> or <see cref="Transaction.RunVoid(Action)" />). For
    ///         example, a b.Sample() in an explicit transaction, with a b.Updates().ListenStrong(...),
    ///         captures the current value and each update. The code keeps each value between the two.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static T Sample<T>(this Behavior<T> b) => b.SampleImpl();

    /// <summary>
    ///     Sample the current value of the behavior lazily.
    /// </summary>
    /// <typeparam name="T">The type of the behavior.</typeparam>
    /// <param name="b">The behavior.</param>
    /// <returns>A lazy value that gives the current value of the behavior.</returns>
    /// <remarks>
    ///     This is a variant of <see cref="Sample{T}" /> that works with the <see cref="BehaviorLoop{T}" /> class
    ///     when no code closed the behavior loop.  Use it in code that is general
    ///     sufficient for a <see cref="BehaviorLoop{T}" /> argument.  See
    ///     <see cref="StreamExtensionMethods.HoldLazy{T}(Stream{T}, Lazy{T})" />.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Lazy<T> SampleLazy<T>(this Behavior<T> b) => b.SampleLazyImpl();

    /// <summary>
    ///     Transforms the values of a behavior with the given function. The behavior from this call
    ///     has the value of that function on the value of the input behavior.
    /// </summary>
    /// <typeparam name="T">The type of the behavior.</typeparam>
    /// <typeparam name="TResult">The type of values fired by the returned behavior.</typeparam>
    /// <param name="b">The behavior.</param>
    /// <param name="f">
    ///     Function to apply to change the values.  It must be a pure function.
    /// </param>
    /// <returns>A behavior which fires values transformed by <paramref name="f" /> for each value fired by
    /// this behavior.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<TResult> Map<T, TResult>(this Behavior<T> b, Func<T, TResult> f) => b.MapImpl(f);

    /// <summary>
    ///     Lift a binary function into behaviors, so the returned behavior always reflects the specified
    ///     function applied to the input behaviors' values.
    /// </summary>
    /// <typeparam name="T">The type of the behavior.</typeparam>
    /// <typeparam name="T2">The type of second behavior.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="b">The behavior.</param>
    /// <param name="b2">The second behavior.</param>
    /// <param name="f">The binary function to lift into the behaviors.</param>
    /// <returns>
    ///     A behavior containing values resulting from the binary function applied to the input behaviors'
    ///     values.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<TResult> Lift<T, T2, TResult>(
        this Behavior<T> b,
        Behavior<T2> b2,
        Func<T, T2, TResult> f) =>
        b.LiftImpl(b2: b2, f: f);

    /// <summary>
    ///     Lift a ternary function into behaviors, so the returned behavior always reflects the specified
    ///     function applied to the input behaviors' values.
    /// </summary>
    /// <typeparam name="T">The type of the behavior.</typeparam>
    /// <typeparam name="T2">The type of second behavior.</typeparam>
    /// <typeparam name="T3">The type of third behavior.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="b">The behavior.</param>
    /// <param name="b2">The second behavior.</param>
    /// <param name="b3">The third behavior.</param>
    /// <param name="f">The binary function to lift into the behaviors.</param>
    /// <returns>
    ///     A behavior containing values resulting from the ternary function applied to the input behaviors'
    ///     values.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<TResult> Lift<T, T2, T3, TResult>(
        this Behavior<T> b,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Func<T, T2, T3, TResult> f) =>
        b.LiftImpl(b2: b2, b3: b3, f: f);

    /// <summary>
    ///     Lift a quaternary function into behaviors, so the returned behavior always reflects the specified
    ///     function applied to the input behaviors' values.
    /// </summary>
    /// <typeparam name="T">The type of the behavior.</typeparam>
    /// <typeparam name="T2">The type of second behavior.</typeparam>
    /// <typeparam name="T3">The type of third behavior.</typeparam>
    /// <typeparam name="T4">The type of fourth behavior.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="b">The behavior.</param>
    /// <param name="b2">The second behavior.</param>
    /// <param name="b3">The third behavior.</param>
    /// <param name="b4">The fourth behavior.</param>
    /// <param name="f">The binary function to lift into the behaviors.</param>
    /// <returns>
    ///     A behavior containing values resulting from the quaternary function applied to the input
    ///     behaviors' values.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<TResult> Lift<T, T2, T3, T4, TResult>(
        this Behavior<T> b,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Func<T, T2, T3, T4, TResult> f) =>
        b.LiftImpl(b2: b2, b3: b3, b4: b4, f: f);

    /// <summary>
    ///     Lift a 5-argument function into behaviors, so the returned behavior always reflects the specified
    ///     function applied to the input behaviors' values.
    /// </summary>
    /// <typeparam name="T">The type of the behavior.</typeparam>
    /// <typeparam name="T2">The type of second behavior.</typeparam>
    /// <typeparam name="T3">The type of third behavior.</typeparam>
    /// <typeparam name="T4">The type of fourth behavior.</typeparam>
    /// <typeparam name="T5">The type of fifth behavior.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="b">The behavior.</param>
    /// <param name="b2">The second behavior.</param>
    /// <param name="b3">The third behavior.</param>
    /// <param name="b4">The fourth behavior.</param>
    /// <param name="b5">The fifth behavior.</param>
    /// <param name="f">The binary function to lift into the behaviors.</param>
    /// <returns>
    ///     A behavior containing values resulting from the 5-argument function applied to the input
    ///     behaviors' values.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<TResult> Lift<T, T2, T3, T4, T5, TResult>(
        this Behavior<T> b,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Func<T, T2, T3, T4, T5, TResult> f) =>
        b.LiftImpl(b2: b2, b3: b3, b4: b4, b5: b5, f: f);

    /// <summary>
    ///     Lift a 6-argument function into behaviors, so the returned behavior always reflects the specified
    ///     function applied to the input behaviors' values.
    /// </summary>
    /// <typeparam name="T">The type of the behavior.</typeparam>
    /// <typeparam name="T2">The type of second behavior.</typeparam>
    /// <typeparam name="T3">The type of third behavior.</typeparam>
    /// <typeparam name="T4">The type of fourth behavior.</typeparam>
    /// <typeparam name="T5">The type of fifth behavior.</typeparam>
    /// <typeparam name="T6">The type of sixth behavior.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="b">The behavior.</param>
    /// <param name="b2">The second behavior.</param>
    /// <param name="b3">The third behavior.</param>
    /// <param name="b4">The fourth behavior.</param>
    /// <param name="b5">The fifth behavior.</param>
    /// <param name="b6">The sixth behavior.</param>
    /// <param name="f">The binary function to lift into the behaviors.</param>
    /// <returns>
    ///     A behavior containing values resulting from the 6-argument function applied to the input
    ///     behaviors' values.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<TResult> Lift<T, T2, T3, T4, T5, T6, TResult>(
        this Behavior<T> b,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Func<T, T2, T3, T4, T5, T6, TResult> f) =>
        b.LiftImpl(b2: b2, b3: b3, b4: b4, b5: b5, b6: b6, f: f);

    /// <summary>
    ///     Apply a value in a behavior to a function in a behavior.  This is the primitive for all function lifting.
    /// </summary>
    /// <typeparam name="T">The type of the behavior.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="b">The behavior.</param>
    /// <param name="bf">The behavior containing the function to apply the value to.</param>
    /// <returns>
    ///     A behavior whose value is the result of applying the current function in behavior
    ///     <paramref name="bf" /> to this behavior's current value.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<TResult> Apply<T, TResult>(this Behavior<T> b, Behavior<Func<T, TResult>> bf) =>
        b.ApplyImpl(bf);

    /// <summary>
    ///     Unwrap a behavior in a second behavior to give a time-varying behavior implementation.
    /// </summary>
    /// <typeparam name="T">The type of the behavior.</typeparam>
    /// <param name="bba">The behavior that holds a second behavior.</param>
    /// <returns>The unwrapped behavior.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<T> SwitchB<T>(this Behavior<Behavior<T>> bba) => bba.SwitchBImpl<T, Behavior<T>>();

    /// <summary>
    ///     Unwrap a cell in a behavior to give a time-varying cell implementation.
    /// </summary>
    /// <typeparam name="T">The type of the cell.</typeparam>
    /// <param name="bca">The behavior containing a cell.</param>
    /// <returns>The unwrapped cell.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<T> SwitchC<T>(this Behavior<Cell<T>> bca) => bca.SwitchCImpl<T, Cell<T>>();

    /// <summary>
    ///     Unwrap a stream in a behavior to give a time-varying stream implementation. At a change to
    ///     the behavior, the output stream fires the simultaneous firing, if there is one. That firing
    ///     comes from the stream that the behavior held at the start of the transaction.
    /// </summary>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <param name="bsa">The behavior containing the stream.</param>
    /// <returns>The unwrapped stream.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Stream<T> SwitchS<T>(this Behavior<Stream<T>> bsa) => bsa.SwitchSImpl<T, Stream<T>>();

    /// <summary>
    ///     Lift a function into a collection of behaviors, so the returned behavior always reflects the
    ///     specified function applied to the input behaviors' values.
    /// </summary>
    /// <typeparam name="T">The type of the behaviors.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="b">The collection of behaviors.</param>
    /// <param name="f">The binary function to lift into the behaviors.</param>
    /// <returns>
    ///     A behavior containing values resulting from the function applied to the input behaviors' values.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<TResult> Lift<T, TResult>(
        this IEnumerable<Behavior<T>> b,
        Func<IReadOnlyList<T>, TResult> f) =>
        b.LiftBehaviorsImpl(f);

    /// <summary>
    ///     Lift a function into a collection of behaviors, so the returned behavior always reflects the
    ///     specified function applied to the input behaviors' values.
    /// </summary>
    /// <typeparam name="T">The type of the behaviors.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="b">The collection of behaviors.</param>
    /// <param name="f">The binary function to lift into the behaviors.</param>
    /// <returns>
    ///     A behavior containing values resulting from the function applied to the input behaviors' values.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<TResult> Lift<T, TResult>(
        this IReadOnlyCollection<Behavior<T>> b,
        Func<IReadOnlyList<T>, TResult> f) =>
        b.LiftBehaviorsImpl(f);

    /// <summary>
    ///     Lift into a collection of behaviors, so the returned behavior always reflects a list of the input behaviors'
    ///     values.
    /// </summary>
    /// <typeparam name="T">The type of the behaviors.</typeparam>
    /// <param name="b">The collection of behaviors.</param>
    /// <returns>A behavior containing a list of the input behaviors' values.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<IReadOnlyList<T>> Lift<T>(this IEnumerable<Behavior<T>> b) =>
        b.LiftBehaviorsImpl<T, Behavior<T>, IReadOnlyList<T>>(static v => v);

    /// <summary>
    ///     Lift into a collection of behaviors, so the returned behavior always reflects a list of the input behaviors'
    ///     values.
    /// </summary>
    /// <typeparam name="T">The type of the behaviors.</typeparam>
    /// <param name="b">The collection of behaviors.</param>
    /// <returns>A behavior containing a list of the input behaviors' values.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<IReadOnlyList<T>> Lift<T>(this IReadOnlyCollection<Behavior<T>> b) =>
        b.LiftBehaviorsImpl<T, Behavior<T>, IReadOnlyList<T>>(static v => v);
}
