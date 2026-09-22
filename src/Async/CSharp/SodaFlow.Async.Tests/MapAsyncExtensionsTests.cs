using System;
using System.Collections.Generic;
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
                operation: static (v, factory, _) => Task.FromResult(factory.FromResult(v.ToUpperInvariant())),
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
                operation: static (_, factory, _) => Task.FromResult(factory.FromResult("done")),
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
                operation: static (v, factory, _) => Task.FromResult(factory.FromResult(v.ToUpperInvariant())),
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
                operation: (_, _, _) => Task.FromException<ResultConstructor<string>>(thrown),
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
    public async Task ConstructResult_RunsInTheTransactionThatSendsTheResult()
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
                    factory.ConstructResult(() =>
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
    public async Task ConstructResult_DoesNotRunWhenTheStrategyDoesNotPublish()
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
                    factory.ConstructResult(() =>
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
    public async Task ConstructResult_DoesNotRunForAnItemThatACancellationStopped()
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

                        return factory.ConstructResult(() =>
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
    public async Task ConstructResult_AThrowPublishesToErrorsAndNoResult()
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
                    factory.ConstructResult(() => throw thrown)),
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
                    ? Task.FromException<ResultConstructor<string>>(new InvalidOperationException("no"))
                    : Task.FromResult(factory.FromResult(v)),
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

    private class Animal;

    private sealed class Dog : Animal;

    /// <summary>Starts each item immediately and then refuses to publish its result. It shows
    /// that the pipeline makes a result only for an item that it publishes.</summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class DropEverythingStrategy : AsyncConcurrencyStrategy<Unit>
    {
        public readonly List<string> Completions = [];

        protected override Unit CreateState() => Unit.Value;

        protected override IReadOnlyList<AsyncToStart<Unit>> Admit(Unit state, AsyncQueuedItem<Unit> incoming) =>
            [new(incoming)];

        protected override AsyncStrategyResult<Unit> OnCompleted(
            Unit state,
            AsyncQueuedItem<Unit> item,
            AsyncCompletion completion)
        {
            lock (this.Completions)
            {
                this.Completions.Add("completed");
            }

            return new AsyncStrategyResult<Unit>(publish: false, next: AsyncStrategyResult<Unit>.None);
        }
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
            AsyncQueuedItem<TStrategyInput> incoming)
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
            AsyncCompletion completion)
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
