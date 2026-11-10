using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Async.Tests;

public sealed class MapAsyncImplTests
{
    [Test]
    public async Task SuccessfulOperationPublishesToResults()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v.ToUpperInvariant())),
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v);

        source.Send("hello");

        TestUtil.WaitUntil(() => received.Count == 1);
        await Assert.That(received[0]).IsEqualTo("HELLO");

        status.Dispose();
        l.Unlisten();
    }

    // The code of an operation before its first await must not run with the transaction lock. Code
    // there that waits for a transaction on a different thread then ends, and does not wait for the
    // lock that its own thread holds. The operation gives true if the lock was free.
    [Test]
    public async Task AnOperationStartsWithTheTransactionLockFree()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<bool> results = Stream.CreateSink<bool>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<bool> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: static (_, factory, token) =>
                {
                    bool lockWasFree =
                        Task.Run(
                                action: static () => Transaction.RunVoid(static () =>
                                {
                                }),
                                cancellationToken: token)
                            .Wait(millisecondsTimeout: 5_000, cancellationToken: token);

                    return Task.FromResult(factory.FromValue(lockWasFree));
                },
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v);

        source.Send("hello");

        TestUtil.WaitUntil(condition: () => received.Count == 1, timeoutMs: 15_000);
        await Assert.That(received[0]).IsTrue();

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task FailedOperationPublishesToErrors()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        InvalidOperationException thrown = new("boom");
        List<Exception> received = [];
        IListener l = errors.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: (_, _, _) => Task.FromException<MapAsyncResult<string>>(thrown),
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v);

        source.Send("hello");

        TestUtil.WaitUntil(() => received.Count == 1);
        await Assert.That(received[0]).IsSameReferenceAs(thrown);

        status.Dispose();
        l.Unlisten();
    }

    // A listener on the results that throws stops the send of that result. The item ends, the next
    // item starts, and the errors get the failure of the listener in a wrapper. Before, the pipeline
    // reported the exception of the listener as a failure of the operation.
    [Test]
    public async Task AResultsListenerThatThrowsEndsTheItemAndReportsADeliveryFailure()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        InvalidOperationException thrown = new("results listener");
        List<int> received = [];
        List<Exception> failures = [];

        using IStrongListener l = results.ListenStrong(v =>
        {
            if (v == 1)
            {
                throw thrown;
            }

            received.Add(v);
        });

        using IStrongListener e = errors.ListenStrong(failures.Add);

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v)),
                strategy: AsyncConcurrencyStrategyFactory.Queue<int>(),
                inputConverter: static v => v);

        source.Send(1);
        source.Send(2);

        TestUtil.WaitUntil(() => received.Count == 1);

        await Assert.That(received).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);
        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0]).IsTypeOf<TestDeliveryException>();
        await Assert.That(failures[0].InnerException).IsSameReferenceAs(thrown);
        await Assert.That((failures[0] as TestDeliveryException)?.UndeliveredError.HasValue()).IsFalse();
        await Assert.That(status.Items.Sample().Count).IsEqualTo(0);
        await Assert.That(status.IsRunning.Sample()).IsFalse();

        status.Dispose();
    }

    // The pipeline reports a failure of a listener to the errors, in a transaction of its own. Thus,
    // a listener on the errors that throws on that report cannot stop the pipeline. The report and
    // that second throw go to handleException together. Before, the item stayed Running, the queue
    // never moved, and Dispose threw.
    [Test]
    public async Task AnErrorsListenerThatThrowsOnTheReportDoesNotStopThePipeline()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        InvalidOperationException thrown = new("results listener");
        List<int> received = [];
        List<Exception> reported = [];
        List<Exception> unhandled = [];

        using IStrongListener l = results.ListenStrong(v =>
        {
            if (v == 1)
            {
                throw thrown;
            }

            received.Add(v);
        });

        // This throws on the failure of the results listener, also when it is in a wrapper.
        using IStrongListener e = errors.ListenStrong(error =>
        {
            reported.Add(error);

            if (ReferenceEquals(objA: error, objB: thrown) || ReferenceEquals(objA: error.InnerException, objB: thrown))
            {
                throw new InvalidOperationException("errors listener");
            }
        });

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: unhandled.Add,
                createDeliveryException: TestDeliveryException.Create,
                operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v)),
                strategy: AsyncConcurrencyStrategyFactory.Queue<int>(),
                inputConverter: static v => v);

        source.Send(1);
        source.Send(2);

        TestUtil.WaitUntil(() => received.Count == 1);

        await Assert.That(received).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);
        await Assert.That(reported.Count).IsEqualTo(1);
        await Assert.That(reported[0]).IsTypeOf<TestDeliveryException>();
        await Assert.That(unhandled.Count).IsEqualTo(1);
        await Assert.That((unhandled[0] as AggregateException)?.InnerExceptions[0]).IsSameReferenceAs(reported[0]);
        await Assert.That(status.Items.Sample().Count).IsEqualTo(0);
        await Assert.That(status.IsRunning.Sample()).IsFalse();
        await Assert.That(status.Dispose).ThrowsNothing();
    }

    // A listener can stop the result of an item. The Execute call of that item then fails with the
    // same report that the errors get.
    [Test]
    public async Task ExecuteFailsWithTheReportWhenItsResultIsNotDelivered()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<Exception> reported = [];
        using IStrongListener l = results.ListenStrong(static _ => throw new InvalidOperationException("results listener"));
        using IStrongListener e = errors.ListenStrong(reported.Add);

        AsyncMapStatus<int, int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v)),
                strategy: AsyncConcurrencyStrategyFactory.Queue<int>(),
                inputConverter: static v => v);

        Task<int> task = status.Execute(1);

        TestUtil.WaitUntil(() => task.IsCompleted);

        await Assert.That(task.IsFaulted).IsTrue();
        await Assert.That(reported.Count).IsEqualTo(1);
        await Assert.That(task.Exception?.InnerException).IsSameReferenceAs(reported[0]);

        status.Dispose();
    }

    // A listener on the errors that throws on the error of an operation stops the send of that error.
    // The report then holds that error, because no other record of it stays.
    [Test]
    public async Task AnErrorThatAListenerStopsComesInTheReport()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        InvalidOperationException operationError = new("operation");
        InvalidOperationException listenerError = new("errors listener");
        List<Exception> reported = [];

        using IStrongListener e = errors.ListenStrong(error =>
        {
            if (ReferenceEquals(objA: error, objB: operationError))
            {
                throw listenerError;
            }

            reported.Add(error);
        });

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: (_, _, _) => Task.FromException<MapAsyncResult<int>>(operationError),
                strategy: AsyncConcurrencyStrategyFactory.Queue<int>(),
                inputConverter: static v => v);

        source.Send(1);

        TestUtil.WaitUntil(() => reported.Count == 1);

        TestDeliveryException? report = reported[0] as TestDeliveryException;
        await Assert.That(report).IsNotNull();
        await Assert.That(report?.InnerException).IsSameReferenceAs(listenerError);
        await Assert.That(report?.UndeliveredError).IsEqualTo(MaybeInternal.Some<Exception>(operationError));
        await Assert.That(status.Items.Sample().Count).IsEqualTo(0);

        status.Dispose();
    }

    // A function from Construct that throws gives an error in the transaction of the send. A
    // listener that stops the send of that error makes the report hold it, as for an operation.
    [Test]
    public async Task AConstructionErrorThatAListenerStopsComesInTheReport()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        InvalidOperationException constructionError = new("construction");
        List<Exception> reported = [];

        using IStrongListener e = errors.ListenStrong(error =>
        {
            if (ReferenceEquals(objA: error, objB: constructionError))
            {
                throw new InvalidOperationException("errors listener");
            }

            reported.Add(error);
        });

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: (_, factory, _) => Task.FromResult(factory.Construct(() => throw constructionError)),
                strategy: AsyncConcurrencyStrategyFactory.Queue<int>(),
                inputConverter: static v => v);

        source.Send(1);

        TestUtil.WaitUntil(() => reported.Count == 1);

        TestDeliveryException? report = reported[0] as TestDeliveryException;
        await Assert.That(report).IsNotNull();
        await Assert.That(report?.UndeliveredError).IsEqualTo(MaybeInternal.Some<Exception>(constructionError));

        status.Dispose();
    }

    // An operation can end in a transaction, here the one that releases it. The pipeline sends its
    // result after that transaction, and a failure of that send stays in the pipeline. Before, the
    // failure came out of the transaction that released the operation.
    [Test]
    public async Task AFailedDeliveryAfterATransactionDoesNotReachTheCodeThatRanIt()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<int, int> op = new();
        List<Exception> failures = [];
        using IStrongListener l = results.ListenStrong(static _ => throw new InvalidOperationException("results listener"));
        using IStrongListener e = errors.ListenStrong(failures.Add);

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Queue<int>(),
                inputConverter: static v => v);

        source.Send(1);
        TestUtil.WaitUntil(() => op.HasStarted(1));

        await Assert.That(() => Transaction.RunVoid(() => op.Release(input: 1, result: 1))).ThrowsNothing();

        TestUtil.WaitUntil(() => failures.Count == 1);

        await Assert.That(failures[0]).IsTypeOf<TestDeliveryException>();
        await Assert.That(status.Items.Sample().Count).IsEqualTo(0);

        status.Dispose();
    }

    // After a transaction, the report of a failure can fail also. That second failure goes to
    // handleException, and not to the code that ran the transaction, which has no part in this
    // pipeline.
    [Test]
    public async Task ASecondFailureAfterATransactionGoesToHandleException()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<int, int> op = new();
        List<Exception> unhandled = [];
        using IStrongListener l = results.ListenStrong(static _ => throw new InvalidOperationException("results listener"));
        using IStrongListener e = errors.ListenStrong(static _ => throw new InvalidOperationException("errors listener"));

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: unhandled.Add,
                createDeliveryException: TestDeliveryException.Create,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Queue<int>(),
                inputConverter: static v => v);

        source.Send(1);
        TestUtil.WaitUntil(() => op.HasStarted(1));

        await Assert.That(() => Transaction.RunVoid(() => op.Release(input: 1, result: 1))).ThrowsNothing();

        TestUtil.WaitUntil(() => unhandled.Count == 1);

        await Assert.That(unhandled[0]).IsTypeOf<AggregateException>();
        await Assert.That(status.Items.Sample().Count).IsEqualTo(0);

        status.Dispose();
    }

    // Here, the send of the result fails, and the second end fails also. The item thus stays in the
    // queue. The pipeline disposes its CancellationTokenSource only when a transaction that ends the
    // item commits. Thus, the cancellation at the disposal finds it live. Before, the first failure
    // disposed it, and Dispose threw ObjectDisposedException.
    [Test]
    public async Task AnItemThatCannotEndKeepsALiveCancellationForDispose()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<Exception> unhandled = [];
        using IStrongListener l = results.ListenStrong(static _ => throw new InvalidOperationException("results listener"));

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: unhandled.Add,
                createDeliveryException: TestDeliveryException.Create,
                operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v)),
                strategy: new FailSecondCompletionStrategy(),
                inputConverter: static v => v,
                cancelOnDispose: true);

        source.Send(1);

        // The operation ends synchronously. Thus, the pipeline tried each end of the item before this
        // point.
        await Assert.That(status.Items.Sample().Count).IsEqualTo(1);
        await Assert.That(unhandled.Count).IsEqualTo(1);
        await Assert.That(unhandled[0]).IsTypeOf<AggregateException>();
        await Assert.That(status.Dispose).ThrowsNothing();
    }

    [Test]
    public async Task InputConverterIsAppliedBeforeTheStrategySeesTheValue()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        RecordingStrategy<int> strategy = new();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        // TStrategyInput, which is an int, has no inheritance relation to TInput, which is a
        // string. The inputConverter is what permits that.
        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v.ToUpperInvariant())),
                strategy: strategy,
                inputConverter: static v => v.Length);

        source.Send("hello");

        TestUtil.WaitUntil(() => received.Count == 1);

        // The strategy sees only the converted int, and never the initial string.
        await Assert.That(strategy.AdmittedValues).IsEquivalentTo(expected: [5], ordering: CollectionOrdering.Matching);

        // The pipeline publishes the output of the operation.
        await Assert.That(received[0]).IsEqualTo("HELLO");

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task CustomStrategyCanRejectAnIncomingValueOutright()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        RejectNegativeStrategy strategy = new();
        List<int> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v)),
                strategy: strategy,
                inputConverter: static v => v);

        source.Send(-1);
        source.Send(2);

        TestUtil.WaitUntil(() => received.Count == 1);

        // The strategy refused -1. It canceled that item and left it with the Queued status
        // permanently, which is the documented method to refuse a value. Thus, -1 never got to the
        // operation, and only the value of zero or more went through.
        await Assert.That(received).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);

        // A user can see the item that the strategy refused, with the Queued status permanently.
        // That is the cost of this method, and the remarks of AsyncConcurrencyStrategy give
        // it.
        await Assert.That(status.Items.Sample().Any(static i => i is { Value: -1, Status: AsyncItemStatus.Queued }))
            .IsTrue();

        status.Dispose();
        l.Unlisten();
    }

    /// <summary>
    ///     A test for a defect that came from
    ///     <see cref="CustomStrategyCanRejectAnIncomingValueOutright" />. A strategy
    ///     can call <see cref="AsyncMapBase.AsyncQueuedItem{TInput}.Cancel" /> on
    ///     <c>incoming</c> and also return it as an
    ///     <see cref="AsyncMapBase.AsyncToStart{TInput}" /> in the same <c>Admit</c>
    ///     call. The contract of that method permits this, and the method to refuse a
    ///     value above is different, because it cancels the item and never promotes
    ///     it. This sequence stopped the process. The branch in
    ///     <c>PromoteAndLaunch</c> for an item that a cancellation removed ended it in
    ///     the transaction that processes the admission. That end opened a transaction
    ///     of its own, and the <c>Send</c> in it threw
    ///     <c>InvalidOperationException("Send may not be called inside a callback.")</c>
    ///     .
    ///     <c>EndItem</c> defers each end where a transaction is open, and one
    ///     <c>Flush</c> after that transaction gives them to the strategy where a
    ///     <c>Send</c> is legal. This test fails without that.
    /// </summary>
    [Test]
    public async Task Admit_CancelingAndPromotingTheSameItemInOneCall_CompletesItAsCanceledInstead()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<int> received = [];
        IListener l = results.ListenStrong(received.Add);
        CancelAndPromoteSameItemStrategy strategy = new();

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v)),
                strategy: strategy,
                inputConverter: static v => v);

        await Assert.That(() => source.Send(1))
            .ThrowsNothing()
            .Because(
                "Canceling and promoting the same item in one Admit call should complete it as "
                + "Canceled, not crash the transaction that admitted it.");

        Thread.Sleep(100);
        await Assert.That(received.Count).IsEqualTo(0).Because("A canceled outcome must never be published.");

        List<int> canceledCounts;

        lock (strategy.CanceledCounts)
        {
            canceledCounts = [.. strategy.CanceledCounts];
        }

        await Assert.That(canceledCounts)
            .IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching)
            .Because(
                "The cancellation and the promotion end one item, thus OnCanceled sees it one time. "
                + "An empty list says that this end went to OnCompleted instead.");

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task Dispose_StopsFurtherAdmission()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v)),
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v);

        status.Dispose();
        source.Send("after-dispose");

        Thread.Sleep(100);
        await Assert.That(received.Count).IsEqualTo(0);

        l.Unlisten();
    }

    [Test]
    public async Task Dispose_WithCancelOnDisposeTrue_CancelsInFlightItem()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v,
                cancelOnDispose: true);

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        status.Dispose();

        Thread.Sleep(200);
        await Assert.That(received.Count).IsEqualTo(0).Because("A canceled outcome must never be published.");

        l.Unlisten();
    }

    [Test]
    public async Task Dispose_WithCancelOnDisposeFalse_LetsInFlightItemFinishAndPublish()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v,
                cancelOnDispose: false);

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        status.Dispose();
        op.Release(input: "a", result: "A");

        TestUtil.WaitUntil(() => received.Count == 1);
        await Assert.That(received).IsEquivalentTo(expected: ["A"], ordering: CollectionOrdering.Matching);

        l.Unlisten();
    }

    // The pipeline finds the items with a dictionary and a set where it ends more than a few items.
    // This test cancels half of a long queue. Then the release of the item that runs must start the
    // next item that remains.
    [Test]
    public async Task CancelMatchingOnALongQueueEndsTheMatchesAndKeepsTheRestInOrder()
    {
        const int count = 20;
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<IReadOnlyCollection<int>> cancelMatching = Stream.CreateSink<IReadOnlyCollection<int>>();
        ControlledOperation<int, int> op = new();
        List<int> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Queue<int>(),
                inputConverter: static v => v,
                cancelMatching: cancelMatching);

        for (int i = 0; i < count; i++)
        {
            source.Send(i);
        }

        TestUtil.WaitUntil(() => op.HasStarted(0));

        // The odd values from 1 to 19: ten Queued items.
        cancelMatching.Send([.. Enumerable.Range(start: 0, count: count / 2).Select(static i => 2 * i + 1)]);

        int[] remaining = [.. status.Items.Sample().Select(static item => item.Value)];

        op.Release(input: 0, result: 0);
        TestUtil.WaitUntil(() => op.HasStarted(2));

        int[] expectedRemaining = [.. Enumerable.Range(start: 0, count: count / 2).Select(static i => 2 * i)];
        bool anOddValueStarted = Enumerable.Range(start: 0, count: count / 2).Any(i => op.HasStarted(2 * i + 1));

        await Assert.That(remaining).IsEquivalentTo(expected: expectedRemaining, ordering: CollectionOrdering.Matching);
        await Assert.That(anOddValueStarted).IsFalse();
        await Assert.That(received).IsEquivalentTo(expected: [0], ordering: CollectionOrdering.Matching);

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task ItemsAndIsRunning_ReflectQueuedAndRunningStatus()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();

        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Queue<string>(),
                inputConverter: static v => v);

        await Assert.That(status.IsRunning.Sample()).IsFalse();
        await Assert.That(status.Items.Sample().Count).IsEqualTo(0);

        source.Send("a");
        source.Send("b");

        TestUtil.WaitUntil(() => op.HasStarted("a"));
        TestUtil.WaitUntil(() => status.IsRunning.Sample());

        IReadOnlyList<AsyncItem<string>> items = status.Items.Sample();
        await Assert.That(items.Count).IsEqualTo(2);
        await Assert.That(items.Any(static i => i is { Value: "a", Status: AsyncItemStatus.Running })).IsTrue();
        await Assert.That(items.Any(static i => i is { Value: "b", Status: AsyncItemStatus.Queued })).IsTrue();

        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => op.HasStarted("b"));

        op.Release(input: "b", result: "B");
        TestUtil.WaitUntil(() => status.Items.Sample().Count == 0);
        await Assert.That(status.IsRunning.Sample()).IsFalse();

        status.Dispose();
    }

    [Test]
    public async Task NullSourceThrowsArgumentNullException()
    {
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();

        await Assert.That(() =>
                AsyncStreamUtility.MapAsyncImpl<string, string, string>(
                    // ReSharper disable once NullableWarningSuppressionIsUsed - Testing for exception on null.
                    source: null!,
                    results: results,
                    errors: errors,
                    handleException: TestUtil.Unexpected,
                    createDeliveryException: TestDeliveryException.Create,
                    operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v)),
                    strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                    inputConverter: static v => v))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task NullResultsThrowsArgumentNullException()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();

        await Assert.That(() =>
                source.MapAsyncImpl<string, string, string>(
                    // ReSharper disable once NullableWarningSuppressionIsUsed - Testing for exception on null.
                    results: null!,
                    errors: errors,
                    handleException: TestUtil.Unexpected,
                    createDeliveryException: TestDeliveryException.Create,
                    operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v)),
                    strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                    inputConverter: static v => v))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task NullErrorsThrowsArgumentNullException()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();

        await Assert.That(() =>
                source.MapAsyncImpl(
                    results: results,
                    // ReSharper disable once NullableWarningSuppressionIsUsed - Testing for exception on null.
                    errors: null!,
                    handleException: TestUtil.Unexpected,
                    createDeliveryException: TestDeliveryException.Create,
                    operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v)),
                    strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                    inputConverter: static v => v))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task NullHandleExceptionThrowsArgumentNullException()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();

        await Assert.That(() =>
                source.MapAsyncImpl(
                    results: results,
                    errors: errors,
                    // ReSharper disable once NullableWarningSuppressionIsUsed - Testing for exception on null.
                    handleException: null!,
                    createDeliveryException: TestDeliveryException.Create,
                    operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v)),
                    strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                    inputConverter: static v => v))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task NullCreateDeliveryExceptionThrowsArgumentNullException()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();

        await Assert.That(() =>
                source.MapAsyncImpl(
                    results: results,
                    errors: errors,
                    handleException: TestUtil.Unexpected,
                    // ReSharper disable once NullableWarningSuppressionIsUsed - Testing for exception on null.
                    createDeliveryException: null!,
                    operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v)),
                    strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                    inputConverter: static v => v))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task NullOperationThrowsArgumentNullException()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();

        await Assert.That(() =>
                source.MapAsyncImpl(
                    results: results,
                    errors: errors,
                    handleException: TestUtil.Unexpected,
                    createDeliveryException: TestDeliveryException.Create,
                    // ReSharper disable once NullableWarningSuppressionIsUsed - Testing for exception on null.
                    operation: null!,
                    strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                    inputConverter: static v => v))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task NullStrategyThrowsArgumentNullException()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();

        await Assert.That(() =>
                source.MapAsyncImpl(
                    results: results,
                    errors: errors,
                    handleException: TestUtil.Unexpected,
                    createDeliveryException: TestDeliveryException.Create,
                    operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v)),
                    // ReSharper disable once NullableWarningSuppressionIsUsed - Testing for exception on null.
                    strategy: null!,
                    inputConverter: static v => v))
            .ThrowsExactly<ArgumentNullException>();
    }

    /// <summary>
    ///     Starts each item immediately and records the converted value of each item
    ///     at its admission.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class RecordingStrategy<TStrategyInput>
        : AsyncConcurrencyStrategy<TStrategyInput, object?>
    {
        // This state is global and is not the state of one MapAsync call. A TState object holds
        // the state of one MapAsync call.
        private readonly List<TStrategyInput> admittedValues = [];

        public IReadOnlyList<TStrategyInput> AdmittedValues => this.admittedValues;

        protected override object? CreateState() => null;

        protected internal override AsyncStrategyStep<TStrategyInput, object?> Admit(
            object? state,
            AsyncQueuedItem<TStrategyInput> incoming,
            IReadOnlyList<AsyncTrackedItem<TStrategyInput>> tracked)
        {
            this.admittedValues.Add(incoming.Value);

            return new AsyncStrategyStep<TStrategyInput, object?>(
                state: state,
                start: [new AsyncToStart<TStrategyInput>(incoming)]);
        }

        protected internal override AsyncStrategyResult<TStrategyInput, object?> OnCompleted(
            object? state,
            AsyncQueuedItem<TStrategyInput> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<TStrategyInput>> tracked) =>
            new(state: state, publish: true, next: AsyncToStart<TStrategyInput>.None);

        protected internal override AsyncStrategyStep<TStrategyInput, object?> OnCanceled(
            object? state,
            IReadOnlyList<AsyncQueuedItem<TStrategyInput>> canceled,
            IReadOnlyList<AsyncTrackedItem<TStrategyInput>> tracked) =>
            new(state: state, start: AsyncToStart<TStrategyInput>.None);
    }

    /// <summary>
    ///     Starts each item immediately, and throws from each call of OnCompleted
    ///     after the first.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class FailSecondCompletionStrategy : AsyncConcurrencyStrategy<int, object?>
    {
        private int completions;

        protected override object? CreateState() => null;

        protected internal override AsyncStrategyStep<int, object?> Admit(
            object? state,
            AsyncQueuedItem<int> incoming,
            IReadOnlyList<AsyncTrackedItem<int>> tracked) =>
            new(state: state, start: [new AsyncToStart<int>(incoming)]);

        protected internal override AsyncStrategyResult<int, object?> OnCompleted(
            object? state,
            AsyncQueuedItem<int> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<int>> tracked) =>
            Interlocked.Increment(ref this.completions) > 1
                ? throw new InvalidOperationException("second completion")
                : new AsyncStrategyResult<int, object?>(state: state, publish: true, next: AsyncToStart<int>.None);

        protected internal override AsyncStrategyStep<int, object?> OnCanceled(
            object? state,
            IReadOnlyList<AsyncQueuedItem<int>> canceled,
            IReadOnlyList<AsyncTrackedItem<int>> tracked) =>
            new(state: state, start: AsyncToStart<int>.None);
    }

    /// <summary>
    ///     Refuses a negative value. It cancels the value at its admission and never
    ///     promotes it.
    ///     That is the method that the base class documents to refuse a value, and it
    ///     leaves the item with the Queued status permanently. The other method
    ///     cancels the value and also returns it as an
    ///     <see cref="AsyncMapBase.AsyncToStart{TInput}" /> to start in the same call.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class RejectNegativeStrategy : AsyncConcurrencyStrategy<int, object?>
    {
        protected override object? CreateState() => null;

        protected internal override AsyncStrategyStep<int, object?> Admit(
            object? state,
            AsyncQueuedItem<int> incoming,
            IReadOnlyList<AsyncTrackedItem<int>> tracked)
        {
            if (incoming.Value < 0)
            {
                incoming.Cancel();
                return new AsyncStrategyStep<int, object?>(state: state, start: AsyncToStart<int>.None);
            }

            return new AsyncStrategyStep<int, object?>(state: state, start: [new AsyncToStart<int>(incoming)]);
        }

        protected internal override AsyncStrategyResult<int, object?> OnCompleted(
            object? state,
            AsyncQueuedItem<int> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<int>> tracked) =>
            new(state: state, publish: true, next: AsyncToStart<int>.None);

        protected internal override AsyncStrategyStep<int, object?> OnCanceled(
            object? state,
            IReadOnlyList<AsyncQueuedItem<int>> canceled,
            IReadOnlyList<AsyncTrackedItem<int>> tracked) =>
            new(state: state, start: AsyncToStart<int>.None);
    }

    /// <summary>
    ///     Cancels each incoming value and returns it as an
    ///     <see cref="AsyncMapBase.AsyncToStart{TInput}" /> to promote in the same
    ///     call.
    ///     <see cref="RejectNegativeStrategy" /> does not return it. This test thus
    ///     runs the branch in <c>PromoteAndLaunch</c> for an item that a cancellation
    ///     removed with the Queued status, synchronously, in the transaction of the
    ///     admission. The usual path to that branch is a subsequent transaction: a
    ///     send on an external cancelAll stream or cancelMatching stream, or the end
    ///     of one item that promotes a Queued item.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class CancelAndPromoteSameItemStrategy : AsyncConcurrencyStrategy<int, object?>
    {
        public readonly List<int> CanceledCounts = [];

        protected override object? CreateState() => null;

        protected internal override AsyncStrategyStep<int, object?> Admit(
            object? state,
            AsyncQueuedItem<int> incoming,
            IReadOnlyList<AsyncTrackedItem<int>> tracked)
        {
            incoming.Cancel();
            return new AsyncStrategyStep<int, object?>(state: state, start: [new AsyncToStart<int>(incoming)]);
        }

        protected internal override AsyncStrategyResult<int, object?> OnCompleted(
            object? state,
            AsyncQueuedItem<int> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<int>> tracked) =>
            new(state: state, publish: true, next: AsyncToStart<int>.None);

        protected internal override AsyncStrategyStep<int, object?> OnCanceled(
            object? state,
            IReadOnlyList<AsyncQueuedItem<int>> canceled,
            IReadOnlyList<AsyncTrackedItem<int>> tracked)
        {
            lock (this.CanceledCounts)
            {
                this.CanceledCounts.Add(canceled.Count);
            }

            return new AsyncStrategyStep<int, object?>(state: state, start: AsyncToStart<int>.None);
        }
    }
}
