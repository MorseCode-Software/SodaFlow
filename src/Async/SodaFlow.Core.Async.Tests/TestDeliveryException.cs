using System;

namespace SodaFlow.Async.Tests;

// The type that the pipelines in these tests send for a stopped send. Each language package has its
// own type. This one keeps the undelivered error as the pipeline gives it.
internal sealed class TestDeliveryException(
    in string message,
    in Exception innerException,
    in MaybeInternal<Exception> undeliveredError)
    : MapAsyncDeliveryExceptionBase(message: message, innerException: innerException)
{
    public MaybeInternal<Exception> UndeliveredError { get; } = undeliveredError;

    public static MapAsyncDeliveryExceptionBase Create(
        string message,
        Exception innerException,
        MaybeInternal<Exception> undeliveredError) =>
        new TestDeliveryException(
            message: message,
            innerException: innerException,
            undeliveredError: undeliveredError);
}
