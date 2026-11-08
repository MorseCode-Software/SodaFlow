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
    /// <summary>The set that roots a strong listener on the updates.</summary>
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
///         then does not hold the behavior. Thus, a strong listener on the updates
///         does not keep the behavior alive, and a collected behavior costs a send
///         nothing.
///     </para>
/// </remarks>
/// <typeparam name="T">The type of the value of the behavior.</typeparam>
internal struct DerivedCore<T>(in IKeepListenersAlive keepListenersAlive)
{
    internal readonly StreamNode<T> Node = new();
    internal readonly IKeepListenersAlive KeepListenersAlive = keepListenersAlive;

    private Behavior<T>? behavior;
    private Stream<T>? updates;

    // A caller can ask for the stream after a value arrived in the same transaction. The stream
    // must then give that value again, as the output stream gave its earlier firings to a later
    // listener. The slot holds the value for that transaction only. A consumer of the updates
    // keeps the last value of a transaction. Thus, the last arrival is sufficient.
    private CaptureSlot<T> arrival;

    internal void Bind(Behavior<T> behavior) => this.behavior = behavior;

    /// <summary>
    ///     Gives one value of the operator to the stream of the updates, when it
    ///     exists, and otherwise to the behavior.
    /// </summary>
    internal void Arrive(TransactionInternal trans, T value)
    {
        if (this.updates is { } stream)
        {
            stream.Send(trans: trans, a: value);
            return;
        }

        this.arrival.Set(trans: trans, value: value);

        // ReSharper disable once NullableWarningSuppressionIsUsed - The behavior binds in its constructor, before a value can arrive, and goes only when the stream comes.
        this.behavior!.QueueUpdate(trans: trans, value: value);
    }

    /// <summary>
    ///     Makes the stream of the updates on the node of the derivation, and makes
    ///     the behavior follow it.
    /// </summary>
    /// <param name="trans">The current transaction.</param>
    /// <param name="retained">
    ///     The listener that keeps the derivation alive while the stream lives. A
    ///     strong listener on the stream holds the stream and not the behavior.
    /// </param>
    internal Stream<T> CreateUpdates(TransactionInternal trans, IListenerInternal retained)
    {
        Stream<T> stream = new(keepListenersAlive: this.KeepListenersAlive, node: this.Node);
        stream.Retain(retained);

        if (this.arrival.Take(trans).TryGetValue(out T value))
        {
            stream.Send(trans: trans, a: value);
        }

        // The link gives the behavior the value above again. The behavior already queued it, and a
        // second update with the same value changes nothing.
        // ReSharper disable once NullableWarningSuppressionIsUsed - The behavior asks for its updates one time, and binds before that.
        this.behavior!.Follow(trans: trans, updates: stream);
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
internal sealed class MapDerivation<TSource, T> : IDerivation<T>, IStreamAction<TSource>, IWeakListenerInternal
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
        this.core = new DerivedCore<T>(source.KeepListenersAlive);

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
    public void Fire(TransactionInternal trans, TSource value) =>
        this.core.Arrive(trans: trans, value: this.f(value));

    /// <inheritdoc />
    public void Unlisten() => this.target.Unlisten();

    /// <inheritdoc />
    public IListenerWithWeakReference GetListenerWithWeakReference() => this.target;
}
