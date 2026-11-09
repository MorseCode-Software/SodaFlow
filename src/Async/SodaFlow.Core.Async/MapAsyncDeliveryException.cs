using System;
using JetBrains.Annotations;

namespace SodaFlow.Async;

/// <summary>
///     The exception that a MapAsync pipeline sends to its errors when it cannot
///     send the outcome of an operation.
/// </summary>
/// <remarks>
///     <para>
///         The pipeline sends an outcome in one transaction. It sends the result or
///         the error, and it removes the item from its queue. A listener that throws
///         in that transaction stops that send. That listener can be on
///         the results, on the errors, or on the items of the pipeline.
///         <see cref="Exception.InnerException" /> is the exception of that listener.
///     </para>
///     <para>
///         The pipeline then ends the item, and sends nothing for it. The next item
///         can start. The pipeline sends this exception after that end, in a
///         transaction of its own. The Task of an Execute call for that item fails
///         with this same exception.
///     </para>
///     <para>
///         The outcome that the pipeline did not send can be an exception. Then
///         <see cref="UndeliveredError" /> holds it.
///     </para>
///     <para>
///         An operation that throws sends its own exception to the errors, and not
///         this type. Thus, a handler can tell a failure of a listener from a
///         failure of an operation.
///     </para>
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class MapAsyncDeliveryException : Exception
{
    internal const string DefaultMessage =
        "A listener threw while a MapAsync pipeline delivered the outcome of an operation. "
        + "The pipeline delivered nothing for that item. See the inner exception.";

    /// <inheritdoc />
    /// <remarks>The message is the default one.</remarks>
    public MapAsyncDeliveryException()
        : base(DefaultMessage)
    {
    }

    /// <inheritdoc />
    public MapAsyncDeliveryException(string message)
        : base(message)
    {
    }

    /// <inheritdoc />
    public MapAsyncDeliveryException(string message, Exception innerException)
        : base(message: message, innerException: innerException)
    {
    }

    /// <inheritdoc cref="MapAsyncDeliveryException(string, Exception)" path="/summary" />
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The exception of the listener.</param>
    /// <param name="undeliveredError">
    ///     The error that the pipeline did not send, or null where the outcome was a
    ///     result.
    /// </param>
    public MapAsyncDeliveryException(string message, Exception innerException, Exception? undeliveredError)
        : base(message: message, innerException: innerException) =>
        this.UndeliveredError = undeliveredError;

    /// <summary>
    ///     The error that the pipeline sent to the errors in the transaction that a
    ///     listener stopped. It is null where that transaction sent a result.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An operation that throws gives this error. A function from
    ///         <see cref="ResultFactory{TResult}.Construct" /> that throws gives it
    ///         also. The failure of the transaction stopped the send of that error. A
    ///         listener after the throw did not get it, and the Task of Execute did not
    ///         get it. Thus, this property is the one record of the error.
    ///     </para>
    ///     <para>
    ///         This property is nullable, and it is not a Maybe. SodaFlow.Core.Async
    ///         has no reference to SodaFlow.Functional, which holds Maybe.
    ///     </para>
    /// </remarks>
    public Exception? UndeliveredError { get; }
}
