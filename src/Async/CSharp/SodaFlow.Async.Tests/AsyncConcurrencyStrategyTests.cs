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

    // SwitchLatest cancels the run in flight for each new value, and records the new value as its
    // current run. The cancel is an effect out of the graph, and the record is state out of the
    // graph. A transaction that fails must leave the two as they were. Before, the run in flight
    // lost its token. Where its operation ignored the token, the strategy did not publish its
    // result, because the strategy held a value that the pipeline did not admit.
    [Test]
    public async Task SwitchLatest_ASendThatFails_KeepsTheRunInFlightAndPublishesItsResult()
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

        Cell<IReadOnlyList<AsyncItem<string>>> tracked = status.Items;

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        IListener thrower = TestUtil.ThrowAfterThePipeline(source);

        await Assert.That(() => source.Send("b")).ThrowsExactly<InvalidOperationException>();

        thrower.Unlisten();

        await Assert.That(op.IsCanceled("a")).IsFalse().Because("the transaction that replaced the run failed");
        await Assert.That(op.HasStarted("b")).IsFalse();

        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 0);

        await Assert.That(received)
            .IsEquivalentTo(expected: ["A"], ordering: CollectionOrdering.Matching)
            .Because("the run in flight is still the current run of the strategy");

        status.Dispose();
        l.Unlisten();
    }

    // The strategy must hold the run in flight as its current run after a send that fails, and
    // not the value of that send. Thus, the next send cancels the run in flight.
    [Test]
    public async Task SwitchLatest_AfterASendThatFails_TheNextSendReplacesTheRunInFlight()
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

        Cell<IReadOnlyList<AsyncItem<string>>> tracked = status.Items;

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        IListener thrower = TestUtil.ThrowAfterThePipeline(source);

        await Assert.That(() => source.Send("b")).ThrowsExactly<InvalidOperationException>();

        thrower.Unlisten();

        source.Send("c");
        TestUtil.WaitUntil(() => op.HasStarted("c"));
        TestUtil.WaitUntil(() => op.IsCanceled("a"));

        op.Release(input: "c", result: "C");
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 0);

        await Assert.That(received).IsEquivalentTo(expected: ["C"], ordering: CollectionOrdering.Matching);

        status.Dispose();
        l.Unlisten();
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

        int admissions =
            strategy.Calls.Count(static call =>
                call.StartsWith(value: "admit:", comparisonType: StringComparison.Ordinal));

        await Assert.That(admissions).IsEqualTo(2);

        status.Dispose();
        l.Unlisten();
    }

    // A strategy returns its next state, and the pipeline gives that state to the next call. The
    // strategy here adds 1 for an admission, 10 for an end, and 100 for a cancellation.
    [Test]
    public async Task CustomStrategy_EachCallGetsTheStateThatTheCallBeforeItReturned()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        StreamSink<Unit> cancelAll = Stream.CreateSink<Unit>();
        ControlledOperation<string, string> op = new();
        CountingStrategy strategy = new();
        List<string> received = [];
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                operation: op.Operation,
                strategy: strategy,
                cancelAll: cancelAll);

        Cell<IReadOnlyList<AsyncItem<string>>> tracked = status.Items;

        source.Send("a");
        source.Send("b");
        TestUtil.WaitUntil(() => op.HasStarted("a") && op.HasStarted("b"));

        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => received.Count == 1);

        cancelAll.Send(Unit.Value);
        TestUtil.WaitUntil(() => Transaction.Run(tracked.Sample).Count == 0);

        source.Send("c");
        TestUtil.WaitUntil(() => op.HasStarted("c"));

        await Assert.That(strategy.Calls)
            .IsEquivalentTo(
                expected: ["admit:0", "admit:1", "completed:2", "canceled:12", "admit:112"],
                ordering: CollectionOrdering.Matching);

        status.Dispose();
        l.Unlisten();
    }

    // The state of a strategy follows the transaction. A listener fails the transaction of the
    // second admission, thus the pipeline discards the state that the call returned, and the third
    // admission gets the state that the second one got.
    [Test]
    public async Task CustomStrategy_AnAdmitWhoseTransactionFails_LeavesTheStateAsItWas()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        CountingStrategy strategy = new();

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                operation: op.Operation,
                strategy: strategy);

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        IListener thrower = TestUtil.ThrowAfterThePipeline(source);

        await Assert.That(() => source.Send("b")).ThrowsExactly<InvalidOperationException>();

        thrower.Unlisten();

        source.Send("c");
        TestUtil.WaitUntil(() => op.HasStarted("c"));

        await Assert.That(strategy.Calls)
            .IsEquivalentTo(expected: ["admit:0", "admit:1", "admit:1"], ordering: CollectionOrdering.Matching);

        status.Dispose();
    }

    // A listener on the results throws, thus the transaction that ends "a" fails, and the pipeline
    // ends "a" again in a new transaction. The second call gets the state that the first call
    // got, and the state that the second call returns is the one that stays.
    [Test]
    public async Task CustomStrategy_AnEndWhoseTransactionFails_GetsTheSameStateInItsSecondCall()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        CountingStrategy strategy = new();
        List<Exception> failures = [];

        using IStrongListener r =
            results.ListenStrong(static _ => throw new InvalidOperationException("results listener"));

        using IStrongListener e = errors.ListenStrong(failures.Add);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                operation: op.Operation,
                strategy: strategy);

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => failures.Count == 1);

        source.Send("b");
        TestUtil.WaitUntil(() => op.HasStarted("b"));

        await Assert.That(strategy.Calls)
            .IsEquivalentTo(
                expected: ["admit:0", "completed:1", "completed:1", "admit:11"],
                ordering: CollectionOrdering.Matching);

        status.Dispose();
    }

    // The input depends on the result, thus the end of "a" and the admission of "ax" are in one
    // transaction. The second call of that transaction must get the state that the first call
    // returned, and not the state from the start of the transaction.
    [Test]
    public async Task CustomStrategy_TwoCallsInOneTransaction_TheSecondGetsTheStateOfTheFirst()
    {
        StreamSink<string> trigger = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        CountingStrategy strategy = new();

        Stream<string> input = trigger.OrElse(results.Filter(static r => r.Length < 2).Map(static r => r + "x"));

        AsyncMapStatus<string> status =
            input.MapAsync(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                operation: op.Operation,
                strategy: strategy);

        trigger.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        op.Release(input: "a", result: "a");
        TestUtil.WaitUntil(() => op.HasStarted("ax"));

        await Assert.That(strategy.Calls)
            .IsEquivalentTo(expected: ["admit:0", "completed:1", "admit:11"], ordering: CollectionOrdering.Matching);

        status.Dispose();
    }

    // The two calls of one transaction, and then a listener fails that transaction. The pipeline
    // discards the state of the two calls together. The second end of "a" gets the state from
    // before the transaction, and the admission after it gets the state of that second end.
    [Test]
    public async Task CustomStrategy_TwoCallsInOneTransactionThatFails_PutsTheStateBackOneTime()
    {
        StreamSink<string> trigger = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        CountingStrategy strategy = new();
        List<Exception> failures = [];

        using IStrongListener r =
            results.ListenStrong(static _ => throw new InvalidOperationException("results listener"));

        using IStrongListener e = errors.ListenStrong(failures.Add);

        Stream<string> input = trigger.OrElse(results.Filter(static r => r.Length < 2).Map(static r => r + "x"));

        AsyncMapStatus<string> status =
            input.MapAsync(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                operation: op.Operation,
                strategy: strategy);

        trigger.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        op.Release(input: "a", result: "a");
        TestUtil.WaitUntil(() => failures.Count == 1);

        await Assert.That(op.HasStarted("ax")).IsFalse().Because("the transaction that admitted it failed");

        trigger.Send("b");
        TestUtil.WaitUntil(() => op.HasStarted("b"));

        await Assert.That(strategy.Calls)
            .IsEquivalentTo(
                expected: ["admit:0", "completed:1", "admit:11", "completed:1", "admit:11"],
                ordering: CollectionOrdering.Matching);

        status.Dispose();
    }

    /// <summary>
    ///     A small custom strategy on the short
    ///     <see cref="AsyncConcurrencyStrategy{TState}" /> shape, where the input type
    ///     is <see cref="Unit" />. Each value starts immediately, as Parallel does.
    ///     The state is a count: an admission adds 1, an end adds 10, and a
    ///     cancellation adds 100. The strategy records the state that each call gets,
    ///     thus a test can read which state the pipeline gave it.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class CountingStrategy : AsyncConcurrencyStrategy<int>
    {
        private readonly ConcurrentQueue<string> calls = new();

        public string[] Calls => [.. this.calls];

        protected override int CreateState() => 0;

        protected override AsyncStrategyStep<Unit, int> Admit(
            int state,
            AsyncQueuedItem<Unit> incoming,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked)
        {
            this.calls.Enqueue("admit:" + state);

            return new AsyncStrategyStep<Unit, int>(state: state + 1, start: [new AsyncToStart<Unit>(incoming)]);
        }

        protected override AsyncStrategyResult<Unit, int> OnCompleted(
            int state,
            AsyncQueuedItem<Unit> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked)
        {
            this.calls.Enqueue("completed:" + state);

            return new AsyncStrategyResult<Unit, int>(state: state + 10, publish: true, next: AsyncToStart<Unit>.None);
        }

        protected override AsyncStrategyStep<Unit, int> OnCanceled(
            int state,
            IReadOnlyList<AsyncQueuedItem<Unit>> canceled,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked)
        {
            this.calls.Enqueue("canceled:" + state);

            return new AsyncStrategyStep<Unit, int>(state: state + 100, start: AsyncToStart<Unit>.None);
        }
    }

    /// <summary>
    ///     Starts each value immediately and cancels each other item that the
    ///     pipeline tracks, as SwitchLatest does, with no state of its own.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class ReplaceEachRunStrategy : AsyncConcurrencyStrategy<Unit>
    {
        protected override Unit CreateState() => Unit.Value;

        protected override AsyncStrategyStep<Unit, Unit> Admit(
            Unit state,
            AsyncQueuedItem<Unit> incoming,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked)
        {
            foreach (AsyncTrackedItem<Unit> other in tracked)
            {
                other.Item.Cancel();
            }

            return new AsyncStrategyStep<Unit, Unit>(state: state, start: [new AsyncToStart<Unit>(incoming)]);
        }

        protected override AsyncStrategyResult<Unit, Unit> OnCompleted(
            Unit state,
            AsyncQueuedItem<Unit> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked) =>
            new(state: state, publish: true, next: AsyncToStart<Unit>.None);

        protected override AsyncStrategyStep<Unit, Unit> OnCanceled(
            Unit state,
            IReadOnlyList<AsyncQueuedItem<Unit>> canceled,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked) =>
            new(state: state, start: AsyncToStart<Unit>.None);
    }

    /// <summary>Cancels each value and starts it in the same call.</summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class CancelAndStartStrategy : AsyncConcurrencyStrategy<Unit>
    {
        protected override Unit CreateState() => Unit.Value;

        protected override AsyncStrategyStep<Unit, Unit> Admit(
            Unit state,
            AsyncQueuedItem<Unit> incoming,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked)
        {
            incoming.Cancel();

            return new AsyncStrategyStep<Unit, Unit>(state: state, start: [new AsyncToStart<Unit>(incoming)]);
        }

        protected override AsyncStrategyResult<Unit, Unit> OnCompleted(
            Unit state,
            AsyncQueuedItem<Unit> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked) =>
            new(state: state, publish: true, next: AsyncToStart<Unit>.None);

        protected override AsyncStrategyStep<Unit, Unit> OnCanceled(
            Unit state,
            IReadOnlyList<AsyncQueuedItem<Unit>> canceled,
            IReadOnlyList<AsyncTrackedItem<Unit>> tracked) =>
            new(state: state, start: AsyncToStart<Unit>.None);
    }
}
