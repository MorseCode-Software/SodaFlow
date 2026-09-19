using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

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
        IListener l = results.ListenStrong(received.Add);

        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v,
                resultConverter: static v => v);

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
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Parallel("unused"),
                inputConverter: static v => v,
                resultConverter: static v => v);

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
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.Queue<string>(),
                inputConverter: static v => v,
                resultConverter: static v => v);

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

        AsyncMapStatus<string> status =
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.QueuePerGroup<string, string, string>(GetGroup),
                inputConverter: static v => v,
                resultConverter: static v => v);

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
        l.Unlisten();
        return;

        // The group is the text before the hyphen. Thus "g1-a" and "g1-b" have one group, and
        // "g2-a" has a different group.
        static string GetGroup(string v) => v.Split('-')[0];
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
            source.MapAsyncImpl(
                results: results,
                errors: errors,
                operation: op.Operation,
                strategy: AsyncConcurrencyStrategyFactory.SwitchLatest<string>(),
                inputConverter: static v => v,
                resultConverter: static v => v);

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
        l.Unlisten();
    }

    [Test]
    public void Queue_SameStrategyInstanceSharedAcrossTwoPipelinesDoesNotCrossSerialize()
    {
        AsyncConcurrencyStrategyBase<string, string> sharedQueue = AsyncConcurrencyStrategyFactory.Queue<string>();

        StreamSink<string> source1 = Stream.CreateSink<string>();
        StreamSink<string> results1 = Stream.CreateSink<string>();
        StreamSink<Exception> errors1 = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op1 = new();
        List<string> received1 = [];
        IListener l1 = results1.ListenStrong(received1.Add);

        StreamSink<string> source2 = Stream.CreateSink<string>();
        StreamSink<string> results2 = Stream.CreateSink<string>();
        StreamSink<Exception> errors2 = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op2 = new();
        List<string> received2 = [];
        IListener l2 = results2.ListenStrong(received2.Add);

        AsyncMapStatus<string> status1 =
            source1.MapAsyncImpl(
                results: results1,
                errors: errors1,
                operation: op1.Operation,
                strategy: sharedQueue,
                inputConverter: static v => v,
                resultConverter: static v => v);

        AsyncMapStatus<string> status2 =
            source2.MapAsyncImpl(
                results: results2,
                errors: errors2,
                operation: op2.Operation,
                strategy: sharedQueue,
                inputConverter: static v => v,
                resultConverter: static v => v);

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
        l1.Unlisten();
        l2.Unlisten();
    }
}
