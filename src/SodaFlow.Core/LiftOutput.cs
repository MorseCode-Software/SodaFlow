using System;

namespace SodaFlow;

/// <summary>
///     The output of a lift while its inputs link into it. It holds the output
///     stream, and it is the gate through which each input sends. A subclass for
///     each arity holds the inputs and the function, and gives the step that puts
///     the inputs together and the initial value.
/// </summary>
/// <remarks>
///     The gate makes one firing for a transaction that updates more than one
///     input. Each input captures its new value as that value moves through the
///     graph. The step that puts the inputs together reads the captures, which is
///     why it gets the transaction. Before, a static method took the step, the
///     initial value, and a factory of the listeners as three delegates, and the
///     factory made an array. Then this object took the step as a closure, and the
///     hold took the initial value as a second closure. Now the subclass gives the
///     two as methods, and it is the source of the lazy sample that the hold
///     starts from. Thus, a lift has no closure and no delegate of its own.
/// </remarks>
/// <typeparam name="TResult">The type of the lifted value.</typeparam>
// ReSharper disable once InheritdocConsiderUsage - The type documents itself.
internal abstract class LiftOutput<TResult> : OnceGate, ISampleSource<TResult>
{
    private readonly Stream<TResult> @out;

    private protected LiftOutput(IKeepListenersAlive keepListenersAlive) =>
        this.@out = new Stream<TResult>(keepListenersAlive);

    internal override Node Node => this.@out.Node;

    /// <summary>Gives the initial value from the current values of the inputs.</summary>
    TResult ISampleSource<TResult>.Read() => this.Initial();

    /// <summary>Links one input into the output through the gate.</summary>
    private protected void Capture<TInput>(
        Behavior<TInput> input,
        TransactionInternal trans,
        InputCapture<TInput> capture) =>
        this.@out.UnsafeAttachListener(
            StreamInternal.Capture(input: input.Updates(), gate: this, trans: trans, capture: capture));

    /// <summary>Holds the output, with an initial value that the first sample computes.</summary>
    internal Behavior<TResult> Hold(TransactionInternal trans) =>
        this.@out.HoldLazyInternal(trans: trans, initialValue: new LazySample<TResult>(source: this));

    /// <summary>Holds the output in a cell, as <see cref="Hold" /> does in a behavior.</summary>
    internal Cell<TResult> HoldCell(TransactionInternal trans) =>
        this.@out.HoldLazyCellInternal(trans: trans, initialValue: new LazySample<TResult>(source: this));

    private protected abstract TResult Recombine(TransactionInternal trans);

    private protected abstract TResult Initial();

    private protected override void Run(TransactionInternal trans) =>
        this.@out.Send(trans: trans, a: this.Recombine(trans));
}

/// <inheritdoc />
internal sealed class LiftOutput<T1, T2, TResult>(
    in Behavior<T1> b1,
    in Behavior<T2> b2,
    in Func<T1, T2, TResult> f)
    : LiftOutput<TResult>(
        KeepListenersAliveInternal.Combine(first: b1.KeepListenersAlive, second: b2.KeepListenersAlive))
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Behavior<T2> b2 = b2;
    private readonly Func<T1, T2, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();

    internal static LiftOutput<TResult> Create(
        TransactionInternal trans,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Func<T1, T2, TResult> f)
    {
        LiftOutput<T1, T2, TResult> lift = new(b1: b1, b2: b2, f: f);

        lift.Capture(input: b1, trans: trans, capture: lift.p1);
        lift.Capture(input: b2, trans: trans, capture: lift.p2);

        return lift;
    }

    private protected override TResult Recombine(TransactionInternal trans) =>
        this.f(
            arg1: this.p1.Take(input: this.b1, trans: trans),
            arg2: this.p2.Take(input: this.b2, trans: trans));

    private protected override TResult Initial() =>
        this.f(
            arg1: this.b1.SampleNoTransaction(),
            arg2: this.b2.SampleNoTransaction());
}

/// <inheritdoc />
internal sealed class LiftOutput<T1, T2, T3, TResult>(
    in Behavior<T1> b1,
    in Behavior<T2> b2,
    in Behavior<T3> b3,
    in Func<T1, T2, T3, TResult> f)
    : LiftOutput<TResult>(
        KeepListenersAliveInternal.Combine(
            b1.KeepListenersAlive,
            b2.KeepListenersAlive,
            b3.KeepListenersAlive))
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Behavior<T2> b2 = b2;
    private readonly Behavior<T3> b3 = b3;
    private readonly Func<T1, T2, T3, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();

    internal static LiftOutput<TResult> Create(
        TransactionInternal trans,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Func<T1, T2, T3, TResult> f)
    {
        LiftOutput<T1, T2, T3, TResult> lift = new(b1: b1, b2: b2, b3: b3, f: f);

        lift.Capture(input: b1, trans: trans, capture: lift.p1);
        lift.Capture(input: b2, trans: trans, capture: lift.p2);
        lift.Capture(input: b3, trans: trans, capture: lift.p3);

        return lift;
    }

    private protected override TResult Recombine(TransactionInternal trans) =>
        this.f(
            arg1: this.p1.Take(input: this.b1, trans: trans),
            arg2: this.p2.Take(input: this.b2, trans: trans),
            arg3: this.p3.Take(input: this.b3, trans: trans));

    private protected override TResult Initial() =>
        this.f(
            arg1: this.b1.SampleNoTransaction(),
            arg2: this.b2.SampleNoTransaction(),
            arg3: this.b3.SampleNoTransaction());
}

/// <inheritdoc />
internal sealed class LiftOutput<T1, T2, T3, T4, TResult>(
    in Behavior<T1> b1,
    in Behavior<T2> b2,
    in Behavior<T3> b3,
    in Behavior<T4> b4,
    in Func<T1, T2, T3, T4, TResult> f)
    : LiftOutput<TResult>(
        KeepListenersAliveInternal.Combine(
            b1.KeepListenersAlive,
            b2.KeepListenersAlive,
            b3.KeepListenersAlive,
            b4.KeepListenersAlive))
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Behavior<T2> b2 = b2;
    private readonly Behavior<T3> b3 = b3;
    private readonly Behavior<T4> b4 = b4;
    private readonly Func<T1, T2, T3, T4, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();

    internal static LiftOutput<TResult> Create(
        TransactionInternal trans,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Func<T1, T2, T3, T4, TResult> f)
    {
        LiftOutput<T1, T2, T3, T4, TResult> lift = new(b1: b1, b2: b2, b3: b3, b4: b4, f: f);

        lift.Capture(input: b1, trans: trans, capture: lift.p1);
        lift.Capture(input: b2, trans: trans, capture: lift.p2);
        lift.Capture(input: b3, trans: trans, capture: lift.p3);
        lift.Capture(input: b4, trans: trans, capture: lift.p4);

        return lift;
    }

    private protected override TResult Recombine(TransactionInternal trans) =>
        this.f(
            arg1: this.p1.Take(input: this.b1, trans: trans),
            arg2: this.p2.Take(input: this.b2, trans: trans),
            arg3: this.p3.Take(input: this.b3, trans: trans),
            arg4: this.p4.Take(input: this.b4, trans: trans));

    private protected override TResult Initial() =>
        this.f(
            arg1: this.b1.SampleNoTransaction(),
            arg2: this.b2.SampleNoTransaction(),
            arg3: this.b3.SampleNoTransaction(),
            arg4: this.b4.SampleNoTransaction());
}

/// <inheritdoc />
internal sealed class LiftOutput<T1, T2, T3, T4, T5, TResult>(
    in Behavior<T1> b1,
    in Behavior<T2> b2,
    in Behavior<T3> b3,
    in Behavior<T4> b4,
    in Behavior<T5> b5,
    in Func<T1, T2, T3, T4, T5, TResult> f)
    : LiftOutput<TResult>(
        KeepListenersAliveInternal.Combine(
            b1.KeepListenersAlive,
            b2.KeepListenersAlive,
            b3.KeepListenersAlive,
            b4.KeepListenersAlive,
            b5.KeepListenersAlive))
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Behavior<T2> b2 = b2;
    private readonly Behavior<T3> b3 = b3;
    private readonly Behavior<T4> b4 = b4;
    private readonly Behavior<T5> b5 = b5;
    private readonly Func<T1, T2, T3, T4, T5, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();
    private readonly InputCapture<T5> p5 = new();

    internal static LiftOutput<TResult> Create(
        TransactionInternal trans,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Func<T1, T2, T3, T4, T5, TResult> f)
    {
        LiftOutput<T1, T2, T3, T4, T5, TResult> lift = new(b1: b1, b2: b2, b3: b3, b4: b4, b5: b5, f: f);

        lift.Capture(input: b1, trans: trans, capture: lift.p1);
        lift.Capture(input: b2, trans: trans, capture: lift.p2);
        lift.Capture(input: b3, trans: trans, capture: lift.p3);
        lift.Capture(input: b4, trans: trans, capture: lift.p4);
        lift.Capture(input: b5, trans: trans, capture: lift.p5);

        return lift;
    }

    private protected override TResult Recombine(TransactionInternal trans) =>
        this.f(
            arg1: this.p1.Take(input: this.b1, trans: trans),
            arg2: this.p2.Take(input: this.b2, trans: trans),
            arg3: this.p3.Take(input: this.b3, trans: trans),
            arg4: this.p4.Take(input: this.b4, trans: trans),
            arg5: this.p5.Take(input: this.b5, trans: trans));

    private protected override TResult Initial() =>
        this.f(
            arg1: this.b1.SampleNoTransaction(),
            arg2: this.b2.SampleNoTransaction(),
            arg3: this.b3.SampleNoTransaction(),
            arg4: this.b4.SampleNoTransaction(),
            arg5: this.b5.SampleNoTransaction());
}

/// <inheritdoc />
internal sealed class LiftOutput<T1, T2, T3, T4, T5, T6, TResult>(
    in Behavior<T1> b1,
    in Behavior<T2> b2,
    in Behavior<T3> b3,
    in Behavior<T4> b4,
    in Behavior<T5> b5,
    in Behavior<T6> b6,
    in Func<T1, T2, T3, T4, T5, T6, TResult> f)
    : LiftOutput<TResult>(
        KeepListenersAliveInternal.Combine(
            b1.KeepListenersAlive,
            b2.KeepListenersAlive,
            b3.KeepListenersAlive,
            b4.KeepListenersAlive,
            b5.KeepListenersAlive,
            b6.KeepListenersAlive))
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Behavior<T2> b2 = b2;
    private readonly Behavior<T3> b3 = b3;
    private readonly Behavior<T4> b4 = b4;
    private readonly Behavior<T5> b5 = b5;
    private readonly Behavior<T6> b6 = b6;
    private readonly Func<T1, T2, T3, T4, T5, T6, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();
    private readonly InputCapture<T5> p5 = new();
    private readonly InputCapture<T6> p6 = new();

    internal static LiftOutput<TResult> Create(
        TransactionInternal trans,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Func<T1, T2, T3, T4, T5, T6, TResult> f)
    {
        LiftOutput<T1, T2, T3, T4, T5, T6, TResult> lift = new(b1: b1, b2: b2, b3: b3, b4: b4, b5: b5, b6: b6, f: f);

        lift.Capture(input: b1, trans: trans, capture: lift.p1);
        lift.Capture(input: b2, trans: trans, capture: lift.p2);
        lift.Capture(input: b3, trans: trans, capture: lift.p3);
        lift.Capture(input: b4, trans: trans, capture: lift.p4);
        lift.Capture(input: b5, trans: trans, capture: lift.p5);
        lift.Capture(input: b6, trans: trans, capture: lift.p6);

        return lift;
    }

    private protected override TResult Recombine(TransactionInternal trans) =>
        this.f(
            arg1: this.p1.Take(input: this.b1, trans: trans),
            arg2: this.p2.Take(input: this.b2, trans: trans),
            arg3: this.p3.Take(input: this.b3, trans: trans),
            arg4: this.p4.Take(input: this.b4, trans: trans),
            arg5: this.p5.Take(input: this.b5, trans: trans),
            arg6: this.p6.Take(input: this.b6, trans: trans));

    private protected override TResult Initial() =>
        this.f(
            arg1: this.b1.SampleNoTransaction(),
            arg2: this.b2.SampleNoTransaction(),
            arg3: this.b3.SampleNoTransaction(),
            arg4: this.b4.SampleNoTransaction(),
            arg5: this.b5.SampleNoTransaction(),
            arg6: this.b6.SampleNoTransaction());
}

/// <inheritdoc />
internal sealed class LiftOutput<T1, T2, T3, T4, T5, T6, T7, TResult>(
    in Behavior<T1> b1,
    in Behavior<T2> b2,
    in Behavior<T3> b3,
    in Behavior<T4> b4,
    in Behavior<T5> b5,
    in Behavior<T6> b6,
    in Behavior<T7> b7,
    in Func<T1, T2, T3, T4, T5, T6, T7, TResult> f)
    : LiftOutput<TResult>(
        KeepListenersAliveInternal.Combine(
            b1.KeepListenersAlive,
            b2.KeepListenersAlive,
            b3.KeepListenersAlive,
            b4.KeepListenersAlive,
            b5.KeepListenersAlive,
            b6.KeepListenersAlive,
            b7.KeepListenersAlive))
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Behavior<T2> b2 = b2;
    private readonly Behavior<T3> b3 = b3;
    private readonly Behavior<T4> b4 = b4;
    private readonly Behavior<T5> b5 = b5;
    private readonly Behavior<T6> b6 = b6;
    private readonly Behavior<T7> b7 = b7;
    private readonly Func<T1, T2, T3, T4, T5, T6, T7, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();
    private readonly InputCapture<T5> p5 = new();
    private readonly InputCapture<T6> p6 = new();
    private readonly InputCapture<T7> p7 = new();

    internal static LiftOutput<TResult> Create(
        TransactionInternal trans,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Behavior<T7> b7,
        Func<T1, T2, T3, T4, T5, T6, T7, TResult> f)
    {
        LiftOutput<T1, T2, T3, T4, T5, T6, T7, TResult> lift =
            new(b1: b1, b2: b2, b3: b3, b4: b4, b5: b5, b6: b6, b7: b7, f: f);

        lift.Capture(input: b1, trans: trans, capture: lift.p1);
        lift.Capture(input: b2, trans: trans, capture: lift.p2);
        lift.Capture(input: b3, trans: trans, capture: lift.p3);
        lift.Capture(input: b4, trans: trans, capture: lift.p4);
        lift.Capture(input: b5, trans: trans, capture: lift.p5);
        lift.Capture(input: b6, trans: trans, capture: lift.p6);
        lift.Capture(input: b7, trans: trans, capture: lift.p7);

        return lift;
    }

    private protected override TResult Recombine(TransactionInternal trans) =>
        this.f(
            arg1: this.p1.Take(input: this.b1, trans: trans),
            arg2: this.p2.Take(input: this.b2, trans: trans),
            arg3: this.p3.Take(input: this.b3, trans: trans),
            arg4: this.p4.Take(input: this.b4, trans: trans),
            arg5: this.p5.Take(input: this.b5, trans: trans),
            arg6: this.p6.Take(input: this.b6, trans: trans),
            arg7: this.p7.Take(input: this.b7, trans: trans));

    private protected override TResult Initial() =>
        this.f(
            arg1: this.b1.SampleNoTransaction(),
            arg2: this.b2.SampleNoTransaction(),
            arg3: this.b3.SampleNoTransaction(),
            arg4: this.b4.SampleNoTransaction(),
            arg5: this.b5.SampleNoTransaction(),
            arg6: this.b6.SampleNoTransaction(),
            arg7: this.b7.SampleNoTransaction());
}

/// <inheritdoc />
internal sealed class LiftOutput<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(
    in Behavior<T1> b1,
    in Behavior<T2> b2,
    in Behavior<T3> b3,
    in Behavior<T4> b4,
    in Behavior<T5> b5,
    in Behavior<T6> b6,
    in Behavior<T7> b7,
    in Behavior<T8> b8,
    in Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult> f)
    : LiftOutput<TResult>(
        KeepListenersAliveInternal.Combine(
            b1.KeepListenersAlive,
            b2.KeepListenersAlive,
            b3.KeepListenersAlive,
            b4.KeepListenersAlive,
            b5.KeepListenersAlive,
            b6.KeepListenersAlive,
            b7.KeepListenersAlive,
            b8.KeepListenersAlive))
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Behavior<T2> b2 = b2;
    private readonly Behavior<T3> b3 = b3;
    private readonly Behavior<T4> b4 = b4;
    private readonly Behavior<T5> b5 = b5;
    private readonly Behavior<T6> b6 = b6;
    private readonly Behavior<T7> b7 = b7;
    private readonly Behavior<T8> b8 = b8;
    private readonly Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();
    private readonly InputCapture<T5> p5 = new();
    private readonly InputCapture<T6> p6 = new();
    private readonly InputCapture<T7> p7 = new();
    private readonly InputCapture<T8> p8 = new();

    internal static LiftOutput<TResult> Create(
        TransactionInternal trans,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Behavior<T7> b7,
        Behavior<T8> b8,
        Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult> f)
    {
        LiftOutput<T1, T2, T3, T4, T5, T6, T7, T8, TResult> lift =
            new(b1: b1, b2: b2, b3: b3, b4: b4, b5: b5, b6: b6, b7: b7, b8: b8, f: f);

        lift.Capture(input: b1, trans: trans, capture: lift.p1);
        lift.Capture(input: b2, trans: trans, capture: lift.p2);
        lift.Capture(input: b3, trans: trans, capture: lift.p3);
        lift.Capture(input: b4, trans: trans, capture: lift.p4);
        lift.Capture(input: b5, trans: trans, capture: lift.p5);
        lift.Capture(input: b6, trans: trans, capture: lift.p6);
        lift.Capture(input: b7, trans: trans, capture: lift.p7);
        lift.Capture(input: b8, trans: trans, capture: lift.p8);

        return lift;
    }

    private protected override TResult Recombine(TransactionInternal trans) =>
        this.f(
            arg1: this.p1.Take(input: this.b1, trans: trans),
            arg2: this.p2.Take(input: this.b2, trans: trans),
            arg3: this.p3.Take(input: this.b3, trans: trans),
            arg4: this.p4.Take(input: this.b4, trans: trans),
            arg5: this.p5.Take(input: this.b5, trans: trans),
            arg6: this.p6.Take(input: this.b6, trans: trans),
            arg7: this.p7.Take(input: this.b7, trans: trans),
            arg8: this.p8.Take(input: this.b8, trans: trans));

    private protected override TResult Initial() =>
        this.f(
            arg1: this.b1.SampleNoTransaction(),
            arg2: this.b2.SampleNoTransaction(),
            arg3: this.b3.SampleNoTransaction(),
            arg4: this.b4.SampleNoTransaction(),
            arg5: this.b5.SampleNoTransaction(),
            arg6: this.b6.SampleNoTransaction(),
            arg7: this.b7.SampleNoTransaction(),
            arg8: this.b8.SampleNoTransaction());
}
