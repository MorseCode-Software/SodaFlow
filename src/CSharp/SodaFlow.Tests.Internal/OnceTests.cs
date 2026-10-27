using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

/// <summary>
///     Covers Once for a stream that fires two times in one transaction. A sink
///     cannot do that, because it combines its sends, thus these tests send on
///     the stream directly.
/// </summary>
public sealed class OnceTests
{
    [Test]
    public async Task OnceSendsOnlyTheFirstOfTwoFiringsInOneTransaction()
    {
        Stream<int> s = new();
        List<int> @out = [];
        IListener l = s.Once().ListenStrong(@out.Add);

        TransactionInternal.Apply((trans, _) =>
        {
            s.Send(trans: trans, a: 1);
            s.Send(trans: trans, a: 2);
            return UnitInternal.Value;
        });

        l.Unlisten();

        await Assert.That(@out).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);
    }

    // Listen sends the earlier firings of the transaction to the new handler in one loop, and
    // Unlisten does not stop that loop. Thus, only the test in the handler of Once stops the
    // second value.
    [Test]
    public async Task OnceAfterTwoFiringsInItsTransactionSendsOnlyTheFirst()
    {
        Stream<int> s = new();
        List<int> @out = [];

        IListener l =
            TransactionInternal.Apply((trans, _) =>
            {
                s.Send(trans: trans, a: 1);
                s.Send(trans: trans, a: 2);
                return s.Once().ListenStrong(@out.Add);
            });

        l.Unlisten();

        await Assert.That(@out).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);
    }
}
