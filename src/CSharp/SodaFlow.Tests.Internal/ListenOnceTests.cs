using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

/// <summary>
///     Covers ListenOnce and ListenOnceStrong for a stream that fires two times in
///     one transaction. A sink cannot do that, because it combines its sends, thus
///     these tests send on the stream directly.
/// </summary>
public sealed class ListenOnceTests
{
    [Test]
    public async Task ListenOnceCallsTheHandlerOnlyForTheFirstOfTwoFiringsInOneTransaction()
    {
        List<int> @out = ListenThenFireTwice(static (s, handler) => s.ListenOnce(handler));

        await Assert.That(@out).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task ListenOnceStrongCallsTheHandlerOnlyForTheFirstOfTwoFiringsInOneTransaction()
    {
        List<int> @out = ListenThenFireTwice(static (s, handler) => s.ListenOnceStrong(handler));

        await Assert.That(@out).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task ListenOnceAfterTwoFiringsInItsTransactionCallsTheHandlerOnlyForTheFirst()
    {
        List<int> @out = FireTwiceThenListen(static (s, handler) => s.ListenOnce(handler));

        await Assert.That(@out).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task ListenOnceStrongAfterTwoFiringsInItsTransactionCallsTheHandlerOnlyForTheFirst()
    {
        List<int> @out = FireTwiceThenListen(static (s, handler) => s.ListenOnceStrong(handler));

        await Assert.That(@out).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);
    }

    // The listener with an outcome is a one-shot listener also. It gives the first of two firings,
    // after the transaction commits.
    [Test]
    public async Task ListenOnceWithOutcomeGivesOnlyTheFirstOfTwoFiringsInOneTransaction()
    {
        Stream<int> s = new();
        List<int> committed = [];
        List<Exception> failed = [];
        IListener l = s.ListenOnceWithOutcomeStrongImpl(onCommitted: committed.Add, onFailed: failed.Add);

        TransactionInternal.Apply((trans, _) =>
        {
            s.Send(trans: trans, a: 1);
            s.Send(trans: trans, a: 2);
            return UnitInternal.Value;
        });

        l.Unlisten();

        await Assert.That(committed).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);
        await Assert.That(failed).IsEmpty();
    }

    [Test]
    public async Task ListenOnceWithOutcomeAfterTwoFiringsInItsTransactionGivesOnlyTheFirst()
    {
        Stream<int> s = new();
        List<int> committed = [];
        List<Exception> failed = [];

        IListener l =
            TransactionInternal.Apply((trans, _) =>
            {
                s.Send(trans: trans, a: 1);
                s.Send(trans: trans, a: 2);
                return s.ListenOnceWithOutcomeStrongImpl(onCommitted: committed.Add, onFailed: failed.Add);
            });

        l.Unlisten();

        await Assert.That(committed).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);
        await Assert.That(failed).IsEmpty();
    }

    // The first firing ends the listener. Without that, each call leaves a listener on the stream
    // for the life of the stream. Each firing after it then queues an entry for nothing.
    [Test]
    public async Task ListenOnceWithOutcomeLeavesNoListenerOnTheStreamAfterItsFiring()
    {
        Stream<int> s = new();
        List<int> committed = [];
        IStrongListener l = s.ListenOnceWithOutcomeStrongImpl(onCommitted: committed.Add, onFailed: static _ => { });
        int before = CountListeners(s);

        TransactionInternal.Apply((trans, _) =>
        {
            s.Send(trans: trans, a: 1);
            return UnitInternal.Value;
        });

        await Assert.That(before).IsEqualTo(1);
        await Assert.That(CountListeners(s)).IsEqualTo(0);
        await Assert.That(committed).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);

        l.Unlisten();
    }

    // The stream fires, and the transaction fails before the entry of the listener runs. The
    // listener gets the exception of the transaction, and that firing ends the listener also.
    [Test]
    public async Task ListenOnceWithOutcomeGetsTheExceptionOfAFiringThatATransactionDiscardsAndLeavesNoListener()
    {
        Stream<int> s = new();
        List<int> committed = [];
        List<Exception> failed = [];
        InvalidOperationException fromBody = new("The test fails this transaction.");
        IStrongListener l = s.ListenOnceWithOutcomeStrongImpl(onCommitted: committed.Add, onFailed: failed.Add);

        await Assert.That(() =>
                TransactionInternal.Apply<UnitInternal>((trans, _) =>
                {
                    s.Send(trans: trans, a: 1);

                    throw fromBody;
                }))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(committed).IsEmpty();
        await Assert.That(failed.Count).IsEqualTo(1);
        await Assert.That(failed[0]).IsSameReferenceAs(fromBody);
        await Assert.That(CountListeners(s)).IsEqualTo(0);

        l.Unlisten();
    }

    private static int CountListeners(Stream<int> s)
    {
        int count = 0;

        foreach (Node<int>.Target _ in s.Node.GetListenersCopy())
        {
            count++;
        }

        return count;
    }

    // Each firing queues an entry for the listener, and the Unlisten after the first firing does
    // not remove the second entry.
    private static List<int> ListenThenFireTwice(Func<Stream<int>, Action<int>, IListener> listenOnce)
    {
        Stream<int> s = new();
        List<int> @out = [];
        IListener l = listenOnce(arg1: s, arg2: @out.Add);

        TransactionInternal.Apply((trans, _) =>
        {
            s.Send(trans: trans, a: 1);
            s.Send(trans: trans, a: 2);
            return UnitInternal.Value;
        });

        l.Unlisten();
        return @out;
    }

    // Listen sends the earlier firings of the transaction to the new handler in one loop, and
    // Unlisten does not stop that loop.
    private static List<int> FireTwiceThenListen(Func<Stream<int>, Action<int>, IListener> listenOnce)
    {
        Stream<int> s = new();
        List<int> @out = [];

        IListener l =
            TransactionInternal.Apply((trans, _) =>
            {
                s.Send(trans: trans, a: 1);
                s.Send(trans: trans, a: 2);
                return listenOnce(arg1: s, arg2: @out.Add);
            });

        l.Unlisten();
        return @out;
    }
}
