using System;
using System.Collections.Generic;

namespace SodaFlow;

/// <summary>
///     The output of a snapshot of the latest values, and its gate. It keeps the
///     firings of the stream in one transaction. A subclass for each arity holds
///     the behaviors, their captures, and the function.
/// </summary>
/// <remarks>
///     <para>
///         A snapshot reads the behaviors when the stream fires. Thus, it gives the
///         values from before the transaction. A snapshot of the latest values
///         waits for the updates of the behaviors, with the mechanism of
///         <see cref="LiftOutput{TResult}" />. Each behavior captures its update and
///         calls the gate, and the stream keeps its firings and also calls the gate.
///         The gate queues its action at the output node, which ranks above each
///         input. Thus, the action runs after each behavior sends its update in the
///         transaction. A behavior commits its update only at the end of the
///         transaction, thus the captured update is the only source of the new
///         value.
///     </para>
///     <para>
///         A snapshot gives one output firing for each input firing, thus this
///         object keeps all of them. The behaviors also call the gate. Thus, a
///         transaction that updates only the behaviors clears the captured values,
///         and this object does not keep a reference to them. A transaction that
///         fails between a firing and the output leaves the firings here, and an
///         update of the behaviors alone then sent them. Thus, <see cref="Discard" />
///         clears the firings when the transaction fails. <see cref="Run" /> clears
///         them before it reads the behaviors or calls <c>f</c>. A read can call a
///         function of the caller, and an exception from <see cref="Run" /> does
///         not call <see cref="Discard" />. Before, this object kept the id of the
///         transaction of the firings, as <see cref="CaptureSlot{T}" /> does for
///         one value. The id did the work of the discard step, which a gate has
///         in all cases.
///     </para>
///     <para>
///         This cannot close a loop, as a snapshot can. If the output updates a
///         behavior in the same transaction, the behavior must have its new value
///         before it can calculate that value. No rank can be higher than the
///         output and the behavior.
///     </para>
///     <para>
///         Before, a function made the links of the captures into an array, and
///         two more functions read the captures and gave them to <c>f</c>. Each was
///         a closure and a delegate. The subclass gives the two steps as methods.
///     </para>
/// </remarks>
/// <typeparam name="T">The type of the values of the stream.</typeparam>
/// <typeparam name="TValues">The type of the values that the behaviors give.</typeparam>
/// <typeparam name="TResult">The type of the values of the output stream.</typeparam>
// ReSharper disable once InheritdocConsiderUsage - The type documents itself.
internal abstract class SnapshotLatestGate<T, TValues, TResult> : OnceGate
{
    private readonly List<T> pending = [];

    private protected SnapshotLatestGate(IKeepListenersAlive keepListenersAlive) =>
        this.Out = new Stream<TResult>(keepListenersAlive);

    internal override Node Node => this.Out.Node;

    private protected Stream<TResult> Out { get; }

    /// <summary>Links the stream whose firings this object keeps.</summary>
    private protected void Keep(Stream<T> stream, TransactionInternal trans) =>
        this.Out.UnsafeAttachListener(
            stream.Listen(target: this.Node, trans: trans, action: new Firing(this), suppressEarlierFirings: false));

    /// <summary>Links one behavior into the output through the gate.</summary>
    private protected void Capture<TInput>(
        Behavior<TInput> input,
        TransactionInternal trans,
        InputCapture<TInput> capture) =>
        this.Out.UnsafeAttachListener(
            StreamInternal.Capture(input: input.Updates(), gate: this, trans: trans, capture: capture));

    /// <summary>Gives the values of the behaviors in the transaction, and clears the captures.</summary>
    private protected abstract TValues Take(TransactionInternal trans);

    /// <summary>
    ///     Clears the captures, and does not read the behaviors that did not update.
    /// </summary>
    private protected abstract void Clear();

    private protected abstract TResult Combine(T a, TValues values);

    // The firings are clear before Take and f run. Thus, after an exception from either, the
    // next transaction does not send a firing of this transaction.
    private protected override void Run(TransactionInternal trans)
    {
        switch (this.pending.Count)
        {
            // Only a behavior updated. A read of a behavior that did not update has no use here.
            case 0:
                this.Clear();
                return;
            case 1:
            {
                T a = this.pending[0];
                this.pending.Clear();
                this.Out.Send(trans: trans, a: this.Combine(a: a, values: this.Take(trans)));
                return;
            }
            default:
            {
                T[] firings = [.. this.pending];
                this.pending.Clear();
                TValues values = this.Take(trans);

                foreach (T a in firings)
                {
                    this.Out.Send(trans: trans, a: this.Combine(a: a, values: values));
                }

                return;
            }
        }
    }

    private protected override void Discard() => this.pending.Clear();

    private void Fired(TransactionInternal trans, T value)
    {
        this.pending.Add(value);
        this.Arrived(trans);
    }

    // The action of the link from the stream.
    private sealed class Firing(in SnapshotLatestGate<T, TValues, TResult> gate) : StreamAction<T>
    {
        private readonly SnapshotLatestGate<T, TValues, TResult> gate = gate;

        public override void Fire(TransactionInternal trans, T value) => this.gate.Fired(trans: trans, value: value);
    }
}

/// <inheritdoc />
internal sealed class SnapshotLatestOutput<T, T1, TResult>(
    in Stream<T> stream,
    in Behavior<T1> b1,
    in Func<T, T1, TResult> f)
    : SnapshotLatestGate<T, T1, TResult>(stream.KeepListenersAlive)
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Func<T, T1, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();

    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T> stream,
        Behavior<T1> b1,
        Func<T, T1, TResult> f)
    {
        SnapshotLatestOutput<T, T1, TResult> snapshot = new(stream: stream, b1: b1, f: f);

        snapshot.Keep(stream: stream, trans: trans);
        snapshot.Capture(input: b1, trans: trans, capture: snapshot.p1);

        return snapshot.Out;
    }

    private protected override T1 Take(TransactionInternal trans) => this.p1.Take(input: this.b1, trans: trans);

    private protected override void Clear() => this.p1.Clear();

    private protected override TResult Combine(T a, T1 values) => this.f(arg1: a, arg2: values);
}

/// <inheritdoc />
internal sealed class SnapshotLatestOutput<T, T1, T2, TResult>(
    in Stream<T> stream,
    in Behavior<T1> b1,
    in Behavior<T2> b2,
    in Func<T, T1, T2, TResult> f)
    : SnapshotLatestGate<T, (T1, T2), TResult>(stream.KeepListenersAlive)
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Behavior<T2> b2 = b2;
    private readonly Func<T, T1, T2, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();

    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T> stream,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Func<T, T1, T2, TResult> f)
    {
        SnapshotLatestOutput<T, T1, T2, TResult> snapshot = new(stream: stream, b1: b1, b2: b2, f: f);

        snapshot.Keep(stream: stream, trans: trans);
        snapshot.Capture(input: b1, trans: trans, capture: snapshot.p1);
        snapshot.Capture(input: b2, trans: trans, capture: snapshot.p2);

        return snapshot.Out;
    }

    private protected override (T1, T2) Take(TransactionInternal trans) =>
    (
        this.p1.Take(input: this.b1, trans: trans),
        this.p2.Take(input: this.b2, trans: trans));

    private protected override void Clear()
    {
        this.p1.Clear();
        this.p2.Clear();
    }

    private protected override TResult Combine(T a, (T1, T2) values) =>
        this.f(
            arg1: a,
            arg2: values.Item1,
            arg3: values.Item2);
}

/// <inheritdoc />
internal sealed class SnapshotLatestOutput<T, T1, T2, T3, TResult>(
    in Stream<T> stream,
    in Behavior<T1> b1,
    in Behavior<T2> b2,
    in Behavior<T3> b3,
    in Func<T, T1, T2, T3, TResult> f)
    : SnapshotLatestGate<T, (T1, T2, T3), TResult>(stream.KeepListenersAlive)
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Behavior<T2> b2 = b2;
    private readonly Behavior<T3> b3 = b3;
    private readonly Func<T, T1, T2, T3, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();

    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T> stream,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Func<T, T1, T2, T3, TResult> f)
    {
        SnapshotLatestOutput<T, T1, T2, T3, TResult> snapshot = new(stream: stream, b1: b1, b2: b2, b3: b3, f: f);

        snapshot.Keep(stream: stream, trans: trans);
        snapshot.Capture(input: b1, trans: trans, capture: snapshot.p1);
        snapshot.Capture(input: b2, trans: trans, capture: snapshot.p2);
        snapshot.Capture(input: b3, trans: trans, capture: snapshot.p3);

        return snapshot.Out;
    }

    private protected override (T1, T2, T3) Take(TransactionInternal trans) =>
    (
        this.p1.Take(input: this.b1, trans: trans),
        this.p2.Take(input: this.b2, trans: trans),
        this.p3.Take(input: this.b3, trans: trans));

    private protected override void Clear()
    {
        this.p1.Clear();
        this.p2.Clear();
        this.p3.Clear();
    }

    private protected override TResult Combine(T a, (T1, T2, T3) values) =>
        this.f(
            arg1: a,
            arg2: values.Item1,
            arg3: values.Item2,
            arg4: values.Item3);
}

/// <inheritdoc />
internal sealed class SnapshotLatestOutput<T, T1, T2, T3, T4, TResult>(
    in Stream<T> stream,
    in Behavior<T1> b1,
    in Behavior<T2> b2,
    in Behavior<T3> b3,
    in Behavior<T4> b4,
    in Func<T, T1, T2, T3, T4, TResult> f)
    : SnapshotLatestGate<T, (T1, T2, T3, T4), TResult>(stream.KeepListenersAlive)
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Behavior<T2> b2 = b2;
    private readonly Behavior<T3> b3 = b3;
    private readonly Behavior<T4> b4 = b4;
    private readonly Func<T, T1, T2, T3, T4, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();

    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T> stream,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Func<T, T1, T2, T3, T4, TResult> f)
    {
        SnapshotLatestOutput<T, T1, T2, T3, T4, TResult> snapshot =
            new(stream: stream, b1: b1, b2: b2, b3: b3, b4: b4, f: f);

        snapshot.Keep(stream: stream, trans: trans);
        snapshot.Capture(input: b1, trans: trans, capture: snapshot.p1);
        snapshot.Capture(input: b2, trans: trans, capture: snapshot.p2);
        snapshot.Capture(input: b3, trans: trans, capture: snapshot.p3);
        snapshot.Capture(input: b4, trans: trans, capture: snapshot.p4);

        return snapshot.Out;
    }

    private protected override (T1, T2, T3, T4) Take(TransactionInternal trans) =>
    (
        this.p1.Take(input: this.b1, trans: trans),
        this.p2.Take(input: this.b2, trans: trans),
        this.p3.Take(input: this.b3, trans: trans),
        this.p4.Take(input: this.b4, trans: trans));

    private protected override void Clear()
    {
        this.p1.Clear();
        this.p2.Clear();
        this.p3.Clear();
        this.p4.Clear();
    }

    private protected override TResult Combine(T a, (T1, T2, T3, T4) values) =>
        this.f(
            arg1: a,
            arg2: values.Item1,
            arg3: values.Item2,
            arg4: values.Item3,
            arg5: values.Item4);
}

/// <inheritdoc />
internal sealed class SnapshotLatestOutput<T, T1, T2, T3, T4, T5, TResult>(
    in Stream<T> stream,
    in Behavior<T1> b1,
    in Behavior<T2> b2,
    in Behavior<T3> b3,
    in Behavior<T4> b4,
    in Behavior<T5> b5,
    in Func<T, T1, T2, T3, T4, T5, TResult> f)
    : SnapshotLatestGate<T, (T1, T2, T3, T4, T5), TResult>(stream.KeepListenersAlive)
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Behavior<T2> b2 = b2;
    private readonly Behavior<T3> b3 = b3;
    private readonly Behavior<T4> b4 = b4;
    private readonly Behavior<T5> b5 = b5;
    private readonly Func<T, T1, T2, T3, T4, T5, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();
    private readonly InputCapture<T5> p5 = new();

    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T> stream,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Func<T, T1, T2, T3, T4, T5, TResult> f)
    {
        SnapshotLatestOutput<T, T1, T2, T3, T4, T5, TResult> snapshot =
            new(stream: stream, b1: b1, b2: b2, b3: b3, b4: b4, b5: b5, f: f);

        snapshot.Keep(stream: stream, trans: trans);
        snapshot.Capture(input: b1, trans: trans, capture: snapshot.p1);
        snapshot.Capture(input: b2, trans: trans, capture: snapshot.p2);
        snapshot.Capture(input: b3, trans: trans, capture: snapshot.p3);
        snapshot.Capture(input: b4, trans: trans, capture: snapshot.p4);
        snapshot.Capture(input: b5, trans: trans, capture: snapshot.p5);

        return snapshot.Out;
    }

    private protected override (T1, T2, T3, T4, T5) Take(TransactionInternal trans) =>
    (
        this.p1.Take(input: this.b1, trans: trans),
        this.p2.Take(input: this.b2, trans: trans),
        this.p3.Take(input: this.b3, trans: trans),
        this.p4.Take(input: this.b4, trans: trans),
        this.p5.Take(input: this.b5, trans: trans));

    private protected override void Clear()
    {
        this.p1.Clear();
        this.p2.Clear();
        this.p3.Clear();
        this.p4.Clear();
        this.p5.Clear();
    }

    private protected override TResult Combine(T a, (T1, T2, T3, T4, T5) values) =>
        this.f(
            arg1: a,
            arg2: values.Item1,
            arg3: values.Item2,
            arg4: values.Item3,
            arg5: values.Item4,
            arg6: values.Item5);
}

/// <inheritdoc />
internal sealed class SnapshotLatestOutput<T, T1, T2, T3, T4, T5, T6, TResult>(
    in Stream<T> stream,
    in Behavior<T1> b1,
    in Behavior<T2> b2,
    in Behavior<T3> b3,
    in Behavior<T4> b4,
    in Behavior<T5> b5,
    in Behavior<T6> b6,
    in Func<T, T1, T2, T3, T4, T5, T6, TResult> f)
    : SnapshotLatestGate<T, (T1, T2, T3, T4, T5, T6), TResult>(stream.KeepListenersAlive)
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Behavior<T2> b2 = b2;
    private readonly Behavior<T3> b3 = b3;
    private readonly Behavior<T4> b4 = b4;
    private readonly Behavior<T5> b5 = b5;
    private readonly Behavior<T6> b6 = b6;
    private readonly Func<T, T1, T2, T3, T4, T5, T6, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();
    private readonly InputCapture<T5> p5 = new();
    private readonly InputCapture<T6> p6 = new();

    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T> stream,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Func<T, T1, T2, T3, T4, T5, T6, TResult> f)
    {
        SnapshotLatestOutput<T, T1, T2, T3, T4, T5, T6, TResult> snapshot =
            new(stream: stream, b1: b1, b2: b2, b3: b3, b4: b4, b5: b5, b6: b6, f: f);

        snapshot.Keep(stream: stream, trans: trans);
        snapshot.Capture(input: b1, trans: trans, capture: snapshot.p1);
        snapshot.Capture(input: b2, trans: trans, capture: snapshot.p2);
        snapshot.Capture(input: b3, trans: trans, capture: snapshot.p3);
        snapshot.Capture(input: b4, trans: trans, capture: snapshot.p4);
        snapshot.Capture(input: b5, trans: trans, capture: snapshot.p5);
        snapshot.Capture(input: b6, trans: trans, capture: snapshot.p6);

        return snapshot.Out;
    }

    private protected override (T1, T2, T3, T4, T5, T6) Take(TransactionInternal trans) =>
    (
        this.p1.Take(input: this.b1, trans: trans),
        this.p2.Take(input: this.b2, trans: trans),
        this.p3.Take(input: this.b3, trans: trans),
        this.p4.Take(input: this.b4, trans: trans),
        this.p5.Take(input: this.b5, trans: trans),
        this.p6.Take(input: this.b6, trans: trans));

    private protected override void Clear()
    {
        this.p1.Clear();
        this.p2.Clear();
        this.p3.Clear();
        this.p4.Clear();
        this.p5.Clear();
        this.p6.Clear();
    }

    private protected override TResult Combine(T a, (T1, T2, T3, T4, T5, T6) values) =>
        this.f(
            arg1: a,
            arg2: values.Item1,
            arg3: values.Item2,
            arg4: values.Item3,
            arg5: values.Item4,
            arg6: values.Item5,
            arg7: values.Item6);
}

/// <inheritdoc />
internal sealed class SnapshotLatestOutput<T, T1, T2, T3, T4, T5, T6, T7, TResult>(
    in Stream<T> stream,
    in Behavior<T1> b1,
    in Behavior<T2> b2,
    in Behavior<T3> b3,
    in Behavior<T4> b4,
    in Behavior<T5> b5,
    in Behavior<T6> b6,
    in Behavior<T7> b7,
    in Func<T, T1, T2, T3, T4, T5, T6, T7, TResult> f)
    : SnapshotLatestGate<T, (T1, T2, T3, T4, T5, T6, T7), TResult>(stream.KeepListenersAlive)
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Behavior<T2> b2 = b2;
    private readonly Behavior<T3> b3 = b3;
    private readonly Behavior<T4> b4 = b4;
    private readonly Behavior<T5> b5 = b5;
    private readonly Behavior<T6> b6 = b6;
    private readonly Behavior<T7> b7 = b7;
    private readonly Func<T, T1, T2, T3, T4, T5, T6, T7, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();
    private readonly InputCapture<T5> p5 = new();
    private readonly InputCapture<T6> p6 = new();
    private readonly InputCapture<T7> p7 = new();

    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T> stream,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Behavior<T7> b7,
        Func<T, T1, T2, T3, T4, T5, T6, T7, TResult> f)
    {
        SnapshotLatestOutput<T, T1, T2, T3, T4, T5, T6, T7, TResult> snapshot =
            new(stream: stream, b1: b1, b2: b2, b3: b3, b4: b4, b5: b5, b6: b6, b7: b7, f: f);

        snapshot.Keep(stream: stream, trans: trans);
        snapshot.Capture(input: b1, trans: trans, capture: snapshot.p1);
        snapshot.Capture(input: b2, trans: trans, capture: snapshot.p2);
        snapshot.Capture(input: b3, trans: trans, capture: snapshot.p3);
        snapshot.Capture(input: b4, trans: trans, capture: snapshot.p4);
        snapshot.Capture(input: b5, trans: trans, capture: snapshot.p5);
        snapshot.Capture(input: b6, trans: trans, capture: snapshot.p6);
        snapshot.Capture(input: b7, trans: trans, capture: snapshot.p7);

        return snapshot.Out;
    }

    private protected override (T1, T2, T3, T4, T5, T6, T7) Take(TransactionInternal trans) =>
    (
        this.p1.Take(input: this.b1, trans: trans),
        this.p2.Take(input: this.b2, trans: trans),
        this.p3.Take(input: this.b3, trans: trans),
        this.p4.Take(input: this.b4, trans: trans),
        this.p5.Take(input: this.b5, trans: trans),
        this.p6.Take(input: this.b6, trans: trans),
        this.p7.Take(input: this.b7, trans: trans));

    private protected override void Clear()
    {
        this.p1.Clear();
        this.p2.Clear();
        this.p3.Clear();
        this.p4.Clear();
        this.p5.Clear();
        this.p6.Clear();
        this.p7.Clear();
    }

    private protected override TResult Combine(T a, (T1, T2, T3, T4, T5, T6, T7) values) =>
        this.f(
            arg1: a,
            arg2: values.Item1,
            arg3: values.Item2,
            arg4: values.Item3,
            arg5: values.Item4,
            arg6: values.Item5,
            arg7: values.Item6,
            arg8: values.Item7);
}

/// <inheritdoc />
internal sealed class SnapshotLatestOutput<T, T1, T2, T3, T4, T5, T6, T7, T8, TResult>(
    in Stream<T> stream,
    in Behavior<T1> b1,
    in Behavior<T2> b2,
    in Behavior<T3> b3,
    in Behavior<T4> b4,
    in Behavior<T5> b5,
    in Behavior<T6> b6,
    in Behavior<T7> b7,
    in Behavior<T8> b8,
    in Func<T, T1, T2, T3, T4, T5, T6, T7, T8, TResult> f)
    : SnapshotLatestGate<T, (T1, T2, T3, T4, T5, T6, T7, T8), TResult>(stream.KeepListenersAlive)
{
    private readonly Behavior<T1> b1 = b1;
    private readonly Behavior<T2> b2 = b2;
    private readonly Behavior<T3> b3 = b3;
    private readonly Behavior<T4> b4 = b4;
    private readonly Behavior<T5> b5 = b5;
    private readonly Behavior<T6> b6 = b6;
    private readonly Behavior<T7> b7 = b7;
    private readonly Behavior<T8> b8 = b8;
    private readonly Func<T, T1, T2, T3, T4, T5, T6, T7, T8, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();
    private readonly InputCapture<T5> p5 = new();
    private readonly InputCapture<T6> p6 = new();
    private readonly InputCapture<T7> p7 = new();
    private readonly InputCapture<T8> p8 = new();

    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T> stream,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Behavior<T7> b7,
        Behavior<T8> b8,
        Func<T, T1, T2, T3, T4, T5, T6, T7, T8, TResult> f)
    {
        SnapshotLatestOutput<T, T1, T2, T3, T4, T5, T6, T7, T8, TResult> snapshot =
            new(stream: stream, b1: b1, b2: b2, b3: b3, b4: b4, b5: b5, b6: b6, b7: b7, b8: b8, f: f);

        snapshot.Keep(stream: stream, trans: trans);
        snapshot.Capture(input: b1, trans: trans, capture: snapshot.p1);
        snapshot.Capture(input: b2, trans: trans, capture: snapshot.p2);
        snapshot.Capture(input: b3, trans: trans, capture: snapshot.p3);
        snapshot.Capture(input: b4, trans: trans, capture: snapshot.p4);
        snapshot.Capture(input: b5, trans: trans, capture: snapshot.p5);
        snapshot.Capture(input: b6, trans: trans, capture: snapshot.p6);
        snapshot.Capture(input: b7, trans: trans, capture: snapshot.p7);
        snapshot.Capture(input: b8, trans: trans, capture: snapshot.p8);

        return snapshot.Out;
    }

    private protected override (T1, T2, T3, T4, T5, T6, T7, T8) Take(TransactionInternal trans) =>
    (
        this.p1.Take(input: this.b1, trans: trans),
        this.p2.Take(input: this.b2, trans: trans),
        this.p3.Take(input: this.b3, trans: trans),
        this.p4.Take(input: this.b4, trans: trans),
        this.p5.Take(input: this.b5, trans: trans),
        this.p6.Take(input: this.b6, trans: trans),
        this.p7.Take(input: this.b7, trans: trans),
        this.p8.Take(input: this.b8, trans: trans));

    private protected override void Clear()
    {
        this.p1.Clear();
        this.p2.Clear();
        this.p3.Clear();
        this.p4.Clear();
        this.p5.Clear();
        this.p6.Clear();
        this.p7.Clear();
        this.p8.Clear();
    }

    private protected override TResult Combine(T a, (T1, T2, T3, T4, T5, T6, T7, T8) values) =>
        this.f(
            arg1: a,
            arg2: values.Item1,
            arg3: values.Item2,
            arg4: values.Item3,
            arg5: values.Item4,
            arg6: values.Item5,
            arg7: values.Item6,
            arg8: values.Item7,
            arg9: values.Item8);
}
