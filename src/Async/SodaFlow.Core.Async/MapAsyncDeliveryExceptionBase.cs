using System;
using JetBrains.Annotations;

namespace SodaFlow.Async;

/// <summary>
///     The base of the exception that a MapAsync pipeline sends to its errors when
///     it cannot send the outcome of an operation.
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
///         Each language package derives one sealed type from this base, with the
///         name MapAsyncDeliveryException. A pipeline always sends that type. It adds
///         UndeliveredError, which holds the outcome that the pipeline did not send
///         where that outcome is an exception. The type of that property is
///         <c>Maybe</c> in C# and <c>option</c> in F#.
///     </para>
///     <para>
///         An operation that throws sends its own exception to the errors, and not
///         this type. Thus, a handler can tell a failure of a listener from a
///         failure of an operation.
///     </para>
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public abstract class MapAsyncDeliveryExceptionBase : Exception
{
    internal const string DefaultMessage =
        "A listener threw while a MapAsync pipeline delivered the outcome of an operation. "
        + "The pipeline delivered nothing for that item. See the inner exception.";

    // Internal, and not protected: only the language packages derive from this base. Each one
    // gives MapAsyncImpl the function that makes its own type.
    internal MapAsyncDeliveryExceptionBase(string message)
        : base(message)
    {
    }

    internal MapAsyncDeliveryExceptionBase(string message, Exception innerException)
        : base(message: message, innerException: innerException)
    {
    }
}
