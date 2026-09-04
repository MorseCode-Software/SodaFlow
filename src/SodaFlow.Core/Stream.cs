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
///     Represents a stream of discrete events/firings.
/// </summary>
/// <typeparam name="T">The type of values fired by the stream.</typeparam>
[PublicAPI]
public class Stream<T>
{
    internal readonly IKeepListenersAlive KeepListenersAlive;
    internal readonly Node<T> Node;

    private readonly StreamListenerManager.StreamListeners trackedListeners;

    // Everything below is allocated on first use. Streams are created in bulk - a single
    // two-cell Lift builds around twenty of them - and a stream that is only ever an
    // intermediate step in a chain never sends, never has a listener attached, and never has
    // AttachListener called on it, so eagerly allocating for all three was most of what a
    // stream cost to construct.

    // ReSharper disable once CollectionNeverQueried.Local
    private List<IListener>? attachedListeners;

    // Cached alongside firings because a method group conversion allocates a fresh delegate
    // each time, and Send hands this to trans.Last on the first firing of every transaction.
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

        // Last, so nothing half-built is reachable from the registry. The registry only ever
        // holds this stream through a weak handle, so registering here does not keep it alive.
        this.trackedListeners = new StreamListenerManager.StreamListeners(this);
    }

    // Created on demand like the rest, but via CompareExchange rather than a plain null check,
    // since there is no other lock available to guard creating this one.
    //
    // Deliberately not volatile, unlike Cell.updates, which is the same shape of lazy read. What
    // volatile buys there is safe publication of an object with fields: a reader must not see the
    // reference before the object's own state. This publishes a bare object() with no state at
    // all, used only for its identity as a monitor, so there is nothing to observe half-built.
    // CompareExchange settles which one wins, and every caller then reads the same winner.
    //
    // Do not copy this pattern to a field holding something with state without adding volatile,
    // which means declaring the field explicitly again: the backing field the field keyword
    // synthesises cannot be marked volatile.
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

        // Only snapshot the firings when they are actually going to be replayed - the copy
        // used to be taken unconditionally, on every listenStrong, including the overwhelmingly
        // common case of a stream that has not fired in this transaction.
        if (!suppressEarlierFirings && this.firings is { Count: > 0 })
        {
            // ReSharper disable once LocalVariableHidesMember
            List<T> firings = [.. this.firings];

            trans.Prioritized(
                node: target,
                action: trans2 =>
                {
                    // Anything sent already in this transaction must be sent now so that
                    // there's no order dependency between send and listenStrong.
                    foreach (T a in firings)
                    {
                        trans2.InCallback++;

                        try
                        {
                            // Don't allow transactions to interfere with SodaFlow
                            // internals.
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
    ///     Clean up the output by discarding any firing other than the last one.
    /// </summary>
    /// <param name="trans">The transaction to get the last firing from.</param>
    /// <returns>A stream containing only the last event firing from the specified transaction.</returns>
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
    ///     Suppresses firings equal to the last one that got through.
    /// </summary>
    /// <remarks>
    ///     Expressed over CarryState, whose state protocol this needs exactly: the last value let
    ///     through, carried between firings and committed at the transaction boundary. It used to
    ///     keep its own copy of that protocol, which meant a fix to one could silently miss the
    ///     other - and the deferral is subtle enough that the duplication was a real hazard rather
    ///     than a stylistic one.
    ///     What kept it separate was cost. Going through CollectLazyImpl meant a looped stream, a
    ///     behavior to hold the state, a snapshot and two maps, plus a filter on the way out - six
    ///     streams to remember one value. Sharing CarryState instead costs nothing: the emit flag
    ///     suppresses in place, so this is still one output stream.
    ///     The state is MaybeInternal rather than T because there may be no previous value yet, and
    ///     None is also a legitimate initial value - which is why CarryState needs its own
    ///     initialized flag rather than reading emptiness as "not started".
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
    ///     Runs <paramref name="f" /> over each firing with state carried between firings, sending
    ///     what it returns for that firing when it asks. Collect and Accum always ask, and differ
    ///     only in what they do with the resulting stream; Calm suppresses the firings it wants to
    ///     swallow.
    /// </summary>
    /// <remarks>
    ///     This used to be assembled out of FRP primitives: a looped stream carrying the state back
    ///     round, a behavior holding it, a snapshot to read it, and a map per output - four streams
    ///     for Collect, two for Accum, to carry one value between firings. It is now a single output
    ///     stream and two fields.
    ///     Those two fields are what the behavior used to provide, and the split matters. A snapshot
    ///     reads a behavior with SampleNoTransaction, so every firing within a transaction saw the
    ///     state as of when that transaction opened, and the behavior committed whatever the final
    ///     firing produced. Keeping a single field updated in place would instead let an earlier
    ///     firing in the same transaction be seen by a later one - a different fold, and one the
    ///     caller's f would notice.
    ///     What the deferral is actually observable through is failure. A transaction that throws
    ///     drops its last queue, so a firing inside it never commits and the state is left as though
    ///     it had not happened. That is the one behavior distinguishing this from committing in
    ///     place, and CalmTests.AFailedTransactionDoesNotCommitTheRememberedValue is what pins it -
    ///     every other test passes either way.
    ///     The emit flag is what lets Calm share this. Returning a Maybe and filtering it out would
    ///     cost a second stream and a node in the rank graph, which is the cost Calm was written
    ///     directly to avoid in the first place; a bool in a tuple that is already a struct costs a
    ///     branch that predicts perfectly.
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

        // Forced in the sample phase as well as on demand, because the behavior this replaces
        // forced its lazy initial value there whether or not anything fired.
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
        // This is a bit long-winded, but it's efficient because it unregisters the listener.
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

    // This is not thread-safe, so one of these two conditions must apply:
    // 1. We are within a transaction, since in the current implementation
    //    a transaction locks out all other threads.
    // 2. The object on which this is being called was created has not yet
    //    been returned from the method where it was created, so it can't
    //    be shared between threads.
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
            // SendEntry rather than a lambda: this runs for every target of every firing,
            // and a closure here costs a display class and a delegate on top of the queue
            // entry that has to be allocated anyway. Carrying the three captured values as
            // fields on the entry collapses that to a single allocation.
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
                // Don't allow transactions to interfere with SodaFlow
                // internals.
                // Dereference the weak reference
                if (this.target.Action.TryGetTarget(out Action<TransactionInternal, T>? action))
                {
                    // If it hasn't been garbage collected, call it.
                    if (this.target.IsActivated)
                    {
                        action(arg1: trans, arg2: this.value);
                    }
                }
                else
                {
                    // If it has been garbage collected, remove it.
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
        // It's essential that we keep the action alive, since the node uses
        // a weak reference.
        // ReSharper disable once UnusedMember.Local
        private readonly Action<TransactionInternal, T> action = action;

        // It's essential that we keep the listener alive while the caller holds
        // the Listener, so that the garbage collector doesn't get triggered.
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

        // One of these exists per root stream, and plenty of streams are never listened to at
        // all, so both collections wait until something actually needs them.
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
