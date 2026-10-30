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

            // The listener on the inner behavior, from the last commit. The handler reads it, and
            // the commit of a switch writes it. MutableListener has no getter, thus this closure
            // keeps its own reference.
            IListener? inner = null;

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
                // ReSharper disable once AccessToModifiedClosure - The handler reads the listener
                // that the last commit set.
                IListener? previous = inner;

                IListener next =
                    ba.Value(trans2)
                        .Listen(
                            target: @out.Node,
                            trans: trans2,
                            action: @out.Send,
                            suppressEarlierFirings: false);

                trans2.Last(
                    commit: () =>
                    {
                        previous?.Unlisten();
                        inner = next;
                        currentListener.SetListenerImpl(next);
                    },
                    discard: next.Unlisten);
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

            void HInitial(TransactionInternal trans2, Stream<T> sa)
            {
                IListener cl = currentListener;
                cl.Unlisten();

                currentListener.SetListenerImpl(
                    sa.Listen(target: @out.Node, trans: trans2, action: @out.Send, suppressEarlierFirings: false));
            }

            void H(TransactionInternal trans2, Stream<T> sa) =>
                trans2.Last(() =>
                {
                    IListener cl = currentListener;
                    cl.Unlisten();

                    currentListener.SetListenerImpl(
                        sa.Listen(
                            target: @out.Node,
                            trans: trans2,
                            action: @out.Send,
                            suppressEarlierFirings: true));
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
            Stream<Action<T[]>> @out =
                new(new FanOutKeepListenersAlive(b.Select(static behavior => behavior.KeepListenersAlive)));

            LazySample<TResult> initialValue =
                new(_ => f([.. b.Select(static behavior => behavior.SampleNoTransaction())]));

            IReadOnlyList<IListener> listeners =
            [
                .. b.Select((behavior, i) =>
                    behavior.Updates()
                        .Listen(
                            target: @out.Node,
                            trans: trans1,
                            action: (trans2, v) => @out.Send(trans: trans2, a: vv => vv[i] = v),
                            suppressEarlierFirings: false))
            ];

            return @out.Coalesce(trans1: trans1, f: static (x, y) => x + y)
                .MapImpl(a =>
                {
                    T[] values = [.. b.Select(static behavior => behavior.SampleNoTransaction())];
                    a(values);
                    return f(values);
                })
                .UnsafeAttachListener(ListenerInternal.CreateCompositeImpl(listeners))
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
