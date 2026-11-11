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

    /// <summary>
    ///     Connects one input stream to a shared gate. Each firing of the input
    ///     fills the slot of <paramref name="capture" /> and then calls the gate.
    ///     Thus, the step that puts the inputs together reads the captured value,
    ///     and not the value of a behavior.
    /// </summary>
    /// <remarks>
    ///     LiftOutput, SnapshotLatestImpl, and MergeMaybesImpl use this. For a
    ///     behavior, give the stream of its updates.
    /// </remarks>
    internal static IListenerInternal Capture<TInput>(
        Stream<TInput> input,
        OnceGate gate,
        TransactionInternal trans,
        InputCapture<TInput> capture) =>
        input.Listen(target: gate.Node, trans: trans, action: capture.Arm(gate), suppressEarlierFirings: false);

    /// <summary>
    ///     Calls an action with a value. The target of a link holds a delegate or
    ///     an <see cref="IStreamAction{T}" />, and this tests which.
    /// </summary>
    internal static void Fire<T>(object action, TransactionInternal trans, T value)
    {
        if (action is IStreamAction<T> streamAction)
        {
            streamAction.Fire(trans: trans, value: value);
        }
        else
        {
            ((Action<TransactionInternal, T>)action)(arg1: trans, arg2: value);
        }
    }
}

/// <summary>
///     A stream of discrete firings.
/// </summary>
/// <typeparam name="T">The type of values fired by the stream.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage - The type documents itself, and the interface is internal.
public class Stream<T> : ILastAction
{
    internal readonly IKeepListenersAlive KeepListenersAlive;
    internal readonly StreamNode<T> Node;

    // SodaFlow allocates the fields below on first use. It creates streams in large
    // numbers. One two-cell Lift builds approximately twenty streams. A stream that is only a
    // middle step in a chain does not send, does not receive a listener, and does not
    // receive a call to AttachListenerInternal. Thus, eager allocation of all three fields was
    // most of the cost to construct a stream.

    // The first attached listener sits in a field, because a stream in a chain has exactly one.
    // The list is for the second and later.
    // ReSharper disable once NotAccessedField.Local
    private IListenerInternal? attachedListener;

    // ReSharper disable once CollectionNeverQueried.Local
    private List<IListenerInternal>? attachedListeners;

    // SodaFlow caches this delegate with the firings. A method group conversion allocates a
    // new delegate each time, and Send gives this delegate to trans.Last on the first firing of
    // each transaction.
    private List<T>? firings;

    internal Stream()
        : this(new KeepListenersAliveImplementation())
    {
    }

    internal Stream(IKeepListenersAlive keepListenersAlive)
    {
        this.KeepListenersAlive = keepListenersAlive;
        this.Node = new StreamNode<T>();
    }

    // The stream of the updates of a derived behavior, on the node that the inputs of the behavior
    // already link into. See IDerivation.
    internal Stream(IKeepListenersAlive keepListenersAlive, StreamNode<T> node)
    {
        this.KeepListenersAlive = keepListenersAlive;
        this.Node = node;
    }

    // SodaFlow creates this at the first read, as it does the other fields. It uses CompareExchange
    // and not a null test, because no other lock can stop more than one thread from creating this one.
    //
    // This field is not volatile. Cell.updates does the same lazy read and is volatile. There,
    // volatile makes the publication of an object with fields safe, because a reader must not
    // see the reference before the state of the object. This field holds an empty object with
    // no state, and the program uses it only as a monitor. Thus, a reader cannot see an
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

    internal IStrongListener ListenStrongImpl(Action<T> handler) => this.KeepAlive(this.ListenImpl(handler));

    internal IStrongListener ListenStrongImpl(TransactionInternal trans, Action<T> handler) =>
        this.KeepAlive(this.ListenImpl(trans: trans, handler: handler));

    // The handler goes behind one object, which is the action and the listener. Before, a
    // closure, a delegate, and a listener object stood between the node and the handler.
    internal IWeakListenerInternal ListenImpl(Action<T> handler) =>
        this.Listen(target: Node<T>.Null, action: new HandlerAction<T>(handler));

    // For a caller that holds the transaction, such as a cell's Listen, which makes the value
    // stream and listens to it in one.
    internal IWeakListenerInternal ListenImpl(TransactionInternal trans, Action<T> handler) =>
        this.Listen(
            target: Node<T>.Null,
            trans: trans,
            action: new HandlerAction<T>(handler),
            suppressEarlierFirings: false);

    internal Stream<T> AttachListenerInternal(IListenerInternal listener)
    {
        lock (this.AttachListenerLock)
        {
            return this.UnsafeAttachListener(listener);
        }
    }

    internal IWeakListener ListenOnceImpl(Action<T> handler) =>
        this.ListenOnce(
            listen: static (stream, trans, action) =>
                stream.Listen(target: Node<T>.Null, trans: trans, action: action, suppressEarlierFirings: false),
            handler: handler);

    internal IStrongListener ListenOnceStrongImpl(Action<T> handler) =>
        this.ListenOnce(
            listen: static (stream, trans, action) =>
                stream.KeepAlive(
                    stream.Listen(target: Node<T>.Null, trans: trans, action: action, suppressEarlierFirings: false)),
            handler: handler);

    // The two listeners below give the outcome of the transaction of the first firing. The task of
    // ListenOnceAsync needs it: a value only from a transaction that committed, and the exception
    // of a transaction that failed. See OnceWithOutcomeAction.
    internal IStrongListener ListenOnceWithOutcomeStrongImpl(Action<T> onCommitted, Action<Exception> onFailed) =>
        TransactionInternal.Apply(
            code: static IStrongListener (trans, _, state) =>
            {
                OnceWithOutcomeAction<T> action = new(onCommitted: state.OnCommitted, onFailed: state.OnFailed);

                StrongListener listener =
                    state.Self.KeepAlive(
                        state.Self.Listen(
                            target: Node<T>.Null,
                            trans: trans,
                            action: action,
                            suppressEarlierFirings: false));

                action.Own(listener);

                return listener;
            },
            state: (Self: this, OnCommitted: onCommitted, OnFailed: onFailed));

    internal IRootableListener ListenOnceWithOutcomeRootableImpl(Action<T> onCommitted, Action<Exception> onFailed) =>
        TransactionInternal.Apply(
            code: static IRootableListener (trans, _, state) =>
            {
                OnceWithOutcomeAction<T> action = new(onCommitted: state.OnCommitted, onFailed: state.OnFailed);

                RootableListener listener =
                    new(
                        stream: state.Self,
                        listener: state.Self.Listen(
                            target: Node<T>.Null,
                            trans: trans,
                            action: action,
                            suppressEarlierFirings: false));

                action.Own(listener);

                return listener;
            },
            state: (Self: this, OnCommitted: onCommitted, OnFailed: onFailed));

    internal IRootableListener ListenOnceRootableImpl(Action<T> handler) =>
        this.ListenOnce<IRootableListener>(
            listen: static (stream, trans, action) =>
                new RootableListener(
                    stream: stream,
                    listener: stream.Listen(
                        target: Node<T>.Null,
                        trans: trans,
                        action: action,
                        suppressEarlierFirings: false)),
            handler: handler);

    // The weak and the strong one-shot listeners do the same thing with a different listener
    // type. The transaction holds the lock until the listener has a value. Thus, no firing on a
    // different thread and no firing that Listen sends again can find the listener without its
    // value. The listen function takes that transaction and does not open a nested one. A nested
    // transaction costs a closure and a delegate for each call.
    private TListener ListenOnce<TListener>(
        Func<Stream<T>, TransactionInternal, Action<TransactionInternal, T>, TListener> listen,
        Action<T> handler)
        where TListener : class, IListener =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.ListenOnce(trans: trans, listen: state.Listen, handler: state.Handler),
            state: (Self: this, Listen: listen, Handler: handler));

    private TListener ListenOnce<TListener>(
        TransactionInternal trans,
        Func<Stream<T>, TransactionInternal, Action<TransactionInternal, T>, TListener> listen,
        Action<T> handler)
        where TListener : class, IListener
    {
        TListener? listener = null;

        TListener listenerToReturn =
            listen(
                arg1: this,
                arg2: trans,
                arg3: (_, a) =>
                {
                    // ReSharper disable once AccessToModifiedClosure - The assignment below
                    // gives listener its value after listen returns, and this closure must read
                    // the value it holds at the time the stream fires.
                    TListener? listenerLocal = listener;

                    // The first firing removes the listener and sets null. Unlisten does not
                    // stop a firing that is in the queue or in the loop of Listen. Thus, this
                    // test stops the second value.
                    if (listenerLocal == null)
                    {
                        return;
                    }

                    listenerLocal.Unlisten();
                    listener = null;
                    handler(a);
                });

        listener = listenerToReturn;
        return listenerToReturn;
    }

    private StrongListener KeepAlive(IWeakListenerInternal innerListener)
    {
        StrongListener? listener = null;

        listener =
            new StrongListener(
                unlisten: () =>
                {
                    innerListener.Unlisten();

                    // ReSharper disable AccessToModifiedClosure
                    if (listener != null)
                    {
                        this.KeepListenersAlive.StopKeepingAlive(listener);
                    }
                    // ReSharper restore AccessToModifiedClosure
                },
                listener: innerListener);

        this.KeepListenersAlive.KeepAlive(listener);
        return listener;
    }

    // A static lambda with a state: no closure and no delegate for the call that each
    // single-input operator makes. See SendImpl in StreamSink.
    internal IWeakListenerInternal Listen(Node target, Action<TransactionInternal, T> action) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Stream.Listen(
                    target: state.Target,
                    trans: trans,
                    action: state.Action,
                    suppressEarlierFirings: false),
            state: (Stream: this, Target: target, Action: action));

    internal IWeakListenerInternal Listen(
        Node target,
        TransactionInternal trans,
        Action<TransactionInternal, T> action,
        bool suppressEarlierFirings) =>
        new ListenerImplementation(
            stream: this,
            action: action,
            target: this.LinkAndReplay(
                target: target,
                trans: trans,
                action: action,
                suppressEarlierFirings: suppressEarlierFirings));

    /// <summary>
    ///     Links an operator's action, which is also the listener that the operator's
    ///     output attaches, and gives it back with its target set. The static lambda
    ///     with a state is as for a delegate.
    /// </summary>
    internal StreamAction<T> Listen(Node target, StreamAction<T> action) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Stream.Listen(
                    target: state.Target,
                    trans: trans,
                    action: state.Action,
                    suppressEarlierFirings: false),
            state: (Stream: this, Target: target, Action: action));

    internal StreamAction<T> Listen(
        Node target,
        TransactionInternal trans,
        StreamAction<T> action,
        bool suppressEarlierFirings)
    {
        action.Linked(
            source: this,
            target: this.LinkAndReplay(
                target: target,
                trans: trans,
                action: action,
                suppressEarlierFirings: suppressEarlierFirings));

        return action;
    }

    /// <summary>
    ///     The same for an object that is not attached. A behavior is one: the node
    ///     reaches it through the target only. A map of a behavior is one, and keeps
    ///     the target as its weak view.
    /// </summary>
    internal Node<T>.Target Listen(
        Node target,
        TransactionInternal trans,
        IStreamAction<T> action,
        bool suppressEarlierFirings) =>
        this.LinkAndReplay(
            target: target,
            trans: trans,
            action: action,
            suppressEarlierFirings: suppressEarlierFirings);

    private Node<T>.Target LinkAndReplay(
        Node target,
        TransactionInternal trans,
        object action,
        bool suppressEarlierFirings)
    {
        Node<T>.Target nodeTarget = this.Node.Link(trans: trans, action: action, target: target);

        // Copy the firings only when the program will send them again. Before, the program
        // made this copy on each call to listenStrong. That includes the most frequent
        // condition, a stream that did not fire in this transaction.
        if (!suppressEarlierFirings && this.firings is { Count: > 0 })
        {
            trans.Prioritized(new ReplayEntry(target: target, action: action, firings: [.. this.firings]));
        }

        return nodeTarget;
    }

    // Sends a new listener the values that this transaction sent before the listener came. This
    // removes the sequence dependency between send and listenStrong. An entry and not a lambda:
    // the entry holds what the lambda captured, and it can tell the action of a discard.
    private sealed class ReplayEntry(in Node target, in object action, in List<T> firings)
        : TransactionInternal.Entry(target)
    {
        private readonly object action = action;
        private readonly List<T> firings = firings;

        public override void Execute(TransactionInternal trans)
        {
            foreach (T a in this.firings)
            {
                trans.InCallback++;

                try
                {
                    // A transaction must not change the internal parts of SodaFlow.
                    StreamInternal.Fire(action: this.action, trans: trans, value: a);
                }
                finally
                {
                    trans.InCallback--;
                }
            }
        }

        public override void Discard(Exception cause) => (this.action as IFailedFiringAction)?.FiringFailed(cause);
    }

    internal Stream<TResult> MapImpl<TResult>(Func<T, TResult> f)
    {
        Stream<TResult> @out = new(this.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            this.Listen(target: @out.Node, action: new MapAction<TResult>(@out: @out, f: f)));
    }

    internal Stream<TResult> MapToImpl<TResult>(TResult value) => this.MapImpl(_ => value);

    internal Cell<T> HoldImpl(T initialValue) => new(stream: this, initialValue: initialValue);

    internal Behavior<T> HoldInternal(T initialValue) => new(stream: this, initialValue: initialValue);

    internal Cell<T> HoldLazyImpl(LazySample<T> initialValue) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.HoldLazyCellInternal(trans: trans, initialValue: state.InitialValue),
            state: (Self: this, InitialValue: initialValue));

    internal Behavior<T> HoldLazyInternal(TransactionInternal trans, LazySample<T> initialValue) =>
        new(trans: trans, stream: this, lazyInitialValue: initialValue);

    internal Cell<T> HoldLazyCellInternal(TransactionInternal trans, LazySample<T> initialValue) =>
        new(trans: trans, stream: this, lazyInitialValue: initialValue);

    internal Stream<TResult> SnapshotImpl<TResult>(Behavior<TResult> b) =>
        this.SnapshotImpl(b: b, f: static (_, a) => a);

    internal Stream<TResult> SnapshotImpl<T1, TResult>(Behavior<T1> b, Func<T, T1, TResult> f)
    {
        Stream<TResult> @out = new(this.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            this.Listen(
                target: @out.Node,
                action: new SnapshotAction<T1, TResult>(
                    @out: @out,
                    b1: b,
                    f: f)));
    }

    internal Stream<TResult> SnapshotImpl<T1, T2, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Func<T, T1, T2, TResult> f)
    {
        Stream<TResult> @out = new(this.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            this.Listen(
                target: @out.Node,
                action: new SnapshotAction<T1, T2, TResult>(
                    @out: @out,
                    b1: b1,
                    b2: b2,
                    f: f)));
    }

    internal Stream<TResult> SnapshotImpl<T1, T2, T3, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Func<T, T1, T2, T3, TResult> f)
    {
        Stream<TResult> @out = new(this.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            this.Listen(
                target: @out.Node,
                action: new SnapshotAction<T1, T2, T3, TResult>(
                    @out: @out,
                    b1: b1,
                    b2: b2,
                    b3: b3,
                    f: f)));
    }

    internal Stream<TResult> SnapshotImpl<T1, T2, T3, T4, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Func<T, T1, T2, T3, T4, TResult> f)
    {
        Stream<TResult> @out = new(this.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            this.Listen(
                target: @out.Node,
                action: new SnapshotAction<T1, T2, T3, T4, TResult>(
                    @out: @out,
                    b1: b1,
                    b2: b2,
                    b3: b3,
                    b4: b4,
                    f: f)));
    }

    internal Stream<TResult> SnapshotImpl<T1, T2, T3, T4, T5, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Func<T, T1, T2, T3, T4, T5, TResult> f)
    {
        Stream<TResult> @out = new(this.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            this.Listen(
                target: @out.Node,
                action: new SnapshotAction<T1, T2, T3, T4, T5, TResult>(
                    @out: @out,
                    b1: b1,
                    b2: b2,
                    b3: b3,
                    b4: b4,
                    b5: b5,
                    f: f)));
    }

    internal Stream<TResult> SnapshotImpl<T1, T2, T3, T4, T5, T6, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Func<T, T1, T2, T3, T4, T5, T6, TResult> f)
    {
        Stream<TResult> @out = new(this.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            this.Listen(
                target: @out.Node,
                action: new SnapshotAction<T1, T2, T3, T4, T5, T6, TResult>(
                    @out: @out,
                    b1: b1,
                    b2: b2,
                    b3: b3,
                    b4: b4,
                    b5: b5,
                    b6: b6,
                    f: f)));
    }

    internal Stream<TResult> SnapshotImpl<T1, T2, T3, T4, T5, T6, T7, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Behavior<T7> b7,
        Func<T, T1, T2, T3, T4, T5, T6, T7, TResult> f)
    {
        Stream<TResult> @out = new(this.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            this.Listen(
                target: @out.Node,
                action: new SnapshotAction<T1, T2, T3, T4, T5, T6, T7, TResult>(
                    @out: @out,
                    b1: b1,
                    b2: b2,
                    b3: b3,
                    b4: b4,
                    b5: b5,
                    b6: b6,
                    b7: b7,
                    f: f)));
    }

    internal Stream<TResult> SnapshotImpl<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Behavior<T7> b7,
        Behavior<T8> b8,
        Func<T, T1, T2, T3, T4, T5, T6, T7, T8, TResult> f)
    {
        Stream<TResult> @out = new(this.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            this.Listen(
                target: @out.Node,
                action: new SnapshotAction<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(
                    @out: @out,
                    b1: b1,
                    b2: b2,
                    b3: b3,
                    b4: b4,
                    b5: b5,
                    b6: b6,
                    b7: b7,
                    b8: b8,
                    f: f)));
    }

    internal Stream<TResult> SnapshotLatestImpl<TResult>(Behavior<TResult> b) =>
        this.SnapshotLatestImpl(b: b, f: static (_, a) => a);

    internal Stream<TResult> SnapshotLatestImpl<T1, TResult>(Behavior<T1> b, Func<T, T1, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) => state.Self.SnapshotLatest(trans: trans, b: state.B, f: state.F),
            state: (Self: this, B: b, F: f));

    private Stream<TResult> SnapshotLatest<T1, TResult>(
        TransactionInternal trans,
        Behavior<T1> b,
        Func<T, T1, TResult> f) =>
        SnapshotLatestOutput<T, T1, TResult>.Create(trans: trans, stream: this, b1: b, f: f);

    internal Stream<TResult> SnapshotLatestImpl<T1, T2, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Func<T, T1, T2, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.SnapshotLatest(trans: trans, b1: state.B1, b2: state.B2, f: state.F),
            state: (Self: this, B1: b1, B2: b2, F: f));

    private Stream<TResult> SnapshotLatest<T1, T2, TResult>(
        TransactionInternal trans,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Func<T, T1, T2, TResult> f) =>
        SnapshotLatestOutput<T, T1, T2, TResult>.Create(trans: trans, stream: this, b1: b1, b2: b2, f: f);

    internal Stream<TResult> SnapshotLatestImpl<T1, T2, T3, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Func<T, T1, T2, T3, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.SnapshotLatest(trans: trans, b1: state.B1, b2: state.B2, b3: state.B3, f: state.F),
            state: (Self: this, B1: b1, B2: b2, B3: b3, F: f));

    private Stream<TResult> SnapshotLatest<T1, T2, T3, TResult>(
        TransactionInternal trans,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Func<T, T1, T2, T3, TResult> f) =>
        SnapshotLatestOutput<T, T1, T2, T3, TResult>.Create(trans: trans, stream: this, b1: b1, b2: b2, b3: b3, f: f);

    internal Stream<TResult> SnapshotLatestImpl<T1, T2, T3, T4, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Func<T, T1, T2, T3, T4, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.SnapshotLatest(
                    trans: trans,
                    b1: state.B1,
                    b2: state.B2,
                    b3: state.B3,
                    b4: state.B4,
                    f: state.F),
            state: (Self: this, B1: b1, B2: b2, B3: b3, B4: b4, F: f));

    private Stream<TResult> SnapshotLatest<T1, T2, T3, T4, TResult>(
        TransactionInternal trans,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Func<T, T1, T2, T3, T4, TResult> f) =>
        SnapshotLatestOutput<T, T1, T2, T3, T4, TResult>.Create(
            trans: trans,
            stream: this,
            b1: b1,
            b2: b2,
            b3: b3,
            b4: b4,
            f: f);

    internal Stream<TResult> SnapshotLatestImpl<T1, T2, T3, T4, T5, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Func<T, T1, T2, T3, T4, T5, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.SnapshotLatest(
                    trans: trans,
                    b1: state.B1,
                    b2: state.B2,
                    b3: state.B3,
                    b4: state.B4,
                    b5: state.B5,
                    f: state.F),
            state: (Self: this, B1: b1, B2: b2, B3: b3, B4: b4, B5: b5, F: f));

    private Stream<TResult> SnapshotLatest<T1, T2, T3, T4, T5, TResult>(
        TransactionInternal trans,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Func<T, T1, T2, T3, T4, T5, TResult> f) =>
        SnapshotLatestOutput<T, T1, T2, T3, T4, T5, TResult>.Create(
            trans: trans,
            stream: this,
            b1: b1,
            b2: b2,
            b3: b3,
            b4: b4,
            b5: b5,
            f: f);

    internal Stream<TResult> SnapshotLatestImpl<T1, T2, T3, T4, T5, T6, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Func<T, T1, T2, T3, T4, T5, T6, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.SnapshotLatest(
                    trans: trans,
                    b1: state.B1,
                    b2: state.B2,
                    b3: state.B3,
                    b4: state.B4,
                    b5: state.B5,
                    b6: state.B6,
                    f: state.F),
            state: (Self: this, B1: b1, B2: b2, B3: b3, B4: b4, B5: b5, B6: b6, F: f));

    private Stream<TResult> SnapshotLatest<T1, T2, T3, T4, T5, T6, TResult>(
        TransactionInternal trans,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Func<T, T1, T2, T3, T4, T5, T6, TResult> f) =>
        SnapshotLatestOutput<T, T1, T2, T3, T4, T5, T6, TResult>.Create(
            trans: trans,
            stream: this,
            b1: b1,
            b2: b2,
            b3: b3,
            b4: b4,
            b5: b5,
            b6: b6,
            f: f);

    internal Stream<TResult> SnapshotLatestImpl<T1, T2, T3, T4, T5, T6, T7, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Behavior<T7> b7,
        Func<T, T1, T2, T3, T4, T5, T6, T7, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.SnapshotLatest(
                    trans: trans,
                    b1: state.B1,
                    b2: state.B2,
                    b3: state.B3,
                    b4: state.B4,
                    b5: state.B5,
                    b6: state.B6,
                    b7: state.B7,
                    f: state.F),
            state: (Self: this, B1: b1, B2: b2, B3: b3, B4: b4, B5: b5, B6: b6, B7: b7, F: f));

    private Stream<TResult> SnapshotLatest<T1, T2, T3, T4, T5, T6, T7, TResult>(
        TransactionInternal trans,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Behavior<T7> b7,
        Func<T, T1, T2, T3, T4, T5, T6, T7, TResult> f) =>
        SnapshotLatestOutput<T, T1, T2, T3, T4, T5, T6, T7, TResult>.Create(
            trans: trans,
            stream: this,
            b1: b1,
            b2: b2,
            b3: b3,
            b4: b4,
            b5: b5,
            b6: b6,
            b7: b7,
            f: f);

    internal Stream<TResult> SnapshotLatestImpl<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Behavior<T7> b7,
        Behavior<T8> b8,
        Func<T, T1, T2, T3, T4, T5, T6, T7, T8, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.SnapshotLatest(
                    trans: trans,
                    b1: state.B1,
                    b2: state.B2,
                    b3: state.B3,
                    b4: state.B4,
                    b5: state.B5,
                    b6: state.B6,
                    b7: state.B7,
                    b8: state.B8,
                    f: state.F),
            state: (Self: this, B1: b1, B2: b2, B3: b3, B4: b4, B5: b5, B6: b6, B7: b7, B8: b8, F: f));

    private Stream<TResult> SnapshotLatest<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(
        TransactionInternal trans,
        Behavior<T1> b1,
        Behavior<T2> b2,
        Behavior<T3> b3,
        Behavior<T4> b4,
        Behavior<T5> b5,
        Behavior<T6> b6,
        Behavior<T7> b7,
        Behavior<T8> b8,
        Func<T, T1, T2, T3, T4, T5, T6, T7, T8, TResult> f) =>
        SnapshotLatestOutput<T, T1, T2, T3, T4, T5, T6, T7, T8, TResult>.Create(
            trans: trans,
            stream: this,
            b1: b1,
            b2: b2,
            b3: b3,
            b4: b4,
            b5: b5,
            b6: b6,
            b7: b7,
            b8: b8,
            f: f);

    internal Stream<T> OrElseImpl(Stream<T> s) => this.MergeImpl(s: s, f: static (left, _) => left);

    internal Stream<T> MergeImpl(Stream<T> s, Func<T, T, T> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) => state.Self.Merge(trans: trans, s: state.S, f: state.F),
            state: (Self: this, S: s, F: f));

    // One node, with the two slots in fields. Before, this went through the list merge with an array
    // of two, and MergeTwo says what that cost. Before that, it was the merge without a function,
    // which fires one time for each input, and a coalesce operation after it: two nodes.
    internal Stream<T> Merge(TransactionInternal trans, Stream<T> s, Func<T, T, T> f) =>
        MergeTwo<T>.Create(trans: trans, first: this, second: s, f: f);

    internal Stream<TResult> MergeMaybesImpl<T2, TResult>(
        Stream<T2> s2,
        Func<MaybeInternal<T>, MaybeInternal<T2>, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) => state.Self.MergeMaybes(trans: trans, s2: state.S2, f: state.F),
            state: (Self: this, S2: s2, F: f));

    // One node, with the two slots in fields. MergeTwoMaybes tells why two inputs do not use
    // MergeMaybesOutput.
    private Stream<TResult> MergeMaybes<T2, TResult>(
        TransactionInternal trans,
        Stream<T2> s2,
        Func<MaybeInternal<T>, MaybeInternal<T2>, TResult> f) =>
        MergeTwoMaybes<T, T2, TResult>.Create(trans: trans, first: this, second: s2, f: f);

    internal Stream<TResult> MergeMaybesImpl<T2, T3, TResult>(
        Stream<T2> s2,
        Stream<T3> s3,
        Func<MaybeInternal<T>, MaybeInternal<T2>, MaybeInternal<T3>, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.MergeMaybes(trans: trans, s2: state.S2, s3: state.S3, f: state.F),
            state: (Self: this, S2: s2, S3: s3, F: f));

    private Stream<TResult> MergeMaybes<T2, T3, TResult>(
        TransactionInternal trans,
        Stream<T2> s2,
        Stream<T3> s3,
        Func<MaybeInternal<T>, MaybeInternal<T2>, MaybeInternal<T3>, TResult> f) =>
        MergeMaybesOutput<T, T2, T3, TResult>.Create(trans: trans, s1: this, s2: s2, s3: s3, f: f);

    internal Stream<TResult> MergeMaybesImpl<T2, T3, T4, TResult>(
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Func<MaybeInternal<T>, MaybeInternal<T2>, MaybeInternal<T3>, MaybeInternal<T4>, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.MergeMaybes(trans: trans, s2: state.S2, s3: state.S3, s4: state.S4, f: state.F),
            state: (Self: this, S2: s2, S3: s3, S4: s4, F: f));

    private Stream<TResult> MergeMaybes<T2, T3, T4, TResult>(
        TransactionInternal trans,
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Func<MaybeInternal<T>, MaybeInternal<T2>, MaybeInternal<T3>, MaybeInternal<T4>, TResult> f) =>
        MergeMaybesOutput<T, T2, T3, T4, TResult>.Create(trans: trans, s1: this, s2: s2, s3: s3, s4: s4, f: f);

    internal Stream<TResult> MergeMaybesImpl<T2, T3, T4, T5, TResult>(
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Stream<T5> s5,
        Func<
            MaybeInternal<T>,
            MaybeInternal<T2>,
            MaybeInternal<T3>,
            MaybeInternal<T4>,
            MaybeInternal<T5>,
            TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.MergeMaybes(
                    trans: trans,
                    s2: state.S2,
                    s3: state.S3,
                    s4: state.S4,
                    s5: state.S5,
                    f: state.F),
            state: (Self: this, S2: s2, S3: s3, S4: s4, S5: s5, F: f));

    private Stream<TResult> MergeMaybes<T2, T3, T4, T5, TResult>(
        TransactionInternal trans,
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Stream<T5> s5,
        Func<
            MaybeInternal<T>,
            MaybeInternal<T2>,
            MaybeInternal<T3>,
            MaybeInternal<T4>,
            MaybeInternal<T5>,
            TResult> f) =>
        MergeMaybesOutput<T, T2, T3, T4, T5, TResult>.Create(
            trans: trans,
            s1: this,
            s2: s2,
            s3: s3,
            s4: s4,
            s5: s5,
            f: f);

    internal Stream<TResult> MergeMaybesImpl<T2, T3, T4, T5, T6, TResult>(
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Stream<T5> s5,
        Stream<T6> s6,
        Func<
            MaybeInternal<T>,
            MaybeInternal<T2>,
            MaybeInternal<T3>,
            MaybeInternal<T4>,
            MaybeInternal<T5>,
            MaybeInternal<T6>,
            TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.MergeMaybes(
                    trans: trans,
                    s2: state.S2,
                    s3: state.S3,
                    s4: state.S4,
                    s5: state.S5,
                    s6: state.S6,
                    f: state.F),
            state: (Self: this, S2: s2, S3: s3, S4: s4, S5: s5, S6: s6, F: f));

    private Stream<TResult> MergeMaybes<T2, T3, T4, T5, T6, TResult>(
        TransactionInternal trans,
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Stream<T5> s5,
        Stream<T6> s6,
        Func<
            MaybeInternal<T>,
            MaybeInternal<T2>,
            MaybeInternal<T3>,
            MaybeInternal<T4>,
            MaybeInternal<T5>,
            MaybeInternal<T6>,
            TResult> f) =>
        MergeMaybesOutput<T, T2, T3, T4, T5, T6, TResult>.Create(
            trans: trans,
            s1: this,
            s2: s2,
            s3: s3,
            s4: s4,
            s5: s5,
            s6: s6,
            f: f);

    internal Stream<TResult> MergeMaybesImpl<T2, T3, T4, T5, T6, T7, TResult>(
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Stream<T5> s5,
        Stream<T6> s6,
        Stream<T7> s7,
        Func<
            MaybeInternal<T>,
            MaybeInternal<T2>,
            MaybeInternal<T3>,
            MaybeInternal<T4>,
            MaybeInternal<T5>,
            MaybeInternal<T6>,
            MaybeInternal<T7>,
            TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.MergeMaybes(
                    trans: trans,
                    s2: state.S2,
                    s3: state.S3,
                    s4: state.S4,
                    s5: state.S5,
                    s6: state.S6,
                    s7: state.S7,
                    f: state.F),
            state: (Self: this, S2: s2, S3: s3, S4: s4, S5: s5, S6: s6, S7: s7, F: f));

    private Stream<TResult> MergeMaybes<T2, T3, T4, T5, T6, T7, TResult>(
        TransactionInternal trans,
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Stream<T5> s5,
        Stream<T6> s6,
        Stream<T7> s7,
        Func<
            MaybeInternal<T>,
            MaybeInternal<T2>,
            MaybeInternal<T3>,
            MaybeInternal<T4>,
            MaybeInternal<T5>,
            MaybeInternal<T6>,
            MaybeInternal<T7>,
            TResult> f) =>
        MergeMaybesOutput<T, T2, T3, T4, T5, T6, T7, TResult>.Create(
            trans: trans,
            s1: this,
            s2: s2,
            s3: s3,
            s4: s4,
            s5: s5,
            s6: s6,
            s7: s7,
            f: f);

    internal Stream<TResult> MergeMaybesImpl<T2, T3, T4, T5, T6, T7, T8, TResult>(
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Stream<T5> s5,
        Stream<T6> s6,
        Stream<T7> s7,
        Stream<T8> s8,
        Func<
            MaybeInternal<T>,
            MaybeInternal<T2>,
            MaybeInternal<T3>,
            MaybeInternal<T4>,
            MaybeInternal<T5>,
            MaybeInternal<T6>,
            MaybeInternal<T7>,
            MaybeInternal<T8>,
            TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.MergeMaybes(
                    trans: trans,
                    s2: state.S2,
                    s3: state.S3,
                    s4: state.S4,
                    s5: state.S5,
                    s6: state.S6,
                    s7: state.S7,
                    s8: state.S8,
                    f: state.F),
            state: (Self: this, S2: s2, S3: s3, S4: s4, S5: s5, S6: s6, S7: s7, S8: s8, F: f));

    private Stream<TResult> MergeMaybes<T2, T3, T4, T5, T6, T7, T8, TResult>(
        TransactionInternal trans,
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Stream<T5> s5,
        Stream<T6> s6,
        Stream<T7> s7,
        Stream<T8> s8,
        Func<
            MaybeInternal<T>,
            MaybeInternal<T2>,
            MaybeInternal<T3>,
            MaybeInternal<T4>,
            MaybeInternal<T5>,
            MaybeInternal<T6>,
            MaybeInternal<T7>,
            MaybeInternal<T8>,
            TResult> f) =>
        MergeMaybesOutput<T, T2, T3, T4, T5, T6, T7, T8, TResult>.Create(
            trans: trans,
            s1: this,
            s2: s2,
            s3: s3,
            s4: s4,
            s5: s5,
            s6: s6,
            s7: s7,
            s8: s8,
            f: f);

    internal Stream<T> Coalesce(TransactionInternal trans1, Func<T, T, T> f)
    {
        Stream<T> @out = new(this.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            this.Listen(
                target: @out.Node,
                trans: trans1,
                action: new CoalesceAction<T>(f: f, @out: @out),
                suppressEarlierFirings: false));
    }

    internal Stream<T> FilterImpl(Func<T, bool> predicate)
    {
        Stream<T> @out = new(this.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            this.Listen(target: @out.Node, action: new FilterAction(@out: @out, predicate: predicate)));
    }

    internal Stream<T> GateImpl(Behavior<bool> b) =>
        this.SnapshotImpl(b: b, f: static (a, pred) => pred ? MaybeInternal.Some(a) : MaybeInternal<T>.None)
            .FilterSomeInternal();

    internal Stream<T> CalmImpl(Func<T, T, bool> areEqual) =>
        this.Calm(init: new LazySample<MaybeInternal<T>>(static _ => MaybeInternal<T>.None), areEqual: areEqual);

    /// <summary>
    ///     Removes a firing that is equal to the last firing that this stream sent.
    /// </summary>
    /// <remarks>
    ///     This method uses CarryState, because it needs that state protocol. The
    ///     protocol keeps the last value that the stream sent, moves it between
    ///     firings, and commits it at the transaction boundary. Before, this method
    ///     kept its own copy of the protocol. A correction to one copy could miss the
    ///     other copy, and the deferral is sufficiently subtle to make that a risk.
    ///     Cost kept the two copies separate. CollectLazyImpl needs a looped stream, a
    ///     behavior to hold the state, a snapshot, two maps, and a filter on the
    ///     output. That is six streams to keep one value. CarryState costs no more,
    ///     because the emit flag removes the firing without a copy. Thus, this method
    ///     is one output stream.
    ///     The state is a MaybeInternal and not a T, because there can be no previous
    ///     value.
    ///     None is also a correct initial value. Thus, CarryState needs its own
    ///     initialized flag and must not read an empty state as "not started".
    /// </remarks>
    internal Stream<T> Calm(LazySample<MaybeInternal<T>> init, Func<T, T, bool> areEqual) =>
        TransactionInternal.Apply(
            code: static (trans1, _, state) =>
                state.Stream.Calm(trans1: trans1, init: state.Init, areEqual: state.AreEqual),
            state: (Stream: this, Init: init, AreEqual: areEqual));

    internal Stream<T> Calm(TransactionInternal trans1, LazySample<MaybeInternal<T>> init, Func<T, T, bool> areEqual) =>
        this.CarryState(
            trans1: trans1,
            initialState: init,
            f: (a, last) =>
            {
                bool emit = !(last.TryGetValue(out T previous) && areEqual(arg1: previous, arg2: a));

                // The state carries forward unchanged for a suppressed firing rather than being
                // cleared, matching what feeding state back through Collect used to give.
                return (emit, a, emit ? MaybeInternal.Some(a) : last);
            });

    internal Stream<TReturn> CollectImpl<TState, TReturn>(
        TState initialState,
        Func<T, TState, (TReturn ReturnValue, TState State)> f) =>
        this.CollectLazyImpl(initialState: new LazySample<TState>(_ => initialState), f: f);

    internal Stream<TReturn> CollectLazyImpl<TState, TReturn>(
        LazySample<TState> initialState,
        Func<T, TState, (TReturn ReturnValue, TState State)> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.CollectLazy(trans: trans, initialState: state.InitialState, f: state.F),
            state: (Self: this, InitialState: initialState, F: f));

    private Stream<TReturn> CollectLazy<TState, TReturn>(
        TransactionInternal trans,
        LazySample<TState> initialState,
        Func<T, TState, (TReturn ReturnValue, TState State)> f) =>
        this.CarryState(
            trans1: trans,
            initialState: initialState,
            f: (a, s) =>
            {
                (TReturn returnValue, TState state) = f(arg1: a, arg2: s);
                return (true, returnValue, state);
            });

    internal Cell<TReturn> AccumImpl<TReturn>(TReturn initialState, Func<T, TReturn, TReturn> f) =>
        this.AccumLazyImpl(initialState: new LazySample<TReturn>(_ => initialState), f: f);

    // The hold takes the same transaction as CarryState. Before, HoldLazyImpl opened its own
    // Apply in this one, which found the transaction open and allocated a closure for nothing.
    internal Cell<TReturn> AccumLazyImpl<TReturn>(LazySample<TReturn> initialState, Func<T, TReturn, TReturn> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.AccumLazy(trans: trans, initialState: state.InitialState, f: state.F),
            state: (Self: this, InitialState: initialState, F: f));

    private Cell<TReturn> AccumLazy<TReturn>(
        TransactionInternal trans,
        LazySample<TReturn> initialState,
        Func<T, TReturn, TReturn> f) =>
        this.CarryState(
                trans1: trans,
                initialState: initialState,
                f: (a, s) =>
                {
                    TReturn next = f(arg1: a, arg2: s);
                    return (true, next, next);
                })
            .HoldLazyCellInternal(trans: trans, initialValue: initialState);

    /// <summary>
    ///     Runs <paramref name="f" /> on each firing and moves state between the
    ///     firings. See <see cref="CarriedState{T,TState,TReturn}" /> for the
    ///     protocol.
    /// </summary>
    private Stream<TReturn> CarryState<TState, TReturn>(
        TransactionInternal trans1,
        LazySample<TState> initialState,
        Func<T, TState, (bool Emit, TReturn ReturnValue, TState State)> f)
    {
        Stream<TReturn> @out = new(this.KeepListenersAlive);
        CarriedState<T, TState, TReturn> state = new(@out: @out, initialState: initialState, f: f);

        // SodaFlow forces the initial state in the sample phase, and a firing reads it until the
        // first commit. A sample that a failed transaction forced forgets its value by itself.
        trans1.Sample(state);

        return @out.UnsafeAttachListener(
            this.Listen(target: @out.Node, trans: trans1, action: state, suppressEarlierFirings: false));
    }

    internal Stream<T> OnceImpl()
    {
        Stream<T> @out = new(this.KeepListenersAlive);

        return @out.UnsafeAttachListener(this.Listen(target: @out.Node, action: new OnceAction(@out)));
    }

    // This method is not thread-safe. One of these two conditions must be applicable:
    // 1. The caller is in a transaction. In this implementation a transaction locks out all
    //    other threads.
    // 2. The method that created this object does not return it at this time. Thus, two threads cannot
    //    share the object.
    internal Stream<T> UnsafeAttachListener(IListenerInternal cleanup)
    {
        if (this.attachedListener == null)
        {
            this.attachedListener = cleanup;
        }
        else
        {
            this.attachedListeners ??= [];
            this.attachedListeners.Add(cleanup);
        }

        this.Node.Attach(stream: this, listener: cleanup.GetListenerWithWeakReference());
        return this;
    }

    /// <summary>
    ///     Holds a listener, as an attach does, but does not attach its weak view to
    ///     the node. The stream of the updates of a derived behavior holds the
    ///     derivation this way. The derivation attached its own links to the node
    ///     when it was made.
    /// </summary>
    internal void Retain(IListenerInternal listener) => this.attachedListener = listener;

    // The firings of a transaction go at its end, whichever way it ends. See Send.
    void ILastAction.Commit() => this.firings?.Clear();

    void ILastAction.Discard() => this.firings?.Clear();

    internal void Send(TransactionInternal trans, T a)
    {
        this.firings ??= [];

        if (this.firings.Count < 1)
        {
            // A transaction that fails must also clear the firings. Otherwise, the count stays above
            // zero, no subsequent transaction queues the clear, and each subsequent Listen sends the
            // stale firings. The stream is its own last action, thus this allocates no delegate.
            trans.Last(this);
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

    private sealed class MapAction<TResult>(in Stream<TResult> @out, in Func<T, TResult> f) : StreamAction<T>
    {
        private readonly Func<T, TResult> f = f;
        private readonly Stream<TResult> @out = @out;

        public override void Fire(TransactionInternal trans, T value) =>
            this.@out.Send(trans: trans, a: this.f(value));
    }

    private sealed class SnapshotAction<T1, TResult>(
        in Stream<TResult> @out,
        in Behavior<T1> b1,
        in Func<T, T1, TResult> f)
        : StreamAction<T>
    {
        private readonly Behavior<T1> b1 = b1;
        private readonly Func<T, T1, TResult> f = f;
        private readonly Stream<TResult> @out = @out;

        public override void Fire(TransactionInternal trans, T value) =>
            this.@out.Send(
                trans: trans,
                a: this.f(
                    arg1: value,
                    arg2: this.b1.SampleNoTransaction()));
    }

    private sealed class SnapshotAction<T1, T2, TResult>(
        in Stream<TResult> @out,
        in Behavior<T1> b1,
        in Behavior<T2> b2,
        in Func<T, T1, T2, TResult> f)
        : StreamAction<T>
    {
        private readonly Behavior<T1> b1 = b1;
        private readonly Behavior<T2> b2 = b2;
        private readonly Func<T, T1, T2, TResult> f = f;
        private readonly Stream<TResult> @out = @out;

        public override void Fire(TransactionInternal trans, T value) =>
            this.@out.Send(
                trans: trans,
                a: this.f(
                    arg1: value,
                    arg2: this.b1.SampleNoTransaction(),
                    arg3: this.b2.SampleNoTransaction()));
    }

    private sealed class SnapshotAction<T1, T2, T3, TResult>(
        in Stream<TResult> @out,
        in Behavior<T1> b1,
        in Behavior<T2> b2,
        in Behavior<T3> b3,
        in Func<T, T1, T2, T3, TResult> f)
        : StreamAction<T>
    {
        private readonly Behavior<T1> b1 = b1;
        private readonly Behavior<T2> b2 = b2;
        private readonly Behavior<T3> b3 = b3;
        private readonly Func<T, T1, T2, T3, TResult> f = f;
        private readonly Stream<TResult> @out = @out;

        public override void Fire(TransactionInternal trans, T value) =>
            this.@out.Send(
                trans: trans,
                a: this.f(
                    arg1: value,
                    arg2: this.b1.SampleNoTransaction(),
                    arg3: this.b2.SampleNoTransaction(),
                    arg4: this.b3.SampleNoTransaction()));
    }

    private sealed class SnapshotAction<T1, T2, T3, T4, TResult>(
        in Stream<TResult> @out,
        in Behavior<T1> b1,
        in Behavior<T2> b2,
        in Behavior<T3> b3,
        in Behavior<T4> b4,
        in Func<T, T1, T2, T3, T4, TResult> f)
        : StreamAction<T>
    {
        private readonly Behavior<T1> b1 = b1;
        private readonly Behavior<T2> b2 = b2;
        private readonly Behavior<T3> b3 = b3;
        private readonly Behavior<T4> b4 = b4;
        private readonly Func<T, T1, T2, T3, T4, TResult> f = f;
        private readonly Stream<TResult> @out = @out;

        public override void Fire(TransactionInternal trans, T value) =>
            this.@out.Send(
                trans: trans,
                a: this.f(
                    arg1: value,
                    arg2: this.b1.SampleNoTransaction(),
                    arg3: this.b2.SampleNoTransaction(),
                    arg4: this.b3.SampleNoTransaction(),
                    arg5: this.b4.SampleNoTransaction()));
    }

    private sealed class SnapshotAction<T1, T2, T3, T4, T5, TResult>(
        in Stream<TResult> @out,
        in Behavior<T1> b1,
        in Behavior<T2> b2,
        in Behavior<T3> b3,
        in Behavior<T4> b4,
        in Behavior<T5> b5,
        in Func<T, T1, T2, T3, T4, T5, TResult> f)
        : StreamAction<T>
    {
        private readonly Behavior<T1> b1 = b1;
        private readonly Behavior<T2> b2 = b2;
        private readonly Behavior<T3> b3 = b3;
        private readonly Behavior<T4> b4 = b4;
        private readonly Behavior<T5> b5 = b5;
        private readonly Func<T, T1, T2, T3, T4, T5, TResult> f = f;
        private readonly Stream<TResult> @out = @out;

        public override void Fire(TransactionInternal trans, T value) =>
            this.@out.Send(
                trans: trans,
                a: this.f(
                    arg1: value,
                    arg2: this.b1.SampleNoTransaction(),
                    arg3: this.b2.SampleNoTransaction(),
                    arg4: this.b3.SampleNoTransaction(),
                    arg5: this.b4.SampleNoTransaction(),
                    arg6: this.b5.SampleNoTransaction()));
    }

    private sealed class SnapshotAction<T1, T2, T3, T4, T5, T6, TResult>(
        in Stream<TResult> @out,
        in Behavior<T1> b1,
        in Behavior<T2> b2,
        in Behavior<T3> b3,
        in Behavior<T4> b4,
        in Behavior<T5> b5,
        in Behavior<T6> b6,
        in Func<T, T1, T2, T3, T4, T5, T6, TResult> f)
        : StreamAction<T>
    {
        private readonly Behavior<T1> b1 = b1;
        private readonly Behavior<T2> b2 = b2;
        private readonly Behavior<T3> b3 = b3;
        private readonly Behavior<T4> b4 = b4;
        private readonly Behavior<T5> b5 = b5;
        private readonly Behavior<T6> b6 = b6;
        private readonly Func<T, T1, T2, T3, T4, T5, T6, TResult> f = f;
        private readonly Stream<TResult> @out = @out;

        public override void Fire(TransactionInternal trans, T value) =>
            this.@out.Send(
                trans: trans,
                a: this.f(
                    arg1: value,
                    arg2: this.b1.SampleNoTransaction(),
                    arg3: this.b2.SampleNoTransaction(),
                    arg4: this.b3.SampleNoTransaction(),
                    arg5: this.b4.SampleNoTransaction(),
                    arg6: this.b5.SampleNoTransaction(),
                    arg7: this.b6.SampleNoTransaction()));
    }

    private sealed class SnapshotAction<T1, T2, T3, T4, T5, T6, T7, TResult>(
        in Stream<TResult> @out,
        in Behavior<T1> b1,
        in Behavior<T2> b2,
        in Behavior<T3> b3,
        in Behavior<T4> b4,
        in Behavior<T5> b5,
        in Behavior<T6> b6,
        in Behavior<T7> b7,
        in Func<T, T1, T2, T3, T4, T5, T6, T7, TResult> f)
        : StreamAction<T>
    {
        private readonly Behavior<T1> b1 = b1;
        private readonly Behavior<T2> b2 = b2;
        private readonly Behavior<T3> b3 = b3;
        private readonly Behavior<T4> b4 = b4;
        private readonly Behavior<T5> b5 = b5;
        private readonly Behavior<T6> b6 = b6;
        private readonly Behavior<T7> b7 = b7;
        private readonly Func<T, T1, T2, T3, T4, T5, T6, T7, TResult> f = f;
        private readonly Stream<TResult> @out = @out;

        public override void Fire(TransactionInternal trans, T value) =>
            this.@out.Send(
                trans: trans,
                a: this.f(
                    arg1: value,
                    arg2: this.b1.SampleNoTransaction(),
                    arg3: this.b2.SampleNoTransaction(),
                    arg4: this.b3.SampleNoTransaction(),
                    arg5: this.b4.SampleNoTransaction(),
                    arg6: this.b5.SampleNoTransaction(),
                    arg7: this.b6.SampleNoTransaction(),
                    arg8: this.b7.SampleNoTransaction()));
    }

    private sealed class SnapshotAction<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(
        in Stream<TResult> @out,
        in Behavior<T1> b1,
        in Behavior<T2> b2,
        in Behavior<T3> b3,
        in Behavior<T4> b4,
        in Behavior<T5> b5,
        in Behavior<T6> b6,
        in Behavior<T7> b7,
        in Behavior<T8> b8,
        in Func<T, T1, T2, T3, T4, T5, T6, T7, T8, TResult> f)
        : StreamAction<T>
    {
        private readonly Behavior<T1> b1 = b1;
        private readonly Behavior<T2> b2 = b2;
        private readonly Behavior<T3> b3 = b3;
        private readonly Behavior<T4> b4 = b4;
        private readonly Behavior<T5> b5 = b5;
        private readonly Behavior<T6> b6 = b6;
        private readonly Behavior<T7> b7 = b7;
        private readonly Behavior<T8> b8 = b8;
        private readonly Func<T, T1, T2, T3, T4, T5, T6, T7, T8, TResult> f = f;
        private readonly Stream<TResult> @out = @out;

        public override void Fire(TransactionInternal trans, T value) =>
            this.@out.Send(
                trans: trans,
                a: this.f(
                    arg1: value,
                    arg2: this.b1.SampleNoTransaction(),
                    arg3: this.b2.SampleNoTransaction(),
                    arg4: this.b3.SampleNoTransaction(),
                    arg5: this.b4.SampleNoTransaction(),
                    arg6: this.b5.SampleNoTransaction(),
                    arg7: this.b6.SampleNoTransaction(),
                    arg8: this.b7.SampleNoTransaction(),
                    arg9: this.b8.SampleNoTransaction()));
    }

    private sealed class FilterAction(in Stream<T> @out, in Func<T, bool> predicate) : StreamAction<T>
    {
        private readonly Stream<T> @out = @out;
        private readonly Func<T, bool> predicate = predicate;

        public override void Fire(TransactionInternal trans, T value)
        {
            if (this.predicate(value))
            {
                this.@out.Send(trans: trans, a: value);
            }
        }
    }

    // The first firing sends and stops the link. Listen can send the earlier firings of a
    // transaction to this action in one loop, and Unlisten does not stop that loop. Thus, the
    // flag stops the second value.
    private sealed class OnceAction(in Stream<T> @out) : StreamAction<T>
    {
        private readonly Stream<T> @out = @out;
        private bool fired;

        public override void Fire(TransactionInternal trans, T value)
        {
            if (this.fired)
            {
                return;
            }

            this.fired = true;
            this.@out.Send(trans: trans, a: value);
            this.Unlisten();
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
                if (this.target.Action.TryGetTarget(out object? action))
                {
                    // The garbage collector did not remove the action, thus call it.
                    if (this.target.IsActivated)
                    {
                        StreamInternal.Fire(action: action, trans: trans, value: this.value);
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

        // The stream fired, and the transaction failed before this entry ran. An action that
        // gives the outcome of that transaction gets the exception here. Each other action
        // ignores a firing that it did not get.
        public override void Discard(Exception cause)
        {
            if (this.target.IsActivated
                && this.target.Action.TryGetTarget(out object? action)
                && action is IFailedFiringAction failed)
            {
                failed.FiringFailed(cause);
            }
        }
    }

    private sealed class StrongListener(Action unlisten, IListenerInternal listener)
        : IStrongListener, IListenerInternal
    {
        // ReSharper disable once ReplaceWithPrimaryConstructorParameter - This field is needed so action is not
        // captured into a mutable variable.
        private readonly IListenerInternal listener = listener;

        // ReSharper disable once ReplaceWithPrimaryConstructorParameter - This field is needed so action is not
        // captured into a mutable variable.
        private readonly Action unlisten = unlisten;

        public void Unlisten() => this.unlisten();

        public IListenerWithWeakReference GetListenerWithWeakReference() =>
            this.listener.GetListenerWithWeakReference();

        public void Dispose() => this.Unlisten();
    }

    // The lock on this object makes Root and Unlisten atomic. Thus, a Root on one thread and the
    // first firing on a different thread cannot keep a listener that stopped in the set. The set
    // gets its own lock inside this one, and never the reverse.
    private sealed class RootableListener(in Stream<T> stream, in IWeakListenerInternal listener) : IRootableListener
    {
        private readonly IWeakListenerInternal listener = listener;
        private readonly Stream<T> stream = stream;
        private bool rooted;
        private bool stopped;

        public void Root()
        {
            lock (this)
            {
                if (this.rooted || this.stopped)
                {
                    return;
                }

                this.rooted = true;
                this.stream.KeepListenersAlive.KeepAlive(this);
            }
        }

        public void Unlisten()
        {
            this.listener.Unlisten();

            lock (this)
            {
                this.stopped = true;

                if (this.rooted)
                {
                    this.rooted = false;
                    this.stream.KeepListenersAlive.StopKeepingAlive(this);
                }
            }
        }

        public IListenerWithWeakReference GetListenerWithWeakReference() =>
            this.listener.GetListenerWithWeakReference();
    }

    // The listener around a delegate. An operator's action is its own listener. See StreamAction.
    private sealed class ListenerImplementation(
        in Stream<T> stream,
        in Action<TransactionInternal, T> action,
        in Node<T>.Target target)
        : IWeakListenerInternal
    {
        // This field keeps the action alive, because the node uses a weak reference.
        // ReSharper disable once UnusedMember.Local
        private readonly Action<TransactionInternal, T> action = action;

        // This field keeps the listener alive while the caller holds the listener. Thus, the
        // garbage collector does not remove it.
        // ReSharper disable once UnusedMember.Local
        private readonly Stream<T> stream = stream;

        // The target is the weak view of this listener. See Node<T>.Target.
        private readonly Node<T>.Target target = target;

        public void Unlisten() => this.target.Unlisten();

        public IListenerWithWeakReference GetListenerWithWeakReference() => this.target;
    }
}
