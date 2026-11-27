using System;

namespace SodaFlow;

/// <summary>
///     The inputs of a derived behavior: a map or a lift. The behavior is the
///     node of the operator, and this gives it its value.
/// </summary>
/// <remarks>
///     <para>
///         Before, a derived behavior was a hold of an output stream: the operator
///         sent into the stream, and the behavior listened to it. That is a stream,
///         its node, a link from the stream to the behavior, and a lazy sample for
///         the initial value. Now each value of the operator arrives at the
///         behavior directly. The behavior makes the stream of its updates only
///         when a caller asks for it. A caller samples or lifts most derived
///         behaviors.
///     </para>
///     <para>
///         The inputs link into the node of the derivation, and the stream of the
///         updates uses that node when it exists. Thus, the stream ranks above the
///         inputs, as the output stream did, and a link from the stream gets a
///         correct rank.
///     </para>
/// </remarks>
/// <typeparam name="T">The type of the value of the behavior.</typeparam>
internal interface IDerivation<T>
{
    /// <summary>The set that roots a wait on the updates.</summary>
    IKeepListenersAlive KeepListenersAlive { get; }

    /// <summary>Records the behavior that each value arrives at.</summary>
    void Bind(Behavior<T> behavior);

    /// <summary>
    ///     Gives the initial value of the behavior. The behavior calls this in a
    ///     transaction, and can call it again after a transaction that failed.
    /// </summary>
    T Initial();

    /// <summary>
    ///     Makes the stream of the updates, at the first ask. The behavior calls
    ///     this in a transaction.
    /// </summary>
    Stream<T> CreateUpdates(TransactionInternal trans);

    /// <summary>
    ///     True while the derivation has no link to its inputs. The behavior then
    ///     reads its value from the inputs, through <see cref="Pull" />.
    /// </summary>
    bool IsUnlinked { get; }

    /// <summary>
    ///     The largest version of the inputs. It changes when the committed value
    ///     of an input changes.
    /// </summary>
    long SourceVersion { get; }

    /// <summary>True when an input has an update in this transaction.</summary>
    bool SourcesHavePending { get; }

    /// <summary>
    ///     The value from the inputs. The committed value comes from a cache while
    ///     <see cref="SourceVersion" /> does not change.
    /// </summary>
    /// <param name="pending">True for the value at the end of this transaction.</param>
    T Pull(bool pending);
}

/// <summary>
///     The state that a map and a lift share as derivations. It holds the node,
///     the behavior or the stream of its updates, and the last value that
///     arrived before the stream existed.
/// </summary>
/// <remarks>
///     <para>
///         This is a struct in a field of each derivation. A map is an action and
///         a lift is a gate, thus the two cannot have one base class.
///     </para>
///     <para>
///         Until a caller asks for the updates, each value goes to the behavior.
///         After that, each value goes to the stream, and the behavior follows the
///         stream through a link on the null rank, as a hold does. The derivation
///         then does not hold the behavior. Thus, a listener on the updates
///         does not keep the behavior alive, and a collected behavior costs a send
///         nothing.
///     </para>
/// </remarks>
/// <typeparam name="T">The type of the value of the behavior.</typeparam>
internal struct DerivedCore<T>(in IKeepListenersAlive keepListenersAlive, in DemandStreamNode<T> node)
{
    // A demand node. After the last listener of the updates goes, the inputs unlink.
    internal readonly DemandStreamNode<T> Node = node;
    internal readonly IKeepListenersAlive KeepListenersAlive = keepListenersAlive;

    private Behavior<T>? behavior;

    // The behavior after the stream exists. A listener on the stream must not keep the
    // behavior alive. Before, the behavior followed the stream through a link, which was a target
    // with a weak reference of its own.
    private WeakReference<Behavior<T>>? weakBehavior;
    private Stream<T>? updates;

    // A caller can ask for the stream after a value arrived in the same transaction. The stream
    // must then give that value again, as the output stream gave its earlier firings to a later
    // listener. The slot holds the value for that transaction only. A consumer of the updates
    // keeps the last value of a transaction. Thus, the last arrival is sufficient.
    private CaptureSlot<T> arrival;

    // The last value that a read got from the inputs while unlinked, and the version of that value.
    private T? cachedValue;
    private long cachedVersion;
    private bool hasCache;

    // True from an unlink to the first read after it. cachedVersion then holds the id of the
    // transaction of the unlink, and not the version of a value.
    private bool keepsCommitted;

    internal readonly bool IsUnlinked => this.Node.IsUnlinked;

    internal void Bind(Behavior<T> behavior) => this.behavior = behavior;

    internal MaybeInternal<T> Cached(long version)
    {
        if (this.hasCache && this.cachedVersion == version)
        {
            // ReSharper disable once NullableWarningSuppressionIsUsed - The cache holds a value when hasCache is true.
            return MaybeInternal.Some(this.cachedValue!);
        }

        // The first read after an unlink. An input that changed after the unlink has a version that
        // is larger than the id of that transaction. Otherwise, the committed value of the behavior
        // is the value for this version, and the function does not run again. Thus, a function that
        // makes a cell does not make a second cell.
        if (this.keepsCommitted)
        {
            this.keepsCommitted = false;

            if (version <= this.cachedVersion && this.Behavior is { } behavior &&
                behavior.Committed.TryGetValue(out T value))
            {
                this.SetCached(version: version, value: value);
                return MaybeInternal.Some(value);
            }
        }

        return MaybeInternal<T>.None;
    }

    internal void SetCached(long version, T value)
    {
        this.cachedValue = value;
        this.cachedVersion = version;
        this.hasCache = true;
    }

    internal readonly void Relinking() => this.Behavior?.EnterLinked();

    // The node unlinked in this transaction. The first read keeps the committed value of the
    // behavior. Before, the first read ran the function again. A read of the value in an after-last
    // action costs 224 B for each unlink, and these fields cost no allocation.
    internal void Unlinked(TransactionInternal trans)
    {
        this.hasCache = false;
        this.cachedVersion = trans.Id;
        this.keepsCommitted = true;
    }

    private readonly Behavior<T>? Behavior =>
        this.behavior ?? (this.weakBehavior != null && this.weakBehavior.TryGetTarget(out Behavior<T>? b) ? b : null);

    /// <summary>
    ///     Gives one value of the operator to the stream of the updates, when it
    ///     exists, and otherwise to the behavior.
    /// </summary>
    internal void Arrive(TransactionInternal trans, T value)
    {
        if (this.updates is { } stream)
        {
            stream.Send(trans: trans, a: value);
        }
        else
        {
            this.arrival.Set(trans: trans, value: value);

            // No stream of the updates exists, thus no listener exists.
            this.Node.Sending(trans);
        }

        // The behavior gets each value directly, also after the stream exists. A link from the stream
        // to the behavior is a listener, and with it the inputs never unlink. A collected behavior gets
        // nothing.
        this.Behavior?.QueueUpdate(trans: trans, value: value);
    }

    /// <summary>
    ///     Makes the stream of the updates on the node of the derivation, and makes
    ///     the behavior follow it.
    /// </summary>
    /// <param name="trans">The current transaction.</param>
    /// <param name="retained">
    ///     The listener that keeps the derivation alive while the stream lives. A
    ///     listener on the stream holds the stream and not the behavior.
    /// </param>
    internal Stream<T> CreateUpdates(TransactionInternal trans, IListenerInternal retained)
    {
        Stream<T> stream = new(keepListenersAlive: this.KeepListenersAlive, node: this.Node);
        stream.Retain(retained);

        if (this.arrival.Take(trans).TryGetValue(out T value))
        {
            stream.Send(trans: trans, a: value);
        }

        // The behavior queued the value above. The behavior does not follow the stream, and from here
        // the derivation holds it weakly. See Arrive.
        // ReSharper disable once NullableWarningSuppressionIsUsed - The behavior asks for its updates one time, and binds before that.
        this.weakBehavior = new WeakReference<Behavior<T>>(this.behavior!);
        this.behavior = null;
        this.updates = stream;

        return stream;
    }
}

/// <summary>
///     The derivation of a map. It is the action of the link from the source, and
///     the listener that holds that link.
/// </summary>
/// <typeparam name="TSource">The type of the value of the source.</typeparam>
/// <typeparam name="T">The type of the value of the map.</typeparam>
// ReSharper disable once InheritdocConsiderUsage - The type documents itself.
internal sealed class MapDerivation<TSource, T>
    : IDerivation<T>, IStreamAction<TSource>, IListenerInternal, IRelinkable, IDemandOwner
{
    // The map holds its source, thus the stream of the source lives while the map does. See the
    // remarks on StreamAction.
    private readonly Behavior<TSource> source;
    private readonly Func<TSource, T> f;
    private readonly Node<TSource>.Target target;
    private DerivedCore<T> core;

    private MapDerivation(TransactionInternal trans, Behavior<TSource> source, Func<TSource, T> f)
    {
        this.source = source;
        this.f = f;
        this.core = new DerivedCore<T>(keepListenersAlive: source.KeepListenersAlive, node: new DemandStreamNode<T>());

        this.target = source.Updates()
            .Listen(target: this.core.Node, trans: trans, action: this, suppressEarlierFirings: false);

        // The registry stops the link when no behavior and no stream holds this map. The target
        // is the weak view, thus the registry needs no handle.
        this.core.Node.Attach(stream: this, listener: this.target);
    }

    /// <inheritdoc />
    public IKeepListenersAlive KeepListenersAlive => this.core.KeepListenersAlive;

    internal static MapDerivation<TSource, T> Create(
        TransactionInternal trans,
        Behavior<TSource> source,
        Func<TSource, T> f) =>
        new(trans: trans, source: source, f: f);

    /// <inheritdoc />
    public void Bind(Behavior<T> behavior) => this.core.Bind(behavior);

    /// <inheritdoc />
    public T Initial() => this.f(this.source.PendingOrCurrent());

    /// <inheritdoc />
    public Stream<T> CreateUpdates(TransactionInternal trans) =>
        this.core.CreateUpdates(trans: trans, retained: this);

    /// <inheritdoc />
    public bool IsUnlinked => this.core.IsUnlinked;

    /// <inheritdoc />
    public long SourceVersion => this.source.Version;

    /// <inheritdoc />
    public bool SourcesHavePending => this.source.HasPendingUpdate;

    /// <inheritdoc />
    public T Pull(bool pending)
    {
        if (pending && this.source.HasPendingUpdate)
        {
            return this.f(this.source.PendingOrCurrent());
        }

        long version = this.source.Version;

        if (this.core.Cached(version).TryGetValue(out T cached))
        {
            return cached;
        }

        T value = this.f(this.source.SampleNoTransaction());
        this.core.SetCached(version: version, value: value);

        return value;
    }

    void IDemandOwner.Relinking(TransactionInternal trans) => this.core.Relinking();

    void IDemandOwner.Unlinked(TransactionInternal trans) => this.core.Unlinked(trans);

    // The demand node links the map to its source again.
    IListenerWithWeakReference IRelinkable.Relink(TransactionInternal trans)
    {
        this.source.Updates().RelinkAndReplay(trans: trans, link: this.target, action: this);

        return this.target;
    }

    /// <inheritdoc />
    public void Fire(TransactionInternal trans, TSource value) =>
        this.core.Arrive(trans: trans, value: this.f(value));

    /// <inheritdoc />
    public void Dispose() => this.target.Unlisten();

    /// <inheritdoc />
    public IListenerWithWeakReference GetListenerWithWeakReference() => this.target;
}
