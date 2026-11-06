using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     Helper methods for creating a <see cref="Behavior{T}" />.
/// </summary>
[PublicAPI]
public static class Behavior
{
    /// <summary>
    ///     Creates a behavior with a constant value.
    /// </summary>
    /// <typeparam name="T">The type of the value of the behavior.</typeparam>
    /// <param name="value">The value of the behavior.</param>
    /// <returns>A behavior with a constant value.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<T> Constant<T>(T value) => BehaviorInternal.ConstantImpl(value);

    /// <summary>
    ///     Creates a behavior with a lazily computed constant value.
    /// </summary>
    /// <typeparam name="T">The type of the value of the behavior.</typeparam>
    /// <param name="value">The lazily computed value of the behavior.</param>
    /// <returns>A behavior with a lazily computed constant value.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<T> ConstantLazy<T>(LazySample<T> value) => BehaviorInternal.ConstantLazyImpl(value);

    /// <summary>
    ///     Creates a writable behavior that uses the last value if
    ///     <see cref="BehaviorSinkExtensionMethods.Send{T}" /> gets more than one call
    ///     per transaction.
    /// </summary>
    /// <param name="initialValue">The initial value of the behavior.</param>
    /// <typeparam name="T">The type of values in the behavior sink.</typeparam>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static BehaviorSink<T> CreateSink<T>(T initialValue) => BehaviorInternal.CreateSinkImpl(initialValue);

    /// <summary>
    ///     Creates a writable behavior that uses <paramref name="coalesce" /> to put
    ///     values together when <see cref="BehaviorSinkExtensionMethods.Send{T}" />
    ///     gets more than one call in one transaction.
    /// </summary>
    /// <param name="initialValue">The initial value of the behavior.</param>
    /// <param name="coalesce">
    ///     Function to put values together when
    ///     <see cref="BehaviorSinkExtensionMethods.Send{T}" /> gets a call more than
    ///     one time per transaction.
    /// </param>
    /// <typeparam name="T">The type of values in the behavior sink.</typeparam>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static BehaviorSink<T> CreateSink<T>(T initialValue, Func<T, T, T> coalesce) =>
        BehaviorInternal.CreateSinkImpl(initialValue: initialValue, coalesce: coalesce);

    /// <summary>
    ///     Makes a <see cref="BehaviorLoop{T}" />. A caller must call this and close
    ///     the loop in one transaction.
    /// </summary>
    /// <typeparam name="T">The type of values in the behavior loop.</typeparam>
    public static BehaviorLoop<T> CreateLoop<T>() => new();

    /// <summary>
    ///     Creates a helper to loop over a behavior for the specified type.
    /// </summary>
    /// <typeparam name="T">The type of the behavior to loop.</typeparam>
    /// <returns>A <see cref="BehaviorLooper{T}" /> which completes the loop.</returns>
    [Pure]
    public static BehaviorLooper<T> Loop<T>() => new();
}

/// <summary>
///     A helper to complete a loop over a behavior.
/// </summary>
/// <typeparam name="T">The type of the behavior in the loop.</typeparam>
public struct BehaviorLooper<T>
{
    /// <summary>
    ///     Loop a behavior and return a value tuple containing the resulting behavior
    ///     and captures.
    /// </summary>
    /// <typeparam name="TCaptures">The type of the captures to return.</typeparam>
    /// <param name="f">
    ///     A function which takes the behavior loop and returns a value tuple
    ///     containing the resulting behavior and captures.
    /// </param>
    /// <returns>A value tuple containing the resulting behavior and captures.</returns>
    [Pure]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public (Behavior<T> Behavior, TCaptures Captures) WithCaptures<TCaptures>(
        Func<LoopedBehavior<T>, (Behavior<T> Behavior, TCaptures Captures)> f) =>
        this.WithCaptures(f: f, close: static (captures, _) => captures);

    /// <summary>
    ///     Loop a behavior and return a value tuple containing the resulting behavior
    ///     and the captures, which a second function converts after the loop closes.
    /// </summary>
    /// <typeparam name="TLoopedCaptures">
    ///     The type of the captures that <paramref name="f" /> returns.
    /// </typeparam>
    /// <typeparam name="TCaptures">The type of the captures to return.</typeparam>
    /// <param name="f">
    ///     A function which takes the behavior loop and returns a value tuple
    ///     containing the resulting behavior and captures.
    /// </param>
    /// <param name="close">
    ///     A function which takes the captures and the <see cref="LoopCloser" /> of
    ///     the loop, and returns the captures with each looped value converted.
    /// </param>
    /// <returns>
    ///     A value tuple containing the resulting behavior and the converted
    ///     captures.
    /// </returns>
    /// <remarks>
    ///     A capture that reads the placeholder is a
    ///     <see cref="LoopedBehavior{T}" /> in <paramref name="f" />. The loop is
    ///     closed when <paramref name="close" /> runs, thus the closer can give it as
    ///     a <see cref="Behavior{T}" />.
    /// </remarks>
    [Pure]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public (Behavior<T> Behavior, TCaptures Captures) WithCaptures<TLoopedCaptures, TCaptures>(
        Func<LoopedBehavior<T>, (Behavior<T> Behavior, TLoopedCaptures Captures)> f,
        Func<TLoopedCaptures, LoopCloser, TCaptures> close) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) => WithCaptures(trans: trans, f: state.F, close: state.Close),
            state: (F: f, Close: close));

    private static (Behavior<T> Behavior, TCaptures Captures) WithCaptures<TLoopedCaptures, TCaptures>(
        TransactionInternal trans,
        Func<LoopedBehavior<T>, (Behavior<T> Behavior, TLoopedCaptures Captures)> f,
        Func<TLoopedCaptures, LoopCloser, TCaptures> close)
    {
        BehaviorLoop<T> loop = new();
        (Behavior<T> Behavior, TLoopedCaptures Captures) result = f(loop);
        LoopCloser closer = loop.Loop(trans: trans, b: result.Behavior);
        return (result.Behavior, close(arg1: result.Captures, arg2: closer));
    }

    /// <summary>
    ///     Loop a behavior and return the resulting behavior.
    /// </summary>
    /// <param name="f">
    ///     A function which takes the behavior loop and returns the
    ///     resulting behavior.
    /// </param>
    /// <returns>The resulting behavior.</returns>
    [Pure]
    public Behavior<T> WithoutCaptures(Func<LoopedBehavior<T>, Behavior<T>> f) =>
        this.WithCaptures(l => (Behavior: f(l), Captures: UnitInternal.Value)).Behavior;
}
