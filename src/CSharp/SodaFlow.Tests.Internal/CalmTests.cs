using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

/// <summary>
///     Covers the state protocol behind Calm rather than its filtering, which StreamTests
///     exercises. These use the internal Calm(Lazy, areEqual) overload so the initial value can be
///     instrumented: from the public API it always comes from SampleLazy and its forcing is invisible.
///     Calm has no denotational coverage, so this is the only specification-level cover
///     the protocol has.
/// </summary>
public sealed class CalmTests
{
    private static Stream<int> Calm(Stream<int> source, Lazy<MaybeInternal<int>> init) =>
        source.Calm(init: init, areEqual: static (x, y) => x == y);

    // The initial value is forced in the sample phase when nothing fires, and this matches the
    // behavior Calm replaced. Nothing observable depends on the value here - only on it having been
    // asked for at all.
    [Test]
    public async Task InitialValueIsForcedEvenWhenNothingFires()
    {
        int forceOperations = 0;
        StreamSink<int> s = Stream.CreateSink<int>();

        IListener l =
            TransactionInternal.Apply((_, _) =>
            {
                Lazy<MaybeInternal<int>> init =
                    new(() =>
                    {
                        forceOperations++;
                        return MaybeInternal<int>.None;
                    });

                return Calm(source: s, init: init)
                    .ListenStrong(static _ =>
                    {
                    });
            });

        await Assert.That(forceOperations)
            .IsEqualTo(1)
            .Because("the initial value should be forced once, in the sample phase");

        l.Unlisten();
    }

    // Forced one time, and not one time for each firing. A bare `committed = init.Value` with no
    // guard forces only one time, because Lazy caches, but it also resets the remembered
    // value at each firing. Thus, the test asserts the count and the output together.
    [Test]
    public async Task InitialValueIsForcedOnceAcrossManyFirings()
    {
        int forceOperations = 0;
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];

        IListener l =
            TransactionInternal.Apply((_, _) =>
            {
                Lazy<MaybeInternal<int>> init =
                    new(() =>
                    {
                        forceOperations++;
                        return MaybeInternal<int>.None;
                    });

                return Calm(source: s, init: init).ListenStrong(@out.Add);
            });

        s.Send(1);
        s.Send(1);
        s.Send(2);
        s.Send(2);
        s.Send(1);

        l.Unlisten();

        await Assert.That(forceOperations).IsEqualTo(1).Because("the initial value should be forced exactly once");

        await Assert.That(@out)
            .IsEquivalentTo(expected: [1, 2, 1], ordering: CollectionOrdering.Matching)
            .Because(
                "re-reading the initial value per firing would reset the remembered value and let "
                + "duplicates through");
    }

    // A non-None initial value seeds the compare, so a first firing equal to it is suppressed.
    // This is the condition a sentinel cannot express: None is a legitimate initial value, so
    // "uninitialized" needs its own flag.
    [Test]
    public async Task NonEmptyInitialValueSuppressesAMatchingFirstFiring()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];

        IListener l =
            TransactionInternal.Apply((_, _) =>
                Calm(source: s, init: new Lazy<MaybeInternal<int>>(static () => MaybeInternal.Some(7)))
                    .ListenStrong(@out.Add));

        s.Send(7);
        s.Send(8);
        s.Send(8);
        s.Send(7);

        l.Unlisten();

        await Assert.That(@out)
            .IsEquivalentTo(expected: [8, 7], ordering: CollectionOrdering.Matching)
            .Because("the first 7 matches the initial value");
    }

    // A suppressed firing must keep the remembered value, and must not clear it, which is
    // what the behavior-backed version got from feeding its state back on each firing.
    [Test]
    public async Task SuppressedFiringKeepsTheRememberedValue()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];

        IListener l =
            TransactionInternal.Apply((_, _) =>
                Calm(source: s, init: new Lazy<MaybeInternal<int>>(static () => MaybeInternal<int>.None))
                    .ListenStrong(@out.Add));

        s.Send(1);
        s.Send(1);
        s.Send(1);
        s.Send(1);
        s.Send(2);

        l.Unlisten();

        await Assert.That(@out)
            .IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching)
            .Because("a run of suppressed firings must not clear what was remembered");
    }

    // A transaction that fails must not keep the remembered value updated. Calm defers the
    // commit to trans.Last, and the failing path drops that queue. Thus, a firing in a transaction
    // that throws leaves no record. A commit at that point records it, and then incorrectly suppresses
    // the same value at the next firing.
    //
    // The throw has to occur downstream of Calm, and not before the send operation, because sends
    // are queued: an exception before the drain aborts the transaction without
    // the handler of Calm ever runs, which cannot tell the two designs apart.
    [Test]
    public async Task AFailedTransactionDoesNotCommitTheRememberedValue()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];

        Stream<int> calmed =
            TransactionInternal.Apply((_, _) =>
                Calm(source: s, init: new Lazy<MaybeInternal<int>>(static () => MaybeInternal<int>.None)));

        IListener good = calmed.ListenStrong(@out.Add);
        IListener boom = calmed.ListenStrong(static _ => throw new InvalidOperationException("abort"));

        await Assert.That(() => s.Send(1)).ThrowsExactly<InvalidOperationException>();

        boom.Unlisten();

        // 1 again. The aborted transaction must not have committed it.
        s.Send(1);

        good.Unlisten();

        await Assert.That(@out)
            .IsEquivalentTo(expected: [1, 1], ordering: CollectionOrdering.Matching)
            .Because("the firing from the failed transaction must not suppress the retry");
    }

    // The remembered value is committed at the end of the transaction, so simultaneous sources
    // feeding one firing compare against what the previous transaction left, not against anything
    // computed in this one.
    [Test]
    public async Task ComparisonUsesTheValueCommittedByThePreviousTransaction()
    {
        StreamSink<int> a = Stream.CreateSink<int>();
        StreamSink<int> b = Stream.CreateSink<int>();
        Stream<int> merged = a.Merge(s2: b, f: static (x, y) => x + y);
        List<int> @out = [];

        IListener l =
            TransactionInternal.Apply((_, _) =>
                Calm(source: merged, init: new Lazy<MaybeInternal<int>>(static () => MaybeInternal<int>.None))
                    .ListenStrong(@out.Add));

        Transaction.RunVoid(() =>
        {
            a.Send(1);
            b.Send(1);
        });

        a.Send(2);
        a.Send(3);

        Transaction.RunVoid(() =>
        {
            a.Send(1);
            b.Send(2);
        });

        a.Send(3);

        l.Unlisten();

        await Assert.That(@out).IsEquivalentTo(expected: [2, 3], ordering: CollectionOrdering.Matching);
    }
}
