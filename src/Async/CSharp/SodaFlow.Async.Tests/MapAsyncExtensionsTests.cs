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

public class MapAsyncExtensionsTests
{
    [Test]
    public async Task MapAsync_UnitErasedStrategy_Overload()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<string> received = new();
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: static (v, _) => Task.FromResult(v.ToUpperInvariant()),
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
        List<string> received = new();
        IListener l = results.ListenStrong(received.Add);
        Dog dog = new();
        AlwaysStartStrategy<Animal, Unit> strategy = new();

        AsyncMapStatus<Dog> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: static (_, _) => Task.FromResult("done"),
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
        List<string> received = new();
        IListener l = results.ListenStrong(received.Add);
        AlwaysStartStrategy<int, Unit> strategy = new();

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: static (v, _) => Task.FromResult(v.ToUpperInvariant()),
                strategy: strategy,
                inputConverter: static v => v.Length);

        source.Send("hello");
        TestUtil.WaitUntil(() => received.Count == 1);

        await Assert.That(strategy.AdmittedValues).IsEquivalentTo(new[] { 5 }, CollectionOrdering.Matching);
        await Assert.That(received[0]).IsEqualTo("HELLO");

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task MapAsync_TStrategyResultWithoutConverter_AcceptsTResultAsSubtypeOfTStrategyResult()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<Dog> results = Stream.CreateSink<Dog>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<Dog> received = new();
        IListener l = results.ListenStrong(received.Add);
        Dog dog = new();

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: (_, _) => Task.FromResult(dog),
                strategy: new AlwaysStartStrategy<Unit, Animal>());

        source.Send("hello");
        TestUtil.WaitUntil(() => received.Count == 1);
        await Assert.That(received[0]).IsSameReferenceAs(dog);

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task MapAsync_TStrategyResultWithConverter_AppliesResultConverter()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<string> received = new();
        IListener l = results.ListenStrong(received.Add);
        AlwaysStartStrategy<Unit, int> strategy = new();

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: static (v, _) => Task.FromResult(v.ToUpperInvariant()),
                strategy: strategy,
                resultConverter: static v => v.Length);

        source.Send("hello");
        TestUtil.WaitUntil(() => received.Count == 1);

        await Assert.That(strategy.CompletedResults).IsEquivalentTo(new[] { 5 }, CollectionOrdering.Matching);
        await Assert.That(received[0]).IsEqualTo("HELLO");

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task MapAsync_FourTypeArgsWithoutConverters_AcceptsBothAsSubtypes()
    {
        StreamSink<Dog> source = Stream.CreateSink<Dog>();
        StreamSink<Dog> results = Stream.CreateSink<Dog>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<Dog> received = new();
        IListener l = results.ListenStrong(received.Add);
        Dog dog = new();

        AsyncMapStatus<Dog> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: static (v, _) => Task.FromResult(v),
                strategy: new AlwaysStartStrategy<Animal, Animal>());

        source.Send(dog);
        TestUtil.WaitUntil(() => received.Count == 1);
        await Assert.That(received[0]).IsSameReferenceAs(dog);

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task MapAsync_FourTypeArgsWithInputConverterOnly_AppliesInputConverter()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<Dog> results = Stream.CreateSink<Dog>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<Dog> received = new();
        IListener l = results.ListenStrong(received.Add);
        Dog dog = new();
        AlwaysStartStrategy<int, Animal> strategy = new();

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: (_, _) => Task.FromResult(dog),
                strategy: strategy,
                inputConverter: static v => v.Length);

        source.Send("hello");
        TestUtil.WaitUntil(() => received.Count == 1);

        await Assert.That(strategy.AdmittedValues).IsEquivalentTo(new[] { 5 }, CollectionOrdering.Matching);
        await Assert.That(received[0]).IsSameReferenceAs(dog);

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task MapAsync_FourTypeArgsWithResultConverterOnly_AppliesResultConverter()
    {
        StreamSink<Dog> source = Stream.CreateSink<Dog>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<string> received = new();
        IListener l = results.ListenStrong(received.Add);
        Dog dog = new();
        AlwaysStartStrategy<Animal, int> strategy = new();

        AsyncMapStatus<Dog> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: static (_, _) => Task.FromResult("done"),
                strategy: strategy,
                resultConverter: static v => v.Length);

        source.Send(dog);
        TestUtil.WaitUntil(() => received.Count == 1);

        await Assert.That(strategy.CompletedResults).IsEquivalentTo(new[] { 4 }, CollectionOrdering.Matching);
        await Assert.That(received[0]).IsEqualTo("done");

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task MapAsync_FullyGeneralOverload_AppliesBothConvertersToUnrelatedTypes()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<string> received = new();
        IListener l = results.ListenStrong(received.Add);
        AlwaysStartStrategy<int, bool> strategy = new();

        // TStrategyInput (int, a length) and TStrategyResult (bool, "is long") are both
        // unrelated by inheritance to TInput/TResult (string) — only this overload permits it.
        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: static (v, _) => Task.FromResult(v.ToUpperInvariant()),
                strategy: strategy,
                inputConverter: static v => v.Length,
                resultConverter: static v => v.Length > 3);

        source.Send("hello");
        TestUtil.WaitUntil(() => received.Count == 1);

        await Assert.That(strategy.AdmittedValues).IsEquivalentTo(new[] { 5 }, CollectionOrdering.Matching);
        await Assert.That(strategy.CompletedResults).IsEquivalentTo(new[] { true }, CollectionOrdering.Matching);
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
        List<string> received = new();
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
        List<string> received = new();
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

        cancelMatching.Send(new[] { "a" });

        op.Release(input: "b", result: "B");
        TestUtil.WaitUntil(() => received.Count == 1);

        Thread.Sleep(100);
        await Assert.That(received).IsEquivalentTo(new[] { "B" }, CollectionOrdering.Matching);

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
        List<string> received = new();
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
        List<string> received = new();
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
        await Assert.That(received).IsEquivalentTo(new[] { "A" }, CollectionOrdering.Matching);

        l.Unlisten();
    }

    [Test]
    public async Task FailedOperationPublishesToErrors()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        InvalidOperationException thrown = new("boom");
        List<Exception> received = new();
        IListener l = errors.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: (_, _) => Task.FromException<string>(thrown),
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

    private class Animal
    {
    }

    private sealed class Dog : Animal
    {
    }

    /// <summary>
    ///     Starts everything immediately, like the built-in Parallel, but works against arbitrary
    ///     TStrategyInput/TStrategyResult and records both what it was admitted with and what it
    ///     saw on completion — so a test can assert a converter actually ran, not merely compiled.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class AlwaysStartStrategy<TStrategyInput, TStrategyResult>
        : AsyncConcurrencyStrategy<TStrategyInput, TStrategyResult, Unit>
    {
        public readonly List<TStrategyInput> AdmittedValues = new();
        public readonly List<TStrategyResult> CompletedResults = new();

        protected override Unit CreateState() => Unit.Value;

        protected override IReadOnlyList<AsyncToStart<TStrategyInput>> Admit(
            Unit state,
            AsyncQueuedItem<TStrategyInput> incoming)
        {
            lock (this.AdmittedValues)
            {
                this.AdmittedValues.Add(incoming.Value);
            }

            return new[] { new AsyncToStart<TStrategyInput>(incoming) };
        }

        protected override AsyncStrategyResult<TStrategyInput> OnCompleted(
            Unit state,
            AsyncQueuedItem<TStrategyInput> item,
            AsyncOutcome<TStrategyResult> outcome)
        {
            outcome.MatchVoid(
                onSucceeded: v =>
                {
                    lock (this.CompletedResults)
                    {
                        this.CompletedResults.Add(v);
                    }
                },
                onFailed: null,
                onCanceled: null);

            return new AsyncStrategyResult<TStrategyInput>(
                publish: true,
                next: AsyncStrategyResult<TStrategyInput>.None);
        }
    }
}
