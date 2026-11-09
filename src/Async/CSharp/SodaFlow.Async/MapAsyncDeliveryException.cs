using System;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Async;

/// <summary>
///     The exception that a MapAsync pipeline sends to its errors when it cannot
///     send the outcome of an operation.
/// </summary>
/// <inheritdoc cref="MapAsyncDeliveryExceptionBase" path="/remarks" />
[PublicAPI]
public sealed class MapAsyncDeliveryException : MapAsyncDeliveryExceptionBase
{
    /// <inheritdoc cref="Exception()" />
    /// <remarks>The message is the default one.</remarks>
    public MapAsyncDeliveryException()
        : base(DefaultMessage)
    {
    }

    /// <inheritdoc cref="Exception(string)" />
    public MapAsyncDeliveryException(string message)
        : base(message)
    {
    }

    /// <inheritdoc cref="Exception(string, Exception)" />
    public MapAsyncDeliveryException(string message, Exception innerException)
        : base(message: message, innerException: innerException)
    {
    }

    /// <inheritdoc cref="Exception(string, Exception)" path="/summary" />
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The exception of the listener.</param>
    /// <param name="undeliveredError">
    ///     The error that the pipeline did not send, or None where the outcome was a
    ///     result.
    /// </param>
    public MapAsyncDeliveryException(string message, Exception innerException, Maybe<Exception> undeliveredError)
        : base(message: message, innerException: innerException) =>
        this.UndeliveredError = undeliveredError;

    /// <summary>
    ///     The error that the pipeline sent to the errors in the transaction that a
    ///     listener stopped. It is None where that transaction sent a result.
    /// </summary>
    /// <remarks>
    ///     An operation that throws gives this error. A function from
    ///     <see cref="ResultFactory{TResult}.Construct" /> that throws gives it also.
    ///     The failure of the transaction stopped the send of that error. A listener
    ///     after the throw did not get it, and the Task of Execute did not get it.
    ///     Thus, this property is the one record of the error.
    /// </remarks>
    public Maybe<Exception> UndeliveredError { get; }

    // The factory that this package gives to MapAsyncImpl.
    internal static MapAsyncDeliveryExceptionBase Create(
        string message,
        Exception innerException,
        MaybeInternal<Exception> undeliveredError) =>
        new MapAsyncDeliveryException(
            message: message,
            innerException: innerException,
            undeliveredError: undeliveredError.Match(
                onSome: Maybe.Some,
                onNone: static () => Maybe<Exception>.None));
}
