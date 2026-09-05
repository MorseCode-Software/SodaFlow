using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Async.Tests;

public class AsyncConcurrencyStrategyFactoryTests
{
    [Test]
    public async Task Parallel_BothStartImmediatelyAndPublishInCompletionOrder()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<string> received = new();
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

        // Both admitted and started before either is released — proves Parallel never waits.
        op.Release(input: "b", result: "B");
        TestUtil.WaitUntil(() => received.Count == 1);
        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => received.Count == 2);

        // Completion order, not submission order.
        await Assert.That(received).IsEquivalentTo(new[] { "B", "A" }, CollectionOrdering.Matching);

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
        List<object> received = new();
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

        // Both admitted and started before either is released — proves Parallel never waits.
        op.Fail(input: "d", error: d);
        TestUtil.WaitUntil(() => received.Count == 1);
        op.Release(input: "c", result: "C");
        TestUtil.WaitUntil(() => received.Count == 2);
        op.Fail(input: "b", error: b);
        TestUtil.WaitUntil(() => received.Count == 3);
        op.Release(input: "a", result: "A");
        TestUtil.WaitUntil(() => received.Count == 4);

        // Completion order, not submission order.
        await Assert.That(received).IsEquivalentTo(new object[] { d, "C", b, "A" }, CollectionOrdering.Matching);

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
        List<string> received = new();
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

        await Assert.That(received).IsEquivalentTo(new[] { "A", "B", "C" }, CollectionOrdering.Matching);

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
        List<string> received = new();
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

        // g1-a and g2-a are in different groups, so both start; g1-b waits behind g1-a.
        TestUtil.WaitUntil(() => op.HasStarted("g1-a") && op.HasStarted("g2-a"));
        await Assert.That(op.HasStarted("g1-b")).IsFalse().Because("g1-b shares a group with g1-a and must wait.");

        op.Release(input: "g1-a", result: "A1");
        TestUtil.WaitUntil(() => op.HasStarted("g1-b"));

        op.Release(input: "g1-b", result: "B1");
        op.Release(input: "g2-a", result: "A2");
        TestUtil.WaitUntil(() => received.Count == 3);

        await Assert.That(received).IsEquivalentTo(new[] { "A1", "B1", "A2" });

        status.Dispose();
        l.Unlisten();
        return;

        // Group is the character before the hyphen: "g1-a"/"g1-b" share a group, "g2-a" doesn't.
        static string GetGroup(string v) => v.Split('-')[0];
    }

    [Test]
    public async Task SwitchLatest_SupersededRunIsNeverPublished()
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op = new();
        List<string> received = new();
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

        // Object "a" is still in flight when it's superseded; releasing it must not publish.
        op.Release(input: "a", result: "A");
        op.Release(input: "b", result: "B");
        TestUtil.WaitUntil(() => received.Count == 1);

        // Give object "a" a fair chance to have published if the supersede logic were broken.
        Thread.Sleep(100);

        await Assert.That(received).IsEquivalentTo(new[] { "B" }, CollectionOrdering.Matching);

        status.Dispose();
        l.Unlisten();
    }

    [Test]
    public async Task Queue_SameStrategyInstanceSharedAcrossTwoPipelinesDoesNotCrossSerialize()
    {
        AsyncConcurrencyStrategyBase<string, string> sharedQueue = AsyncConcurrencyStrategyFactory.Queue<string>();

        StreamSink<string> source1 = Stream.CreateSink<string>();
        StreamSink<string> results1 = Stream.CreateSink<string>();
        StreamSink<Exception> errors1 = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op1 = new();
        List<string> received1 = new();
        IListener l1 = results1.ListenStrong(received1.Add);

        StreamSink<string> source2 = Stream.CreateSink<string>();
        StreamSink<string> results2 = Stream.CreateSink<string>();
        StreamSink<Exception> errors2 = Stream.CreateSink<Exception>();
        ControlledOperation<string, string> op2 = new();
        List<string> received2 = new();
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

        // Pipeline 2 must be able to start immediately despite pipeline 1's queue being busy —
        // proves each call gets its own independent scheduling state, per CreateState's contract.
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
