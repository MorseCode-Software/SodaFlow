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
///         The pipeline then ends the item, and sends nothing for it. That end
///         cancels the Task of an Execute call for that item, and the next item can
///         start.
///         The pipeline sends this exception after that end, in a transaction of
///         its own.
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
}
