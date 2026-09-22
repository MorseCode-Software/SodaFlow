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
    public async Task OnCompleted_SeesTheQueueWithTheItemThatEnds()
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

        // The item that ended remains in the list, with the status it had.
        await Assert.That(strategy.CompletedSaw)
            .IsEquivalentTo(expected: ["a:R,b:Q", "b:R"], ordering: CollectionOrdering.Matching);

        await Assert.That(received)
            .IsEquivalentTo(expected: ["A", "B"], ordering: CollectionOrdering.Matching);

        status.Dispose();
        l.Unlisten();
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
            AsyncQueuedItem<Unit> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked)
        {
            lock (this.Completions)
            {
                this.Completions.Add("completed");
            }

            return new AsyncStrategyResult<Unit>(publish: false, next: AsyncStrategyResult<Unit>.None);
        }
    }

    /// <summary>
    ///     Queues each item and starts one at a time, as the Queue strategy in the library does,
    ///     but with no queue of its own: it reads the queue of the pipeline. It also records what
    ///     that queue held at each call.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class QueueFromTrackedStrategy : AsyncConcurrencyStrategy<string, Unit>
    {
        public readonly List<string> AdmitSaw = [];
        public readonly List<string> CompletedSaw = [];

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
            AsyncQueuedItem<string> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<string>> tracked)
        {
            lock (this.CompletedSaw)
            {
                this.CompletedSaw.Add(Describe(tracked));
            }

            // The item that ends remains in the list, thus this selects the first Queued item
            // that is a different one.
            AsyncTrackedItem<string>? next =
                tracked.FirstOrDefault(
                    predicate: candidate =>
                        candidate.Status == AsyncItemStatus.Queued
                        && !ReferenceEquals(objA: candidate.Item, objB: item));

            return new AsyncStrategyResult<string>(
                publish: true,
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
            AsyncQueuedItem<TStrategyInput> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<TStrategyInput>> tracked)
        {
            lock (this.Completions)
            {
                completion.MatchVoid(
                    onSucceeded: () => this.Completions.Add("succeeded"),
                    onFailed: e => this.Completions.Add("failed:" + e.Message),
                    onCanceled: () => this.Completions.Add("canceled"));
            }

            return new AsyncStrategyResult<TStrategyInput>(
                publish: true,
                next: AsyncStrategyResult<TStrategyInput>.None);
        }
    }
}
