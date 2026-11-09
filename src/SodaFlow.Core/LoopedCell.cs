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

    // A cell loop as a block, for the assemblies that build on Core. The block gets its state as
    // an argument, thus a static block makes no closure. The class name gives the type of the
    // loop, thus the compiler can infer the other type arguments.
    internal static Cell<T> WithoutCapturesImpl<TState>(
        TransactionInternal trans,
        TState state,
        Func<LoopedCell<T>, TState, Cell<T>> f)
    {
        LoopedCell<T> loop = new();
        Cell<T> result = f(arg1: loop, arg2: state);
        loop.Loop(trans: trans, c: result);
        return result;
    }

    // A cell loop as a block with captures, as above.
    internal static (Cell<T> Cell, TCaptures Captures) WithCapturesImpl<TState, TCaptures>(
        TransactionInternal trans,
        TState state,
        Func<LoopedCell<T>, TState, (Cell<T> Cell, TCaptures Captures)> f)
    {
        LoopedCell<T> loop = new();
        (Cell<T> Cell, TCaptures Captures) result = f(arg1: loop, arg2: state);
        loop.Loop(trans: trans, c: result.Cell);
        return result;
    }

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
