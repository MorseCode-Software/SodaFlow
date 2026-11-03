using System;
using JetBrains.Annotations;

namespace SodaFlow;

internal static class CellInternal
{
    internal static Cell<T> ConstantImpl<T>(T value) => new(StreamInternal.NeverImpl<T>().HoldInternal(value));

    internal static Cell<T> ConstantLazyImpl<T>(LazySample<T> value) =>
        TransactionInternal.Apply((trans, _) =>
            new Cell<T>(StreamInternal.NeverImpl<T>().HoldLazyInternal(trans: trans, initialValue: value)));

    internal static CellSink<T> CreateSinkImpl<T>(T initialValue) => new(initialValue);

    internal static CellSink<T> CreateSinkImpl<T>(T initialValue, Func<T, T, T> coalesce) =>
        new(initialValue: initialValue, coalesce: coalesce);

    internal static CellStreamSink<T> CreateStreamSinkImpl<T>() => new();

    internal static CellStreamSink<T> CreateStreamSinkImpl<T>(Func<T, T, T> coalesce) => new(coalesce);
}

/// <summary>
///     A value that changes with time in discrete steps.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <remarks>
///     A cell is a <see cref="LoopedCell{T}" /> that has a value now. Thus, a caller
///     can read the value, and a looped cell does not let a caller read it before its loop
///     closes.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class Cell<T> : LoopedCell<T>
{
    internal Cell(Behavior<T> behavior)
        : base(behavior)
    {
    }

    // The constructor takes a Behavior, thus the cast cannot fail. A field costs each cell
    // a reference for a read that only AsBehavior and the explicit loop make.
    internal Behavior<T> PlainBehaviorImpl => (Behavior<T>)this.BehaviorImpl;
}
