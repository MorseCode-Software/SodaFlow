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
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage - The type documents itself, and the interface is internal.
public class Behavior<T> : ISampleSource<T>, ILastAction
{
    // A constant behavior has no stream until a caller asks for its updates. Most constants
    // are only sampled or lifted. Updates makes the stream in the transaction, as Cell.updates
    // does. The field is volatile for the same cause: a reader must not see the reference
    // before the state of the stream. See the comment on that field.
    private volatile Stream<T>? stream;

    // ReSharper disable once NotAccessedField.Local - Used to keep object from being garbage collected
    private readonly IListener? streamListener;

    private T valueProperty;
    private MaybeInternal<T> valueUpdate;

    internal Behavior(T value) => this.valueProperty = value;

    internal Behavior(Stream<T> stream, T initialValue)
    {
        this.stream = stream;
        this.valueProperty = initialValue;
        this.UsingInitialValue = true;

        // SodaFlow gives this a value before Listen, because Listen can send the firings that
        // this transaction made in this transaction. Thus, the handler below can run before the constructor
        // returns.
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
                            trans2.Last(this);
                        }

                        this.valueUpdate = MaybeInternal.Some(a);
                    },
                    suppressEarlierFirings: false));
    }

    internal IKeepListenersAlive KeepListenersAlive => this.Updates().KeepListenersAlive;

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

    // The behavior is its own last action, thus a hold allocates no delegate for the commit of
    // its update. A derived behavior with a lazy initial value adds its own work to each.
    void ILastAction.Commit() => this.CommitLast();

    void ILastAction.Discard() => this.DiscardLast();

    private protected virtual void CommitLast() => this.ApplyValueUpdate();

    private protected virtual void DiscardLast() => this.DiscardValueUpdate();

    // The value at the end of this transaction: the pending update when the behavior got one,
    // and the current value when it did not. A lazy sample reads this in the sample phase.
    T ISampleSource<T>.Read() => this.PendingOrCurrent();

    internal T PendingOrCurrent() => this.valueUpdate.TryGetValue(out T v) ? v : this.SampleNoTransaction();

    /// <summary>
    ///     Makes this behavior use its initial value again. A transaction that set
    ///     the value from a lazy sample failed, thus the value is not correct.
    /// </summary>
    protected void UseInitialValueAgain() => this.UsingInitialValue = true;

    internal T SampleImpl() => TransactionInternal.Apply((_, _) => this.SampleNoTransaction());

    internal LazySample<T> SampleLazyImpl() => TransactionInternal.Apply((trans, _) => this.SampleLazy(trans));

    internal LazySample<T> SampleLazy(TransactionInternal trans)
    {
        LazySample<T> s = new(source: this);

        // The sample phase runs after each send and each prioritized action. Thus, the pending
        // update, when this behavior got one, is the value at the end of the transaction. A
        // transaction that fails discards that update, and the capture goes with it. The sample
        // reads for itself, thus this allocates no closure.
        trans.Sample(s);

        return s;
    }

    internal LazySample<TResult> SampleLazyMappedImpl<TResult>(Func<T, TResult> f) =>
        TransactionInternal.Apply((trans, _) => this.SampleLazyMapped(trans: trans, f: f));

    // A sample of this behavior through f: one sample and one small object. Before, this was
    // a sample and a map on it: two samples, each with its own closure and delegate. The function
    // runs in the sample phase, one time for each value, as it did through the map.
    internal LazySample<TResult> SampleLazyMapped<TResult>(TransactionInternal trans, Func<T, TResult> f)
    {
        LazySample<TResult> s = new(source: new MappedSampleSource<T, TResult>(behavior: this, f: f));
        trans.Sample(s);
        return s;
    }

    internal virtual T SampleNoTransaction() => this.ValueProperty;

    // A constant makes its never stream here, at the first ask. SodaFlow creates it in the
    // transaction. Thus, the test and the set are safe while they hold no lock of their own.
    // The library runs one transaction at a time across the process, thus only one thread can
    // be here. See the remarks on SodaFlow.Transaction.
    internal Stream<T> Updates() =>
        this.stream
        ?? TransactionInternal.Apply(
            code: static (_, _, behavior) =>
                // ReSharper disable once NonAtomicCompoundOperator - This is fine since the stream field is only
                // set within a Transaction.
                behavior.stream ??= new Stream<T>(),
            state: this);

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
        Stream<T> @out = new(this.Updates().KeepListenersAlive);

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

    internal Behavior<TResult> MapImpl<TResult>(Func<T, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
            this.Updates()
                .MapImpl(f)
                .HoldLazyInternal(trans: trans, initialValue: this.SampleLazyMapped(trans: trans, f: f)));

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

    internal Behavior<TResult> LiftImpl<T2, TResult>(Behavior<T2> b2, Func<T, T2, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            InputCapture<T> p1 = new();
            InputCapture<T2> p2 = new();

            LiftOutput<TResult> output =
                new(
                    keepListenersAlive: KeepListenersAliveInternal.Combine(
                        first: this.KeepListenersAlive,
                        second: b2.KeepListenersAlive),
                    recombine: trans2 =>
                        f(
                            arg1: p1.Take(input: this, trans: trans2),
                            arg2: p2.Take(input: b2, trans: trans2)));

            output.Capture(input: this.Updates(), trans: trans, capture: p1);
            output.Capture(input: b2.Updates(), trans: trans, capture: p2);

            return output.Hold(
                trans: trans,
                initialValue: _ =>
                    f(arg1: this.SampleNoTransaction(), arg2: b2.SampleNoTransaction()));
        });

    internal Behavior<TResult> LiftImpl<T2, T3, TResult>(
        Behavior<T2> b2,
        Behavior<T3> b3,
        Func<T, T2, T3, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            InputCapture<T> p1 = new();
            InputCapture<T2> p2 = new();
            InputCapture<T3> p3 = new();

            LiftOutput<TResult> output =
                new(
                    keepListenersAlive: KeepListenersAliveInternal.Combine(
                        this.KeepListenersAlive,
                        b2.KeepListenersAlive,
                        b3.KeepListenersAlive),
                    recombine: trans2 =>
                        f(
                            arg1: p1.Take(input: this, trans: trans2),
                            arg2: p2.Take(input: b2, trans: trans2),
                            arg3: p3.Take(input: b3, trans: trans2)));

            output.Capture(input: this.Updates(), trans: trans, capture: p1);
            output.Capture(input: b2.Updates(), trans: trans, capture: p2);
            output.Capture(input: b3.Updates(), trans: trans, capture: p3);

            return output.Hold(
                trans: trans,
                initialValue: _ =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction()));
        });

    internal Behavior<TResult> LiftImpl<T2, T3, T4, TResult>(
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Func<T, T2, T3, T4, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            InputCapture<T> p1 = new();
            InputCapture<T2> p2 = new();
            InputCapture<T3> p3 = new();
            InputCapture<T4> p4 = new();

            LiftOutput<TResult> output =
                new(
                    keepListenersAlive: KeepListenersAliveInternal.Combine(
                        this.KeepListenersAlive,
                        b2.KeepListenersAlive,
                        b3.KeepListenersAlive,
                        b4.KeepListenersAlive),
                    recombine: trans2 =>
                        f(
                            arg1: p1.Take(input: this, trans: trans2),
                            arg2: p2.Take(input: b2, trans: trans2),
                            arg3: p3.Take(input: b3, trans: trans2),
                            arg4: p4.Take(input: b4, trans: trans2)));

            output.Capture(input: this.Updates(), trans: trans, capture: p1);
            output.Capture(input: b2.Updates(), trans: trans, capture: p2);
            output.Capture(input: b3.Updates(), trans: trans, capture: p3);
            output.Capture(input: b4.Updates(), trans: trans, capture: p4);

            return output.Hold(
                trans: trans,
                initialValue: _ =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction(),
                        arg4: b4.SampleNoTransaction()));
        });

    internal Behavior<TResult> LiftImpl<T2, T3, T4, T5, TResult>(
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Func<T, T2, T3, T4, T5, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            InputCapture<T> p1 = new();
            InputCapture<T2> p2 = new();
            InputCapture<T3> p3 = new();
            InputCapture<T4> p4 = new();
            InputCapture<T5> p5 = new();

            LiftOutput<TResult> output =
                new(
                    keepListenersAlive: KeepListenersAliveInternal.Combine(
                        this.KeepListenersAlive,
                        b2.KeepListenersAlive,
                        b3.KeepListenersAlive,
                        b4.KeepListenersAlive,
                        b5.KeepListenersAlive),
                    recombine: trans2 =>
                        f(
                            arg1: p1.Take(input: this, trans: trans2),
                            arg2: p2.Take(input: b2, trans: trans2),
                            arg3: p3.Take(input: b3, trans: trans2),
                            arg4: p4.Take(input: b4, trans: trans2),
                            arg5: p5.Take(input: b5, trans: trans2)));

            output.Capture(input: this.Updates(), trans: trans, capture: p1);
            output.Capture(input: b2.Updates(), trans: trans, capture: p2);
            output.Capture(input: b3.Updates(), trans: trans, capture: p3);
            output.Capture(input: b4.Updates(), trans: trans, capture: p4);
            output.Capture(input: b5.Updates(), trans: trans, capture: p5);

            return output.Hold(
                trans: trans,
                initialValue: _ =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction(),
                        arg4: b4.SampleNoTransaction(),
                        arg5: b5.SampleNoTransaction()));
        });

    internal Behavior<TResult> LiftImpl<T2, T3, T4, T5, T6, TResult>(
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Func<T, T2, T3, T4, T5, T6, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            InputCapture<T> p1 = new();
            InputCapture<T2> p2 = new();
            InputCapture<T3> p3 = new();
            InputCapture<T4> p4 = new();
            InputCapture<T5> p5 = new();
            InputCapture<T6> p6 = new();

            LiftOutput<TResult> output =
                new(
                    keepListenersAlive: KeepListenersAliveInternal.Combine(
                        this.KeepListenersAlive,
                        b2.KeepListenersAlive,
                        b3.KeepListenersAlive,
                        b4.KeepListenersAlive,
                        b5.KeepListenersAlive,
                        b6.KeepListenersAlive),
                    recombine: trans2 =>
                        f(
                            arg1: p1.Take(input: this, trans: trans2),
                            arg2: p2.Take(input: b2, trans: trans2),
                            arg3: p3.Take(input: b3, trans: trans2),
                            arg4: p4.Take(input: b4, trans: trans2),
                            arg5: p5.Take(input: b5, trans: trans2),
                            arg6: p6.Take(input: b6, trans: trans2)));

            output.Capture(input: this.Updates(), trans: trans, capture: p1);
            output.Capture(input: b2.Updates(), trans: trans, capture: p2);
            output.Capture(input: b3.Updates(), trans: trans, capture: p3);
            output.Capture(input: b4.Updates(), trans: trans, capture: p4);
            output.Capture(input: b5.Updates(), trans: trans, capture: p5);
            output.Capture(input: b6.Updates(), trans: trans, capture: p6);

            return output.Hold(
                trans: trans,
                initialValue: _ =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction(),
                        arg4: b4.SampleNoTransaction(),
                        arg5: b5.SampleNoTransaction(),
                        arg6: b6.SampleNoTransaction()));
        });

    internal Behavior<TResult> LiftImpl<T2, T3, T4, T5, T6, T7, TResult>(
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Behavior<T7> b7,
        Func<T, T2, T3, T4, T5, T6, T7, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            InputCapture<T> p1 = new();
            InputCapture<T2> p2 = new();
            InputCapture<T3> p3 = new();
            InputCapture<T4> p4 = new();
            InputCapture<T5> p5 = new();
            InputCapture<T6> p6 = new();
            InputCapture<T7> p7 = new();

            LiftOutput<TResult> output =
                new(
                    keepListenersAlive: KeepListenersAliveInternal.Combine(
                        this.KeepListenersAlive,
                        b2.KeepListenersAlive,
                        b3.KeepListenersAlive,
                        b4.KeepListenersAlive,
                        b5.KeepListenersAlive,
                        b6.KeepListenersAlive,
                        b7.KeepListenersAlive),
                    recombine: trans2 =>
                        f(
                            arg1: p1.Take(input: this, trans: trans2),
                            arg2: p2.Take(input: b2, trans: trans2),
                            arg3: p3.Take(input: b3, trans: trans2),
                            arg4: p4.Take(input: b4, trans: trans2),
                            arg5: p5.Take(input: b5, trans: trans2),
                            arg6: p6.Take(input: b6, trans: trans2),
                            arg7: p7.Take(input: b7, trans: trans2)));

            output.Capture(input: this.Updates(), trans: trans, capture: p1);
            output.Capture(input: b2.Updates(), trans: trans, capture: p2);
            output.Capture(input: b3.Updates(), trans: trans, capture: p3);
            output.Capture(input: b4.Updates(), trans: trans, capture: p4);
            output.Capture(input: b5.Updates(), trans: trans, capture: p5);
            output.Capture(input: b6.Updates(), trans: trans, capture: p6);
            output.Capture(input: b7.Updates(), trans: trans, capture: p7);

            return output.Hold(
                trans: trans,
                initialValue: _ =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction(),
                        arg4: b4.SampleNoTransaction(),
                        arg5: b5.SampleNoTransaction(),
                        arg6: b6.SampleNoTransaction(),
                        arg7: b7.SampleNoTransaction()));
        });

    internal Behavior<TResult> LiftImpl<T2, T3, T4, T5, T6, T7, T8, TResult>(
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Behavior<T7> b7,
        Behavior<T8> b8,
        Func<T, T2, T3, T4, T5, T6, T7, T8, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            InputCapture<T> p1 = new();
            InputCapture<T2> p2 = new();
            InputCapture<T3> p3 = new();
            InputCapture<T4> p4 = new();
            InputCapture<T5> p5 = new();
            InputCapture<T6> p6 = new();
            InputCapture<T7> p7 = new();
            InputCapture<T8> p8 = new();

            LiftOutput<TResult> output =
                new(
                    keepListenersAlive: KeepListenersAliveInternal.Combine(
                        this.KeepListenersAlive,
                        b2.KeepListenersAlive,
                        b3.KeepListenersAlive,
                        b4.KeepListenersAlive,
                        b5.KeepListenersAlive,
                        b6.KeepListenersAlive,
                        b7.KeepListenersAlive,
                        b8.KeepListenersAlive),
                    recombine: trans2 =>
                        f(
                            arg1: p1.Take(input: this, trans: trans2),
                            arg2: p2.Take(input: b2, trans: trans2),
                            arg3: p3.Take(input: b3, trans: trans2),
                            arg4: p4.Take(input: b4, trans: trans2),
                            arg5: p5.Take(input: b5, trans: trans2),
                            arg6: p6.Take(input: b6, trans: trans2),
                            arg7: p7.Take(input: b7, trans: trans2),
                            arg8: p8.Take(input: b8, trans: trans2)));

            output.Capture(input: this.Updates(), trans: trans, capture: p1);
            output.Capture(input: b2.Updates(), trans: trans, capture: p2);
            output.Capture(input: b3.Updates(), trans: trans, capture: p3);
            output.Capture(input: b4.Updates(), trans: trans, capture: p4);
            output.Capture(input: b5.Updates(), trans: trans, capture: p5);
            output.Capture(input: b6.Updates(), trans: trans, capture: p6);
            output.Capture(input: b7.Updates(), trans: trans, capture: p7);
            output.Capture(input: b8.Updates(), trans: trans, capture: p8);

            return output.Hold(
                trans: trans,
                initialValue: _ =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction(),
                        arg4: b4.SampleNoTransaction(),
                        arg5: b5.SampleNoTransaction(),
                        arg6: b6.SampleNoTransaction(),
                        arg7: b7.SampleNoTransaction(),
                        arg8: b8.SampleNoTransaction()));
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
        Behavior<TInput> input,
        TransactionInternal trans) =>
        pending.Take(trans).TryGetValue(out TInput captured) ? captured : input.SampleNoTransaction();

    // A lift of the function and the argument. Before, each input came in through Value, which
    // is an output stream and a coalesce operation, and the output had a coalesce operation of
    // its own: six nodes where the lift has one.
    internal Behavior<TResult> ApplyImpl<TResult>(Behavior<Func<T, TResult>> bf) =>
        bf.LiftImpl(b2: this, f: static (function, value) => function(value));
}
