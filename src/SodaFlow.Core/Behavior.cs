using System;
using JetBrains.Annotations;

namespace SodaFlow;

internal static class BehaviorInternal
{
    internal static Behavior<T> ConstantImpl<T>(T value) => new(value);

    internal static Behavior<T> ConstantLazyImpl<T>(Lazy<T> value) =>
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
public class Behavior<T>
{
    // This captures only this behavior, thus SodaFlow builds it one time and not on each
    // firing. Only the constructor that takes a stream needs it, because a constant behavior
    // does not update.
    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable - This is done for performance reasons.
    private readonly Action? applyValueUpdate;
    private readonly Stream<T> stream;

    // ReSharper disable once NotAccessedField.Local - Used to keep object from being garbage collected
    private readonly IListener? streamListener;

    private T valueProperty;
    private MaybeInternal<T> valueUpdate;

    internal Behavior(T value)
    {
        this.stream = new Stream<T>();
        this.valueProperty = value;
    }

    internal Behavior(Stream<T> stream, T initialValue)
    {
        this.stream = stream;
        this.valueProperty = initialValue;
        this.UsingInitialValue = true;

        // SodaFlow gives this a value before Listen, because Listen can send the firings that
        // this transaction made in this transaction. Thus the handler below can run before the constructor
        // returns.
        this.applyValueUpdate = this.ApplyValueUpdate;

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
                            trans2.Last(this.applyValueUpdate);
                        }

                        this.valueUpdate = MaybeInternal.Some(a);
                    },
                    suppressEarlierFirings: false));
    }

    internal IKeepListenersAlive KeepListenersAlive => this.stream.KeepListenersAlive;

    /// <summary>
    ///     Gets or sets the value this behavior reports when sampled not in a transaction.
    /// </summary>
    /// <value>The current value of the behavior.</value>
    /// <remarks>
    ///     A set of this property clears <see cref="UsingInitialValue" />, because a behavior
    ///     with a new value does not use the value from its construction. A derived type must
    ///     write through this property and not through the backing field, to keep that flag
    ///     correct.
    /// </remarks>
    protected T ValueProperty
    {
        get => this.valueProperty;
        set
        {
            this.valueProperty = value;
            this.NotUsingInitialValue();
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

    /// <summary>
    ///     Records that this behavior no longer depends on the value it was constructed with.
    /// </summary>
    /// <remarks>
    ///     SodaFlow calls this when it gives <see cref="ValueProperty" /> a value. A derived type
    ///     overrides this to release the data that it holds only to make that initial value.
    ///     For an example, see <see cref="LoopedBehavior{T}" />. It releases its deferred
    ///     initial value here, thus a closed loop does not keep that value alive.
    /// </remarks>
    protected virtual void NotUsingInitialValue() => this.UsingInitialValue = false;

    internal T SampleImpl() => TransactionInternal.Apply((_, _) => this.SampleNoTransaction());

    internal Lazy<T> SampleLazyImpl() => TransactionInternal.Apply((trans, _) => this.SampleLazy(trans));

    internal Lazy<T> SampleLazy(TransactionInternal trans)
    {
        LazySample s = new(this);

        trans.Sample(() =>
        {
            s.Value = this.valueUpdate.Match(onSome: static v => v, onNone: this.SampleNoTransaction);
            s.Behavior = null;
        });

        // ReSharper disable once NullableWarningSuppressionIsUsed - Optimization.  Only sets should be above.
        return new Lazy<T>(() => s.Behavior == null ? s.Value! : s.Behavior.SampleImpl());
    }

    internal virtual T SampleNoTransaction() => this.ValueProperty;

    internal Stream<T> Updates() => this.stream;

    /// <summary>
    ///     The stream of the value of this behavior. It gives the current value in this
    ///     transaction, and then each update.
    /// </summary>
    /// <remarks>
    ///     The two sources send into one output stream. They do not use a spark stream, a
    ///     snapshot of it, and a merge, which is four streams where two are sufficient. Value is
    ///     below Cell.ListenStrong, Apply, and the switch operations, thus it was a large part of
    ///     the cost of each of them.
    ///     SodaFlow queues the initial send against a new node of its own, as the spark
    ///     stream that it replaces did, and for the same cause. A new node ranks below all
    ///     other nodes, thus SodaFlow supplies the value also when a caller calls Value
    ///     during a drain. SwitchB does this, because its handler builds a Value for the newly
    ///     selected behavior in the middle of a transaction. An initial send on the output node
    ///     makes the switch supply a previous value. The node costs nothing, but the two
    ///     intermediate streams were expensive.
    ///     A coalesce operation in which the right value wins keeps an update from this
    ///     transaction in front of the initial value. A merge with (left, right) =&gt; right
    ///     gave the same result.
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

    internal Behavior<TResult> MapImpl<TResult>(Func<T, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
            this.Updates()
                .MapImpl(f)
                .HoldLazyInternal(trans: trans, initialValue: this.SampleLazy(trans).MapImpl(f)));

    // Lift no longer uses Apply. One call to ApplyImpl for each additional input made each
    // input pay for a Value(), which is a spark stream, a snapshot, a merge, and a coalesce
    // operation. Thus a six-way lift built approximately fifty streams and cost approximately
    // 87KB. This method builds three streams for all arities: one pulse stream that each input
    // sends into, one coalesce operation that makes the updates of a transaction into one
    // firing, and one map that puts the inputs together again. The IEnumerable overload in
    // BehaviorExtensionMethods already used this method.
    //
    // SodaFlow captures the new value of each input as that value moves through the graph,
    // and does not read the value from the behavior after that. A behavior applies its update
    // through a listener on Node<T>.Null. The priority queue drains the entries with a null
    // rank only after all the ranked entries. Thus the behaviors hold their previous
    // values when the map below runs. The sequence of the ranks makes the capture safe,
    // because each input links into pulse.Node. Thus pulse.Node ranks above all of them, and
    // SodaFlow fills each slot before any code after the coalesce operation can run.

    internal Behavior<TResult> LiftImpl<T2, TResult>(Behavior<T2> b2, Func<T, T2, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            Stream<UnitInternal> pulse = new(this.stream.KeepListenersAlive);

            MaybeInternal<T> p1 = MaybeInternal<T>.None;
            MaybeInternal<T2> p2 = MaybeInternal<T2>.None;

            IListener[] listeners =
            [
                Pulse(input: this, pulse: pulse, trans: trans, capture: v => p1 = MaybeInternal.Some(v)),
                Pulse(input: b2, pulse: pulse, trans: trans, capture: v => p2 = MaybeInternal.Some(v))
            ];

            return HoldLifted(
                pulse: pulse,
                trans: trans,
                recombine: () =>
                {
                    TResult result =
                        f(arg1: Take(pending: ref p1, input: this), arg2: Take(pending: ref p2, input: b2));

                    return result;
                },
                initialValue: () => f(arg1: this.SampleNoTransaction(), arg2: b2.SampleNoTransaction()),
                listeners: listeners);
        });

    internal Behavior<TResult> LiftImpl<T2, T3, TResult>(
        Behavior<T2> b2,
        Behavior<T3> b3,
        Func<T, T2, T3, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            Stream<UnitInternal> pulse = new(this.stream.KeepListenersAlive);

            MaybeInternal<T> p1 = MaybeInternal<T>.None;
            MaybeInternal<T2> p2 = MaybeInternal<T2>.None;
            MaybeInternal<T3> p3 = MaybeInternal<T3>.None;

            IListener[] listeners =
            [
                Pulse(input: this, pulse: pulse, trans: trans, capture: v => p1 = MaybeInternal.Some(v)),
                Pulse(input: b2, pulse: pulse, trans: trans, capture: v => p2 = MaybeInternal.Some(v)),
                Pulse(input: b3, pulse: pulse, trans: trans, capture: v => p3 = MaybeInternal.Some(v))
            ];

            return HoldLifted(
                pulse: pulse,
                trans: trans,
                recombine: () =>
                    f(
                        arg1: Take(pending: ref p1, input: this),
                        arg2: Take(pending: ref p2, input: b2),
                        arg3: Take(pending: ref p3, input: b3)),
                initialValue: () =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction()),
                listeners: listeners);
        });

    internal Behavior<TResult> LiftImpl<T2, T3, T4, TResult>(
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Func<T, T2, T3, T4, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            Stream<UnitInternal> pulse = new(this.stream.KeepListenersAlive);

            MaybeInternal<T> p1 = MaybeInternal<T>.None;
            MaybeInternal<T2> p2 = MaybeInternal<T2>.None;
            MaybeInternal<T3> p3 = MaybeInternal<T3>.None;
            MaybeInternal<T4> p4 = MaybeInternal<T4>.None;

            IListener[] listeners =
            [
                Pulse(input: this, pulse: pulse, trans: trans, capture: v => p1 = MaybeInternal.Some(v)),
                Pulse(input: b2, pulse: pulse, trans: trans, capture: v => p2 = MaybeInternal.Some(v)),
                Pulse(input: b3, pulse: pulse, trans: trans, capture: v => p3 = MaybeInternal.Some(v)),
                Pulse(input: b4, pulse: pulse, trans: trans, capture: v => p4 = MaybeInternal.Some(v))
            ];

            return HoldLifted(
                pulse: pulse,
                trans: trans,
                recombine: () =>
                    f(
                        arg1: Take(pending: ref p1, input: this),
                        arg2: Take(pending: ref p2, input: b2),
                        arg3: Take(pending: ref p3, input: b3),
                        arg4: Take(pending: ref p4, input: b4)),
                initialValue: () =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction(),
                        arg4: b4.SampleNoTransaction()),
                listeners: listeners);
        });

    internal Behavior<TResult> LiftImpl<T2, T3, T4, T5, TResult>(
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Func<T, T2, T3, T4, T5, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
        {
            Stream<UnitInternal> pulse = new(this.stream.KeepListenersAlive);

            MaybeInternal<T> p1 = MaybeInternal<T>.None;
            MaybeInternal<T2> p2 = MaybeInternal<T2>.None;
            MaybeInternal<T3> p3 = MaybeInternal<T3>.None;
            MaybeInternal<T4> p4 = MaybeInternal<T4>.None;
            MaybeInternal<T5> p5 = MaybeInternal<T5>.None;

            IListener[] listeners =
            [
                Pulse(input: this, pulse: pulse, trans: trans, capture: v => p1 = MaybeInternal.Some(v)),
                Pulse(input: b2, pulse: pulse, trans: trans, capture: v => p2 = MaybeInternal.Some(v)),
                Pulse(input: b3, pulse: pulse, trans: trans, capture: v => p3 = MaybeInternal.Some(v)),
                Pulse(input: b4, pulse: pulse, trans: trans, capture: v => p4 = MaybeInternal.Some(v)),
                Pulse(input: b5, pulse: pulse, trans: trans, capture: v => p5 = MaybeInternal.Some(v))
            ];

            return HoldLifted(
                pulse: pulse,
                trans: trans,
                recombine: () =>
                    f(
                        arg1: Take(pending: ref p1, input: this),
                        arg2: Take(pending: ref p2, input: b2),
                        arg3: Take(pending: ref p3, input: b3),
                        arg4: Take(pending: ref p4, input: b4),
                        arg5: Take(pending: ref p5, input: b5)),
                initialValue: () =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction(),
                        arg4: b4.SampleNoTransaction(),
                        arg5: b5.SampleNoTransaction()),
                listeners: listeners);
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
            Stream<UnitInternal> pulse = new(this.stream.KeepListenersAlive);

            MaybeInternal<T> p1 = MaybeInternal<T>.None;
            MaybeInternal<T2> p2 = MaybeInternal<T2>.None;
            MaybeInternal<T3> p3 = MaybeInternal<T3>.None;
            MaybeInternal<T4> p4 = MaybeInternal<T4>.None;
            MaybeInternal<T5> p5 = MaybeInternal<T5>.None;
            MaybeInternal<T6> p6 = MaybeInternal<T6>.None;

            IListener[] listeners =
            [
                Pulse(input: this, pulse: pulse, trans: trans, capture: v => p1 = MaybeInternal.Some(v)),
                Pulse(input: b2, pulse: pulse, trans: trans, capture: v => p2 = MaybeInternal.Some(v)),
                Pulse(input: b3, pulse: pulse, trans: trans, capture: v => p3 = MaybeInternal.Some(v)),
                Pulse(input: b4, pulse: pulse, trans: trans, capture: v => p4 = MaybeInternal.Some(v)),
                Pulse(input: b5, pulse: pulse, trans: trans, capture: v => p5 = MaybeInternal.Some(v)),
                Pulse(input: b6, pulse: pulse, trans: trans, capture: v => p6 = MaybeInternal.Some(v))
            ];

            return HoldLifted(
                pulse: pulse,
                trans: trans,
                recombine: () =>
                    f(
                        arg1: Take(pending: ref p1, input: this),
                        arg2: Take(pending: ref p2, input: b2),
                        arg3: Take(pending: ref p3, input: b3),
                        arg4: Take(pending: ref p4, input: b4),
                        arg5: Take(pending: ref p5, input: b5),
                        arg6: Take(pending: ref p6, input: b6)),
                initialValue: () =>
                    f(
                        arg1: this.SampleNoTransaction(),
                        arg2: b2.SampleNoTransaction(),
                        arg3: b3.SampleNoTransaction(),
                        arg4: b4.SampleNoTransaction(),
                        arg5: b5.SampleNoTransaction(),
                        arg6: b6.SampleNoTransaction()),
                listeners: listeners);
        });

    /// <summary>
    ///     Connects one lifted input to the shared pulse stream. It keeps the new value of that
    ///     input, thus the step that puts the inputs together does not read the value from the
    ///     behavior.
    /// </summary>
    private static IListener Pulse<TInput>(
        Behavior<TInput> input,
        Stream<UnitInternal> pulse,
        TransactionInternal trans,
        Action<TInput> capture) =>
        input.Updates()
            .Listen(
                target: pulse.Node,
                trans: trans,
                action: (trans2, v) =>
                {
                    capture(v);
                    pulse.Send(trans: trans2, a: UnitInternal.Value);
                },
                suppressEarlierFirings: false);

    /// <summary>
    ///     Reads the value of an input for this firing. If the input updated in this
    ///     transaction, this is the captured value. If it did not, this is the current value of
    ///     the behavior.
    /// </summary>
    /// <remarks>
    ///     SodaFlow clears the slot to keep the object clean, and not to make it correct. A slot that
    ///     keeps its value still gives the applicable answer, because the behavior commits that
    ///     same value before the next transaction reads the slot. A test of this removed the
    ///     reset, and no test could find the difference. SodaFlow clears the slot so that the
    ///     closure does not hold a second reference to the last value of each input for the full
    ///     life of the lifted behavior.
    /// </remarks>
    private static TInput Take<TInput>(ref MaybeInternal<TInput> pending, Behavior<TInput> input)
    {
        TInput value = pending.TryGetValue(out TInput captured) ? captured : input.SampleNoTransaction();
        pending = MaybeInternal<TInput>.None;
        return value;
    }

    private static Behavior<TResult> HoldLifted<TResult>(
        Stream<UnitInternal> pulse,
        TransactionInternal trans,
        Func<TResult> recombine,
        Func<TResult> initialValue,
        // ReSharper disable once ParameterTypeCanBeEnumerable.Local - Typed as array for performance reasons
        IListener[] listeners)
    {
        // The coalesce operation makes one firing for a transaction that updates more than one
        // input. SodaFlow captured the new value of each input.
        Stream<TResult> result = pulse.Coalesce(trans1: trans, f: static (x, _) => x).MapImpl(_ => recombine());

        // ReSharper disable once LoopCanBeConvertedToQuery - Foreach for performance reasons
        foreach (IListener listener in listeners)
        {
            result = result.UnsafeAttachListener(listener);
        }

        return result.HoldLazyInternal(trans: trans, initialValue: new Lazy<TResult>(initialValue));
    }

    internal Behavior<TResult> ApplyImpl<TResult>(Behavior<Func<T, TResult>> bf) =>
        TransactionInternal.Apply((trans0, _) =>
        {
            Stream<TResult> @out = new(this.stream.KeepListenersAlive);

            Node<TResult> outTarget = @out.Node;
            Node<UnitInternal> inTarget = new();

            Node<UnitInternal>.Target nodeTarget =
                inTarget.Link(
                    trans: trans0,
                    action: NoOp,
                    target: outTarget);

            Func<T, TResult>? f = null;
            T? a = default;
            bool isASet = false;

            IListener l1 =
                bf.Value(trans0)
                    .Listen(
                        target: inTarget,
                        trans: trans0,
                        action: (trans1, ff) =>
                        {
                            f = ff;

                            if (isASet)
                            {
                                H(trans1: trans1);
                            }
                        },
                        suppressEarlierFirings: false);

            IListener l2 =
                this.Value(trans0)
                    .Listen(
                        target: inTarget,
                        trans: trans0,
                        action: (trans1, aa) =>
                        {
                            a = aa;
                            isASet = true;

                            if (f != null)
                            {
                                H(trans1: trans1);
                            }
                        },
                        suppressEarlierFirings: false);

            return @out.LastFiringOnly(trans0)
                .UnsafeAttachListener(l1)
                .UnsafeAttachListener(l2)
                .UnsafeAttachListener(ListenerInternal.CreateFromNodeAndTarget(node: inTarget, target: nodeTarget))
                .HoldLazyInternal(
                    trans: trans0,
                    initialValue: new Lazy<TResult>(() => bf.SampleNoTransaction()(this.SampleNoTransaction())));

            static void NoOp(TransactionInternal _, UnitInternal __)
            {
            }

            // ReSharper disable once PossibleNullReferenceException
            void H(TransactionInternal trans1) =>
                // ReSharper disable once NullableWarningSuppressionIsUsed - Since isASet is checked before H is
                // called and it is only true when a is non-null, a will be non-null here.
                trans1.Prioritized(node: @out.Node, action: trans2 => @out.Send(trans: trans2, a: f(a!)));
        });

    private sealed class LazySample
    {
        internal Behavior<T>? Behavior;
        internal T? Value;

        internal LazySample(Behavior<T> behavior) => this.Behavior = behavior;
    }
}
