using System;

namespace SodaFlow;

/// <summary>
///     The derivation of a lift. It is the gate through which each input sends,
///     and it gives each value to the lifted behavior. A subclass for each arity
///     holds the inputs and the function, and gives the step that puts the inputs
///     together and the initial value.
/// </summary>
/// <remarks>
///     The gate makes one firing for a transaction that updates more than one
///     input. Each input captures its new value as that value moves through the
///     graph. The step that puts the inputs together reads the captures, which is
///     why it gets the transaction. Before, a static method took the step, the
///     initial value, and a factory of the listeners as three delegates, and the
///     factory made an array. Then this object took the step as a closure, and the
///     hold took the initial value as a second closure. Now the subclass gives the
///     two as methods. Thus, a lift has no closure and no delegate of its own. The
///     lift also had an output stream and a hold of it. The lifted behavior now
///     makes its stream only when a caller asks for the updates. See
///     <see cref="IDerivation{T}" />.
/// </remarks>
/// <typeparam name="TResult">The type of the lifted value.</typeparam>
// ReSharper disable once InheritdocConsiderUsage - The type documents itself.
internal abstract class LiftOutput<TResult> : OnceGate, IDerivation<TResult>, IDemandOwner
{
    private DerivedCore<TResult> core;

    // The capture of the first input. It holds this gate, thus a stream of the updates that holds it
    // keeps the lift alive. See DerivedCore.CreateUpdates.
    private IListenerInternal? firstCapture;

    private protected LiftOutput(IKeepListenersAlive keepListenersAlive) =>
        this.core = new DerivedCore<TResult>(keepListenersAlive: keepListenersAlive, node: new DemandStreamNode<TResult>());

    internal override Node Node => this.core.Node;

    /// <inheritdoc />
    public IKeepListenersAlive KeepListenersAlive => this.core.KeepListenersAlive;

    /// <summary>
    ///     Links one input into the node of the lift through the gate. The gate
    ///     holds the capture.
    /// </summary>
    private protected void Capture<TInput>(
        Behavior<TInput> input,
        TransactionInternal trans,
        InputCapture<TInput> capture) =>
        this.Attach(StreamInternal.Capture(input: input.Updates(), gate: this, trans: trans, capture: capture));

    /// <summary>
    ///     Keeps the link of one input. The lift of a list links its inputs through
    ///     an action of its own, and not through a capture.
    /// </summary>
    private protected void Attach(IListenerInternal listener)
    {
        this.firstCapture ??= listener;

        // The registry stops the links when no behavior and no stream holds this lift. The weak view
        // of a capture is its target, thus the registry needs no handle.
        this.core.Node.Attach(stream: this, listener: listener.GetListenerWithWeakReference());
    }

    /// <summary>Makes the lifted behavior.</summary>
    internal Behavior<TResult> Hold(TransactionInternal trans) => new(trans: trans, derivation: this);

    /// <summary>Makes the lifted cell, as <see cref="Hold" /> makes the behavior.</summary>
    internal Cell<TResult> HoldCell(TransactionInternal trans) => new(trans: trans, derivation: this);

    /// <inheritdoc />
    public void Bind(Behavior<TResult> behavior) => this.core.Bind(behavior);

    /// <inheritdoc />
    TResult IDerivation<TResult>.Initial() => this.Initial();

    /// <inheritdoc />
    // A lift of no inputs has no link to keep.
    public Stream<TResult> CreateUpdates(TransactionInternal trans) =>
        this.core.CreateUpdates(trans: trans, retained: this.firstCapture ?? ListenerInternal.EmptyInternal);

    /// <inheritdoc />
    public bool IsUnlinked => this.core.IsUnlinked;

    /// <inheritdoc />
    long IDerivation<TResult>.SourceVersion => this.SourceVersion;

    /// <inheritdoc />
    bool IDerivation<TResult>.SourcesHavePending => this.SourcesHavePending;

    private protected abstract long SourceVersion { get; }

    private protected abstract bool SourcesHavePending { get; }

    /// <inheritdoc />
    public TResult Pull(bool pending)
    {
        if (pending && this.SourcesHavePending)
        {
            return this.PendingValue();
        }

        long version = this.SourceVersion;

        if (this.core.Cached(version).TryGetValue(out TResult cached))
        {
            return cached;
        }

        TResult value = this.Initial();
        this.core.SetCached(version: version, value: value);

        return value;
    }

    void IDemandOwner.Relinking(TransactionInternal trans) => this.core.Relinking();

    void IDemandOwner.Unlinked(TransactionInternal trans) => this.core.Unlinked(trans);

    private protected abstract TResult Recombine(TransactionInternal trans);

    private protected abstract TResult Initial();

    // The function over the value of each input at the end of this transaction.
    private protected abstract TResult PendingValue();

    private protected override void Run(TransactionInternal trans) =>
        this.core.Arrive(trans: trans, value: this.Recombine(trans));
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

    private protected override long SourceVersion =>
        Math.Max(val1: this.b1.Version, val2: this.b2.Version);

    private protected override bool SourcesHavePending =>
        this.b1.HasPendingUpdate || this.b2.HasPendingUpdate;

    private protected override TResult PendingValue() =>
        this.f(
            arg1: this.b1.PendingOrCurrent(),
            arg2: this.b2.PendingOrCurrent());
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

    private protected override long SourceVersion =>
        Math.Max(val1: Math.Max(val1: this.b1.Version, val2: this.b2.Version), val2: this.b3.Version);

    private protected override bool SourcesHavePending =>
        this.b1.HasPendingUpdate || this.b2.HasPendingUpdate || this.b3.HasPendingUpdate;

    private protected override TResult PendingValue() =>
        this.f(
            arg1: this.b1.PendingOrCurrent(),
            arg2: this.b2.PendingOrCurrent(),
            arg3: this.b3.PendingOrCurrent());
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

    private protected override long SourceVersion =>
        Math.Max(val1: Math.Max(val1: Math.Max(val1: this.b1.Version, val2: this.b2.Version), val2: this.b3.Version), val2: this.b4.Version);

    private protected override bool SourcesHavePending =>
        this.b1.HasPendingUpdate || this.b2.HasPendingUpdate || this.b3.HasPendingUpdate || this.b4.HasPendingUpdate;

    private protected override TResult PendingValue() =>
        this.f(
            arg1: this.b1.PendingOrCurrent(),
            arg2: this.b2.PendingOrCurrent(),
            arg3: this.b3.PendingOrCurrent(),
            arg4: this.b4.PendingOrCurrent());
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

    private protected override long SourceVersion =>
        Math.Max(val1: Math.Max(val1: Math.Max(val1: Math.Max(val1: this.b1.Version, val2: this.b2.Version), val2: this.b3.Version), val2: this.b4.Version), val2: this.b5.Version);

    private protected override bool SourcesHavePending =>
        this.b1.HasPendingUpdate || this.b2.HasPendingUpdate || this.b3.HasPendingUpdate || this.b4.HasPendingUpdate || this.b5.HasPendingUpdate;

    private protected override TResult PendingValue() =>
        this.f(
            arg1: this.b1.PendingOrCurrent(),
            arg2: this.b2.PendingOrCurrent(),
            arg3: this.b3.PendingOrCurrent(),
            arg4: this.b4.PendingOrCurrent(),
            arg5: this.b5.PendingOrCurrent());
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

    private protected override long SourceVersion =>
        Math.Max(val1: Math.Max(val1: Math.Max(val1: Math.Max(val1: Math.Max(val1: this.b1.Version, val2: this.b2.Version), val2: this.b3.Version), val2: this.b4.Version), val2: this.b5.Version), val2: this.b6.Version);

    private protected override bool SourcesHavePending =>
        this.b1.HasPendingUpdate || this.b2.HasPendingUpdate || this.b3.HasPendingUpdate || this.b4.HasPendingUpdate || this.b5.HasPendingUpdate || this.b6.HasPendingUpdate;

    private protected override TResult PendingValue() =>
        this.f(
            arg1: this.b1.PendingOrCurrent(),
            arg2: this.b2.PendingOrCurrent(),
            arg3: this.b3.PendingOrCurrent(),
            arg4: this.b4.PendingOrCurrent(),
            arg5: this.b5.PendingOrCurrent(),
            arg6: this.b6.PendingOrCurrent());
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

    private protected override long SourceVersion =>
        Math.Max(val1: Math.Max(val1: Math.Max(val1: Math.Max(val1: Math.Max(val1: Math.Max(val1: this.b1.Version, val2: this.b2.Version), val2: this.b3.Version), val2: this.b4.Version), val2: this.b5.Version), val2: this.b6.Version), val2: this.b7.Version);

    private protected override bool SourcesHavePending =>
        this.b1.HasPendingUpdate || this.b2.HasPendingUpdate || this.b3.HasPendingUpdate || this.b4.HasPendingUpdate || this.b5.HasPendingUpdate || this.b6.HasPendingUpdate || this.b7.HasPendingUpdate;

    private protected override TResult PendingValue() =>
        this.f(
            arg1: this.b1.PendingOrCurrent(),
            arg2: this.b2.PendingOrCurrent(),
            arg3: this.b3.PendingOrCurrent(),
            arg4: this.b4.PendingOrCurrent(),
            arg5: this.b5.PendingOrCurrent(),
            arg6: this.b6.PendingOrCurrent(),
            arg7: this.b7.PendingOrCurrent());
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

    private protected override long SourceVersion =>
        Math.Max(val1: Math.Max(val1: Math.Max(val1: Math.Max(val1: Math.Max(val1: Math.Max(val1: Math.Max(val1: this.b1.Version, val2: this.b2.Version), val2: this.b3.Version), val2: this.b4.Version), val2: this.b5.Version), val2: this.b6.Version), val2: this.b7.Version), val2: this.b8.Version);

    private protected override bool SourcesHavePending =>
        this.b1.HasPendingUpdate || this.b2.HasPendingUpdate || this.b3.HasPendingUpdate || this.b4.HasPendingUpdate || this.b5.HasPendingUpdate || this.b6.HasPendingUpdate || this.b7.HasPendingUpdate || this.b8.HasPendingUpdate;

    private protected override TResult PendingValue() =>
        this.f(
            arg1: this.b1.PendingOrCurrent(),
            arg2: this.b2.PendingOrCurrent(),
            arg3: this.b3.PendingOrCurrent(),
            arg4: this.b4.PendingOrCurrent(),
            arg5: this.b5.PendingOrCurrent(),
            arg6: this.b6.PendingOrCurrent(),
            arg7: this.b7.PendingOrCurrent(),
            arg8: this.b8.PendingOrCurrent());
}
