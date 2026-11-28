using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using QueuedItem = SodaFlow.Async.AsyncMapBase.AsyncQueuedItem<int>;
using TrackedItem = SodaFlow.Async.AsyncMapBase.AsyncTrackedItem<int>;

namespace SodaFlow.Async.Tests;

public sealed class AsyncConcurrencyStrategyFactoryTests
{
    [Test]
    public async Task Parallel_BothStartImmediatelyAndPublishInCompletionOrder()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<string> received = [];
        IListener l = results.Listen(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v);

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
        l.Dispose();
    }

    [Test]
    public async Task Parallel_BothStartImmediatelyAndPublishInCompletionOrderWithFailures()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<object> received = [];
        IListener l = results.Listen(received.Add);
        IListener l2 = errors.Listen(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v);

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
        l.Dispose();
        l2.Dispose();
    }

    [Test]
    public async Task Queue_SecondDoesNotStartUntilFirstCompletes()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<string> received = [];
        IListener l = results.Listen(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Queue<string>(),
                inputConverter: static v => v);

        source.Send("a");
        source.Send("b");
        source.Send("c");

        TestUtil.WaitUntil(() => op.HasStarted("a"));
        await Assert.That(op.HasStarted("b")).IsFalse().Because("b must stay queued while a is running.");
        await Assert.That(op.HasStarted("c")).IsFalse().Because("c must stay queued while a is running.");

        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => op.HasStarted("b"));
        await Assert.That(op.HasStarted("c")).IsFalse().Because("c must stay queued while b is running.");

        op.Release(input: "b", result: "B");
        TestUtil.WaitUntil(() => op.HasStarted("c"));

        op.Release(input: "c", result: "C");
        TestUtil.WaitUntil(() => received.Count == 3);

        await Assert.That(received).IsEquivalentTo(expected: ["A", "B", "C"], ordering: CollectionOrdering.Matching);

        status.Dispose();
        l.Dispose();
    }

    [Test]
    public async Task QueuePerGroup_DifferentGroupsRunConcurrentlyButSameGroupSerializes()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<string> received = [];
        IListener l = results.Listen(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.QueuePerGroup<string, string, string>(GetGroup),
                inputConverter: static v => v);

        source.Send("g1-a");
        source.Send("g1-b");
        source.Send("g2-a");

        // g1-a and g2-a are in different groups, thus the two start. g1-b waits behind g1-a.
        TestUtil.WaitUntil(() => op.HasStarted("g1-a") && op.HasStarted("g2-a"));
        await Assert.That(op.HasStarted("g1-b")).IsFalse().Because("g1-b shares a group with g1-a and must wait.");

        op.Release(input: "g1-a", result: "A1");
        TestUtil.WaitUntil(() => op.HasStarted("g1-b"));

        op.Release(input: "g1-b", result: "B1");
        op.Release(input: "g2-a", result: "A2");
        TestUtil.WaitUntil(() => received.Count == 3);

        await Assert.That(received).IsEquivalentTo(["A1", "B1", "A2"]);

        status.Dispose();
        l.Dispose();
        return;

        // The group is the text before the hyphen. Thus, "g1-a" and "g1-b" have one group, and
        // "g2-a" has a different group.
        static string GetGroup(string v) => v.Split('-')[0];
    }

    // OnCanceled against its rule, over random queues. The rule takes the groups of the canceled
    // items in their sequence. Each group starts its first item that waits, if no item of the
    // group runs.
    // The random queues do not keep the rules of the pipeline. Thus, a call can
    // start items, and more than eight groups in one call make the strategy use its dictionary.
    // A call also reads the group of each canceled item and of each item of the queue at most
    // one time.
    [Test]
    public async Task QueuePerGroup_OnCanceledStartsTheFirstWaitingItemOfEachFreeGroup()
    {
        Random random = new(Seed: 7);
        StrongBox<int> reads = new(0);
        int serial = 0;
        int wrongStarts = 0;
        int mostExtraReads = int.MinValue;
        int dictionaryCalls = 0;
        using AsyncItemCancellationSource cancellation = new();

        AsyncMapBase.IStateManager<int> manager =
            AsyncConcurrencyStrategyFactory.QueuePerGroup<object, int, int>(
                    getGroup: v =>
                    {
                        reads.Value++;
                        return v / 1000;
                    },
                    groupComparer: EqualityComparer<int>.Default)
                .CreateStateManager();

        for (int run = 0; run < 2000; run++)
        {
            int groupCount = random.Next(minValue: 1, maxValue: 20);

            List<TrackedItem> tracked = [];

            for (int i = random.Next(minValue: 0, maxValue: 30); i > 0; i--)
            {
                tracked.Add(
                    new TrackedItem(
                        item: NewItem(random.Next(groupCount)),
                        status: random.Next(3) == 0 ? AsyncItemStatus.Running : AsyncItemStatus.Queued));
            }

            List<QueuedItem> canceled = [];

            for (int i = random.Next(minValue: 0, maxValue: 15); i > 0; i--)
            {
                canceled.Add(NewItem(random.Next(groupCount)));
            }

            if (canceled.Select(static c => c.Value / 1000).Distinct().Count() > 8)
            {
                dictionaryCalls++;
            }

            reads.Value = 0;

            IReadOnlyList<AsyncMapBase.AsyncToStart<int>> starts =
                Transaction.Run(() => manager.OnCanceled(canceled: canceled, tracked: tracked));

            if (!starts.Select(static s => s.Item).SequenceEqual(Expected(canceled: canceled, tracked: tracked)))
            {
                wrongStarts++;
            }

            mostExtraReads = Math.Max(val1: mostExtraReads, val2: reads.Value - canceled.Count - tracked.Count);
        }

        await Assert.That(dictionaryCalls).IsGreaterThan(100).Because("the dictionary needs a test also");
        await Assert.That(wrongStarts).IsEqualTo(0);
        await Assert.That(mostExtraReads).IsLessThanOrEqualTo(0);

        return;

        QueuedItem NewItem(int group) =>
            new(
                id: Guid.NewGuid(),
                value: group * 1000 + serial++,
                cancellation: cancellation,
                reportCancelFailure: static e => throw e);

        static IEnumerable<QueuedItem> Expected(IEnumerable<QueuedItem> canceled, IReadOnlyCollection<TrackedItem> tracked) =>
            canceled
                .Select(static c => c.Value / 1000)
                .Distinct()
                .Where(g => !tracked.Any(t => t.Status == AsyncItemStatus.Running && t.Item.Value / 1000 == g))
                .SelectMany(g => tracked.Where(t => t.Status == AsyncItemStatus.Queued && t.Item.Value / 1000 == g).Take(1))
                .Select(static t => t.Item);
    }

    [Test]
    public async Task SwitchLatest_SupersededRunIsNeverPublished()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<string> received = [];
        IListener l = results.Listen(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.SwitchLatest<string>(),
                inputConverter: static v => v);

        source.Send("a");
        TestUtil.WaitUntil(() => op.HasStarted("a"));

        source.Send("b");
        TestUtil.WaitUntil(() => op.HasStarted("b"));

        // Object "a" runs when a new value replaces it. A release of it must publish nothing.
        op.Release(input: "a", result: "A");
        op.Release(input: "b", result: "B");
        TestUtil.WaitUntil(() => received.Count == 1);

        // This gives object "a" sufficient time to publish, to find a defect in the code that
        // replaces a run.
        Thread.Sleep(100);

        await Assert.That(received).IsEquivalentTo(expected: ["B"], ordering: CollectionOrdering.Matching);

        status.Dispose();
        l.Dispose();
    }

    [Test]
    public void Queue_SameStrategyInstanceSharedAcrossTwoPipelinesDoesNotCrossSerialize()
    {
        AsyncConcurrencyStrategyBase<string> sharedQueue = AsyncConcurrencyStrategyFactory.Queue<string>();

        StreamSink<string> source1 = Stream.CreateSink<string>();
        StreamSink<string> results1 = Stream.CreateSink<string>();
        StreamSink<Exception> errors1 = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op1 = new();
        List<string> received1 = [];
        IListener l1 = results1.Listen(received1.Add);

        StreamSink<string> source2 = Stream.CreateSink<string>();
        StreamSink<string> results2 = Stream.CreateSink<string>();
        StreamSink<Exception> errors2 = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op2 = new();
        List<string> received2 = [];
        IListener l2 = results2.Listen(received2.Add);

        AsyncMapStatus<string> status1 =
            source1.MapAsyncImpl(
                results: results1,
                errors: errors1,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: op1.Operation,
                strategy: sharedQueue,
                inputConverter: static v => v);

        AsyncMapStatus<string> status2 =
            source2.MapAsyncImpl(
                results: results2,
                errors: errors2,
                handleException: TestUtil.Unexpected,
                createDeliveryException: TestDeliveryException.Create,
                operation: op2.Operation,
                strategy: sharedQueue,
                inputConverter: static v => v);

        source1.Send("x");

        // Pipeline 2 must start immediately while the queue of pipeline 1 is busy. This shows
        // that each call gets its own scheduling state, as the contract of CreateState gives.
        TestUtil.WaitUntil(() => op1.HasStarted("x"));
        source2.Send("y");
        TestUtil.WaitUntil(() => op2.HasStarted("y"));

        op1.Release(input: "x", result: "X");
        op2.Release(input: "y", result: "Y");
        TestUtil.WaitUntil(() => received1.Count == 1 && received2.Count == 1);

        status1.Dispose();
        status2.Dispose();
        l1.Dispose();
        l2.Dispose();
    }
}
