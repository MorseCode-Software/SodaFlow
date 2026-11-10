using System;
using System.Collections.Concurrent;
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
                handleException: TestUtil.Unexpected,
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
                handleException: TestUtil.Unexpected,
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
                handleException: TestUtil.Unexpected,
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

        AsyncConcurrencyStrategyBase<string> strategy =
            AsyncConcurrencyStrategy.QueuePerGroup<string>().Create(static v => v.Split('-')[0]);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
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
    public async Task QueuePerGroup_ACancellationStartsTheNextItemOfThatGroupAlone()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<IReadOnlyCollection<string>> cancelMatching = Stream.CreateSink<IReadOnlyCollection<string>>();
        ControlledOperation<string, string> op = new();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncConcurrencyStrategyBase<string> strategy =
            AsyncConcurrencyStrategy.QueuePerGroup<string>().Create(static v => v.Split('-')[0]);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                operation: op.Operation,
                strategy: strategy,
                cancelMatching: cancelMatching);

        source.Send("g1-a");
        source.Send("g1-b");
        source.Send("g2-a");

        TestUtil.WaitUntil(() => op.HasStarted("g1-a") && op.HasStarted("g2-a"));
        await Assert.That(op.HasStarted("g1-b")).IsFalse().Because("one item of a group runs at a time");

        // No operation ends here, thus OnCanceled is the one method that can start "g1-b".
        cancelMatching.Send(["g1-a"]);
        TestUtil.WaitUntil(() => op.HasStarted("g1-b"));

        op.Release(input: "g1-b", result: "B1");
        op.Release(input: "g2-a", result: "A2");
        TestUtil.WaitUntil(() => received.Count == 2);

        await Assert.That(received)
            .IsEquivalentTo(["B1", "A2"])
            .Because("a canceled item publishes nothing, and the group of it goes on");

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
                handleException: TestUtil.Unexpected,
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
    public async Task SwitchLatest_LeavesNoItemQueued()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<Unit> cancelAll = Stream.CreateSink<Unit>();
        ControlledOperation<string, string> op = new();
        List<string> statuses = [];

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategy.SwitchLatest(),
                cancelAll: cancelAll);

        // Each value of the cell, and not a sample at one moment. OnCanceled must start a Queued
        // item at any instant. The Admit of this strategy promotes each item that it admits, thus
        // no value here holds such an item, and a cancellation starts nothing.
        // The closure below reads this cell and not `status`. A closure over `status` reads it after
        // the disposal at the end of this method.
        Cell<IReadOnlyList<AsyncItem<string>>> tracked = status.Items;

        IListener probe =
            Transaction.Run(() =>
                tracked.ListenStrong(items =>
                {
                    lock (statuses)
                    {
                        statuses.AddRange(items.Select(static item => item.Value + ":" + item.Status));
                    }
                }));

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        // A new send cancels "a" and starts "b" in the same transaction.
        source.Send("b");
        TestUtil.WaitUntil(() => op.HasStarted("b"));

        // A cancellation with one item in flight, which is the case that OnCanceled answers.
        cancelAll.Send(Unit.Value);
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 0);

        source.Send("c");
        TestUtil.WaitUntil(() => op.HasStarted("c"));
        op.Release(input: "c", result: "C");
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 0);

        List<string> seen;

        lock (statuses)
        {
            seen = [.. statuses];
        }

        List<string> queued =
            seen.FindAll(static s => s.EndsWith(value: ":Queued", comparisonType: StringComparison.Ordinal));

        await Assert.That(queued)
            .IsEmpty()
            .Because("SwitchLatest promotes each item at its admission, thus none waits");

        await Assert.That(seen)
            .Contains("b:Running")
            .Because("the probe reads the cell and would see a Queued item where one existed");

        probe.Unlisten();
        status.Dispose();
    }

    // A callback on the token of the run that a new value replaces can throw. Before, the cancel
    // ran in the Admit of the strategy, thus the throw failed the transaction of the new value.
    // The cancel now runs after the commit. There the throw has no caller, and it goes to
    // handleException.
    [Test]
    public async Task SwitchLatest_WhenACallbackOfTheReplacedRunThrows_AdmitsTheValueAndGivesTheThrowToHandleException()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        ConcurrentQueue<Exception> handled = new();
        InvalidOperationException fromCallback = new("The callback of the token throws.");

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                handleException: handled.Enqueue,
                operation: (input, resultFactory, token) =>
                {
                    if (input == "a")
                    {
                        // The registration lives as long as the token, which the pipeline owns.
                        _ = token.Register(() => throw fromCallback);
                    }

                    return op.Operation(input: input, resultFactory: resultFactory, token: token);
                },
                strategy: AsyncConcurrencyStrategy.SwitchLatest());

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        source.Send("b");

        TestUtil.WaitUntil(() => op.HasStarted("b"));
        TestUtil.WaitUntil(() => handled.Count == 1);

        await Assert.That(op.IsCanceled("a")).IsTrue();

        // The token of the operation is a linked one. Thus, the throw comes in two
        // AggregateException objects: one from the linked source, and one from the source of
        // the item.
        Exception only = handled.Single();

        await Assert.That(only).IsTypeOf<AggregateException>();

        await Assert.That(((AggregateException)only).Flatten().InnerExceptions.Single())
            .IsSameReferenceAs(fromCallback);

        status.Dispose();
    }

    // A custom strategy cancels through the same call as SwitchLatest. This one keeps no state: it
    // cancels each item that the pipeline tracks. A transaction that fails cancels nothing, and
    // the same strategy in a transaction that commits cancels as before.
    [Test]
    public async Task CustomStrategy_ACancelInAnAdmitWhoseTransactionFails_CancelsNothing()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                operation: op.Operation,
                strategy: new ReplaceEachRunStrategy());

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        IListener thrower = TestUtil.ThrowAfterThePipeline(source);

        await Assert.That(() => source.Send("b")).ThrowsExactly<InvalidOperationException>();

        thrower.Unlisten();

        await Assert.That(op.IsCanceled("a")).IsFalse().Because("the transaction that asked for the cancel failed");
        await Assert.That(op.HasStarted("b")).IsFalse();

        source.Send("c");
        TestUtil.WaitUntil(() => op.HasStarted("c"));
        TestUtil.WaitUntil(() => op.IsCanceled("a"));

        status.Dispose();
    }

    // A strategy can cancel a value and start it in one call. The cancel waits for the commit,
    // thus the promotion does not find it. The start of the operation does, and the pipeline ends
    // the item and does not call the operation.
    [Test]
    public async Task CustomStrategy_AValueThatItCancelsAndStarts_RunsNoOperationAndEnds()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();

        AsyncMapStatus<string, string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                operation: op.Operation,
                strategy: new CancelAndStartStrategy());

        Cell<IReadOnlyList<AsyncItem<string>>> tracked = status.Items;

        Task<string> task = status.Execute("a");

        TestUtil.WaitUntil(() => task.IsCompleted);

        await Assert.That(task.IsCanceled).IsTrue();
        await Assert.That(op.HasStarted("a")).IsFalse().Because("a canceled item runs no operation");
        await Assert.That(Transaction.Run(tracked.Sample)).IsEmpty();

        status.Dispose();
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
                handleException: TestUtil.Unexpected,
                operation: static (_, factory, _) => Task.FromResult(factory.FromValue(Unit.Value)),
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
    ///     <see cref="AsyncConcurrencyStrategy{TState}" /> shape, where the input type
    ///     is <see cref="Unit" />. Each value starts immediately, as Parallel does,
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
            AsyncQueuedItem<Unit> incoming,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked)
        {
            Interlocked.Increment(ref this.count);
            return [new AsyncToStart<Unit>(incoming)];
        }

        protected override AsyncStrategyResult<Unit> OnCompleted(
            int state,
            AsyncQueuedItem<Unit> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked) =>
            new(publish: true, next: AsyncStrategyResult<Unit>.None);

        protected override IReadOnlyList<AsyncToStart<Unit>> OnCanceled(
            int state,
            IReadOnlyList<AsyncQueuedItem<Unit>> canceled,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked) =>
            AsyncStrategyResult<Unit>.None;
    }

    /// <summary>
    ///     Starts each value immediately and cancels each other item that the
    ///     pipeline tracks, as SwitchLatest does, with no state of its own.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class ReplaceEachRunStrategy : AsyncConcurrencyStrategy<Unit>
    {
        protected override Unit CreateState() => Unit.Value;

        protected override IReadOnlyList<AsyncToStart<Unit>> Admit(
            Unit state,
            AsyncQueuedItem<Unit> incoming,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked)
        {
            foreach (AsyncTrackedItem<Unit> other in tracked)
            {
                other.Item.Cancel();
            }

            return [new AsyncToStart<Unit>(incoming)];
        }

        protected override AsyncStrategyResult<Unit> OnCompleted(
            Unit state,
            AsyncQueuedItem<Unit> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked) =>
            new(publish: true, next: AsyncStrategyResult<Unit>.None);

        protected override IReadOnlyList<AsyncToStart<Unit>> OnCanceled(
            Unit state,
            IReadOnlyList<AsyncQueuedItem<Unit>> canceled,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked) =>
            AsyncStrategyResult<Unit>.None;
    }

    /// <summary>Cancels each value and starts it in the same call.</summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class CancelAndStartStrategy : AsyncConcurrencyStrategy<Unit>
    {
        protected override Unit CreateState() => Unit.Value;

        protected override IReadOnlyList<AsyncToStart<Unit>> Admit(
            Unit state,
            AsyncQueuedItem<Unit> incoming,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked)
        {
            incoming.Cancel();

            return [new AsyncToStart<Unit>(incoming)];
        }

        protected override AsyncStrategyResult<Unit> OnCompleted(
            Unit state,
            AsyncQueuedItem<Unit> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked) =>
            new(publish: true, next: AsyncStrategyResult<Unit>.None);

        protected override IReadOnlyList<AsyncToStart<Unit>> OnCanceled(
            Unit state,
            IReadOnlyList<AsyncQueuedItem<Unit>> canceled,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked) =>
            AsyncStrategyResult<Unit>.None;
    }
}
