using System;
using JetBrains.Annotations;

namespace SodaFlow;

internal static class BehaviorInternal
{
    internal static Behavior<T> ConstantImpl<T>(T value) => new(value);

    internal static Behavior<T> ConstantLazyImpl<T>(LazySample<T> value) =>
        TransactionInternal.Apply((trans, _) =>
            StreamInternal.NeverImpl<T>().HoldLazyInternal(trans: trans, initialValue: value));

    internal static BehaviorSink<T> CreateSinkImpl<T>(T initialValue) => new(initialValue);

    internal static BehaviorSink<T> CreateSinkImpl<T>(T initialValue, Func<T, T, T> coalesce) =>
        new(initialValue: initialValue, coalesce: coalesce);
}

/// <summary>
///     A value that changes with time.
/// </summary>
/// <typeparam name="T">The type of values in the behavior.</typeparam>
/// <remarks>
///     A behavior is a <see cref="LoopedBehavior{T}" /> that has a value now. Thus, a
///     caller can read the value, and a looped behavior does not let a caller read it before its
///     loop closes.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class Behavior<T> : LoopedBehavior<T>
{
    internal Behavior(T value)
        : base(value)
    {
    }

    internal Behavior(Stream<T> stream, T initialValue)
        : base(stream: stream, initialValue: initialValue)
    {
    }
}
