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
                new Behavior<T>(
                    trans: trans,
                    derivation: BehaviorSwitch<T, T2>.Create(trans: trans, outer: bba, select: static ba => ba)),
            state: bba);

    internal static Cell<T> SwitchCImpl<T, T2>(this Behavior<T2> bca)
        where T2 : Cell<T> =>
        TransactionInternal.Apply(
            code: static (trans, _, bca) =>
                new Cell<T>(
                    trans: trans,
                    derivation: BehaviorSwitch<T, T2>.Create(trans: trans, outer: bca, select: static c => c)),
            state: bca);

    internal static Stream<T> SwitchSImpl<T, T2>(this Behavior<T2> bsa)
        where T2 : Stream<T> =>
        TransactionInternal.Apply(
            code: static (trans1, _, state) => SwitchS<T, T2>(trans1: trans1, bsa: state),
            state: bsa);

    private static Stream<T> SwitchS<T, T2>(TransactionInternal trans1, Behavior<T2> bsa)
        where T2 : Stream<T>
    {
        SwitchKeepListenersAlive keepListenersAlive = new(bsa.KeepListenersAlive);

        // A demand node. The inner link is a mutable listener, which the node cannot link again.
        // Thus, the action owns the node, and selects again when the node links again.
        Stream<T> @out = new(keepListenersAlive: keepListenersAlive, node: new DemandStreamNode<T>());
        SwitchSAction<T, T2> action = new(@out: @out, outer: bsa, keepListenersAlive: keepListenersAlive);

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

        IListenerInternal l1 =
            bsa.Updates().Listen(target: new Node<T>(), trans: trans1, action: action, suppressEarlierFirings: false);

        return @out.UnsafeAttachListener(l1).UnsafeAttachListener(action.Current);
    }

    // The action of the outer link of a switch of streams. It holds the output, the mutable
    // listener that holds the inner link, and the keep-alive set of the switch.
    private sealed class SwitchSAction<T, T2>(
        in Stream<T> @out,
        in Behavior<T2> outer,
        in SwitchKeepListenersAlive keepListenersAlive)
        : StreamAction<T2>, IDemandOwner
        where T2 : Stream<T>
    {
        private readonly SwitchKeepListenersAlive keepListenersAlive = keepListenersAlive;
        private readonly Stream<T> @out = @out;
        private readonly Behavior<T2> outer = outer;

        internal MutableListener Current { get; } = new();

        // An unlink in the last phase sometimes comes before this selection. A selection after the
        // unlink links an inner stream to a switch with no listener, and nothing unlinks it after
        // that. Thus, the selection stops here, and the next link of the switch selects from the
        // outer behavior.
        public override void Fire(TransactionInternal trans, T2 value) =>
            trans.Last(() =>
            {
                if (this.@out.Node is DemandStreamNode<T> { IsUnlinked: true })
                {
                    return;
                }

                this.Select(trans: trans, sa: value, suppressEarlierFirings: true);
            });

        // The switch selects from the value of the outer behavior immediately, and not at rank zero
        // as the first selection does. A transaction that fails discards a queued step, but it keeps
        // the listener that linked the switch again. The firings of the inner stream in this
        // transaction come in a queued replay, thus no value goes to the output two times.
        void IDemandOwner.Relinking(TransactionInternal trans) =>
            this.Select(trans: trans, sa: this.outer.SampleNoTransaction(), suppressEarlierFirings: false);

        // A stream holds no value, thus nothing goes stale.
        void IDemandOwner.Unlinked(TransactionInternal trans)
        {
        }

        // Each set stops the previous listener, which the mutable listener holds. The keep-alive
        // set moves its listeners to the root of the new stream at the same time.
        internal void Select(TransactionInternal trans, Stream<T> sa, bool suppressEarlierFirings)
        {
            this.Current.SetListener(
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
            code: static (trans1, _, state) =>
                LiftBehaviors(trans1: trans1, inputs: state.Inputs, f: state.F).Hold(trans1),
            state: (Inputs: inputs, F: f));

    // The lift of a list is a derivation, as the lift of a fixed number of inputs is.
    // LiftBehaviorsImpl holds it in a behavior, and LiftCellsImpl holds it in a cell. Before, the
    // lift was a stream, and a hold kept its value. A hold cannot unlink, because its value then
    // becomes stale.
    internal static LiftOutput<TResult> LiftBehaviors<T, T2, TResult>(
        TransactionInternal trans1,
        T2[] inputs,
        Func<IReadOnlyList<T>, TResult> f)
        where T2 : Behavior<T>
    {
        IKeepListenersAlive[] roots = [.. inputs.Select(static behavior => behavior.KeepListenersAlive)];
        ListLift<T, T2, TResult> lift = new(keepListenersAlive: KeepListenersAliveInternal.Combine(roots), inputs: inputs, f: f);
        lift.Link(trans1);

        return lift;
    }

    // The mechanism of Behavior.LiftImpl, with a slot for each input in an array. Before, each
    // update sent a closure that wrote its index, a coalesce operation added the closures into one
    // delegate, and a map ran it. A transaction that updated k inputs allocated about 2k delegates.
    // This object is the output, the slots, and the gate, and the source of the initial value.
    private sealed class ListLift<T, T2, TResult>(
        in IKeepListenersAlive keepListenersAlive,
        in T2[] inputs,
        in Func<IReadOnlyList<T>, TResult> f)
        : LiftOutput<TResult>(keepListenersAlive)
        where T2 : Behavior<T>
    {
        // The link of an input holds its action only through a weak reference. Thus, the lift holds
        // the action of each input, as a lift of a fixed number of inputs holds each capture.
        // ReSharper disable once CollectionNeverQueried.Local - The array holds the actions alive, because each link holds its action only weakly.
        private readonly LiftInput<T, T2, TResult>[] actions = new LiftInput<T, T2, TResult>[inputs.Length];

        private readonly Func<IReadOnlyList<T>, TResult> f = f;
        private readonly T2[] inputs = inputs;
        private readonly CaptureSlot<T>[] slots = new CaptureSlot<T>[inputs.Length];

        internal void Link(TransactionInternal trans)
        {
            for (int i = 0; i < this.inputs.Length; i++)
            {
                LiftInput<T, T2, TResult> action = new(lift: this, index: i);
                this.actions[i] = action;

                this.Attach(
                    this.inputs[i]
                        .Updates()
                        .Listen(target: this.Node, trans: trans, action: action, suppressEarlierFirings: false));
            }
        }

        internal void Capture(TransactionInternal trans, int index, T value)
        {
            this.slots[index].Set(trans: trans, value: value);
            this.Arrived(trans);
        }

        private protected override long SourceVersion
        {
            get
            {
                long version = 0;

                // ReSharper disable once LoopCanBeConvertedToQuery - Each read of an unlinked lift comes here, and on .NET Framework the query allocates an enumerator.
                foreach (T2 input in this.inputs)
                {
                    version = Math.Max(val1: version, val2: input.Version);
                }

                return version;
            }
        }

        private protected override bool SourcesHavePending
        {
            get
            {
                // ReSharper disable once LoopCanBeConvertedToQuery - Each read of an unlinked lift comes here, and on .NET Framework the query allocates an enumerator.
                foreach (T2 input in this.inputs)
                {
                    if (input.HasPendingUpdate)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private protected override TResult Recombine(TransactionInternal trans)
        {
            T[] values = new T[this.inputs.Length];

            for (int i = 0; i < this.inputs.Length; i++)
            {
                values[i] = Behavior<T>.Take(pending: ref this.slots[i], input: this.inputs[i], trans: trans);
            }

            return this.f(values);
        }

        private protected override TResult Initial() =>
            this.f([.. this.inputs.Select(static behavior => behavior.SampleNoTransaction())]);

        private protected override TResult PendingValue() =>
            this.f([.. this.inputs.Select(static behavior => behavior.PendingOrCurrent())]);
    }

    // The action of one input of the list lift: it fills the slot at its index and calls the
    // gate. Before, a lambda captured the index, the slots, and the gate.
    // It is also the owner that the demand node finds through its first link, as a capture is.
    private sealed class LiftInput<T, T2, TResult>(in ListLift<T, T2, TResult> lift, in int index)
        : StreamAction<T>, IDemandOwner
        where T2 : Behavior<T>
    {
        private readonly int index = index;
        private readonly ListLift<T, T2, TResult> lift = lift;

        void IDemandOwner.Relinking(TransactionInternal trans) => ((IDemandOwner)this.lift).Relinking(trans);

        void IDemandOwner.Unlinked(TransactionInternal trans) => ((IDemandOwner)this.lift).Unlinked(trans);

        public override void Fire(TransactionInternal trans, T value) =>
            this.lift.Capture(trans: trans, index: this.index, value: value);
    }
}
