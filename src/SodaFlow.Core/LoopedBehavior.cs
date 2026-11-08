using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A forward reference to a <see cref="Behavior{T}" />. It is equal to the
///     <see cref="Behavior{T}" /> that the loop supplies.
/// </summary>
/// <typeparam name="T">The type of values in the behavior loop.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class LoopedBehavior<T> : Behavior<T>
{
    private readonly LoopedStream<T> streamLoop;

    internal LoopedBehavior()
        : this(new LoopedStream<T>())
    {
    }

    private LoopedBehavior(LoopedStream<T> streamLoop)
        // ReSharper disable once NullableWarningSuppressionIsUsed - initialValue is assigned to valueProperty on
        // the base class. Only SampleNoTransaction() reads that value, and the base replaces it
        // with the lazy initial value of the loop before the return.
        : base(stream: streamLoop, initialValue: default!) =>
        this.streamLoop = streamLoop;

    internal void Loop(TransactionInternal trans, Behavior<T> b)
    {
        this.streamLoop.Loop(trans: trans, stream: b.Updates());
        this.SetLazyInitialValue(b.SampleLazy(trans));
    }

    internal override T SampleNoTransaction() =>
        this.streamLoop.IsAssigned
            ? base.SampleNoTransaction()
            : throw new InvalidOperationException("LoopedBehavior was sampled before it was looped.");
}
