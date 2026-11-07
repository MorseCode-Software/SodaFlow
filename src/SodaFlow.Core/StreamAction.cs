namespace SodaFlow;

/// <summary>
///     The action of a link: what a node calls with each value that its stream
///     sends. The target of the link holds it through a weak reference.
/// </summary>
/// <remarks>
///     Before, each link held a lambda, which is a closure and a delegate. A
///     listener object kept the delegate alive for the weak reference. An
///     operator now gives one object that holds what the lambda captured, is the
///     action, and is the listener that the output stream attaches. Thus, a link
///     is one object where it was three. A handler that a caller gives to Listen
///     stays a delegate, behind a listener of its own. Thus, the target holds a
///     delegate or an action.
/// </remarks>
/// <typeparam name="T">The type of the values of the stream.</typeparam>
internal interface IStreamAction<in T>
{
    /// <summary>Takes one value of the stream, in the transaction that sends it.</summary>
    void Fire(TransactionInternal trans, T value);
}

/// <summary>
///     The action of an operator's link, and the listener that the operator's
///     output attaches. <c>Listen</c> links it and sets its target.
/// </summary>
/// <remarks>
///     Unlisten removes the target from the source node, and the weak view of
///     this listener is that target, as for a listener around a delegate. A
///     derived type holds the output and the function of its operator as fields.
///     The action also holds its source stream. The output holds the action,
///     thus the source lives while the output does. Then, the registry does not
///     release the source's own links while a consumer of it exists.
/// </remarks>
/// <typeparam name="T">The type of the values of the source stream.</typeparam>
// ReSharper disable once InheritdocConsiderUsage - The type documents itself.
internal abstract class StreamAction<T> : IStreamAction<T>, IWeakListenerInternal
{
    // ReSharper disable once NotAccessedField.Local - Holds the source stream, see the remarks.
    private Stream<T>? source;

    private Node<T>.Target? target;

    /// <summary>
    ///     Records the link that <c>Listen</c> made: the source stream, and the
    ///     target in its node.
    /// </summary>
    internal void Linked(Stream<T> source, Node<T>.Target target)
    {
        this.source = source;
        this.target = target;
    }

    /// <inheritdoc />
    public abstract void Fire(TransactionInternal trans, T value);

    /// <inheritdoc />
    public void Unlisten() => this.target?.Unlisten();

    // ReSharper disable once NullableWarningSuppressionIsUsed - Listen links the action before it gives it to a
    // caller.
    public IListenerWithWeakReference GetListenerWithWeakReference() => this.target!;
}

/// <summary>
///     The action that sends each value into a stream. A looped stream takes its
///     definition through one, and a switch of streams takes each inner through
///     one.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage - The type documents itself.
internal sealed class SendInto<T>(in Stream<T> @out) : StreamAction<T>
{
    private readonly Stream<T> @out = @out;

    /// <inheritdoc />
    public override void Fire(TransactionInternal trans, T value) => this.@out.Send(trans: trans, a: value);
}

/// <summary>
///     The action around a handler that a caller gave to <c>Listen</c>. It holds
///     the handler, thus the handler lives while the caller holds this listener.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage - The type documents itself.
internal sealed class HandlerAction<T>(in System.Action<T> handler) : StreamAction<T>
{
    private readonly System.Action<T> handler = handler;

    /// <inheritdoc />
    public override void Fire(TransactionInternal trans, T value) => this.handler(value);
}
