using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Async.Tests;

/// <summary>
///     Covers the time and the thread of the cancel of a tracked item. The
///     pipeline records the cancel while it holds the transaction lock, and it
///     cancels the token after the release of that lock.
/// </summary>
public sealed class MapAsyncCancellationTests
{
    /// <summary>The source of the cancel in a test.</summary>
    public enum Canceler
    {
        /// <summary>A send on the cancelAll stream.</summary>
        CancelAll,

        /// <summary>A disposal of the status, with cancelOnDispose true.</summary>
        Dispose,

        /// <summary>A second value to a SwitchLatest pipeline, which replaces the first run.</summary>
        SwitchLatest
    }

    // The callbacks of a token run on the thread that cancels it. The code after an await that a
    // callback completes runs there also. The pipeline cancels the token after the release of the
    // lock. Thus, that code runs with no transaction open, a transaction on a different thread can
    // run, and a send there is legal. Before, that code ran with the lock for each of the three. A
    // disposal also ran it in a listener callback, where a send throws.
    [Test]
    [Arguments(Canceler.CancelAll)]
    [Arguments(Canceler.Dispose)]
    [Arguments(Canceler.SwitchLatest)]
    public async Task TheCodeAfterACanceledAwaitRunsWithNoTransactionOpen(Canceler canceler)
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<UnitInternal> cancelAll = Stream.CreateSink<UnitInternal>();
        StreamSink<int> other = Stream.CreateSink<int>();
        ConcurrentQueue<int> otherReceived = new();

        TaskCompletionSource<(bool InTransaction, bool OtherSendEnded, Exception? SendHere)> cleanUp =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        using IListener l = other.Listen(otherReceived.Enqueue);

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: async (v, factory, token) =>
                {
                    if (v != 1)
                    {
                        return factory.FromValue(v);
                    }

                    TaskCompletionSource<bool> canceled = new();

                    // ReSharper disable once UseAwaitUsing - CancellationTokenRegistration is only
                    // IAsyncDisposable from .NET Core 3.0, and this compiles for net472 as well.
                    using CancellationTokenRegistration registration =
                        token.Register(() => canceled.TrySetCanceled(token));

                    try
                    {
                        await canceled.Task.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // The code of an operation after the cancel, such as a clean up. The wait
                        // has a limit, thus a lock that this thread holds fails the test and does not
                        // stop the test run.
                        bool inTransaction = Transaction.IsActive();

                        bool otherSendEnded =
                            Task.Run(action: () => other.Send(1), cancellationToken: CancellationToken.None)
                                .Wait(millisecondsTimeout: 5_000, cancellationToken: CancellationToken.None);

                        Exception? sendHere = null;

                        try
                        {
                            other.Send(2);
                        }
                        catch (Exception e)
                        {
                            sendHere = e;
                        }

                        cleanUp.SetResult((inTransaction, otherSendEnded, sendHere));

                        throw;
                    }

                    return factory.FromValue(v);
                },
                strategy: canceler == Canceler.SwitchLatest
                    ? AsyncConcurrencyStrategyFactory.SwitchLatest<int>()
                    : AsyncConcurrencyStrategyFactory.Parallel(0),
                inputConverter: static v => v,
                cancelAll: cancelAll,
                cancelOnDispose: true);

        // The operation starts inline after the release of the lock. Thus, it awaits its token now.
        source.Send(1);

        switch (canceler)
        {
            case Canceler.CancelAll:
                cancelAll.Send(UnitInternal.Value);
                break;
            case Canceler.Dispose:
                status.Dispose();
                break;
            case Canceler.SwitchLatest:
                source.Send(2);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    paramName: nameof(canceler),
                    actualValue: canceler,
                    message: null);
        }

        (bool inTransaction, bool otherSendEnded, Exception? sendHere) = await cleanUp.Task;

        await Assert.That(inTransaction).IsFalse();

        await Assert.That(otherSendEnded)
            .IsTrue()
            .Because("a transaction on a different thread must not wait for the code after the await");

        await Assert.That(sendHere).IsNull();

        TestUtil.WaitUntil(() => otherReceived.Count == 2);

        status.Dispose();
    }

    // EndItem reads the record of a cancel when the operation returns, and the flush that publishes
    // takes the lock after that. Here, the operation returns in a transaction, which defers the flush
    // to the release of the lock. The same transaction then cancels. Thus, on one thread, the cancel
    // commits after that read and before the flush. A different thread that cancels while the flush
    // waits for the lock is the same condition. Before, the flush published the result, also for an
    // operation that ignores its token.
    [Test]
    [Arguments(Canceler.CancelAll, false)]
    [Arguments(Canceler.CancelAll, true)]
    [Arguments(Canceler.Dispose, false)]
    [Arguments(Canceler.Dispose, true)]
    public async Task ACancelThatCommitsAfterTheReturnOfTheOperationAndBeforeItsFlushPublishesNothing(
        Canceler canceler,
        bool execute)
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<UnitInternal> cancelAll = Stream.CreateSink<UnitInternal>();
        TaskCompletionSource<int> gate = new();
        ConcurrentQueue<int> received = new();
        ConcurrentQueue<Exception> receivedErrors = new();

        using IListener l = results.Listen(received.Enqueue);
        using IListener e = errors.Listen(receivedErrors.Enqueue);

        // The operation ignores its token.
        AsyncMapStatus<int, int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: async (v, factory, _) => factory.FromValue(await gate.Task.ConfigureAwait(false) + v),
                strategy: AsyncConcurrencyStrategyFactory.Parallel(0),
                inputConverter: static v => v,
                cancelAll: cancelAll,
                cancelOnDispose: true);

        Task<int>? executed = null;

        if (execute)
        {
            executed = status.Execute(1);
        }
        else
        {
            source.Send(1);
        }

        Transaction.RunVoid(() =>
        {
            // The continuation of the operation runs here, inline. Its end sees this transaction and
            // waits for the release of the lock.
            gate.SetResult(41);

            if (canceler == Canceler.CancelAll)
            {
                cancelAll.Send(UnitInternal.Value);
            }
            else
            {
                status.Dispose();
            }
        });

        await Assert.That(received).IsEmpty().Because("a cancel that committed holds the result");
        await Assert.That(receivedErrors).IsEmpty();
        await Assert.That(status.Items.Sample().Count).IsEqualTo(0);

        if (executed != null)
        {
            await Assert.That(executed.IsCanceled).IsTrue();
        }

        status.Dispose();
    }

    // A Queue pipeline runs 1 and holds 2 and 3. A callback on the token of 1 throws at the disposal.
    // Before, that throw stopped the cancels at 1, made the transaction of Dispose fail, and came out
    // of Dispose. The listener of the trigger removed itself before the throw, thus nothing canceled
    // 2 and 3 again. The end of 1 then started 2 after the disposal.
    [Test]
    public async Task ACallbackOfATokenThatThrowsAtADisposalStartsNoQueuedItem()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        InvalidOperationException thrown = new("The callback of the token throws.");
        ConcurrentQueue<int> started = new();
        ConcurrentQueue<Exception> handled = new();

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: handled.Enqueue,
                createDeliveryException: TestDeliveryException.Create,
                operation: (v, factory, token) => CancelableOperation(
                    value: v,
                    factory: factory,
                    started: started,
                    thrown: v == 1 ? thrown : null,
                    token: token),
                strategy: AsyncConcurrencyStrategyFactory.Queue<int>(),
                inputConverter: static v => v,
                cancelOnDispose: true);

        source.Send(1);
        source.Send(2);
        source.Send(3);

        await Assert.That(status.Dispose).ThrowsNothing();

        await Assert.That(started)
            .IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching)
            .Because("a queued item must not start after a disposal that canceled it");

        await Assert.That(status.Items.Sample().Count).IsEqualTo(0);
        await Assert.That(handled.Count).IsEqualTo(1);

        // The operation gets a linked token. Thus, the source of the item wraps the AggregateException
        // of the linked source in a second one.
        await Assert.That(((AggregateException)handled.Single()).Flatten().InnerExceptions.Single())
            .IsSameReferenceAs(thrown);
    }

    // A Parallel pipeline runs 1, 2, and 3, and a callback on the token of 1 throws at the disposal.
    // The cancel of 2 and 3 must occur also. Before, the throw stopped the cancels at 1. Items 2 and
    // 3 continued with tokens that no code canceled.
    [Test]
    public async Task ACallbackOfATokenThatThrowsAtADisposalStopsNoOtherCancel()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        InvalidOperationException thrown = new("The callback of the token throws.");
        ConcurrentQueue<int> started = new();
        ConcurrentQueue<Exception> handled = new();

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: handled.Enqueue,
                createDeliveryException: TestDeliveryException.Create,
                operation: (v, factory, token) => CancelableOperation(
                    value: v,
                    factory: factory,
                    started: started,
                    thrown: v == 1 ? thrown : null,
                    token: token),
                strategy: AsyncConcurrencyStrategyFactory.Parallel(0),
                inputConverter: static v => v,
                cancelOnDispose: true);

        source.Send(1);
        source.Send(2);
        source.Send(3);

        await Assert.That(status.Dispose).ThrowsNothing();

        await Assert.That(started.OrderBy(static v => v))
            .IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);

        await Assert.That(status.Items.Sample().Count).IsEqualTo(0).Because("each operation obeys its token");
        await Assert.That(handled.Count).IsEqualTo(1);

        // The operation gets a linked token. Thus, the source of the item wraps the AggregateException
        // of the linked source in a second one.
        await Assert.That(((AggregateException)handled.Single()).Flatten().InnerExceptions.Single())
            .IsSameReferenceAs(thrown);
    }

    // Waits for its token. Where `thrown` is not null, a second callback on the token throws it.
    private static async Task<MapAsyncResult<int>> CancelableOperation(
        int value,
        ResultFactory<int> factory,
        ConcurrentQueue<int> started,
        Exception? thrown,
        CancellationToken token)
    {
        started.Enqueue(value);

        TaskCompletionSource<int> canceled = new();

        // ReSharper disable UseAwaitUsing - CancellationTokenRegistration is only IAsyncDisposable
        // from .NET Core 3.0, and this compiles for net472 as well.
        using CancellationTokenRegistration ends = token.Register(() => canceled.TrySetCanceled(token));

        using CancellationTokenRegistration throws =
            thrown is null ? default : token.Register(() => throw thrown);

        // ReSharper restore UseAwaitUsing

        return factory.FromValue(await canceled.Task.ConfigureAwait(false) + value);
    }

    // A disposal that is not in a callback sends its trigger in its transaction. The trigger reads
    // the queue as it was at the start of that transaction, thus a value that the same transaction
    // admits is not in it, and the trigger does not cancel that value. Such a disposal comes only
    // from the body of a transaction: the code of a listener or of an operator runs in a callback,
    // where a disposal posts its trigger. The body runs before the admission, and the disposal
    // marks the pipeline as disposed at once. Thus, the admission refuses the value, whatever the
    // order of the send and the disposal in the body.
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AValueThatTheTransactionOfADisposalSendsDoesNotStart(bool sendFirst)
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ConcurrentQueue<int> started = new();
        ConcurrentQueue<int> received = new();
        using IListener resultsListener = results.Listen(received.Enqueue);

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: (v, factory, token) => CancelableOperation(
                    value: v,
                    factory: factory,
                    started: started,
                    thrown: null,
                    token: token),
                strategy: AsyncConcurrencyStrategyFactory.Parallel(0),
                inputConverter: static v => v,
                cancelOnDispose: true);

        Transaction.RunVoid(() =>
        {
            if (sendFirst)
            {
                source.Send(1);
            }

            status.Dispose();

            if (!sendFirst)
            {
                source.Send(1);
            }
        });

        await Assert.That(started).IsEmpty().Because("the disposal came before the admission");
        await Assert.That(status.Items.Sample().Count).IsEqualTo(0);
        await Assert.That(received).IsEmpty();
    }
}
