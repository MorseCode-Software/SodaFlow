using System;
using System.Collections.Generic;
using System.Linq;

namespace SodaFlow;

internal static class BehaviorExtensionMethodsInternal
{
    internal static Behavior<T> SwitchBImpl<T, T2>(this Behavior<T2> bba)
        where T2 : Behavior<T> =>
        TransactionInternal.Apply(
            code: static (trans, _, bba) =>
                BehaviorSwitch<T, T2>.Create(
                    trans: trans,
                    outer: bba,
                    select: static ba => ba,
                    sampleInitial: static ba => ba.SampleImpl()),
            state: bba);

    internal static Cell<T> SwitchCImpl<T, T2>(this Behavior<T2> bca)
        where T2 : Cell<T> =>
        new(
            TransactionInternal.Apply(
                code: static (trans, _, bca) =>
                    BehaviorSwitch<T, T2>.Create(
                        trans: trans,
                        outer: bca,
                        select: static c => c.BehaviorImpl,
                        sampleInitial: static c => c.SampleImpl()),
                state: bca));

    internal static Stream<T> SwitchSImpl<T, T2>(this Behavior<T2> bsa)
        where T2 : Stream<T> =>
        TransactionInternal.Apply(
            code: static (trans1, _, state) => SwitchS<T, T2>(trans1: trans1, bsa: state),
            state: bsa);

    private static Stream<T> SwitchS<T, T2>(TransactionInternal trans1, Behavior<T2> bsa)
        where T2 : Stream<T>
    {
        SwitchKeepListenersAlive keepListenersAlive = new(bsa.KeepListenersAlive);
        Stream<T> @out = new(keepListenersAlive);
        SwitchSAction<T, T2> action = new(@out: @out, keepListenersAlive: keepListenersAlive);

        // The first selection queues on the shared rank-zero node, and the outer link gets a new
        // node. The output node, which SwitchB links its outer behavior to, is not one of them.
        // The action does its work in trans2.Last, after each prioritized action of the transaction.
        // Thus, the outer update needs no rank below the output, and the first selection samples
        // the outer behavior out of the rank order too. A link from the outer
        // behavior to the output node puts the output in the rank graph of its own selector. A
        // selector that holds the output through a loop is a legal graph. The hold breaks the
        // dependency in time and not in the links. With that link, TestCellLoopSwitchS throws
        // DependencyCycleException. SwitchB reads the new inner behavior in the same prioritized
        // step as the outer update, thus its rank is necessary.
        trans1.PrioritizedAtRankZero(trans2 =>
            action.Select(trans: trans2, sa: bsa.SampleNoTransaction(), suppressEarlierFirings: false));

        IListener l1 =
            bsa.Updates().Listen(target: new Node<T>(), trans: trans1, action: action, suppressEarlierFirings: false);

        return @out.UnsafeAttachListener(l1).UnsafeAttachListener(action.Current);
    }

    // The action of the outer link of a switch of streams. It holds the output, the mutable
    // listener that holds the inner link, and the keep-alive set of the switch.
    private sealed class SwitchSAction<T, T2>(in Stream<T> @out, in SwitchKeepListenersAlive keepListenersAlive)
        : StreamAction<T2>
        where T2 : Stream<T>
    {
        private readonly SwitchKeepListenersAlive keepListenersAlive = keepListenersAlive;
        private readonly Stream<T> @out = @out;

        internal MutableListener Current { get; } = new();

        public override void Fire(TransactionInternal trans, T2 value) =>
            trans.Last(() => this.Select(trans: trans, sa: value, suppressEarlierFirings: true));

        // Each set stops the previous listener, which the mutable listener holds. The keep-alive
        // set moves its listeners to the root of the new stream at the same time.
        internal void Select(TransactionInternal trans, Stream<T> sa, bool suppressEarlierFirings)
        {
            this.Current.SetListenerImpl(
                sa.Listen(
                    target: this.@out.Node,
                    trans: trans,
                    action: new SendInto<T>(this.@out),
                    suppressEarlierFirings: suppressEarlierFirings));

            this.keepListenersAlive.SetInner(sa.KeepListenersAlive);
        }
    }

    internal static Behavior<TResult> LiftBehaviorsImpl<T, T2, TResult>(
        this IEnumerable<T2> b,
        Func<IReadOnlyList<T>, TResult> f)
        where T2 : Behavior<T> =>
        LiftBehaviorsImpl(inputs: [.. b], f: f);

    internal static Behavior<TResult> LiftBehaviorsImpl<T, T2, TResult>(
        this IReadOnlyCollection<T2> b,
        Func<IReadOnlyList<T>, TResult> f)
        where T2 : Behavior<T> =>
        LiftBehaviorsImpl(inputs: [.. b], f: f);

    // The array is the lift's own copy. A caller gives it through one of the overloads above,
    // which copy one time. Before, the enumerable overload copied to an array, and this method
    // copied that array again. This is not an extension method. Thus, a caller can give a
    // collection expression, and a call on an array does not go to the collection overload.
    internal static Behavior<TResult> LiftBehaviorsImpl<T, T2, TResult>(
        T2[] inputs,
        Func<IReadOnlyList<T>, TResult> f)
        where T2 : Behavior<T> =>
        TransactionInternal.Apply(
            code: static (trans1, _, state) => LiftBehaviors(trans1: trans1, inputs: state.Inputs, f: state.F),
            state: (Inputs: inputs, F: f));

    // ReSharper disable once SuggestBaseTypeForParameter - CA1859 asks for the array, which the one caller makes, and the indexer and Length of an array cost less than the interface.
    private static Behavior<TResult> LiftBehaviors<T, T2, TResult>(
        TransactionInternal trans1,
        T2[] inputs,
        Func<IReadOnlyList<T>, TResult> f)
        where T2 : Behavior<T>
    {
        IKeepListenersAlive[] roots = [.. inputs.Select(static behavior => behavior.KeepListenersAlive)];

        LazySample<TResult> initialValue =
            new(_ => f([.. inputs.Select(static behavior => behavior.SampleNoTransaction())]));

        ListLift<T, T2, TResult> lift =
            new(@out: new Stream<TResult>(KeepListenersAliveInternal.Combine(roots)), inputs: inputs, f: f);

        IListener[] listeners = new IListener[inputs.Length];

        for (int i = 0; i < inputs.Length; i++)
        {
            listeners[i] =
                inputs[i]
                    .Updates()
                    .Listen(
                        target: lift.Node,
                        trans: trans1,
                        action: new LiftInput<T, T2, TResult>(lift: lift, index: i),
                        suppressEarlierFirings: false);
        }

        return lift.Out.UnsafeAttachListener(ListenerInternal.CreateCompositeImpl(listeners))
            .HoldLazyInternal(trans: trans1, initialValue: initialValue);
    }

    // The mechanism of Behavior.LiftImpl, with a slot for each input in an array. Before, each
    // update sent a closure that wrote its index, a coalesce operation added the closures into one
    // delegate, and a map ran it. A transaction that updated k inputs allocated about 2k delegates.
    // This object is the output, the slots, and the gate.
    private sealed class ListLift<T, T2, TResult>(
        in Stream<TResult> @out,
        in T2[] inputs,
        in Func<IReadOnlyList<T>, TResult> f)
        : OnceGate
        where T2 : Behavior<T>
    {
        internal readonly Stream<TResult> Out = @out;
        private readonly Func<IReadOnlyList<T>, TResult> f = f;
        private readonly T2[] inputs = inputs;
        private readonly CaptureSlot<T>[] slots = new CaptureSlot<T>[inputs.Length];

        internal override Node Node => this.Out.Node;

        internal void Capture(TransactionInternal trans, int index, T value)
        {
            this.slots[index].Set(trans: trans, value: value);
            this.Arrived(trans);
        }

        private protected override void Run(TransactionInternal trans)
        {
            T[] values = new T[this.inputs.Length];

            for (int i = 0; i < this.inputs.Length; i++)
            {
                values[i] = Behavior<T>.Take(pending: ref this.slots[i], input: this.inputs[i], trans: trans);
            }

            this.Out.Send(trans: trans, a: this.f(values));
        }
    }

    // The action of one input of the list lift: it fills the slot at its index and calls the
    // gate. Before, a lambda captured the index, the slots, and the gate.
    private sealed class LiftInput<T, T2, TResult>(in ListLift<T, T2, TResult> lift, in int index) : StreamAction<T>
        where T2 : Behavior<T>
    {
        private readonly int index = index;
        private readonly ListLift<T, T2, TResult> lift = lift;

        public override void Fire(TransactionInternal trans, T value) =>
            this.lift.Capture(trans: trans, index: this.index, value: value);
    }
}
