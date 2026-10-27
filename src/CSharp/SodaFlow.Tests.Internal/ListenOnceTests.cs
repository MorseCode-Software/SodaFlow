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
