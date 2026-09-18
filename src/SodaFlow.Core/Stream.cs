using System;
using System.Collections.Generic;
using System.Threading;
using JetBrains.Annotations;

namespace SodaFlow;

internal static class StreamInternal
{
    internal static Stream<T> NeverImpl<T>() => new();
    internal static StreamSink<T> CreateSinkImpl<T>() => new();
    internal static StreamSink<T> CreateSinkImpl<T>(Func<T, T, T> coalesce) => new(coalesce);
}

/// <summary>
///     A stream of discrete firings.
/// </summary>
/// <typeparam name="T">The type of values fired by the stream.</typeparam>
[PublicAPI]
public class Stream<T>
{
    internal readonly IKeepListenersAlive KeepListenersAlive;
    internal readonly Node<T> Node;

    private readonly StreamListenerManager.StreamListeners trackedListeners;

    // SodaFlow allocates the fields below on first use. It creates streams in large
    // numbers. One two-cell Lift builds approximately twenty streams. A stream that is only a
    // middle step in a chain does not send, does not receive a listener, and does not
    // receive a call to AttachListener. Thus eager allocation of all three fields was most of
    // the cost to construct a stream.

    // ReSharper disable once CollectionNeverQueried.Local
    private List<IListener>? attachedListeners;

    // SodaFlow caches this delegate with the firings. A method group conversion allocates a
    // new delegate each time, and Send gives this delegate to trans.Last on the first firing of
    // each transaction.
    private Action? clearFirings;
    private List<T>? firings;

    internal Stream()
        : this(new KeepListenersAliveImplementation())
    {
    }

    internal Stream(IKeepListenersAlive keepListenersAlive)
    {
        this.KeepListenersAlive = keepListenersAlive;
        this.Node = new Node<T>();

        // This is last, so the registry cannot find an incomplete object. The registry holds
        // this stream with a weak reference only. Thus this registration does not keep the
        // stream alive.
        this.trackedListeners = new StreamListenerManager.StreamListeners(this);
    }

    // SodaFlow creates this at the first read, as it does the other fields. It uses CompareExchange
    // and not a null test, because no other lock can stop more than one thread from creating this one.
    //
    // This field is not volatile. Cell.updates does the same lazy read and is volatile. There,
    // volatile makes the publication of an object with fields safe, because a reader must not
    // see the reference before the state of the object. This field holds an empty object with
    // no state, and the program uses it only as a monitor. Thus a reader cannot see an
    // incomplete object. CompareExchange selects the winner, and all callers read that winner.
    //
    // Do not use this pattern for a field that holds state unless you add volatile. That makes
    // an explicit field necessary again, because you cannot make the backing field of the field
    // keyword volatile.
    private object AttachListenerLock
    {
        get
        {
            object? existing = field;

            if (existing != null)
            {
                return existing;
            }

            Interlocked.CompareExchange(
                location1: ref field,
                value: new object(),
                comparand: null);

            return field;
        }
    }

    internal IStrongListener ListenStrongImpl(Action<T> handler)
    {
        IWeakListener innerListener = this.ListenImpl(handler);
        StrongListener? listener = null;

        listener =
            new StrongListener(
                unlisten: () =>
                {
                    innerListener.Unlisten();

                    // ReSharper disable AccessToModifiedClosure
                    if (listener != null)
                    {
                        lock (this.KeepListenersAlive)
                        {
                            this.KeepListenersAlive.StopKeepingListenerAlive(listener);
                        }
                    }
                    // ReSharper restore AccessToModifiedClosure
                },
                listener: innerListener);

        lock (this.KeepListenersAlive)
        {
            this.KeepListenersAlive.KeepListenerAlive(listener);
        }

        return listener;
    }

    internal IWeakListener ListenImpl(Action<T> handler) =>
        this.Listen(target: Node<T>.Null, action: (_, a) => handler(a));

    internal Stream<T> AttachListenerImpl(IListener listener)
    {
        lock (this.AttachListenerLock)
        {
            return this.UnsafeAttachListener(listener);
        }
    }

    internal IStrongListener ListenOnceImpl(Action<T> handler)
    {
        IStrongListener? listener = null;
        bool unlistenEarly = false;

        IStrongListener listenerToReturn =
            this.ListenStrongImpl(a =>
            {
                // ReSharper disable once AccessToModifiedClosure
                IListener? listenerLocal = listener;

                if (listenerLocal == null)
                {
                    unlistenEarly = true;
                }
                else
                {
                    listenerLocal.Unlisten();
                    listener = null;
                }

                handler(a);
            });

        listener = listenerToReturn;

        if (unlistenEarly)
        {
            listenerToReturn.Unlisten();
            listenerToReturn = NoListener.Value;
            listener = null;
        }

        return listenerToReturn;
    }

    internal IWeakListener Listen(Node target, Action<TransactionInternal, T> action) =>
        TransactionInternal.Apply((trans1, _) =>
            this.Listen(target: target, trans: trans1, action: action, suppressEarlierFirings: false));

    internal IWeakListener Listen(
        Node target,
        TransactionInternal trans,
        Action<TransactionInternal, T> action,
        bool suppressEarlierFirings)
    {
        Node<T>.Target nodeTarget = this.Node.Link(trans: trans, action: action, target: target);

        // Copy the firings only when the program will send them again. Before, the program
        // made this copy on each call to listenStrong. That includes the most frequent
        // condition, a stream that did not fire in this transaction.
        if (!suppressEarlierFirings && this.firings is { Count: > 0 })
        {
            // ReSharper disable once LocalVariableHidesMember
            List<T> firings = [.. this.firings];

            trans.Prioritized(
                node: target,
                action: trans2 =>
                {
                    // Send the values that this transaction sent before now. This removes the
                    // sequence dependency between send and listenStrong.
                    foreach (T a in firings)
                    {
                        trans2.InCallback++;

                        try
                        {
                            // A transaction must not change the internal parts of
                            // SodaFlow.
                            action(arg1: trans2, arg2: a);
                        }
                        finally
                        {
                            trans2.InCallback--;
                        }
                    }
                });
        }

        return new ListenerImplementation(stream: this, action: action, target: nodeTarget);
    }

    internal Stream<TResult> MapImpl<TResult>(Func<T, TResult> f)
    {
        Stream<TResult> @out = new(this.KeepListenersAlive);
        IListener l = this.Listen(target: @out.Node, action: (trans2, a) => @out.Send(trans: trans2, a: f(a)));
        return @out.UnsafeAttachListener(l);
    }

    internal Stream<TResult> MapToImpl<TResult>(TResult value) => this.MapImpl(_ => value);

    internal Cell<T> HoldImpl(T initialValue) => new(this.HoldInternal(initialValue));

    internal Behavior<T> HoldInternal(T initialValue) => new(stream: this, initialValue: initialValue);

    internal Cell<T> HoldLazyImpl(Lazy<T> initialValue) =>
        TransactionInternal.Apply((trans, _) =>
            new Cell<T>(this.HoldLazyInternal(trans: trans, initialValue: initialValue)));

    internal Behavior<T> HoldLazyInternal(TransactionInternal trans, Lazy<T> initialValue) =>
        new LazyBehavior<T>(trans: trans, stream: this, lazyInitialValue: initialValue);

    internal Stream<TResult> SnapshotImpl<TResult>(Cell<TResult> c) => this.SnapshotImpl(c.BehaviorImpl);

    internal Stream<TResult> SnapshotImpl<TResult>(Behavior<TResult> b) =>
        this.SnapshotImpl(b: b, f: static (_, a) => a);

    internal Stream<TResult> SnapshotImpl<T1, TResult>(Cell<T1> c, Func<T, T1, TResult> f) =>
        this.SnapshotImpl(b: c.BehaviorImpl, f: f);

    internal Stream<TResult> SnapshotImpl<T1, TResult>(Behavior<T1> b, Func<T, T1, TResult> f)
    {
        Stream<TResult> @out = new(this.KeepListenersAlive);

        IListener l =
            this.Listen(
                target: @out.Node,
                action: (trans2, a) => @out.Send(trans: trans2, a: f(arg1: a, arg2: b.SampleNoTransaction())));

        return @out.UnsafeAttachListener(l);
    }

    internal Stream<TResult> SnapshotImpl<T1, T2, TResult>(
        Cell<T1> c1,
        Cell<T2> c2,
        Func<T, T1, T2, TResult> f) =>
        this.SnapshotImpl(b1: c1.BehaviorImpl, b2: c2.BehaviorImpl, f: f);

    internal Stream<TResult> SnapshotImpl<T1, T2, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Func<T, T1, T2, TResult> f)
    {
        Stream<TResult> @out = new(this.KeepListenersAlive);

        IListener l =
            this.Listen(
                target: @out.Node,
                action: (trans2, a) =>
                    @out.Send(
                        trans: trans2,
                        a: f(arg1: a, arg2: b1.SampleNoTransaction(), arg3: b2.SampleNoTransaction())));

        return @out.UnsafeAttachListener(l);
    }

    internal Stream<TResult> SnapshotImpl<T1, T2, T3, TResult>(
        Cell<T1> c1,
        Cell<T2> c2,
        Cell<T3> c3,
        Func<T, T1, T2, T3, TResult> f) =>
        this.SnapshotImpl(b1: c1.BehaviorImpl, b2: c2.BehaviorImpl, b3: c3.BehaviorImpl, f: f);

    internal Stream<TResult> SnapshotImpl<T1, T2, T3, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Func<T, T1, T2, T3, TResult> f)
    {
        Stream<TResult> @out = new(this.KeepListenersAlive);

        IListener l =
            this.Listen(
                target: @out.Node,
                action: (trans2, a) =>
                    @out.Send(
                        trans: trans2,
                        a: f(
                            arg1: a,
                            arg2: b1.SampleNoTransaction(),
                            arg3: b2.SampleNoTransaction(),
                            arg4: b3.SampleNoTransaction())));

        return @out.UnsafeAttachListener(l);
    }

    internal Stream<TResult> SnapshotImpl<T1, T2, T3, T4, TResult>(
        Cell<T1> c1,
        Cell<T2> c2,
        Cell<T3> c3,
        Cell<T4> c4,
        Func<T, T1, T2, T3, T4, TResult> f) =>
        this.SnapshotImpl(b1: c1.BehaviorImpl, b2: c2.BehaviorImpl, b3: c3.BehaviorImpl, b4: c4.BehaviorImpl, f: f);

    internal Stream<TResult> SnapshotImpl<T1, T2, T3, T4, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Func<T, T1, T2, T3, T4, TResult> f)
    {
        Stream<TResult> @out = new(this.KeepListenersAlive);

        IListener l =
            this.Listen(
                target: @out.Node,
                action: (trans2, a) =>
                    @out.Send(
                        trans: trans2,
                        a: f(
                            arg1: a,
                            arg2: b1.SampleNoTransaction(),
                            arg3: b2.SampleNoTransaction(),
                            arg4: b3.SampleNoTransaction(),
                            arg5: b4.SampleNoTransaction())));

        return @out.UnsafeAttachListener(l);
    }

    internal Stream<T> OrElseImpl(Stream<T> s) => this.MergeImpl(s: s, f: static (left, _) => left);

    private Stream<T> Merge(TransactionInternal trans, Stream<T> s)
    {
        Stream<T> @out = new(this.KeepListenersAlive);
        Node<T> left = new();
        Node<T> right = @out.Node;

        Node<T>.Target nodeTarget =
            left.Link(
                trans: trans,
                action: static (_, _) =>
                {
                },
                target: right);

        Action<TransactionInternal, T> h = @out.Send;
        IListener l1 = this.Listen(target: left, action: h);
        IListener l2 = s.Listen(target: right, action: h);

        return @out.UnsafeAttachListener(l1)
            .UnsafeAttachListener(l2)
            .UnsafeAttachListener(ListenerInternal.CreateFromNodeAndTarget(node: left, target: nodeTarget));
    }

    internal Stream<T> MergeImpl(Stream<T> s, Func<T, T, T> f) =>
        TransactionInternal.Apply((trans, _) => this.Merge(trans: trans, s: s, f: f));

    internal Stream<T> Merge(TransactionInternal trans, Stream<T> s, Func<T, T, T> f) =>
        this.Merge(trans: trans, s: s).Coalesce(trans1: trans, f: f);

    internal Stream<T> Coalesce(TransactionInternal trans1, Func<T, T, T> f)
    {
        Stream<T> @out = new(this.KeepListenersAlive);
        Action<TransactionInternal, T> h = CoalesceHandler.Create(f: f, @out: @out);
        IListener l = this.Listen(target: @out.Node, trans: trans1, action: h, suppressEarlierFirings: false);
        return @out.UnsafeAttachListener(l);
    }

    /// <summary>
    ///     Removes all firings from the output except the last one.
    /// </summary>
    /// <param name="trans">The transaction that supplies the last firing.</param>
    /// <returns>A stream that contains only the last firing from the given transaction.</returns>
    internal Stream<T> LastFiringOnly(TransactionInternal trans) =>
        this.Coalesce(trans1: trans, f: static (_, second) => second);

    internal Stream<T> FilterImpl(Func<T, bool> predicate)
    {
        Stream<T> @out = new(this.KeepListenersAlive);

        IListener l =
            this.Listen(
                target: @out.Node,
                action: (trans2, a) =>
                {
                    if (predicate(a))
                    {
                        @out.Send(trans: trans2, a: a);
                    }
                });

        return @out.UnsafeAttachListener(l);
    }

    internal Stream<T> GateImpl(Cell<bool> c) => this.GateImpl(c.BehaviorImpl);

    internal Stream<T> GateImpl(Behavior<bool> b) =>
        this.SnapshotImpl(b: b, f: static (a, pred) => pred ? MaybeInternal.Some(a) : MaybeInternal<T>.None)
            .FilterSomeInternal();

    internal Stream<T> CalmImpl(Func<T, T, bool> areEqual) =>
        this.Calm(init: new Lazy<MaybeInternal<T>>(static () => MaybeInternal<T>.None), areEqual: areEqual);

    /// <summary>
    ///     Removes a firing that is equal to the last firing that this stream sent.
    /// </summary>
    /// <remarks>
    ///     This method uses CarryState, because it needs that state protocol. The
    ///     protocol keeps the last value that the stream sent, moves it between firings, and
    ///     commits it at the transaction boundary. Before, this method kept its own copy of the
    ///     protocol. A correction to one copy could miss the other copy, and the deferral is
    ///     sufficiently subtle to make that a risk.
    ///     Cost kept the two copies separate. CollectLazyImpl needs a looped stream, a behavior
    ///     to hold the state, a snapshot, two maps, and a filter on the output. That is six
    ///     streams to keep one value. CarryState costs no more, because the emit flag removes
    ///     the firing without a copy. Thus this method is one output stream.
    ///     The state is a MaybeInternal and not a T, because there can be no previous value.
    ///     None is also a correct initial value. Thus CarryState needs its own initialized flag
    ///     and must not read an empty state as "not started".
    /// </remarks>
    internal Stream<T> Calm(Lazy<MaybeInternal<T>> init, Func<T, T, bool> areEqual) =>
        TransactionInternal.Apply((trans1, _) =>
            this.CarryState(
                trans1: trans1,
                initialState: init,
                f: (a, last) =>
                {
                    bool emit = !(last.TryGetValue(out T previous) && areEqual(arg1: previous, arg2: a));

                    // The state carries forward unchanged for a suppressed firing rather than being
                    // cleared, matching what feeding state back through Collect used to give.
                    return (emit, a, emit ? MaybeInternal.Some(a) : last);
                }));

    internal Stream<TReturn> CollectImpl<TState, TReturn>(
        TState initialState,
        Func<T, TState, (TReturn ReturnValue, TState State)> f) =>
        this.CollectLazyImpl(initialState: new Lazy<TState>(() => initialState), f: f);

    internal Stream<TReturn> CollectLazyImpl<TState, TReturn>(
        Lazy<TState> initialState,
        Func<T, TState, (TReturn ReturnValue, TState State)> f) =>
        TransactionInternal.Apply((trans, _) =>
            this.CarryState(
                trans1: trans,
                initialState: initialState,
                f: (a, s) =>
                {
                    (TReturn returnValue, TState state) = f(arg1: a, arg2: s);
                    return (true, returnValue, state);
                }));

    internal Cell<TReturn> AccumImpl<TReturn>(TReturn initialState, Func<T, TReturn, TReturn> f) =>
        this.AccumLazyImpl(initialState: new Lazy<TReturn>(() => initialState), f: f);

    internal Cell<TReturn> AccumLazyImpl<TReturn>(Lazy<TReturn> initialState, Func<T, TReturn, TReturn> f) =>
        TransactionInternal.Apply((trans, _) =>
            this.CarryState(
                    trans1: trans,
                    initialState: initialState,
                    f: (a, s) =>
                    {
                        TReturn next = f(arg1: a, arg2: s);
                        return (true, next, next);
                    })
                .HoldLazyImpl(initialState));

    /// <summary>
    ///     Runs <paramref name="f" /> on each firing and moves state between the firings. It
    ///     sends the result for a firing when <paramref name="f" /> asks for that. Collect and
    ///     Accum always ask and differ only in their use of the output stream. Calm removes the
    ///     firings that it must not send.
    /// </summary>
    /// <remarks>
    ///     Before, FRP primitives made this method: a looped stream to move the state back, a
    ///     behavior to hold the state, a snapshot to read it, and one map for each output. That
    ///     is four streams for Collect and two for Accum, to move one value between firings.
    ///     This method is now one output stream and two fields.
    ///     The behavior supplied those two fields, and the division between them is important.
    ///     A snapshot reads a behavior with SampleNoTransaction. Thus each firing in a
    ///     transaction saw the state from the start of that transaction, and the behavior
    ///     committed the result of the last firing. One field with an update without a copy lets a
    ///     later firing in the same transaction see an earlier one. That is a different fold,
    ///     and the caller function f can find it.
    ///     Failure shows the deferral. A transaction that throws discards its last
    ///     queue. Thus a firing in that transaction does not commit, and the state stays as it
    ///     was before. This is the one difference from a commit without a copy.
    ///     CalmTests.AFailedTransactionDoesNotCommitTheRememberedValue holds this behavior. All
    ///     other tests pass with one of the two methods.
    ///     The emit flag lets Calm use this method. A Maybe result with a filter costs a second
    ///     stream and a node in the rank graph. Calm exists to prevent that cost. A bool in a
    ///     tuple that is already a struct costs one branch, and the processor predicts that
    ///     branch.
    /// </remarks>
    private Stream<TReturn> CarryState<TState, TReturn>(
        TransactionInternal trans1,
        Lazy<TState> initialState,
        Func<T, TState, (bool Emit, TReturn ReturnValue, TState State)> f)
    {
        Stream<TReturn> @out = new(this.KeepListenersAlive);

        TState? committed = default;
        bool committedIsSet = false;
        TState? pending = default;
        bool hasPending = false;

        // SodaFlow forces this in the sample phase and also when a caller reads it. The behavior that
        // this code replaced forced its lazy initial value in that phase, also when nothing
        // fired.
        trans1.Sample(EnsureCommittedIsSet);

        IListener l =
            this.Listen(
                target: @out.Node,
                trans: trans1,
                action: (trans2, a) =>
                {
                    EnsureCommittedIsSet();

                    // ReSharper disable once NullableWarningSuppressionIsUsed - After EnsureCommittedIsSet() is called
                    // committed will be non-null.
                    (bool emit, TReturn returnValue, TState state) = f(arg1: a, arg2: committed!);

                    if (!hasPending)
                    {
                        hasPending = true;

                        trans2.Last(() =>
                        {
                            // ReSharper disable once AccessToModifiedClosure - We want to use the latest value of
                            // pending here.
                            committed = pending;
                            hasPending = false;
                        });
                    }

                    pending = state;

                    if (emit)
                    {
                        @out.Send(trans: trans2, a: returnValue);
                    }
                },
                suppressEarlierFirings: false);

        return @out.UnsafeAttachListener(l);

        void EnsureCommittedIsSet()
        {
            if (!committedIsSet)
            {
                committed = initialState.Value;
                committedIsSet = true;
            }
        }
    }

    internal Stream<T> OnceImpl()
    {
        // This code is long, but it is satisfactory, because it removes the listener.
        Stream<T> @out = new(this.KeepListenersAlive);
        IWeakListener? listener = null;
        bool unlistenEarly = false;

        IWeakListener listenerToReturn =
            this.Listen(
                target: @out.Node,
                action: (trans, a) =>
                {
                    // ReSharper disable AccessToModifiedClosure
                    if (listener != null)
                    {
                        @out.Send(trans: trans, a: a);

                        IWeakListener? listenerLocal = listener;

                        if (listenerLocal == null)
                        {
                            unlistenEarly = true;
                        }
                        else
                        {
                            listenerLocal.Unlisten();
                            listener = null;
                        }
                    }
                    // ReSharper restore AccessToModifiedClosure
                });

        listener = listenerToReturn;

        if (unlistenEarly)
        {
            listenerToReturn.Unlisten();
            listener = null;
            return @out;
        }

        return @out.UnsafeAttachListener(listenerToReturn);
    }

    // This method is not thread-safe. One of these two conditions must be applicable:
    // 1. The caller is in a transaction. In this implementation a transaction locks out all
    //    other threads.
    // 2. The method that created this object does not return it at this time. Thus two threads cannot
    //    share the object.
    internal Stream<T> UnsafeAttachListener(IListener cleanup)
    {
        this.attachedListeners ??= [];
        this.attachedListeners.Add(cleanup);
        this.trackedListeners.AddListener(cleanup.GetListenerWithWeakReference());
        return this;
    }

    internal void Send(TransactionInternal trans, T a)
    {
        if (this.firings == null)
        {
            this.firings = [];
            this.clearFirings = this.firings.Clear;
        }

        if (this.firings.Count < 1)
        {
            // ReSharper disable once NullableWarningSuppressionIsUsed - this.clearFirings will be non-null when
            // this.firings is non-null.
            trans.Last(this.clearFirings!);
        }

        this.firings.Add(a);

        foreach (Node<T>.Target target in this.Node.GetListenersCopy())
        {
            // This uses SendEntry and not a lambda. This code runs for each target of each
            // firing. A closure costs a display class and a delegate, and SodaFlow must
            // allocate the queue entry in all conditions. The entry holds the three values as
            // fields, thus SodaFlow makes only one allocation.
            trans.Prioritized(new SendEntry(stream: this, target: target, value: a));
        }
    }

    private sealed class SendEntry(Stream<T> stream, Node<T>.Target target, T value)
        : TransactionInternal.Entry(target.Node)
    {
        // ReSharper disable once ReplaceWithPrimaryConstructorParameter - This field is needed so action is not
        // captured into a mutable variable.
        private readonly Stream<T> stream = stream;

        // ReSharper disable once ReplaceWithPrimaryConstructorParameter - This field is needed so action is not
        // captured into a mutable variable.
        private readonly Node<T>.Target target = target;

        // ReSharper disable once ReplaceWithPrimaryConstructorParameter - This field is needed so action is not
        // captured into a mutable variable.
        private readonly T value = value;

        public override void Execute(TransactionInternal trans)
        {
            trans.InCallback++;

            try
            {
                // A transaction must not change the internal parts of SodaFlow.
                // Read the weak reference.
                if (this.target.Action.TryGetTarget(out Action<TransactionInternal, T>? action))
                {
                    // The garbage collector did not remove the action, thus call it.
                    if (this.target.IsActivated)
                    {
                        action(arg1: trans, arg2: this.value);
                    }
                }
                else
                {
                    // The garbage collector removed the action, thus remove the target.
                    this.stream.Node.RemoveListener(this.target);
                }
            }
            finally
            {
                trans.InCallback--;
            }
        }
    }

    private sealed class StrongListener(Action unlisten, IListener listener) : IStrongListener
    {
        // ReSharper disable once ReplaceWithPrimaryConstructorParameter - This field is needed so action is not
        // captured into a mutable variable.
        private readonly IListener listener = listener;

        // ReSharper disable once ReplaceWithPrimaryConstructorParameter - This field is needed so action is not
        // captured into a mutable variable.
        private readonly Action unlisten = unlisten;

        public void Unlisten() => this.unlisten();

        public IListenerWithWeakReference GetListenerWithWeakReference() =>
            this.listener.GetListenerWithWeakReference();

        public void Dispose() => this.Unlisten();
    }

    private sealed class ListenerImplementation(
        Stream<T> stream,
        Action<TransactionInternal, T> action,
        Node<T>.Target target)
        : IWeakListener
    {
        // This field keeps the action alive, because the node uses a weak reference.
        // ReSharper disable once UnusedMember.Local
        private readonly Action<TransactionInternal, T> action = action;

        // This field keeps the listener alive while the caller holds the listener. Thus the
        // garbage collector does not remove it.
        // ReSharper disable once UnusedMember.Local
        private readonly Stream<T> stream = stream;

        private readonly WeakListener weakListener = new(node: stream.Node, target: target);

        public void Unlisten() => this.weakListener.Unlisten();

        public IListenerWithWeakReference GetListenerWithWeakReference() => this.weakListener;
    }

    private sealed class WeakListener(Node<T> node, Node<T>.Target target) : IListenerWithWeakReference
    {
        // ReSharper disable once ReplaceWithPrimaryConstructorParameter - This field is needed so action is not
        // captured into a mutable variable.
        private readonly Node<T> node = node;

        // ReSharper disable once ReplaceWithPrimaryConstructorParameter - This field is needed so action is not
        // captured into a mutable variable.
        private readonly Node<T>.Target target = target;

        public void Unlisten() => this.node.Unlink(this.target);
    }

    private sealed class KeepListenersAliveImplementation : IKeepListenersAlive
    {
        // ReSharper disable once CollectionNeverQueried.Local
        private List<IKeepListenersAlive>? childKeepListenersAliveList;

        // SodaFlow makes one of these for each root stream, and many streams get no
        // listener. Thus the two collections wait until the code needs them.
        // ReSharper disable once CollectionNeverQueried.Local
        private HashSet<IListener>? listeners;

        public void KeepListenerAlive(IListener listener)
        {
            this.listeners ??= [];
            this.listeners.Add(listener);
        }

        public void StopKeepingListenerAlive(IListener listener) => this.listeners?.Remove(listener);

        public void Use(IKeepListenersAlive childKeepListenersAlive)
        {
            this.childKeepListenersAliveList ??= [];
            this.childKeepListenersAliveList.Add(childKeepListenersAlive);
        }
    }
}
