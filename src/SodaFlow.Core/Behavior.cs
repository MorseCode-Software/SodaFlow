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
///     Represents a value that changes over time.
/// </summary>
/// <typeparam name="T">The type of values in the behavior.</typeparam>
[PublicAPI]
public class Behavior<T>
{
    // Captures nothing but this behavior, so it is built once rather than on every firing.
    // Only the stream-backed constructor needs it; a constant behavior never updates.
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

        // Assigned before Listen, because listening can replay firings already made in this
        // transaction and so run the handler below before the constructor returns.
        this.applyValueUpdate = this.ApplyValueUpdate;

        this.streamListener =
            TransactionInternal.Apply((trans1, _) =>
                this.stream.Listen(
                    target: Node<T>.Null,
                    trans: trans1,
                    action: (trans2, a) =>
                    {
                        // Deliberately not MatchNone/MatchSome: those take callbacks, and this
                        // runs on every firing of every cell, so the closures they require were
                        // showing up as a large share of the cost of a single Send.
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
    ///     Gets or sets the value this behavior reports when sampled outside a transaction.
    /// </summary>
    /// <value>The behavior's current value.</value>
    /// <remarks>
    ///     Setting this clears <see cref="UsingInitialValue" />, because a behavior that has been
    ///     given a value is no longer relying on the one it was constructed with. Derived types
    ///     should assign through this property rather than the backing field so that flag stays
    ///     accurate.
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
    ///     Gets a value indicating whether this behavior is still reporting the value it was
    ///     constructed with, rather than one it has since been given.
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
    ///     Called when <see cref="ValueProperty" /> is assigned. Derived types override this to
    ///     release anything they were holding only to produce that initial value; see
    ///     <see cref="LoopedBehavior{T}" />, which drops its deferred initial value here so a closed
    ///     loop does not keep it alive.
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
    ///     The stream of this behavior's value: its current value, delivered in this transaction,
    ///     followed by every update.
    /// </summary>
    /// <remarks>
    ///     Both sources feed one output stream directly rather than going through a spark stream, a
    ///     snapshot of it and a merge - four streams where two will do. Value sits underneath
    ///     Cell.ListenStrong, Apply and the switches, so it was a large part of what each of those cost.
    ///     The initial send is queued against a bare node of its own, exactly as the spark stream it
    ///     replaces was, and for the same reason: a fresh node ranks below everything, so the value
    ///     is delivered even when Value is called part-way through a drain. SwitchB does precisely
    ///     that - its handler builds a Value for the newly selected behavior mid-transaction - and
    ///     hanging the initial send off the output node instead makes the switch deliver a stale
    ///     value. The node costs nothing; it is the two intermediate streams that were expensive.
    ///     Coalescing right-wins leaves an update from this transaction in front of the initial
    ///     value, which is what merging with (left, right) =&gt; right used to do.
    /// </remarks>
    internal Stream<T> Value(TransactionInternal trans1)
    {
        Stream<T> @out = new(this.stream.KeepListenersAlive);

        // This will always run first since it has Rank = 0.
        trans1.Prioritized(
            node: new Node<UnitInternal>(),
            action: trans2 => @out.Send(trans: trans2, a: this.SampleNoTransaction()));

        // This listener will queue an action that will always run after the previous one since,
        // even if this.Updates() has Rank = 0, the listener attached to it will have Rank > 0.
        IListener l =
            this.Updates()
                .Listen(
                    target: @out.Node,
                    trans: trans1,
                    action: @out.Send,
                    suppressEarlierFirings: false);

        // The order is assured without having to link the first node and the second node.
        return @out.UnsafeAttachListener(l).Coalesce(trans1: trans1, f: static (_, right) => right);
    }

    internal Behavior<TResult> MapImpl<TResult>(Func<T, TResult> f) =>
        TransactionInternal.Apply((trans, _) =>
            this.Updates()
                .MapImpl(f)
                .HoldLazyInternal(trans: trans, initialValue: this.SampleLazy(trans).MapImpl(f)));

    // Lift is deliberately no longer built out of Apply. Chaining ApplyImpl once per extra
    // input made every input pay for a Value() - a spark stream, a snapshot, a merge and a
    // coalesce - so a six-way lift constructed around fifty streams and cost roughly 87KB.
    // This shape, which the IEnumerable overload in BehaviorExtensionMethods already used,
    // builds three streams whatever the arity: one pulse stream that every input feeds, a
    // coalesce operation collapsing a transaction's updates into a single firing, and a map that
    // recombines the inputs.
    //
    // Each input's new value is captured as it propagates rather than read back off the
    // behavior afterward. A behavior applies its update through a listener on Node<T>.Null,
    // and the priority queue drains null-ranked entries only after every ranked one, so at the
    // point the map below runs the behaviors still hold their previous values. Rank ordering is
    // what makes capturing safe: every input links into pulse.Node, so pulse.Node outranks all
    // of them and each slot is filled before anything downstream of the coalesce can run.

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
    ///     Wires one lifted input to the shared pulse stream, recording its new value on the way
    ///     through so the recombine step does not have to read it back off the behavior.
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
    ///     Reads an input's value for this firing: the value captured on the way through if it
    ///     updated in this transaction, otherwise the behavior's current one.
    /// </summary>
    /// <remarks>
    ///     Clearing the slot afterward is hygiene rather than correctness. A slot left set would
    ///     still give the right answer, because by the time the next transaction reads it the
    ///     behavior has committed that same value - verified by removing the reset and finding no
    ///     test could tell. It is cleared so the closure does not hold a second reference to every
    ///     input's last value for as long as the lifted behavior lives.
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
        // Coalescing means a transaction that updates several inputs produces exactly one
        // firing, with every input's new value already captured.
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
