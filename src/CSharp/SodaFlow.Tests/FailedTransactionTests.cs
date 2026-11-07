using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SodaFlow.Functional;
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

    // The throw is in a map of the updates of a, made after the lift. The node of the map and the
    // output node of the lift have the same rank. The queue runs the entries of one rank in the
    // sequence of their links. Thus, the lift captures the value of a, and then the map throws,
    // before the lift puts its inputs together. A lift that kept the captured value gives 120 and
    // not 21 at the next update of b. A lift whose gate stayed closed gives nothing.
    [Test]
    public async Task ALiftUsesTheCommittedValuesAfterAFailureBetweenItsCaptureAndItsOutput()
    {
        CellSink<int> a = Cell.CreateSink(1);
        CellSink<int> b = Cell.CreateSink(10);
        Cell<int> sum = a.Lift(c2: b, f: static (x, y) => x + y);

        Fail(
            s: a.Updates().Map<int, int>(static _ => throw new InvalidOperationException("abort")),
            send: () => a.Send(100));

        List<int> @out = [];
        IListener l = sum.Updates().ListenStrong(@out.Add);
        b.Send(20);
        l.Unlisten();

        await Assert.That(sum.Sample()).IsEqualTo(21);
        await Assert.That(@out).IsEquivalentTo(expected: [21], ordering: CollectionOrdering.Matching);
    }

    // The same for the lift of a list, which keeps a slot for each input in an array.
    [Test]
    public async Task AListLiftUsesTheCommittedValuesAfterAFailureBetweenItsCaptureAndItsOutput()
    {
        CellSink<int> a = Cell.CreateSink(1);
        CellSink<int> b = Cell.CreateSink(10);
        CellSink<int> c = Cell.CreateSink(100);
        Cell<IReadOnlyList<int>> values = new List<Cell<int>> { a, b, c }.Lift();

        Fail(
            s: a.Updates().Map<int, int>(static _ => throw new InvalidOperationException("abort")),
            send: () => a.Send(1000));

        List<IReadOnlyList<int>> @out = [];
        IListener l = values.Updates().ListenStrong(@out.Add);
        b.Send(20);
        l.Unlisten();

        await Assert.That(values.Sample())
            .IsEquivalentTo(expected: [1, 20, 100], ordering: CollectionOrdering.Matching);

        await Assert.That(@out).Count().IsEqualTo(1);
        await Assert.That(@out[0]).IsEquivalentTo(expected: [1, 20, 100], ordering: CollectionOrdering.Matching);
    }

    // The throw comes after the output of the apply. The apply has seen the new function, and the
    // transaction then discards it from the cell. An apply that kept the function gives 20 and not
    // 3 at the next update of the value.
    [Test]
    public async Task AnApplyUsesTheCommittedFunctionAfterAFailureDownstreamOfIt()
    {
        CellSink<Func<int, int>> cf = Cell.CreateSink<Func<int, int>>(static x => x + 1);
        CellSink<int> ca = Cell.CreateSink(1);
        Cell<int> applied = ca.Apply(cf);
        Fail(s: applied.Updates(), send: () => cf.Send(static x => x * 10));

        List<int> @out = [];
        IListener l = applied.Updates().ListenStrong(@out.Add);
        ca.Send(2);
        l.Unlisten();

        await Assert.That(applied.Sample()).IsEqualTo(3);
        await Assert.That(@out).IsEquivalentTo(expected: [3], ordering: CollectionOrdering.Matching);
    }

    // The same as the lift, for the cell of a SnapshotLatest.
    [Test]
    public async Task ASnapshotLatestUsesTheCommittedValueAfterAFailureBetweenItsCaptureAndItsOutput()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        CellSink<int> c = Cell.CreateSink(10);
        Stream<string> latest = s.SnapshotLatest(c: c, f: static (a, v) => $"{a},{v}");

        Fail(
            s: c.Updates().Map<int, int>(static _ => throw new InvalidOperationException("abort")),
            send: () => c.Send(100));

        List<string> @out = [];
        IListener l = latest.ListenStrong(@out.Add);
        s.Send(1);
        l.Unlisten();

        await Assert.That(@out).IsEquivalentTo(expected: ["1,10"], ordering: CollectionOrdering.Matching);
    }

    // The stream of a SnapshotLatest keeps its firings until the output runs. A failure between
    // the two left a firing in that list, and the next update of the cell alone then sent it. The
    // map that throws stays linked to s, thus this test does not send to s again.
    [Test]
    public async Task ASnapshotLatestDoesNotSendTheFiringOfAFailedTransaction()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        CellSink<int> c = Cell.CreateSink(10);
        Stream<string> latest = s.SnapshotLatest(c: c, f: static (a, v) => $"{a},{v}");

        Fail(
            s: s.Map<int, int>(static _ => throw new InvalidOperationException("abort")),
            send: () => s.Send(7));

        List<string> @out = [];
        IListener l = latest.ListenStrong(@out.Add);
        c.Send(20);
        l.Unlisten();

        await Assert.That(@out).IsEmpty();
    }

    // An exception from f fails the transaction in the output step. The gate has left the queue,
    // thus the transaction does not discard it. The output clears its firings before it calls f.
    // Thus, the next update of the cell alone does not send the firing again.
    [Test]
    public async Task ASnapshotLatestDoesNotSendAFiringAgainAfterAnExceptionFromItsFunction()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        CellSink<int> c = Cell.CreateSink(10);

        Stream<string> latest =
            s.SnapshotLatest(
                c: c,
                f: static (a, v) => a == 7 ? throw new InvalidOperationException("abort") : $"{a},{v}");

        List<string> @out = [];
        IListener l = latest.ListenStrong(@out.Add);

        await Assert.That(() => s.Send(7)).ThrowsExactly<InvalidOperationException>().WithMessage("abort");

        c.Send(20);
        s.Send(1);
        l.Unlisten();

        await Assert.That(@out).IsEquivalentTo(expected: ["1,20"], ordering: CollectionOrdering.Matching);
    }

    // The same as the lift, for a merge of two streams of different types. A merge that kept the
    // captured firing gives (7, 5) and not (none, 5).
    [Test]
    public async Task AMergeOfTwoTypesDoesNotKeepTheFiringOfAFailedTransaction()
    {
        StreamSink<int> s1 = Stream.CreateSink<int>();
        StreamSink<int> s2 = Stream.CreateSink<int>();
        Stream<(Maybe<int>, Maybe<int>)> merged = s1.Merge(s2);

        Fail(
            s: s1.Map<int, int>(static _ => throw new InvalidOperationException("abort")),
            send: () => s1.Send(7));

        List<(Maybe<int>, Maybe<int>)> @out = [];
        IListener l = merged.ListenStrong(@out.Add);
        s2.Send(5);
        l.Unlisten();

        await Assert.That(@out)
            .IsEquivalentTo(expected: [(Maybe<int>.None, Maybe.Some(5))], ordering: CollectionOrdering.Matching);
    }

    // The switch ran in the handler, and the throw came after it, in a listener. The transaction
    // discards the update of the selector, thus the switch must go with it. A switch that stayed
    // followed c2 and gave 20 at the next update of c2, and ignored the update of c1.
    [Test]
    public async Task ASwitchCDoesNotKeepASwitchFromAFailedTransaction()
    {
        CellSink<int> c1 = Cell.CreateSink(1);
        CellSink<int> c2 = Cell.CreateSink(10);
        CellSink<Cell<int>> selector = Cell.CreateSink<Cell<int>>(c1);
        Cell<int> switched = selector.SwitchC();
        Fail(s: switched.Updates(), send: () => selector.Send(c2));

        await Assert.That(ReferenceEquals(objA: selector.Sample(), objB: c1)).IsTrue();
        await Assert.That(switched.Sample()).IsEqualTo(1);

        List<int> @out = [];
        IListener l = switched.Updates().ListenStrong(@out.Add);
        c2.Send(20);
        c1.Send(2);
        l.Unlisten();

        await Assert.That(switched.Sample()).IsEqualTo(2);
        await Assert.That(@out).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);
    }

    // The previous inner cell keeps its listener until the commit. Thus, an update of that cell
    // in the switch transaction must not get to the output after the value of the new cell. Its
    // chain has the lower rank, and the drain runs it first. The chain of maps puts the previous
    // cell above the selector in rank. That is the sequence that shows a stale value if the rank
    // order does not hold.
    [Test]
    public async Task AnUpdateOfTheOldCellInTheSwitchTransactionDoesNotReachTheOutputLast()
    {
        CellSink<int> source = Cell.CreateSink(1);
        Cell<int> c1 = source.Map(static v => v).Map(static v => v).Map(static v => v).Map(static v => v);
        CellSink<int> c2 = Cell.CreateSink(10);
        CellSink<Cell<int>> selector = Cell.CreateSink(c1);
        Cell<int> switched = selector.SwitchC();

        List<int> @out = [];
        IListener l = switched.Updates().ListenStrong(@out.Add);

        Transaction.RunVoid(() =>
        {
            selector.Send(c2);
            source.Send(5);
        });

        l.Unlisten();

        await Assert.That(switched.Sample()).IsEqualTo(10);
        await Assert.That(@out).IsEquivalentTo(expected: [10], ordering: CollectionOrdering.Matching);
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

    // A throw from the body of a transaction fails it, as a throw from a listener does. The
    // sends before the throw do not reach the graph, and the next transaction works.
    [Test]
    public async Task AThrowFromTheBodyDiscardsTheSendsBeforeIt()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        Cell<int> held = s.Hold(0);
        List<int> @out = [];
        IListener l = s.ListenStrong(@out.Add);

        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    s.Send(1);
                    throw new InvalidOperationException("body");
                }))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage("body");

        await Assert.That(@out).IsEmpty();
        await Assert.That(held.Sample()).IsEqualTo(0);

        s.Send(2);
        l.Unlisten();

        await Assert.That(@out).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);
        await Assert.That(held.Sample()).IsEqualTo(2);
    }

    // A nested Run that throws reaches the body of the root transaction, and the root fails.
    [Test]
    public async Task AThrowFromANestedRunDiscardsTheSendsOfTheRoot()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];
        IListener l = s.ListenStrong(@out.Add);

        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    s.Send(1);
                    Transaction.RunVoid(static () => throw new InvalidOperationException("nested"));
                }))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage("nested");

        l.Unlisten();

        await Assert.That(@out).IsEmpty();
    }

    // A post with a release, queued before the throw, gets its release with the cause, and the
    // posted action does not run.
    [Test]
    public async Task AThrowFromTheBodyReleasesThePostsBeforeIt()
    {
        bool ran = false;
        Exception? cause = null;
        InvalidOperationException thrown = new("body");

        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    Transaction.Post(action: () => ran = true, onFailure: e => cause = e);
                    throw thrown;
                }))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage("body");

        await Assert.That(ran).IsFalse();
        await Assert.That(cause).IsSameReferenceAs(thrown);
    }
}
