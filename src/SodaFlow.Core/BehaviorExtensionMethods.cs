using System;
using System.Collections.Generic;
using System.Linq;

namespace SodaFlow;

internal static class BehaviorExtensionMethodsInternal
{
    internal static Behavior<T> SwitchBImpl<T, T2>(this Behavior<T2> bba)
        where T2 : Behavior<T> =>
        TransactionInternal.Apply((trans1, _) =>
        {
            LazySample<T> za = bba.SampleLazyImpl().MapImpl(static ba => ba.SampleImpl());
            Stream<T> @out = new(bba.KeepListenersAlive);
            MutableListener currentListener = new();

            IListener l1 =
                bba.Value(trans1)
                    .Listen(target: @out.Node, trans: trans1, action: H, suppressEarlierFirings: false);

            return @out.UnsafeAttachListener(l1)
                .UnsafeAttachListener(currentListener)
                .HoldLazyInternal(trans: trans1, initialValue: za);

            void H(TransactionInternal trans2, Behavior<T> ba)
            {
                // The new behavior gets its listener now, thus its value goes to the output in
                // this transaction. The previous listener stays until the commit. A transaction
                // that fails discards the update of the outer behavior. Thus, it must discard the
                // switch too: the discard removes the new listener and keeps the previous one.
                //
                // The previous listener cannot send a stale value after this point. Its update
                // chain links into the output node, thus it has a lower rank, and the drain ran it
                // before this handler. A dependency of an inner behavior on the output is a cycle,
                // which Link refuses. FailedTransactionTests holds the two cases.
                IListener next =
                    ba.Value(trans2)
                        .Listen(
                            target: @out.Node,
                            trans: trans2,
                            action: @out.Send,
                            suppressEarlierFirings: false);

                // The set stops the previous listener, which the mutable listener holds.
                trans2.Last(commit: () => currentListener.SetListenerImpl(next), discard: next.Unlisten);
            }
        });

    internal static Cell<T> SwitchCImpl<T, T2>(this Behavior<T2> bca)
        where T2 : Cell<T> =>
        new(bca.MapImpl(static c => c.BehaviorImpl).SwitchBImpl<T, Behavior<T>>());

    internal static Stream<T> SwitchSImpl<T, T2>(this Behavior<T2> bsa)
        where T2 : Stream<T> =>
        TransactionInternal.Apply((trans1, _) =>
        {
            Stream<T> @out = new(bsa.KeepListenersAlive);
            MutableListener currentListener = new();

            trans1.Prioritized(
                node: new Node<T>(),
                action: trans2 => HInitial(trans2: trans2, sa: bsa.SampleNoTransaction()));

            IListener l1 =
                bsa.Updates()
                    .Listen(target: new Node<T>(), trans: trans1, action: H, suppressEarlierFirings: false);

            return @out.UnsafeAttachListener(l1).UnsafeAttachListener(currentListener);

            // Each set stops the previous listener, which the mutable listener holds.
            void HInitial(TransactionInternal trans2, Stream<T> sa) =>
                currentListener.SetListenerImpl(
                    sa.Listen(target: @out.Node, trans: trans2, action: @out.Send, suppressEarlierFirings: false));

            void H(TransactionInternal trans2, Stream<T> sa) =>
                trans2.Last(() =>
                    currentListener.SetListenerImpl(
                        sa.Listen(
                            target: @out.Node,
                            trans: trans2,
                            action: @out.Send,
                            suppressEarlierFirings: true)));
        });

    internal static Behavior<TResult> LiftBehaviorsImpl<T, T2, TResult>(
        this IEnumerable<T2> b,
        Func<IReadOnlyList<T>, TResult> f)
        where T2 : Behavior<T> =>
        b.ToArray().LiftBehaviorsImpl(f);

    internal static Behavior<TResult> LiftBehaviorsImpl<T, T2, TResult>(
        this IReadOnlyCollection<T2> b,
        Func<IReadOnlyList<T>, TResult> f)
        where T2 : Behavior<T> =>
        TransactionInternal.Apply((trans1, _) =>
        {
            T2[] inputs = [.. b];

            Stream<TResult> @out =
                new(new FanOutKeepListenersAlive(inputs.Select(static behavior => behavior.KeepListenersAlive)));

            LazySample<TResult> initialValue =
                new(_ => f([.. inputs.Select(static behavior => behavior.SampleNoTransaction())]));

            // The mechanism of Behavior.LiftImpl, with a slot for each input in an array. Before,
            // each update sent a closure that wrote its index, a coalesce operation added the
            // closures into one delegate, and a map ran it. A transaction that updated k inputs
            // allocated about 2k delegates.
            CaptureSlot<T>[] slots = new CaptureSlot<T>[inputs.Length];

            OnceGate gate =
                new(
                    node: @out.Node,
                    action: trans2 =>
                    {
                        T[] values = new T[inputs.Length];

                        for (int i = 0; i < inputs.Length; i++)
                        {
                            values[i] = Behavior<T>.Take(pending: ref slots[i], input: inputs[i], trans: trans2);
                        }

                        @out.Send(trans: trans2, a: f(values));
                    });

            IListener[] listeners = new IListener[inputs.Length];

            for (int i = 0; i < inputs.Length; i++)
            {
                int index = i;

                listeners[index] =
                    inputs[index]
                        .Updates()
                        .Listen(
                            target: gate.Node,
                            trans: trans1,
                            action: (trans2, v) =>
                            {
                                slots[index].Set(trans: trans2, value: v);
                                gate.Arrived(trans2);
                            },
                            suppressEarlierFirings: false);
            }

            return @out.UnsafeAttachListener(ListenerInternal.CreateCompositeImpl(listeners))
                .HoldLazyInternal(trans: trans1, initialValue: initialValue);
        });

    private sealed class FanOutKeepListenersAlive(IEnumerable<IKeepListenersAlive> keepListenersAliveEnumerable)
        : IKeepListenersAlive
    {
        private readonly IReadOnlyList<IKeepListenersAlive> keepListenersAliveList = [.. keepListenersAliveEnumerable];

        public void KeepListenerAlive(IListener listener)
        {
            foreach (IKeepListenersAlive keepListenersAlive in this.keepListenersAliveList)
            {
                keepListenersAlive.KeepListenerAlive(listener);
            }
        }

        public void StopKeepingListenerAlive(IListener listener)
        {
            foreach (IKeepListenersAlive keepListenersAlive in this.keepListenersAliveList)
            {
                keepListenersAlive.StopKeepingListenerAlive(listener);
            }
        }

        public void Use(IKeepListenersAlive childKeepListenersAlive)
        {
            foreach (IKeepListenersAlive keepListenersAlive in this.keepListenersAliveList)
            {
                keepListenersAlive.Use(childKeepListenersAlive);
            }
        }
    }
}
