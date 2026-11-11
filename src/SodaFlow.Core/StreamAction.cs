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
///     An action that must know of a firing that it did not get. A transaction
///     queues each firing for each action, and a transaction that fails before
///     the action runs discards that entry.
/// </summary>
internal interface IFailedFiringAction
{
    /// <summary>
    ///     Tells the action that a transaction queued a firing for it, and failed
    ///     before the action ran.
    /// </summary>
    /// <param name="cause">The exception that failed the transaction.</param>
    void FiringFailed(System.Exception cause);
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

/// <summary>
///     The action of a one-shot listener that gives the outcome of the transaction
///     of its firing. A transaction that commits gives the value, after the commit.
///     A transaction that fails gives its exception.
/// </summary>
/// <remarks>
///     <para>
///         A handler runs in the transaction of the firing, and that transaction
///         can fail after the handler. Thus, this action gives the value to the
///         transaction as a posted action. A commit runs it, and a failure runs its
///         release with the exception of the transaction.
///     </para>
///     <para>
///         A transaction can also fail before this action runs, where an operator
///         or a different listener throws. The stream fired in that transaction,
///         thus the action gets the exception through
///         <see cref="IFailedFiringAction" />.
///     </para>
///     <para>
///         The first firing ends the listener, on each of the two paths. A second
///         entry of the same transaction then finds no owner and does nothing.
///     </para>
/// </remarks>
/// <typeparam name="T">The type of the values of the stream.</typeparam>
// ReSharper disable once InheritdocConsiderUsage - The type documents itself.
internal sealed class OnceWithOutcomeAction<T>(
    in System.Action<T> onCommitted,
    in System.Action<System.Exception> onFailed)
    : StreamAction<T>, IFailedFiringAction
{
    private readonly System.Action<T> onCommitted = onCommitted;
    private readonly System.Action<System.Exception> onFailed = onFailed;

    // The listener that a caller holds, which this action stops at the first firing. It is null
    // after that. Only a thread that holds the transaction lock reads it or writes it.
    private IListener? owner;

    /// <summary>Gives this action the listener to stop at the first firing.</summary>
    internal void Own(IListener listener) => this.owner = listener;

    /// <inheritdoc />
    public override void Fire(TransactionInternal trans, T value)
    {
        if (!this.End())
        {
            return;
        }

        TransactionInternal.PostImpl(
            action: static state => state.Self.onCommitted(state.Value),
            state: (Self: this, Value: value),
            onFailure: this.onFailed);
    }

    /// <inheritdoc />
    public void FiringFailed(System.Exception cause)
    {
        if (this.End())
        {
            this.onFailed(cause);
        }
    }

    // Stops the listener, and tells the caller if this call is the first.
    private bool End()
    {
        IListener? listener = this.owner;

        if (listener == null)
        {
            return false;
        }

        this.owner = null;
        listener.Unlisten();

        return true;
    }
}
