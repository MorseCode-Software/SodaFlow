using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

/// <summary>
///     Covers the state that a transaction keeps until it closes. A transaction
///     that fails must discard that state, and the graph must then work as if the
///     transaction did not occur.
/// </summary>
public sealed class FailedTransactionTests
{
    [Test]
    public async Task AStreamDoesNotSendTheFiringsOfAFailedTransactionToALaterListener()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        Fail(s: s, value: 1);

        List<int> @out = [];
        IListener l = s.ListenStrong(@out.Add);
        s.Send(2);
        l.Unlisten();

        List<int> later = [];
        IListener l2 = s.ListenStrong(later.Add);
        l2.Unlisten();

        await Assert.That(@out).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);
        await Assert.That(later).IsEmpty();
    }

    // The loop that is not looped throws from its last action. The clear of the firings is the
    // last action after it, thus this covers a failure in the last phase.
    [Test]
    public async Task AStreamDoesNotKeepItsFiringsWhenALastActionFails()
    {
        StreamSink<int> s = Stream.CreateSink<int>();

        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    _ = Stream.CreateLoop<int>();
                    s.Send(1);
                }))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage("Loop was not looped.");

        List<int> @out = [];
        IListener l = s.ListenStrong(@out.Add);
        s.Send(2);
        l.Unlisten();

        await Assert.That(@out).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task ACellUpdatesAfterAFailedTransaction()
    {
        CellSink<int> c = Cell.CreateSink(0);
        Fail(s: c.Updates(), send: () => c.Send(1));

        int afterFailure = c.Sample();
        c.Send(2);
        int afterSecond = c.Sample();
        c.Send(3);

        await Assert.That(afterFailure).IsEqualTo(0);
        await Assert.That(afterSecond).IsEqualTo(2);
        await Assert.That(c.Sample()).IsEqualTo(3);
    }

    [Test]
    public async Task AccumContinuesFromTheLastCommittedStateAfterAFailedTransaction()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        Cell<int> sum = s.Accum(initialState: 0, f: static (a, total) => a + total);
        Fail(s: sum.Updates(), send: () => s.Send(1));

        s.Send(10);
        s.Send(100);

        await Assert.That(sum.Sample()).IsEqualTo(110);
    }

    [Test]
    public async Task ASinkAcceptsASendAfterATransactionThatSentToItTwice()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];
        IListener l = s.ListenStrong(@out.Add);

        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    s.Send(1);
                    s.Send(2);
                }))
            .ThrowsExactly<InvalidOperationException>();

        s.Send(3);
        l.Unlisten();

        await Assert.That(@out).IsEquivalentTo(expected: [3], ordering: CollectionOrdering.Matching);
    }

    // The merge combines a value only when a throw stops the transaction after the merge and before
    // its output. The rank of the throw sets that, thus this test tries each depth of a chain. A
    // merge that kept the value from the failed transaction fires nothing at that depth.
    [Test]
    public async Task AMergeFiresAfterATransactionThatFailedWhileItCombined()
    {
        for (int depth = 0; depth < 6; depth++)
        {
            StreamSink<int> s1 = Stream.CreateSink<int>();
            StreamSink<int> s2 = Stream.CreateSink<int>();
            Stream<int> merged = s1.Merge(s2: s2, f: static (x, y) => x + y);

            Stream<int> chain = s1;

            for (int i = 0; i < depth; i++)
            {
                chain = chain.Map(static x => x);
            }

            // A listener runs after each ranked node, thus the throw comes from a map, which has a rank.
            Fail(
                s: chain.Map<int, int>(static _ => throw new InvalidOperationException("abort")),
                send: () => s1.Send(1));

            List<int> @out = [];
            IListener l = merged.ListenStrong(@out.Add);
            s2.Send(5);
            s2.Send(6);
            l.Unlisten();

            await Assert.That(@out)
                .IsEquivalentTo(expected: [5, 6], ordering: CollectionOrdering.Matching)
                .Because($"the throw is at depth {depth}");
        }
    }

    // The throw comes after the lift. The update of b comes first. Thus, a lift that kept the value
    // of a from the transaction that failed gives 22 and not 21.
    [Test]
    public async Task ALiftUsesTheCommittedValuesAfterAFailureDownstreamOfIt()
    {
        CellSink<int> a = Cell.CreateSink(1);
        CellSink<int> b = Cell.CreateSink(10);
        Cell<int> sum = a.Lift(c2: b, f: static (x, y) => x + y);
        Fail(s: sum.Updates(), send: () => a.Send(2));

        int afterFailure = sum.Sample();
        List<int> @out = [];
        IListener l = sum.Updates().ListenStrong(@out.Add);
        b.Send(20);
        a.Send(3);
        l.Unlisten();

        await Assert.That(afterFailure).IsEqualTo(11);
        await Assert.That(@out).IsEquivalentTo(expected: [21, 23], ordering: CollectionOrdering.Matching);
        await Assert.That(sum.Sample()).IsEqualTo(23);
    }

    private static void Fail(StreamSink<int> s, int value) => Fail(s: s, send: () => s.Send(value));

    // Sends with a listener that throws, and removes the listener after the failure.
    private static void Fail<T>(Stream<T> s, Action send)
    {
        IListener boom = s.ListenStrong(static _ => throw new InvalidOperationException("abort"));

        try
        {
            send();
        }
        catch (InvalidOperationException)
        {
        }

        boom.Unlisten();
    }
}
