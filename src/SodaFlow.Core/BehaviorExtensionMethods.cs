using System;
using System.Collections.Generic;
using System.Linq;

namespace SodaFlow;

internal static class BehaviorExtensionMethodsInternal
{
    internal static Behavior<T> SwitchBImpl<T, T2>(this Behavior<T2> bba)
        where T2 : Behavior<T> =>
        TransactionInternal.Apply((trans, _) =>
            BehaviorSwitch<T, T2>.Create(
                trans: trans,
                outer: bba,
                select: static ba => ba,
                sampleInitial: static ba => ba.SampleImpl()));

    internal static Cell<T> SwitchCImpl<T, T2>(this Behavior<T2> bca)
        where T2 : Cell<T> =>
        new(
            TransactionInternal.Apply((trans, _) =>
                BehaviorSwitch<T, T2>.Create(
                    trans: trans,
                    outer: bca,
                    select: static c => c.BehaviorImpl,
                    sampleInitial: static c => c.SampleImpl())));

    internal static Stream<T> SwitchSImpl<T, T2>(this Behavior<T2> bsa)
        where T2 : Stream<T> =>
        TransactionInternal.Apply((trans1, _) =>
        {
            SwitchKeepListenersAlive keepListenersAlive = new(bsa.KeepListenersAlive);
            Stream<T> @out = new(keepListenersAlive);
            MutableListener currentListener = new();

            trans1.Prioritized(
                node: new Node<T>(),
                action: trans2 => HInitial(trans2: trans2, sa: bsa.SampleNoTransaction()));

            IListener l1 =
                bsa.Updates()
                    .Listen(target: new Node<T>(), trans: trans1, action: H, suppressEarlierFirings: false);

            return @out.UnsafeAttachListener(l1).UnsafeAttachListener(currentListener);

            // Each set stops the previous listener, which the mutable listener holds. The keep-alive
            // set moves its listeners to the root of the new stream at the same time.
            void HInitial(TransactionInternal trans2, Stream<T> sa)
            {
                currentListener.SetListenerImpl(
                    sa.Listen(target: @out.Node, trans: trans2, action: @out.Send, suppressEarlierFirings: false));

                keepListenersAlive.SetInner(sa.KeepListenersAlive);
            }

            void H(TransactionInternal trans2, Stream<T> sa) =>
                trans2.Last(() =>
                {
                    currentListener.SetListenerImpl(
                        sa.Listen(
                            target: @out.Node,
                            trans: trans2,
                            action: @out.Send,
                            suppressEarlierFirings: true));

                    keepListenersAlive.SetInner(sa.KeepListenersAlive);
                });
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

            IKeepListenersAlive[] roots = [.. inputs.Select(static behavior => behavior.KeepListenersAlive)];
            Stream<TResult> @out = new(KeepListenersAliveInternal.Combine(roots));

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
}
