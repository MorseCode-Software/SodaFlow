using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A value that changes with time, which can be in a loop that is not closed. It
///     has each operation of a <see cref="Behavior{T}" /> except a read of the
///     current value.
/// </summary>
/// <typeparam name="T">The type of values in the behavior.</typeparam>
/// <remarks>
///     <para>
///         A loop block gives one of these as the placeholder. An
///         operation that starts its result from the current value of a looped
///         behavior also gives one, because that value is not available before the
///         loop closes. Thus, the type says which values a caller cannot read now.
///     </para>
///     <para>
///         A <see cref="Behavior{T}" /> is a looped behavior that has a value now,
///         and it adds the read. The <see cref="LoopCloser" /> that a loop gives
///         when it closes gives the behavior back, and <c>AsBehavior</c> asserts it
///         before the close.
///     </para>
/// </remarks>
[PublicAPI]
public class LoopedBehavior<T>
{
    // These capture only this behavior, thus SodaFlow builds them one time and not on each
    // firing. Only the constructor that takes a stream needs them, because a constant behavior
    // does not update.
    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable - This is done for performance reasons.
    private readonly Action? applyValueUpdate;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable - This is done for performance reasons.
    private readonly Action? discardValueUpdate;
    private readonly Stream<T> stream;

    // ReSharper disable once NotAccessedField.Local - Used to keep object from being garbage collected
    private readonly IListener? streamListener;

    private T valueProperty;
    private MaybeInternal<T> valueUpdate;

    internal LoopedBehavior(T value)
    {
        this.stream = new Stream<T>();
        this.valueProperty = value;
    }

    internal LoopedBehavior(Stream<T> stream, T initialValue)
    {
        this.stream = stream;
        this.valueProperty = initialValue;
        this.UsingInitialValue = true;

        // SodaFlow gives this a value before Listen, because Listen can send the firings that
        // this transaction made in this transaction. Thus, the handler below can run before the constructor
        // returns.
        this.applyValueUpdate = this.ApplyValueUpdate;
        this.discardValueUpdate = this.DiscardValueUpdate;

        this.streamListener =
            TransactionInternal.Apply((trans1, _) =>
                this.stream.Listen(
                    target: Node<T>.Null,
                    trans: trans1,
                    action: (trans2, a) =>
                    {
                        // This does not use MatchNone or MatchSome, because they take
                        // callbacks. This code runs on each firing of each cell, thus the
                        // necessary closures were a large part of the cost of one Send.
                        if (!this.valueUpdate.HasValue())
                        {
                            trans2.Last(commit: this.applyValueUpdate, discard: this.discardValueUpdate);
                        }

                        this.valueUpdate = MaybeInternal.Some(a);
                    },
                    suppressEarlierFirings: false));
    }

    internal IKeepListenersAlive KeepListenersAlive => this.stream.KeepListenersAlive;

    /// <summary>
    ///     Gets or sets the value this behavior reports when sampled not in a
    ///     transaction.
    /// </summary>
    /// <value>The current value of the behavior.</value>
    /// <remarks>
    ///     A set of this property clears <see cref="UsingInitialValue" />, because a
    ///     behavior with a new value does not use the value from its construction. A
    ///     derived type must write through this property and not through the backing
    ///     field, to keep that flag correct.
    /// </remarks>
    protected T ValueProperty
    {
        get => this.valueProperty;
        set
        {
            this.valueProperty = value;
            this.UsingInitialValue = false;
        }
    }

    /// <summary>
    ///     Tells you if this behavior gives the value from its construction, and not a
    ///     value that it received later.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> until <see cref="ValueProperty" /> is assigned, and
    ///     <see langword="false" /> afterward.
    /// </value>
    protected bool UsingInitialValue { get; private set; }

    private void ApplyValueUpdate()
    {
        if (this.valueUpdate.TryGetValue(out T v))
        {
            this.ValueProperty = v;
        }

        this.valueUpdate = MaybeInternal<T>.None;
    }

    // A transaction that fails does not apply its update. The update must also go, because the
    // handler queues the apply action only when it has no update.
    private void DiscardValueUpdate() => this.valueUpdate = MaybeInternal<T>.None;

    /// <summary>
    ///     Makes this behavior use its initial value again. A transaction that set
    ///     the value from a lazy sample failed, thus the value is not correct.
    /// </summary>
    protected void UseInitialValueAgain() => this.UsingInitialValue = true;

    internal T SampleImpl() => TransactionInternal.Apply((_, _) => this.SampleNoTransaction());

    internal LazySample<T> SampleLazyImpl() => TransactionInternal.Apply((trans, _) => this.SampleLazy(trans));

    internal LazySample<T> SampleLazy(TransactionInternal trans)
    {
        LazySample<T> s = new(this);

        // The sample phase runs after each send and each prioritized action. Thus, the pending
        // update, when this behavior got one, is the value at the end of the transaction. A
        // transaction that fails discards that update, and the capture goes with it.
        trans.Sample(() =>
            s.Capture(
                trans: trans,
                captured: this.valueUpdate.Match(onSome: static v => v, onNone: this.SampleNoTransaction)));

        return s;
    }

    internal virtual T SampleNoTransaction() => this.ValueProperty;

    internal Stream<T> Updates() => this.stream;

    /// <summary>
    ///     The stream of the value of this behavior. It gives the current value in
    ///     this transaction, and then each update.
    /// </summary>
    /// <remarks>
    ///     The two sources send into one output stream. They do not use a spark
    ///     stream, a snapshot of it, and a merge, which is four streams where two are
    ///     sufficient. Value is below Cell.ListenStrong, Apply, and the switch
    ///     operations, thus it was a large part of the cost of each of them.
    ///     SodaFlow queues the initial send against a new node of its own, as the
    ///     spark stream that it replaces did, and for the same cause. A new node ranks
    ///     below all other nodes, thus SodaFlow supplies the value also when a caller
    ///     calls Value during a drain. SwitchB does this, because its handler builds a
    ///     Value for the newly selected behavior in the middle of a transaction. An
    ///     initial send on the output node makes the switch supply a previous value.
    ///     The node costs nothing, but the two intermediate streams were expensive.
    ///     A coalesce operation in which the right value wins keeps an update from
    ///     this transaction in front of the initial value. A merge with (left, right)
    ///     =&gt; right gave the same result.
    /// </remarks>
    internal Stream<T> Value(TransactionInternal trans1)
    {
        Stream<T> @out = new(this.stream.KeepListenersAlive);

        // This always runs first, because its Rank is 0.
        trans1.Prioritized(
            node: new Node<UnitInternal>(),
            action: trans2 => @out.Send(trans: trans2, a: this.SampleNoTransaction()));

        // This listener queues an action that always runs after the previous action. The Rank
        // of this.Updates() can be 0, but the Rank of its listener is more than 0.
        IListener l =
            this.Updates()
                .Listen(
                    target: @out.Node,
                    trans: trans1,
                    action: @out.Send,
                    suppressEarlierFirings: false);

        // The sequence is correct, and a link between the first node and the second node is
        // not necessary.
        return @out.UnsafeAttachListener(l).Coalesce(trans1: trans1, f: static (_, right) => right);
    }

    /// <summary>
    ///     Gives this behavior as a <see cref="Behavior{T}" />, and does not check that
    ///     its loops are closed.
    /// </summary>
    /// <remarks>
    ///     Each behavior that an operation makes is a <see cref="Behavior{T}" />, thus
    ///     this usually gives the same object. Only a placeholder is not one. For it,
    ///     this holds its updates from the value that the loop gives it, which is the
    ///     same value with one more node.
    /// </remarks>
    internal Behavior<T> AsBehaviorImpl() =>
        this as Behavior<T>
        ?? TransactionInternal.Apply((trans, _) =>
            this.Updates().HoldLazyInternal(trans: trans, initialValue: this.SampleLazy(trans)));

    internal Behavior<TResult> MapImpl<TResult>(Func<T, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
            this.Updates()
                .MapImpl(f)
                .HoldLazyInternal(trans: trans, initialValue: this.SampleLazy(trans).MapImpl(f)));

    // Lift no longer uses Apply. One call to ApplyImpl for each additional input made each
    // input pay for a Value(), which is a spark stream, a snapshot, a merge, and a coalesce
    // operation. Thus, a six-way lift built approximately fifty streams and cost approximately
    // 87KB. This method builds one stream for all arities: the output. Each input sends into it
    // through an OnceGate, which queues one action for the transaction, and that action puts the
    // inputs together again. Before the gate, there was a pulse stream and a coalesce operation
    // before the output, thus three streams. The IEnumerable overload in
    // BehaviorExtensionMethods already used this method.
    //
    // SodaFlow captures the new value of each input as that value moves through the graph,
    // and does not read the value from the behavior after that. A behavior applies its update
    // through a listener on Node<T>.Null. The priority queue drains the entries with a null
    // rank only after all the ranked entries. Thus, the behaviors hold their previous
    // values when the action of the gate runs. The sequence of the ranks makes the capture
    // safe, because each input links into the output node. Thus, the output node ranks above
    // all of them, and SodaFlow fills each slot before the action of the gate can run.

    internal Behavior<TResult> LiftImpl<T2, TResult>(LoopedBehavior<T2> b2, Func<T, T2, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            CaptureSlot<T> p1 = default;
            CaptureSlot<T2> p2 = default;

            return HoldLifted(
                keepListenersAlive: this.stream.KeepListenersAlive,
                trans: trans,
                recombine: trans2 =>
                {
                    TResult result =
                        f(
                            arg1: Take(pending: ref p1, input: this, trans: trans2),
                            arg2: Take(pending: ref p2, input: b2, trans: trans2));

                    return result;
                },
                initialValue: () => f(arg1: this.SampleNoTransaction(), arg2: b2.SampleNoTransaction()),
                listeners: gate =>
                [
                    StreamInternal.Capture(
                        input: this.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p1.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b2.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p2.Set(trans: t, value: v))
                ]);
        });

    internal Behavior<TResult> LiftImpl<T2, T3, TResult>(
        LoopedBehavior<T2> b2,
        LoopedBehavior<T3> b3,
        Func<T, T2, T3, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            CaptureSlot<T> p1 = default;
            CaptureSlot<T2> p2 = default;
            CaptureSlot<T3> p3 = default;

            return HoldLifted(
                keepListenersAlive: this.stream.KeepListenersAlive,
                trans: trans,
                recombine: trans2 =>
                    f(
                        arg1: Take(pending: ref p1, input: this, trans: trans2),
                        arg2: Take(pending: ref p2, input: b2, trans: trans2),
                        arg3: Take(pending: ref p3, input: b3, trans: trans2)),
                initialValue: () =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction()),
                listeners: gate =>
                [
                    StreamInternal.Capture(
                        input: this.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p1.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b2.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p2.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b3.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p3.Set(trans: t, value: v))
                ]);
        });

    internal Behavior<TResult> LiftImpl<T2, T3, T4, TResult>(
        LoopedBehavior<T2> b2,
        LoopedBehavior<T3> b3,
        LoopedBehavior<T4> b4,
        Func<T, T2, T3, T4, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            CaptureSlot<T> p1 = default;
            CaptureSlot<T2> p2 = default;
            CaptureSlot<T3> p3 = default;
            CaptureSlot<T4> p4 = default;

            return HoldLifted(
                keepListenersAlive: this.stream.KeepListenersAlive,
                trans: trans,
                recombine: trans2 =>
                    f(
                        arg1: Take(pending: ref p1, input: this, trans: trans2),
                        arg2: Take(pending: ref p2, input: b2, trans: trans2),
                        arg3: Take(pending: ref p3, input: b3, trans: trans2),
                        arg4: Take(pending: ref p4, input: b4, trans: trans2)),
                initialValue: () =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction(),
                        arg4: b4.SampleNoTransaction()),
                listeners: gate =>
                [
                    StreamInternal.Capture(
                        input: this.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p1.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b2.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p2.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b3.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p3.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b4.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p4.Set(trans: t, value: v))
                ]);
        });

    internal Behavior<TResult> LiftImpl<T2, T3, T4, T5, TResult>(
        LoopedBehavior<T2> b2,
        LoopedBehavior<T3> b3,
        LoopedBehavior<T4> b4,
        LoopedBehavior<T5> b5,
        Func<T, T2, T3, T4, T5, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            CaptureSlot<T> p1 = default;
            CaptureSlot<T2> p2 = default;
            CaptureSlot<T3> p3 = default;
            CaptureSlot<T4> p4 = default;
            CaptureSlot<T5> p5 = default;

            return HoldLifted(
                keepListenersAlive: this.stream.KeepListenersAlive,
                trans: trans,
                recombine: trans2 =>
                    f(
                        arg1: Take(pending: ref p1, input: this, trans: trans2),
                        arg2: Take(pending: ref p2, input: b2, trans: trans2),
                        arg3: Take(pending: ref p3, input: b3, trans: trans2),
                        arg4: Take(pending: ref p4, input: b4, trans: trans2),
                        arg5: Take(pending: ref p5, input: b5, trans: trans2)),
                initialValue: () =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction(),
                        arg4: b4.SampleNoTransaction(),
                        arg5: b5.SampleNoTransaction()),
                listeners: gate =>
                [
                    StreamInternal.Capture(
                        input: this.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p1.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b2.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p2.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b3.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p3.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b4.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p4.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b5.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p5.Set(trans: t, value: v))
                ]);
        });

    internal Behavior<TResult> LiftImpl<T2, T3, T4, T5, T6, TResult>(
        LoopedBehavior<T2> b2,
        LoopedBehavior<T3> b3,
        LoopedBehavior<T4> b4,
        LoopedBehavior<T5> b5,
        LoopedBehavior<T6> b6,
        Func<T, T2, T3, T4, T5, T6, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            CaptureSlot<T> p1 = default;
            CaptureSlot<T2> p2 = default;
            CaptureSlot<T3> p3 = default;
            CaptureSlot<T4> p4 = default;
            CaptureSlot<T5> p5 = default;
            CaptureSlot<T6> p6 = default;

            return HoldLifted(
                keepListenersAlive: this.stream.KeepListenersAlive,
                trans: trans,
                recombine: trans2 =>
                    f(
                        arg1: Take(pending: ref p1, input: this, trans: trans2),
                        arg2: Take(pending: ref p2, input: b2, trans: trans2),
                        arg3: Take(pending: ref p3, input: b3, trans: trans2),
                        arg4: Take(pending: ref p4, input: b4, trans: trans2),
                        arg5: Take(pending: ref p5, input: b5, trans: trans2),
                        arg6: Take(pending: ref p6, input: b6, trans: trans2)),
                initialValue: () =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction(),
                        arg4: b4.SampleNoTransaction(),
                        arg5: b5.SampleNoTransaction(),
                        arg6: b6.SampleNoTransaction()),
                listeners: gate =>
                [
                    StreamInternal.Capture(
                        input: this.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p1.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b2.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p2.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b3.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p3.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b4.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p4.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b5.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p5.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b6.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p6.Set(trans: t, value: v))
                ]);
        });

    internal Behavior<TResult> LiftImpl<T2, T3, T4, T5, T6, T7, TResult>(
        LoopedBehavior<T2> b2,
        LoopedBehavior<T3> b3,
        LoopedBehavior<T4> b4,
        LoopedBehavior<T5> b5,
        LoopedBehavior<T6> b6,
        LoopedBehavior<T7> b7,
        Func<T, T2, T3, T4, T5, T6, T7, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            CaptureSlot<T> p1 = default;
            CaptureSlot<T2> p2 = default;
            CaptureSlot<T3> p3 = default;
            CaptureSlot<T4> p4 = default;
            CaptureSlot<T5> p5 = default;
            CaptureSlot<T6> p6 = default;
            CaptureSlot<T7> p7 = default;

            return HoldLifted(
                keepListenersAlive: this.stream.KeepListenersAlive,
                trans: trans,
                recombine: trans2 =>
                    f(
                        arg1: Take(pending: ref p1, input: this, trans: trans2),
                        arg2: Take(pending: ref p2, input: b2, trans: trans2),
                        arg3: Take(pending: ref p3, input: b3, trans: trans2),
                        arg4: Take(pending: ref p4, input: b4, trans: trans2),
                        arg5: Take(pending: ref p5, input: b5, trans: trans2),
                        arg6: Take(pending: ref p6, input: b6, trans: trans2),
                        arg7: Take(pending: ref p7, input: b7, trans: trans2)),
                initialValue: () =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction(),
                        arg4: b4.SampleNoTransaction(),
                        arg5: b5.SampleNoTransaction(),
                        arg6: b6.SampleNoTransaction(),
                        arg7: b7.SampleNoTransaction()),
                listeners: gate =>
                [
                    StreamInternal.Capture(
                        input: this.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p1.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b2.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p2.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b3.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p3.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b4.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p4.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b5.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p5.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b6.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p6.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b7.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p7.Set(trans: t, value: v))
                ]);
        });

    internal Behavior<TResult> LiftImpl<T2, T3, T4, T5, T6, T7, T8, TResult>(
        LoopedBehavior<T2> b2,
        LoopedBehavior<T3> b3,
        LoopedBehavior<T4> b4,
        LoopedBehavior<T5> b5,
        LoopedBehavior<T6> b6,
        LoopedBehavior<T7> b7,
        LoopedBehavior<T8> b8,
        Func<T, T2, T3, T4, T5, T6, T7, T8, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            CaptureSlot<T> p1 = default;
            CaptureSlot<T2> p2 = default;
            CaptureSlot<T3> p3 = default;
            CaptureSlot<T4> p4 = default;
            CaptureSlot<T5> p5 = default;
            CaptureSlot<T6> p6 = default;
            CaptureSlot<T7> p7 = default;
            CaptureSlot<T8> p8 = default;

            return HoldLifted(
                keepListenersAlive: this.stream.KeepListenersAlive,
                trans: trans,
                recombine: trans2 =>
                    f(
                        arg1: Take(pending: ref p1, input: this, trans: trans2),
                        arg2: Take(pending: ref p2, input: b2, trans: trans2),
                        arg3: Take(pending: ref p3, input: b3, trans: trans2),
                        arg4: Take(pending: ref p4, input: b4, trans: trans2),
                        arg5: Take(pending: ref p5, input: b5, trans: trans2),
                        arg6: Take(pending: ref p6, input: b6, trans: trans2),
                        arg7: Take(pending: ref p7, input: b7, trans: trans2),
                        arg8: Take(pending: ref p8, input: b8, trans: trans2)),
                initialValue: () =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction(),
                        arg4: b4.SampleNoTransaction(),
                        arg5: b5.SampleNoTransaction(),
                        arg6: b6.SampleNoTransaction(),
                        arg7: b7.SampleNoTransaction(),
                        arg8: b8.SampleNoTransaction()),
                listeners: gate =>
                [
                    StreamInternal.Capture(
                        input: this.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p1.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b2.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p2.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b3.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p3.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b4.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p4.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b5.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p5.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b6.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p6.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b7.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p7.Set(trans: t, value: v)),
                    StreamInternal.Capture(
                        input: b8.Updates(),
                        gate: gate,
                        trans: trans,
                        capture: (t, v) => p8.Set(trans: t, value: v))
                ]);
        });

    /// <summary>
    ///     Reads the value of an input for this firing. If the input updated in this
    ///     transaction, this is the captured value. If it did not, this is the current
    ///     value of the behavior.
    /// </summary>
    /// <remarks>
    ///     The slot gives its value only to the transaction that filled it. A
    ///     transaction that failed after the capture left a value that the input
    ///     never committed. The next firing then put the inputs together with it.
    ///     The current value of the behavior is correct in that case, because a
    ///     transaction that fails discards the update of the behavior. See
    ///     <see cref="CaptureSlot{T}" />.
    /// </remarks>
    internal static TInput Take<TInput>(
        ref CaptureSlot<TInput> pending,
        LoopedBehavior<TInput> input,
        TransactionInternal trans) =>
        pending.Take(trans).TryGetValue(out TInput captured) ? captured : input.SampleNoTransaction();

    private static Behavior<TResult> HoldLifted<TResult>(
        IKeepListenersAlive keepListenersAlive,
        TransactionInternal trans,
        Func<TransactionInternal, TResult> recombine,
        Func<TResult> initialValue,
        Func<OnceGate, IListener[]> listeners)
    {
        // The gate makes one firing for a transaction that updates more than one input. SodaFlow
        // captured the new value of each input. This is a map, written out, thus the step that
        // puts the inputs together gets the transaction, which the slots need.
        Stream<TResult> @out = new(keepListenersAlive);
        OnceGate gate = new(node: @out.Node, action: trans2 => @out.Send(trans: trans2, a: recombine(trans2)));
        Stream<TResult> result = @out;

        // ReSharper disable once LoopCanBeConvertedToQuery - Foreach for performance reasons
        foreach (IListener listener in listeners(gate))
        {
            result = result.UnsafeAttachListener(listener);
        }

        return result.HoldLazyInternal(trans: trans, initialValue: new LazySample<TResult>(_ => initialValue()));
    }

    // A lift of the function and the argument. Before, each input came in through Value, which
    // is an output stream and a coalesce operation, and the output had a coalesce operation of
    // its own: six nodes where the lift has one.
    internal Behavior<TResult> ApplyImpl<TResult>(LoopedBehavior<Func<T, TResult>> bf) =>
        bf.LiftImpl(b2: this, f: static (function, value) => function(value));
}
