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
                operation: static (v, _) => Task.FromResult(v.ToUpperInvariant()),
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v,
                resultConverter: static v => v);

        source.Send("hello");

        TestUtil.WaitUntil(() => received.Count == 1);
        await Assert.That(received[0]).IsEqualTo("HELLO");

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
                operation: (_, _) => Task.FromException<string>(thrown),
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v,
                resultConverter: static v => v);

        source.Send("hello");

        TestUtil.WaitUntil(() => received.Count == 1);
        await Assert.That(received[0]).IsSameReferenceAs(thrown);

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task InputAndResultConvertersAreAppliedBeforeTheStrategySeesThem()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        RecordingStrategy<int, int> strategy = new();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        // TStrategyInput and TStrategyResult, which are an int and a length, have no inheritance
        // relation to TInput and TResult, which are a string. Only this fully general overload
        // permits that.
        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                operation: static (v, _) => Task.FromResult(v.ToUpperInvariant()),
                strategy: strategy,
                inputConverter: static v => v.Length,
                resultConverter: static v => v.Length);

        source.Send("hello");

        TestUtil.WaitUntil(() => received.Count == 1);

        // The strategy sees only the converted int, and never the initial string.
        await Assert.That(strategy.AdmittedValues).IsEquivalentTo(expected: [5], ordering: CollectionOrdering.Matching);

        // The TResult that the pipeline publishes is the output of the operation, with no
        // conversion.
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
                operation: static (v, _) => Task.FromResult(v),
                strategy: strategy,
                inputConverter: static v => v,
                resultConverter: static v => v);

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
    ///     <see cref="CustomStrategyCanRejectAnIncomingValueOutright" />. A strategy can call
    ///     <see cref="AsyncMapBase.AsyncQueuedItem{TInput}.Cancel" /> on <c>incoming</c> and also
    ///     return it as an <see cref="AsyncMapBase.AsyncToStart{TInput}" /> in the same
    ///     <c>Admit</c> call. The contract of that method permits this, and the method to refuse a
    ///     value above is different, because it cancels the item and never promotes it. This
    ///     sequence now stops the process. The branch in <c>PromoteAndLaunch</c> for an item that
    ///     a cancellation removed calls <c>Complete</c> synchronously, inline, in the transaction
    ///     that processes the admission. The branch for a usual start below it defers through
    ///     <c>TransactionInternal.PostImpl</c> to prevent that. <c>Complete</c> then opens its own
    ///     transaction with <c>TransactionInternal.RunImpl</c>, which is not legal while a
    ///     transaction is open, and the <c>Send</c> in it throws
    ///     <c>InvalidOperationException("Send may not be called inside a callback.")</c>. This
    ///     test fails until <c>PromoteAndLaunch</c> defers that branch in the same manner.
    /// </summary>
    [Test]
    public async Task Admit_CancelingAndPromotingTheSameItemInOneCall_CompletesItAsCanceledInstead()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> results = Stream.CreateSink<int>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<int> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<int> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                operation: static (v, _) => Task.FromResult(v),
                strategy: new CancelAndPromoteSameItemStrategy(),
                inputConverter: static v => v,
                resultConverter: static v => v);

        await Assert.That(() => source.Send(1))
            .ThrowsNothing()
            .Because(
                "Canceling and promoting the same item in one Admit call should complete it as "
                + "Canceled, not crash the transaction that admitted it.");

        Thread.Sleep(100);
        await Assert.That(received.Count).IsEqualTo(0).Because("A canceled outcome must never be published.");

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
                operation: static (v, _) => Task.FromResult(v),
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v,
                resultConverter: static v => v);

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
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v,
                resultConverter: static v => v,
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
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v,
                resultConverter: static v => v,
                cancelOnDispose: false);

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        status.Dispose();
        op.Release(input: "a", result: "A");

        TestUtil.WaitUntil(() => received.Count == 1);
        await Assert.That(received).IsEquivalentTo(expected: ["A"], ordering: CollectionOrdering.Matching);

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
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Queue<string>(),
                inputConverter: static v => v,
                resultConverter: static v => v);

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
                AsyncStreamUtility.MapAsyncImpl<string, string, string, string>(
                    // ReSharper disable once NullableWarningSuppressionIsUsed - Testing for exception on null.
                    source: null!,
                    results: results,
                    errors: errors,
                    operation: static (v, _) => Task.FromResult(v),
                    strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                    inputConverter: static v => v,
                    resultConverter: static v => v))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task NullResultsThrowsArgumentNullException()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();

        await Assert.That(() =>
                source.MapAsyncImpl(
                    // ReSharper disable once NullableWarningSuppressionIsUsed - Testing for exception on null.
                    results: null!,
                    errors: errors,
                    operation: static (v, _) => Task.FromResult(v),
                    strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                    inputConverter: static v => v,
                    resultConverter: static v => v))
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
                    operation: static (v, _) => Task.FromResult(v),
                    strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                    inputConverter: static v => v,
                    resultConverter: static v => v))
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
                    // ReSharper disable once NullableWarningSuppressionIsUsed - Testing for exception on null.
                    operation: null!,
                    strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                    inputConverter: static v => v,
                    resultConverter: static v => v))
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
                    operation: static (v, _) => Task.FromResult(v),
                    // ReSharper disable once NullableWarningSuppressionIsUsed - Testing for exception on null.
                    strategy: null!,
                    inputConverter: static v => v,
                    resultConverter: static v => v))
            .ThrowsExactly<ArgumentNullException>();
    }

    /// <summary>Starts each item immediately and records the converted value of each item at its
    /// admission.</summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class RecordingStrategy<TStrategyInput, TStrategyResult>
        : AsyncConcurrencyStrategy<TStrategyInput, TStrategyResult, object?>
    {
        // This state is global and is not the state of one MapAsync call. A TState object holds
        // the state of one MapAsync call.
        private readonly List<TStrategyInput> admittedValues = [];

        public IReadOnlyList<TStrategyInput> AdmittedValues => this.admittedValues;

        protected override object? CreateState() => null;

        protected internal override IReadOnlyList<AsyncToStart<TStrategyInput>> Admit(
            object? state,
            AsyncQueuedItem<TStrategyInput> incoming)
        {
            this.admittedValues.Add(incoming.Value);

            return [new AsyncToStart<TStrategyInput>(incoming)];
        }

        protected internal override AsyncStrategyResult<TStrategyInput> OnCompleted(
            object? state,
            AsyncQueuedItem<TStrategyInput> item,
            AsyncOutcome<TStrategyResult> outcome) =>
            new(publish: true, next: AsyncStrategyResult<TStrategyInput>.None);
    }

    /// <summary>
    ///     Refuses a negative value. It cancels the value at its admission and never promotes it.
    ///     That is the method that the base class documents to refuse a value, and it leaves the
    ///     item with the Queued status permanently. The other method cancels the value and also
    ///     returns it as an <see cref="AsyncMapBase.AsyncToStart{TInput}" /> to start in the same
    ///     call.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class RejectNegativeStrategy : AsyncConcurrencyStrategy<int, int, object?>
    {
        protected override object? CreateState() => null;

        protected internal override IReadOnlyList<AsyncToStart<int>> Admit(
            object? state,
            AsyncQueuedItem<int> incoming)
        {
            if (incoming.Value < 0)
            {
                incoming.Cancel();
                return AsyncStrategyResult<int>.None;
            }

            return [new AsyncToStart<int>(incoming)];
        }

        protected internal override AsyncStrategyResult<int> OnCompleted(
            object? state,
            AsyncQueuedItem<int> item,
            AsyncOutcome<int> outcome) =>
            new(publish: true, next: AsyncStrategyResult<int>.None);
    }

    /// <summary>
    ///     Cancels each incoming value and returns it as an
    ///     <see cref="AsyncMapBase.AsyncToStart{TInput}" /> to promote in the same call.
    ///     <see cref="RejectNegativeStrategy" /> does not return it. This test thus runs the
    ///     branch in <c>PromoteAndLaunch</c> for an item that a cancellation removed with the
    ///     Queued status, synchronously, in the transaction of the admission. The usual path to
    ///     that branch is a subsequent transaction: a send on an external cancelAll stream or
    ///     cancelMatching stream, or the end of one item that promotes a Queued item.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class CancelAndPromoteSameItemStrategy : AsyncConcurrencyStrategy<int, int, object?>
    {
        protected override object? CreateState() => null;

        protected internal override IReadOnlyList<AsyncToStart<int>> Admit(
            object? state,
            AsyncQueuedItem<int> incoming)
        {
            incoming.Cancel();
            return [new AsyncToStart<int>(incoming)];
        }

        protected internal override AsyncStrategyResult<int> OnCompleted(
            object? state,
            AsyncQueuedItem<int> item,
            AsyncOutcome<int> outcome) =>
            new(publish: true, next: AsyncStrategyResult<int>.None);
    }
}
