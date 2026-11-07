using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

/// <summary>
///     Covers the reads that a snapshot of the latest values makes of its
///     behaviors. A behavior in these tests counts its reads.
/// </summary>
public sealed class SnapshotLatestTests
{
    // An update of one behavior alone queues the output, but the stream did not fire. The output
    // clears the captures and sends nothing. Before, it also read each behavior that did not update.
    [Test]
    public async Task AnUpdateOfABehaviorAloneDoesNotReadTheOtherBehaviors()
    {
        Stream<int> s = new();
        Stream<int> updates = new();
        Behavior<int> b1 = updates.HoldInternal(10);
        CountingBehavior b2 = new(20);
        List<int> @out = [];

        IListener l =
            s.SnapshotLatestImpl(b1: b1, b2: b2, f: static (a, v1, v2) => a + v1 + v2).ListenStrong(@out.Add);

        b2.Reads = 0;

        Send(s: updates, a: 11);
        int readsAfterTheUpdate = b2.Reads;
        Send(s: s, a: 1);
        l.Unlisten();

        await Assert.That(readsAfterTheUpdate).IsEqualTo(0);
        await Assert.That(b2.Reads).IsEqualTo(1);
        await Assert.That(@out).IsEquivalentTo(expected: [32], ordering: CollectionOrdering.Matching);
    }

    private static void Send(Stream<int> s, int a) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
            {
                state.S.Send(trans: trans, a: state.A);
                return UnitInternal.Value;
            },
            state: (S: s, A: a));

    private sealed class CountingBehavior(in int value) : Behavior<int>(value)
    {
        public int Reads { get; set; }

        internal override int SampleNoTransaction()
        {
            this.Reads++;
            return base.SampleNoTransaction();
        }
    }
}
