using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SodaFlow.Tests.Internal
{
    /// <summary>
    ///     Covers the state protocol behind Calm rather than its filtering, which StreamTests already
    ///     exercises. These reach the internal Calm(Lazy, areEqual) overload so the initial value can be
    ///     instrumented: from the public API it always comes from SampleLazy and its forcing is invisible.
    ///     Calm has no denotational conformance coverage, so this is the only specification-level cover
    ///     the protocol has.
    /// </summary>
    [TestFixture]
    public class CalmTests
    {
        private static Stream<int> Calm(Stream<int> source, Lazy<MaybeInternal<int>> init) =>
            source.Calm(init, (x, y) => x == y);

        // The initial value is forced in the sample phase whether or not anything fires, matching the
        // behavior Calm replaced. Nothing observable depends on the value here - only on it having been
        // asked for at all.
        [Test]
        public void InitialValueIsForcedEvenWhenNothingFires()
        {
            int forcings = 0;
            StreamSink<int> s = Stream.CreateSink<int>();

            IListener l = TransactionInternal.Apply(
                (trans, _) =>
                {
                    Lazy<MaybeInternal<int>> init = new Lazy<MaybeInternal<int>>(
                        () =>
                        {
                            forcings++;
                            return MaybeInternal.None;
                        });

                    return Calm(s, init).ListenStrong(v => { });
                });

            Assert.AreEqual(1, forcings, "the initial value should be forced once, in the sample phase");

            l.Unlisten();
        }

        // Forced once, not once per firing. A bare `committed = init.Value` without the guard would
        // still force only once - Lazy caches - but would also reset the remembered value on every
        // firing, so the count and the output are asserted together.
        [Test]
        public void InitialValueIsForcedOnceAcrossManyFirings()
        {
            int forcings = 0;
            StreamSink<int> s = Stream.CreateSink<int>();
            List<int> @out = new List<int>();

            IListener l = TransactionInternal.Apply(
                (trans, _) =>
                {
                    Lazy<MaybeInternal<int>> init = new Lazy<MaybeInternal<int>>(
                        () =>
                        {
                            forcings++;
                            return MaybeInternal.None;
                        });

                    return Calm(s, init).ListenStrong(@out.Add);
                });

            s.Send(1);
            s.Send(1);
            s.Send(2);
            s.Send(2);
            s.Send(1);

            l.Unlisten();

            Assert.AreEqual(1, forcings, "the initial value should be forced exactly once");
            CollectionAssert.AreEqual(
                new[] { 1, 2, 1 },
                @out,
                "re-reading the initial value per firing would reset the remembered value and let " +
                "duplicates through");
        }

        // A non-None initial value seeds the comparison, so a first firing equal to it is suppressed.
        // This is the case a sentinel cannot express: None is a legitimate initial value, so
        // "uninitialized" needs its own flag.
        [Test]
        public void NonEmptyInitialValueSuppressesAMatchingFirstFiring()
        {
            StreamSink<int> s = Stream.CreateSink<int>();
            List<int> @out = new List<int>();

            IListener l = TransactionInternal.Apply(
                (trans, _) => Calm(s, new Lazy<MaybeInternal<int>>(() => MaybeInternal.Some(7)))
                    .ListenStrong(@out.Add));

            s.Send(7);
            s.Send(8);
            s.Send(8);
            s.Send(7);

            l.Unlisten();

            CollectionAssert.AreEqual(new[] { 8, 7 }, @out, "the first 7 matches the initial value");
        }

        // A suppressed firing must carry the remembered value forward rather than clearing it, which is
        // what the behavior-backed version got from feeding its state back on every firing.
        [Test]
        public void SuppressedFiringKeepsTheRememberedValue()
        {
            StreamSink<int> s = Stream.CreateSink<int>();
            List<int> @out = new List<int>();

            IListener l = TransactionInternal.Apply(
                (trans, _) => Calm(s, new Lazy<MaybeInternal<int>>(() => MaybeInternal.None))
                    .ListenStrong(@out.Add));

            s.Send(1);
            s.Send(1);
            s.Send(1);
            s.Send(1);
            s.Send(2);

            l.Unlisten();

            CollectionAssert.AreEqual(
                new[] { 1, 2 },
                @out,
                "a run of suppressed firings must not clear what was remembered");
        }

        // A transaction that fails must not leave the remembered value updated. Calm defers the
        // commit to trans.Last, and the failing path drops that queue, so a firing inside a
        // transaction that throws is as though it never happened. Committing in place instead would
        // record it and wrongly suppress the same value next time.
        //
        // The throw has to come from downstream of Calm rather than before the send, because sends
        // are queued: an exception raised before the drain would abort the transaction without
        // Calm's handler ever running, which cannot tell the two designs apart.
        [Test]
        public void AFailedTransactionDoesNotCommitTheRememberedValue()
        {
            StreamSink<int> s = Stream.CreateSink<int>();
            List<int> @out = new List<int>();

            Stream<int> calmed = TransactionInternal.Apply(
                (trans, _) => Calm(s, new Lazy<MaybeInternal<int>>(() => MaybeInternal.None)));

            IListener good = calmed.ListenStrong(@out.Add);
            IListener boom = calmed.ListenStrong(v => throw new InvalidOperationException("abort"));

            Assert.Throws<InvalidOperationException>(() => s.Send(1));

            boom.Unlisten();

            // 1 again. The aborted transaction must not have committed it.
            s.Send(1);

            good.Unlisten();

            CollectionAssert.AreEqual(
                new[] { 1, 1 },
                @out,
                "the firing from the failed transaction must not suppress the retry");
        }

        // The remembered value is committed at the end of the transaction, so simultaneous sources
        // feeding one firing compare against what the previous transaction left, not against anything
        // computed within this one.
        [Test]
        public void ComparisonUsesTheValueCommittedByThePreviousTransaction()
        {
            StreamSink<int> a = Stream.CreateSink<int>();
            StreamSink<int> b = Stream.CreateSink<int>();
            Stream<int> merged = a.Merge(b, (x, y) => x + y);
            List<int> @out = new List<int>();

            IListener l = TransactionInternal.Apply(
                (trans, _) => Calm(merged, new Lazy<MaybeInternal<int>>(() => MaybeInternal.None))
                    .ListenStrong(@out.Add));

            Transaction.RunVoid(
                () =>
                {
                    a.Send(1);
                    b.Send(1);
                });

            a.Send(2);
            a.Send(3);

            Transaction.RunVoid(
                () =>
                {
                    a.Send(1);
                    b.Send(2);
                });

            a.Send(3);

            l.Unlisten();

            CollectionAssert.AreEqual(new[] { 2, 3 }, @out);
        }
    }
}
