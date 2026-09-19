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

public sealed class AsyncConcurrencyStrategyTests
{
    [Test]
    public async Task Parallel_BothStartImmediatelyAndPublishInCompletionOrder()
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
                strategy: AsyncConcurrencyStrategy.Parallel());

        source.Send("a");
        source.Send("b");

        TestUtil.WaitUntil(() => op.HasStarted("a") && op.HasStarted("b"));

        // The pipeline admits and starts the two items before a release of one of them. This
        // shows that Parallel never waits.
        op.Release(input: "b", result: "B");
        TestUtil.WaitUntil(() => received.Count == 1);
        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => received.Count == 2);

        // This is the sequence of the ends, and not the sequence of the inputs.
        await Assert.That(received).IsEquivalentTo(expected: ["B", "A"], ordering: CollectionOrdering.Matching);

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task Parallel_BothStartImmediatelyAndPublishInCompletionOrderWithFailures()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<object> received = [];
        IListener l = results.ListenStrong(received.Add);
        IListener l2 = errors.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.Parallel());

        source.Send("a");
        source.Send("b");
        source.Send("c");
        source.Send("d");

        TestUtil.WaitUntil(() => op.HasStarted("a") && op.HasStarted("b"));

        Exception b = new("D");
        Exception d = new("D");

        // The pipeline admits and starts the two items before a release of one of them. This
        // shows that Parallel never waits.
        op.Fail(input: "d", error: d);
        TestUtil.WaitUntil(() => received.Count == 1);
        op.Release(input: "c", result: "C");
        TestUtil.WaitUntil(() => received.Count == 2);
        op.Fail(input: "b", error: b);
        TestUtil.WaitUntil(() => received.Count == 3);
        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => received.Count == 4);

        // This is the sequence of the ends, and not the sequence of the inputs.
        await Assert.That(received)
            .IsEquivalentTo(expected: new object[] { d, "C", b, "A" }, ordering: CollectionOrdering.Matching);

        status.Dispose();
        l.Unlisten();
        l2.Unlisten();
    }

    [Test]
    public async Task Queue_SecondDoesNotStartUntilFirstCompletes()
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
                strategy: AsyncConcurrencyStrategy.Queue());

        source.Send("a");
        source.Send("b");

        TestUtil.WaitUntil(() => op.HasStarted("a"));
        await Assert.That(op.HasStarted("b")).IsFalse();

        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => op.HasStarted("b"));
        op.Release(input: "b", result: "B");

        TestUtil.WaitUntil(() => received.Count == 2);
        await Assert.That(received).IsEquivalentTo(expected: ["A", "B"], ordering: CollectionOrdering.Matching);

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task QueuePerGroup_DifferentGroupsRunConcurrentlyButSameGroupSerializes()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncConcurrencyStrategyBase<string, Unit> strategy =
            AsyncConcurrencyStrategy.QueuePerGroup<string>().Create(static v => v.Split('-')[0]);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: strategy);

        source.Send("g1-a");
        source.Send("g1-b");
        source.Send("g2-a");

        TestUtil.WaitUntil(() => op.HasStarted("g1-a") && op.HasStarted("g2-a"));
        await Assert.That(op.HasStarted("g1-b")).IsFalse();

        op.Release(input: "g1-a", result: "A1");
        TestUtil.WaitUntil(() => op.HasStarted("g1-b"));

        op.Release(input: "g1-b", result: "B1");
        op.Release(input: "g2-a", result: "A2");
        TestUtil.WaitUntil(() => received.Count == 3);

        await Assert.That(received).IsEquivalentTo(["A1", "B1", "A2"]);

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task SwitchLatest_SupersededRunIsNeverPublished()
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
                strategy: AsyncConcurrencyStrategy.SwitchLatest());

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));
        source.Send("b");
        TestUtil.WaitUntil(() => op.HasStarted("b"));

        op.Release(input: "a", result: "A");
        op.Release(input: "b", result: "B");
        TestUtil.WaitUntil(() => received.Count == 1);

        Thread.Sleep(100);
        await Assert.That(received).IsEquivalentTo(expected: ["B"], ordering: CollectionOrdering.Matching);

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task Parallel_Queue_SwitchLatest_EachReturnTheSameCachedInstanceEveryCall()
    {
        // The documentation gives these as strategies with no state that more than one call can
        // use. The wrapper keeps one instance of each strategy in a cache, and does not allocate a
        // new instance at each call.
        await Assert.That(AsyncConcurrencyStrategy.Parallel()).IsSameReferenceAs(AsyncConcurrencyStrategy.Parallel());
        await Assert.That(AsyncConcurrencyStrategy.Queue()).IsSameReferenceAs(AsyncConcurrencyStrategy.Queue());

        await Assert.That(AsyncConcurrencyStrategy.SwitchLatest())
            .IsSameReferenceAs(AsyncConcurrencyStrategy.SwitchLatest());
    }

    [Test]
    public async Task CustomStrategy_ViaUnitShorthandBase_Works()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<Unit> results = Stream.CreateSink<Unit>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        List<Unit> received = [];
        IListener l = results.ListenStrong(received.Add);

        CountingStrategy strategy = new();

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                operation: static (_, _) => Task.FromResult(Unit.Value),
                strategy: strategy);

        source.Send("a");
        source.Send("b");

        TestUtil.WaitUntil(() => received.Count == 2);
        await Assert.That(strategy.AdmittedCount).IsEqualTo(2);

        status.Dispose();
        l.Unlisten();
    }

    /// <summary>
    ///     A small custom strategy on the short
    ///     <see cref="AsyncConcurrencyStrategy{TState}" /> shape, where the input type and the
    ///     result type are <see cref="Unit" />. Each value starts immediately, as Parallel does,
    ///     and this strategy also counts the admissions.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class CountingStrategy : AsyncConcurrencyStrategy<int>
    {
        private int count;

        public int AdmittedCount => this.count;

        protected override int CreateState() => 0;

        protected override IReadOnlyList<AsyncToStart<Unit>> Admit(
            int state,
            AsyncQueuedItem<Unit> incoming)
        {
            Interlocked.Increment(ref this.count);
            return [new AsyncToStart<Unit>(incoming)];
        }

        protected override AsyncStrategyResult<Unit> OnCompleted(
            int state,
            AsyncQueuedItem<Unit> item,
            AsyncOutcome<Unit> outcome) =>
            new(publish: true, next: AsyncStrategyResult<Unit>.None);
    }
}
