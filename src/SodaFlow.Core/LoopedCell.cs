using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A forward reference to a <see cref="Cell{T}" />. It is equal to the
///     <see cref="Cell{T}" /> that the loop supplies.
/// </summary>
/// <typeparam name="T">The type of values in the cell loop.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class LoopedCell<T> : Cell<T>
{
    private readonly LoopedStream<T> streamLoop;

    internal LoopedCell()
        : this(new LoopedStream<T>())
    {
    }

    private LoopedCell(LoopedStream<T> streamLoop)
        // ReSharper disable once NullableWarningSuppressionIsUsed - initialValue is assigned to valueProperty on
        // the base class. Only SampleNoTransaction() reads that value, and the base replaces it
        // with the lazy initial value of the loop before the return.
        : base(stream: streamLoop, initialValue: default!) =>
        this.streamLoop = streamLoop;

    // This is LoopedBehavior.Loop again. A cell is a behavior, but a looped cell cannot also be a
    // looped behavior, thus the two classes each keep the loop stream.
    internal void Loop(TransactionInternal trans, Cell<T> c)
    {
        this.streamLoop.Loop(trans: trans, stream: c.Updates());
        this.SetLazyInitialValue(c.SampleLazy(trans));
    }

    internal override T SampleNoTransaction() =>
        this.streamLoop.IsAssigned
            ? base.SampleNoTransaction()
            : throw new InvalidOperationException("LoopedCell was sampled before it was looped.");
}
