using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Async.Tests;

public sealed class MapAsyncExtensionsTests
{
    [Test]
    public async Task MapAsync_UnitErasedStrategy_Overload()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v.ToUpperInvariant())),
                strategy: AsyncConcurrencyStrategy.Parallel());

        source.Send("hello");
        TestUtil.WaitUntil(() => received.Count == 1);
        await Assert.That(received[0]).IsEqualTo("HELLO");

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task MapAsync_TStrategyInputWithoutConverter_AcceptsTInputAsSubtypeOfTStrategyInput()
    {
        StreamSink<Dog> source = Stream.CreateSink<Dog>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);
        Dog dog = new();
        AlwaysStartStrategy<Animal> strategy = new();

        AsyncMapStatus<Dog> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: static (_, factory, _) => Task.FromResult(factory.FromValue("done")),
                strategy: strategy);

        source.Send(dog);
        TestUtil.WaitUntil(() => received.Count == 1);

        await Assert.That(strategy.AdmittedValues[0]).IsSameReferenceAs(dog);
        await Assert.That(received[0]).IsEqualTo("done");

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task MapAsync_TStrategyInputWithConverter_AppliesInputConverter()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);
        AlwaysStartStrategy<int> strategy = new();

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: static (v, factory, _) => Task.FromResult(factory.FromValue(v.ToUpperInvariant())),
                strategy: strategy,
                inputConverter: static v => v.Length);

        source.Send("hello");
        TestUtil.WaitUntil(() => received.Count == 1);

        await Assert.That(strategy.AdmittedValues).IsEquivalentTo(expected: [5], ordering: CollectionOrdering.Matching);
        await Assert.That(received[0]).IsEqualTo("HELLO");

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task CancelAll_CancelsEveryTrackedOperation()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<Unit> cancelAll = Stream.CreateSink<Unit>();
        ControlledOperation<string, string> op = new();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Parallel(),
                cancelAll: cancelAll);

        source.Send("a");
        source.Send("b");
        TestUtil.WaitUntil(() => op.HasStarted("a") && op.HasStarted("b"));

        cancelAll.Send(Unit.Value);

        Thread.Sleep(200);

        await Assert.That(received.Count).IsEqualTo(0).Because("A canceled outcome must never be published.");

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task CancelMatching_CancelsOnlyTrackedOperationsForMatchingInputValues()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<IReadOnlyCollection<string>> cancelMatching = Stream.CreateSink<IReadOnlyCollection<string>>();
        ControlledOperation<string, string> op = new();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Parallel(),
                cancelMatching: cancelMatching);

        source.Send("a");
        source.Send("b");
        TestUtil.WaitUntil(() => op.HasStarted("a") && op.HasStarted("b"));

        cancelMatching.Send(["a"]);

        op.Release(input: "b", result: "B");
        TestUtil.WaitUntil(() => received.Count == 1);

        Thread.Sleep(100);
        await Assert.That(received).IsEquivalentTo(expected: ["B"], ordering: CollectionOrdering.Matching);

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task CancelAll_EndsARunningItemAndStopsTrackingIt()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<Unit> cancelAll = Stream.CreateSink<Unit>();
        ControlledOperation<string, string> op = new();

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Parallel(),
                cancelAll: cancelAll);

        Cell<IReadOnlyList<AsyncItem<string>>> tracked = status.Items;

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        // Cancel() runs the registrations of the token on this thread, one of those ends the
        // operation, and the continuation of that operation runs here, inside the callback of the
        // listener for this stream. Complete sends, and a send in a callback throws. The throw
        // went into the machinery of Cancel() and no code reported it, thus the item kept the
        // Running status with no operation behind it.
        cancelAll.Send(Unit.Value);

        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 0);

        await Assert.That(Transaction.Run(tracked.Sample))
            .IsEmpty()
            .Because("a cancellation of a running item should end it and stop the tracking");

        status.Dispose();
    }

    [Test]
    public async Task CancelMatching_EndsAQueuedItemWithNoOtherCompletion()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<IReadOnlyCollection<string>> cancelMatching = Stream.CreateSink<IReadOnlyCollection<string>>();
        ControlledOperation<string, string> op = new();

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Queue(),
                cancelMatching: cancelMatching);

        Cell<IReadOnlyList<AsyncItem<string>>> tracked = status.Items;

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        // "b" waits behind "a" and runs no operation, thus nothing observes its token.
        source.Send("b");
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 2);

        cancelMatching.Send(["b"]);

        // Nothing else completes here. Before this, the end of "b" waited for a promotion, and a
        // promotion comes from the end of "a". Thus "b" stayed in the queue with the Queued
        // status.
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 1);

        await Assert.That(
                Transaction.Run(tracked.Sample).Select(static item => item.Value + ":" + item.Status))
            .IsEquivalentTo(expected: ["a:Running"], ordering: CollectionOrdering.Matching)
            .Because("a cancellation should end a queued item and not wait for another completion");

        await Assert.That(op.HasStarted("b")).IsFalse().Because("a canceled queued item should not run");

        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 0);

        status.Dispose();
    }

    [Test]
    public async Task CancelMatching_EndsSeveralQueuedItemsInOneStep()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<IReadOnlyCollection<string>> cancelMatching = Stream.CreateSink<IReadOnlyCollection<string>>();
        ControlledOperation<string, string> op = new();

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Queue(),
                cancelMatching: cancelMatching);

        Cell<IReadOnlyList<AsyncItem<string>>> tracked = status.Items;

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        source.Send("b");
        source.Send("c");
        source.Send("d");
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 4);

        List<int> counts = [];
        IListener l = tracked.Updates().ListenStrong(items => counts.Add(items.Count));

        // One transaction ends "b", "c", and "d". One end for each of those gives the strategy
        // three decisions, and the queue takes three edits that an observer can see.
        cancelMatching.Send(["b", "c", "d"]);

        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 1);

        Thread.Sleep(100);

        await Assert.That(counts)
            .IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching)
            .Because("the ends of one transaction should make one edit of the queue");

        await Assert.That(op.HasStarted("b") || op.HasStarted("c") || op.HasStarted("d"))
            .IsFalse()
            .Because("no canceled item should start");

        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 0);

        l.Unlisten();
        status.Dispose();
    }

    [Test]
    public async Task CancelMatching_GivesTheStrategyOneDecisionForTheWholeTransaction()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<IReadOnlyCollection<string>> cancelMatching = Stream.CreateSink<IReadOnlyCollection<string>>();
        ControlledOperation<string, string> op = new();
        QueueFromTrackedStrategy strategy = new();

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: strategy,
                cancelMatching: cancelMatching);

        Cell<IReadOnlyList<AsyncItem<string>>> tracked = status.Items;

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        source.Send("b");
        source.Send("c");
        source.Send("d");
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 4);

        cancelMatching.Send(["b", "c", "d"]);

        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 1);

        Thread.Sleep(100);

        List<string> endedSaw;
        List<string> completedSaw;

        lock (strategy.EndedSaw)
        {
            endedSaw = [..strategy.EndedSaw];
        }

        lock (strategy.CompletedSaw)
        {
            completedSaw = [..strategy.CompletedSaw];
        }

        await Assert.That(endedSaw)
            .IsEquivalentTo(expected: ["b,c,d"], ordering: CollectionOrdering.Matching)
            .Because("one transaction should give one decision over each item that ends in it");

        await Assert.That(completedSaw)
            .IsEquivalentTo(expected: ["a:R"], ordering: CollectionOrdering.Matching)
            .Because("the queue of that decision should hold no item that ends in it");

        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 0);

        status.Dispose();
    }

    [Test]
    public async Task CancelOnDisposeTrue_CancelsInFlightItem()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Parallel(),
                cancelOnDispose: true);

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        status.Dispose();

        Thread.Sleep(200);
        await Assert.That(received.Count).IsEqualTo(0);

        l.Unlisten();
    }

    [Test]
    public async Task CancelOnDisposeFalse_LetsInFlightItemFinishAndPublish()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Parallel(),
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
    public async Task FailedOperationPublishesToErrors()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        InvalidOperationException thrown = new("boom");
        List<Exception> received = [];
        IListener l = errors.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: (_, _, _) => Task.FromException<MapAsyncResult<string>>(thrown),
                strategy: AsyncConcurrencyStrategy.Parallel());

        source.Send("hello");
        TestUtil.WaitUntil(() => received.Count == 1);
        await Assert.That(received[0]).IsSameReferenceAs(thrown);

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
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Queue());

        await Assert.That(status.IsRunning.Sample()).IsFalse();
        await Assert.That(status.Items.Sample().Count).IsEqualTo(0);

        source.Send("a");
        source.Send("b");
        TestUtil.WaitUntil(() => op.HasStarted("a"));
        TestUtil.WaitUntil(() => status.IsRunning.Sample());

        IReadOnlyList<AsyncItem<string>> items = status.Items.Sample();
        await Assert.That(items.Count).IsEqualTo(2);

        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => op.HasStarted("b"));
        op.Release(input: "b", result: "B");

        TestUtil.WaitUntil(() => status.Items.Sample().Count == 0);
        await Assert.That(status.IsRunning.Sample()).IsFalse();

        status.Dispose();
    }

    [Test]
    public async Task NonGenericStatus_GivesIsRunningAndDisposalWithoutTheInputType()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();

        // The type on the left is the point of this test: a caller that reads IsRunning and
        // disposes the pipeline does not name the input type.
        AsyncMapStatus status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Queue());

        await Assert.That(status.IsRunning.Sample()).IsFalse();

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));
        TestUtil.WaitUntil(() => status.IsRunning.Sample());

        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => !status.IsRunning.Sample());

        status.Dispose();

        // A second disposal does nothing, which the base must keep true now that it holds
        // Dispose.
        status.Dispose();
    }

    [Test]
    public async Task Construct_RunsInTheTransactionThatSendsTheResult()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);
        bool? inTransaction = null;

        AsyncMapStatus status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: (v, factory, _) => Task.FromResult(
                    factory.Construct(() =>
                    {
                        inTransaction = Transaction.IsActive();

                        return v.ToUpperInvariant();
                    })),
                strategy: AsyncConcurrencyStrategy.Parallel());

        source.Send("hello");
        TestUtil.WaitUntil(() => received.Count == 1);

        // A result that holds a cell or a stream needs this, and it is the cause for a
        // constructor and not a value in the operation.
        await Assert.That(inTransaction).IsTrue();
        await Assert.That(received[0]).IsEqualTo("HELLO");

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task Construct_DoesNotRunWhenTheStrategyDoesNotPublish()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);
        DropEverythingStrategy strategy = new();
        List<string> constructions = [];

        AsyncMapStatus status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: (v, factory, _) => Task.FromResult(
                    factory.Construct(() =>
                    {
                        lock (constructions)
                        {
                            constructions.Add(v);
                        }

                        return v;
                    })),
                strategy: strategy);

        source.Send("hello");
        TestUtil.WaitUntil(() => strategy.Completions.Count == 1);

        await Assert.That(received.Count).IsEqualTo(0);
        await Assert.That(constructions.Count).IsEqualTo(0);

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task Construct_DoesNotRunForAnItemThatACancellationStopped()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<Unit> cancelAll = Stream.CreateSink<Unit>();
        List<string> received = [];
        List<Exception> failures = [];
        IListener lr = results.ListenStrong(received.Add);
        IListener le = errors.ListenStrong(failures.Add);
        AlwaysStartStrategy<Unit> strategy = new();
        ManualResetEventSlim release = new(false);
        List<string> constructions = [];

        // The operation ignores its token and returns a constructor, thus the cancellation and
        // not the operation is what stops the result here.
        AsyncMapStatus status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: (v, factory, _) => Task.Run(
                    function: () =>
                    {
                        release.Wait(millisecondsTimeout: 5000);

                        return factory.Construct(() =>
                        {
                            lock (constructions)
                            {
                                constructions.Add(v);
                            }

                            return v;
                        });
                    },
                    cancellationToken: CancellationToken.None),
                strategy: strategy,
                cancelAll: cancelAll);

        source.Send("a");
        TestUtil.WaitUntil(() => status.IsRunning.Sample());

        cancelAll.Send(Unit.Value);
        release.Set();
        TestUtil.WaitUntil(() => strategy.Completions.Count == 1);

        await Assert.That(strategy.Completions[0]).IsEqualTo("canceled");
        await Assert.That(received.Count).IsEqualTo(0);
        await Assert.That(failures.Count).IsEqualTo(0);
        await Assert.That(constructions.Count).IsEqualTo(0);

        status.Dispose();
        lr.Unlisten();
        le.Unlisten();
    }

    [Test]
    public async Task Construct_AThrowPublishesToErrorsAndNoResult()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        InvalidOperationException thrown = new("boom");
        List<string> received = [];
        List<Exception> failures = [];
        IListener lr = results.ListenStrong(received.Add);
        IListener le = errors.ListenStrong(failures.Add);

        AsyncMapStatus status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: (_, factory, _) => Task.FromResult(
                    factory.Construct(() => throw thrown)),
                strategy: AsyncConcurrencyStrategy.Parallel());

        source.Send("hello");
        TestUtil.WaitUntil(() => failures.Count == 1);

        await Assert.That(failures[0]).IsSameReferenceAs(thrown);
        await Assert.That(received.Count).IsEqualTo(0);

        status.Dispose();
        lr.Unlisten();
        le.Unlisten();
    }

    [Test]
    public async Task OnCompleted_SeesHowTheOperationEnded()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        AlwaysStartStrategy<Unit> strategy = new();

        AsyncMapStatus status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: static (v, factory, _) => v == "fail"
                    ? Task.FromException<MapAsyncResult<string>>(new InvalidOperationException("no"))
                    : Task.FromResult(factory.FromValue(v)),
                strategy: strategy);

        source.Send("ok");
        TestUtil.WaitUntil(() => strategy.Completions.Count == 1);

        source.Send("fail");
        TestUtil.WaitUntil(() => strategy.Completions.Count == 2);

        // The strategy reads how the operation ended and never a result.
        await Assert.That(strategy.Completions)
            .IsEquivalentTo(expected: ["succeeded", "failed:no"], ordering: CollectionOrdering.Matching);

        status.Dispose();
    }

    [Test]
    public async Task Admit_SeesTheQueueWithoutTheIncomingItem()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        QueueFromTrackedStrategy strategy = new();

        AsyncMapStatus status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: strategy);

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        source.Send("b");
        TestUtil.WaitUntil(() => strategy.AdmitSaw.Count == 2);

        source.Send("c");
        TestUtil.WaitUntil(() => strategy.AdmitSaw.Count == 3);

        // The first admission sees an empty pipeline. The second sees the first item Running,
        // and no call sees the value that it is admitting.
        await Assert.That(strategy.AdmitSaw)
            .IsEquivalentTo(
                expected: [string.Empty, "a:R", "a:R,b:Q"],
                ordering: CollectionOrdering.Matching);

        status.Dispose();
    }

    [Test]
    public async Task OnCompleted_SeesTheQueueWithoutTheItemThatEnds()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        QueueFromTrackedStrategy strategy = new();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: strategy);

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));
        source.Send("b");
        TestUtil.WaitUntil(() => strategy.AdmitSaw.Count == 2);

        // This strategy keeps no queue of its own. It takes the next item out of the list that
        // the pipeline gives it, which is the point of the test.
        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => op.HasStarted("b"));

        op.Release(input: "b", result: "B");
        TestUtil.WaitUntil(() => received.Count == 2);

        // The pipeline takes the item that ends out of the list before it calls OnCompleted, thus
        // the first call sees only the item behind it, and the second call sees an empty list.
        await Assert.That(strategy.CompletedSaw)
            .IsEquivalentTo(expected: ["b:Q", string.Empty], ordering: CollectionOrdering.Matching);

        await Assert.That(received)
            .IsEquivalentTo(expected: ["A", "B"], ordering: CollectionOrdering.Matching);

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task Execute_GivesTheResultAndPublishesTheSameValue()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<string> published = [];
        IListener l = results.ListenStrong(published.Add);

        AsyncMapStatus<string, string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Parallel());

        Task<string> task = status.Execute("a");

        TestUtil.WaitUntil(() => op.HasStarted("a"));
        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => task.IsCompleted);

        await Assert.That(task.Status).IsEqualTo(TaskStatus.RanToCompletion);
        await Assert.That(await task).IsEqualTo("A");

        await Assert.That(published)
            .IsEquivalentTo(expected: ["A"], ordering: CollectionOrdering.Matching)
            .Because("the value of the Task is the value that the results stream gets");

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task Execute_CarriesTheExceptionOfTheOperation()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        InvalidOperationException thrown = new("boom");
        List<Exception> reported = [];
        IListener l = errors.ListenStrong(reported.Add);

        AsyncMapStatus<string, string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Parallel());

        Task<string> task = status.Execute("a");

        TestUtil.WaitUntil(() => op.HasStarted("a"));
        op.Fail(input: "a", error: thrown);
        TestUtil.WaitUntil(() => task.IsCompleted);

        await Assert.That(task.IsFaulted).IsTrue();
        await Assert.That(task.Exception?.InnerException).IsSameReferenceAs(thrown);

        TestUtil.WaitUntil(() => reported.Count == 1);

        await Assert.That(reported[0])
            .IsSameReferenceAs(thrown)
            .Because("the errors stream gets the same exception that the Task carries");

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task Execute_IsCanceledByACancellation()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<Unit> cancelAll = Stream.CreateSink<Unit>();
        ControlledOperation<string, string> op = new();

        AsyncMapStatus<string, string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Parallel(),
                cancelAll: cancelAll);

        Task<string> task = status.Execute("a");

        TestUtil.WaitUntil(() => op.HasStarted("a"));
        cancelAll.Send(Unit.Value);
        TestUtil.WaitUntil(() => task.IsCompleted);

        await Assert.That(task.IsCanceled)
            .IsTrue()
            .Because("a cancellation of the value cancels the Task of Execute");

        status.Dispose();
    }

    [Test]
    public async Task Execute_IsCanceledWhereTheStrategyDoesNotPublish()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();

        AsyncMapStatus<string, string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: new DropEverythingStrategy());

        Task<string> task = status.Execute("a");

        TestUtil.WaitUntil(() => op.HasStarted("a"));
        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => task.IsCompleted);

        await Assert.That(task.IsCanceled)
            .IsTrue()
            .Because("a strategy that does not publish says that no code wants the result");

        status.Dispose();
    }

    [Test]
    public async Task Execute_IsCanceledWhereTheStrategyRefusesTheValue()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();

        AsyncMapStatus<string, string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: new RefuseEverythingStrategy());

        Task<string> task = status.Execute("a");

        TestUtil.WaitUntil(() => task.IsCompleted);

        await Assert.That(task.IsCanceled)
            .IsTrue()
            .Because("a refused value never ends, thus nothing else answers the Task");

        await Assert.That(op.HasStarted("a")).IsFalse().Because("a refused value runs no operation");

        status.Dispose();
    }

    [Test]
    public async Task Execute_IsCanceledAfterADisposal()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();

        AsyncMapStatus<string, string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Parallel());

        status.Dispose();

        Task<string> task = status.Execute("a");

        TestUtil.WaitUntil(() => task.IsCompleted);

        await Assert.That(task.IsCanceled)
            .IsTrue()
            .Because("a disposed pipeline admits no value, thus the Task cannot give a result");

        await Assert.That(op.HasStarted("a")).IsFalse();
    }

    [Test]
    public async Task Execute_ObeysTheStrategy()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();

        AsyncMapStatus<string, string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Queue());

        Task<string> first = status.Execute("a");

        TestUtil.WaitUntil(() => op.HasStarted("a"));

        Task<string> second = status.Execute("b");

        Thread.Sleep(100);

        await Assert.That(op.HasStarted("b"))
            .IsFalse()
            .Because("the Queue strategy holds the second value while the first one runs");

        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => op.HasStarted("b"));

        op.Release(input: "b", result: "B");
        TestUtil.WaitUntil(() => first.IsCompleted && second.IsCompleted);

        await Assert.That(await first).IsEqualTo("A");
        await Assert.That(await second).IsEqualTo("B");

        status.Dispose();
    }

    [Test]
    public async Task Execute_AndTheSourceStreamShareOneQueue()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<string> published = [];
        IListener l = results.ListenStrong(published.Add);

        AsyncMapStatus<string, string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Queue());

        Cell<IReadOnlyList<AsyncItem<string>>> tracked = status.Items;

        // A value from the source stream starts, and an Execute value waits behind it. The two go
        // through one stream, thus this shows that the merge of the two keeps each value.
        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        Task<string> second = status.Execute("b");
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 2);

        await Assert.That(op.HasStarted("b"))
            .IsFalse()
            .Because("the Execute value obeys the same queue as a value from the source stream");

        // A third value from the source stream, behind the Execute value.
        source.Send("c");
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 3);

        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => op.HasStarted("b"));

        op.Release(input: "b", result: "B");
        TestUtil.WaitUntil(() => op.HasStarted("c"));

        op.Release(input: "c", result: "C");
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 0);

        await Assert.That(await second)
            .IsEqualTo("B")
            .Because("the Task of Execute gives the result of its own value");

        TestUtil.WaitUntil(() => published.Count == 3);

        await Assert.That(published)
            .IsEquivalentTo(expected: ["A", "B", "C"], ordering: CollectionOrdering.Matching)
            .Because("each value reaches the results stream, in the sequence of the admissions");

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task Execute_InsideATransaction_Throws()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();

        AsyncMapStatus<string, string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Parallel());

        // The closure below reads this delegate and not `status`. A closure over `status` reads it
        // after the disposal at the end of this method.
        Func<string, Task<string>> execute = status.Execute;

        bool threw = false;

        try
        {
            Transaction.RunVoid(() => _ = execute("a"));
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        status.Dispose();

        await Assert.That(threw)
            .IsTrue()
            .Because("Execute sends into the graph, thus a transaction must not be open");
    }

    [Test]
    public async Task Execute_TheContinuationDoesNotRunInATransaction()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();

        AsyncMapStatus<string, string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Parallel());

        Task<string> task = status.Execute("a");

        TestUtil.WaitUntil(() => op.HasStarted("a"));

        // The pipeline answers the Task in the transaction that publishes. Where that answer runs
        // the continuation of an await on that thread, a send from the continuation joins that
        // transaction. It throws where the code is in a callback. ExecuteSynchronously asks for the
        // worst condition: the continuation runs on the thread that answers, if the Task permits.
        Task<bool> wasInTransaction = task.ContinueWith(
            continuationFunction: static _ => Transaction.IsActive(),
            cancellationToken: CancellationToken.None,
            continuationOptions: TaskContinuationOptions.ExecuteSynchronously,
            scheduler: TaskScheduler.Default);

        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => wasInTransaction.IsCompleted);

        await Assert.That(await wasInTransaction)
            .IsFalse()
            .Because("the Task must not run its continuations in the transaction that publishes");

        status.Dispose();
    }

    /// <summary>
    ///     Refuses each value: it cancels the value and never promotes it, which is the documented
    ///     method for a strategy to refuse one. The entry then keeps the Queued status permanently,
    ///     thus no end comes for it.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class RefuseEverythingStrategy : AsyncConcurrencyStrategy<Unit>
    {
        protected override Unit CreateState() => Unit.Value;

        protected override IReadOnlyList<AsyncToStart<Unit>> Admit(
            Unit state,
            AsyncQueuedItem<Unit> incoming,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked)
        {
            incoming.Cancel();

            return AsyncStrategyResult<Unit>.None;
        }

        protected override AsyncStrategyResult<Unit> OnCompleted(
            Unit state,
            IReadOnlyList<AsyncEnd<Unit>> ended,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked) =>
            new(publish: ItemsOf(ended), next: AsyncStrategyResult<Unit>.None);
    }

    private class Animal;

    private sealed class Dog : Animal;

    /// <summary>Starts each item immediately and then refuses to publish its result. It shows
    /// that the pipeline makes a result only for an item that it publishes.</summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class DropEverythingStrategy : AsyncConcurrencyStrategy<Unit>
    {
        public readonly List<string> Completions = [];

        protected override Unit CreateState() => Unit.Value;

        protected override IReadOnlyList<AsyncToStart<Unit>> Admit(
            Unit state,
            AsyncQueuedItem<Unit> incoming,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked) =>
            [new(incoming)];

        protected override AsyncStrategyResult<Unit> OnCompleted(
            Unit state,
            IReadOnlyList<AsyncEnd<Unit>> ended,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked)
        {
            lock (this.Completions)
            {
                for (int i = 0; i < ended.Count; i++)
                {
                    this.Completions.Add("completed");
                }
            }

            return new AsyncStrategyResult<Unit>(
                publish: AsyncStrategyResult<Unit>.PublishNone,
                next: AsyncStrategyResult<Unit>.None);
        }
    }

    /// <summary>
    ///     Queues each item and starts one at a time, as the Queue strategy in the library does,
    ///     but with no queue of its own: it reads the queue of the pipeline. It also records what
    ///     that queue held at each call.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    [Test]
    public async Task ResultThatFeedsTheInputStartsTheNextItemInTheSameTransaction()
    {
        StreamSink<string> trigger = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        // The input depends on the result. Thus Complete publishes, the input fires, and Admit
        // runs in the transaction that Complete opened. The two read the queue, and the two must
        // see that the item which ends here does not count as an item that runs.
        Stream<string> input = trigger.OrElse(results.Filter(static r => r.Length < 2).Map(static r => r + "x"));

        AsyncMapStatus<string> status =
            input.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Queue());

        Cell<IReadOnlyList<AsyncItem<string>>> tracked = status.Items;

        trigger.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        // This is the assertion of the test. The result admits "ax" in the transaction that ends
        // "a". Where OnCompleted and Admit each read the queue from the start of that transaction,
        // the two find no item to start. OnCompleted finds nothing Queued, and Admit finds "a"
        // with the Running status. Thus, no code starts "ax", here or in a transaction after it.
        op.Release(input: "a", result: "a");
        TestUtil.WaitUntil(() => op.HasStarted("ax"));

        // The queue after that transaction. Two edits got to the cell by two paths here: Complete
        // sent one, and the transform that admitted "ax" returned one. The cell must keep the edit
        // that this pipeline made last, which is the admission. A cell that keeps the other one
        // holds an empty queue, and this assertion fails.
        await Assert.That(Transaction.Run(tracked.Sample).Select(static item => item.Value + ":" + item.Status))
            .IsEquivalentTo(expected: ["ax:Running"], ordering: CollectionOrdering.Matching)
            .Because("the queue should hold the item that the result admitted");

        op.Release(input: "ax", result: "ax");
        TestUtil.WaitUntil(() => received.Count == 2);

        await Assert.That(received)
            .IsEquivalentTo(expected: ["a", "ax"], ordering: CollectionOrdering.Matching);

        await Assert.That(Transaction.Run(tracked.Sample))
            .IsEmpty()
            .Because("the pipeline should hold nothing once the chain ends");

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task TheQueueAfterACompletionHoldsOnlyTheItemThatStarts()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Queue());

        Cell<IReadOnlyList<AsyncItem<string>>> tracked = status.Items;

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));
        source.Send("b");

        // One transaction removes "a" and promotes "b". The cell takes the value that this
        // pipeline made, thus the two edits get to it in the sequence that made them. A cell that
        // took the removals, then the additions, then the promotions can give a different answer.
        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => op.HasStarted("b"));

        IReadOnlyList<AsyncItem<string>> items = Transaction.Run(tracked.Sample);

        await Assert.That(items.Select(static item => item.Value + ":" + item.Status))
            .IsEquivalentTo(expected: ["b:Running"], ordering: CollectionOrdering.Matching)
            .Because("the item that ended should be gone and the item that started should run");

        op.Release(input: "b", result: "B");
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 0);

        status.Dispose();
    }

    private sealed class QueueFromTrackedStrategy : AsyncConcurrencyStrategy<string, Unit>
    {
        public readonly List<string> AdmitSaw = [];
        public readonly List<string> CompletedSaw = [];
        public readonly List<string> EndedSaw = [];

        protected override Unit CreateState() => Unit.Value;

        protected override IReadOnlyList<AsyncToStart<string>> Admit(
            Unit state,
            AsyncQueuedItem<string> incoming,
            IReadOnlyList<AsyncTrackedItem<string>> tracked)
        {
            lock (this.AdmitSaw)
            {
                this.AdmitSaw.Add(Describe(tracked));
            }

            // Nothing runs when no tracked item has the Running status.
            bool idle = tracked.All(static item => item.Status != AsyncItemStatus.Running);

            return idle ? [new AsyncToStart<string>(incoming)] : [];
        }

        protected override AsyncStrategyResult<string> OnCompleted(
            Unit state,
            IReadOnlyList<AsyncEnd<string>> ended,
            IReadOnlyList<AsyncTrackedItem<string>> tracked)
        {
            lock (this.CompletedSaw)
            {
                this.CompletedSaw.Add(Describe(tracked));
            }

            lock (this.EndedSaw)
            {
                this.EndedSaw.Add(string.Join(separator: ",", values: ended.Select(static e => e.Item.Value)));
            }

            // The queue holds no item that ends now, thus this takes the first Queued item with no
            // test against them.
            AsyncTrackedItem<string>? next =
                tracked.FirstOrDefault(predicate: static candidate => candidate.Status == AsyncItemStatus.Queued);

            return new AsyncStrategyResult<string>(
                publish: ItemsOf(ended),
                next: next is null ? AsyncStrategyResult<string>.None : [new AsyncToStart<string>(next.Item)]);
        }

        private static string Describe(IEnumerable<AsyncTrackedItem<string>> tracked) =>
            string.Join(
                separator: ",",
                values: tracked.Select(
                    static e => e.Item.Value + ":" + (e.Status == AsyncItemStatus.Running ? "R" : "Q")));
    }

    /// <summary>
    ///     Starts each item immediately, as the Parallel strategy in the library does. It
    ///     operates on each TStrategyInput, and records the value at the admission and how each
    ///     item ended. Thus, a test can show that a converter ran, and not only that it compiled.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class AlwaysStartStrategy<TStrategyInput> : AsyncConcurrencyStrategy<TStrategyInput, Unit>
    {
        public readonly List<TStrategyInput> AdmittedValues = [];
        public readonly List<string> Completions = [];

        protected override Unit CreateState() => Unit.Value;

        protected override IReadOnlyList<AsyncToStart<TStrategyInput>> Admit(
            Unit state,
            AsyncQueuedItem<TStrategyInput> incoming,
            IReadOnlyList<AsyncTrackedItem<TStrategyInput>> tracked)
        {
            lock (this.AdmittedValues)
            {
                this.AdmittedValues.Add(incoming.Value);
            }

            return [new AsyncToStart<TStrategyInput>(incoming)];
        }

        protected override AsyncStrategyResult<TStrategyInput> OnCompleted(
            Unit state,
            IReadOnlyList<AsyncEnd<TStrategyInput>> ended,
            IReadOnlyList<AsyncTrackedItem<TStrategyInput>> tracked)
        {
            lock (this.Completions)
            {
                foreach (AsyncEnd<TStrategyInput> end in ended)
                {
                    end.Completion.MatchVoid(
                        onSucceeded: () => this.Completions.Add("succeeded"),
                        onFailed: e => this.Completions.Add("failed:" + e.Message),
                        onCanceled: () => this.Completions.Add("canceled"));
                }
            }

            return new AsyncStrategyResult<TStrategyInput>(
                publish: ItemsOf(ended),
                next: AsyncStrategyResult<TStrategyInput>.None);
        }
    }
}
