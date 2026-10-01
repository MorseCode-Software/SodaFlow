using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public sealed class StreamTests
{
    [Test]
    public async Task TestStreamSend()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];
        IListener l = s.ListenStrong(@out.Add);
        s.Send(5);
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [5], ordering: CollectionOrdering.Matching);
        s.Send(6);
        await Assert.That(@out).IsEquivalentTo(expected: [5], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestStreamSendInCallbackThrowsException()
    {
        InvalidOperationException? actual = null;

        StreamSink<int> s = Stream.CreateSink<int>();
        StreamSink<int> s2 = Stream.CreateSink<int>();

        using (s.ListenStrong(s2.Send))
        {
            try
            {
                s.Send(5);
            }
            catch (InvalidOperationException e)
            {
                actual = e;
            }
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("Send may not be called inside a callback.");
    }

    [Test]
    public async Task TestStreamSendInMapThrowsException()
    {
        InvalidOperationException? actual = null;

        StreamSink<int> s = Stream.CreateSink<int>();
        StreamSink<int> s2 = Stream.CreateSink<int>();

        using (s.Map(v =>
                   {
                       s2.Send(v);
                       return Unit.Value;
                   })
                   .ListenStrong(static _ =>
                   {
                   }))
        {
            try
            {
                s.Send(5);
            }
            catch (InvalidOperationException e)
            {
                actual = e;
            }
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("Send may not be called inside a callback.");
    }

    [Test]
    public async Task TestStreamSendInCellMapThrowsException()
    {
        InvalidOperationException? actual = null;

        CellSink<int> c = Cell.CreateSink(5);
        StreamSink<int> s2 = Stream.CreateSink<int>();

        try
        {
            using (c.Map(v =>
                       {
                           s2.Send(v);
                           return Unit.Value;
                       })
                       .ListenStrong(static _ =>
                       {
                       }))
            {
            }
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("Send may not be called inside a callback.");
    }

    [Test]
    public async Task TestStreamSendInCellLiftThrowsException()
    {
        InvalidOperationException? actual = null;

        Cell<int> c = Cell.Constant(5);
        Cell<int> c2 = Cell.Constant(7);
        StreamSink<int> s2 = Stream.CreateSink<int>();

        try
        {
            using (c.Lift(
                           c2: c2,
                           f: (_, _) =>
                           {
                               s2.Send(5);
                               return Unit.Value;
                           })
                       .ListenStrong(static _ =>
                       {
                       }))
            {
            }
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("Send may not be called inside a callback.");
    }

    [Test]
    public async Task TestStreamSendInCellApplyThrowsException()
    {
        InvalidOperationException? actual = null;

        Cell<int> c = Cell.Constant(5);
        StreamSink<int> s2 = Stream.CreateSink<int>();

        Cell<Func<int, Unit>> c2 =
            Cell.Constant<Func<int, Unit>>(_ =>
            {
                s2.Send(5);
                return Unit.Value;
            });

        try
        {
            using (c.Apply(c2)
                       .ListenStrong(static _ =>
                       {
                       }))
            {
            }
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("Send may not be called inside a callback.");
    }

    [Test]
    public async Task TestMap()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        Stream<string> m = s.Map(static x => (x + 2).ToString());
        List<string> @out = [];
        IListener l = m.ListenStrong(@out.Add);
        s.Send(5);
        s.Send(3);
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: ["7", "5"], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestOrElseNonSimultaneous()
    {
        StreamSink<int> s1 = Stream.CreateSink<int>();
        StreamSink<int> s2 = Stream.CreateSink<int>();
        List<int> @out = [];
        IListener l = s1.OrElse(s2).ListenStrong(@out.Add);
        s1.Send(7);
        s2.Send(9);
        s1.Send(8);
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [7, 9, 8], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestOrElseSimultaneous1()
    {
        StreamSink<int> s1 = Stream.CreateSink<int>(static (_, r) => r);
        StreamSink<int> s2 = Stream.CreateSink<int>(static (_, r) => r);
        List<int> @out = [];
        IListener l = s2.OrElse(s1).ListenStrong(@out.Add);

        Transaction.RunVoid(() =>
        {
            s1.Send(7);
            s2.Send(60);
        });

        Transaction.RunVoid(() =>
        {
            s1.Send(9);
        });

        Transaction.RunVoid(() =>
        {
            s1.Send(7);
            s1.Send(60);
            s2.Send(8);
            s2.Send(90);
        });

        Transaction.RunVoid(() =>
        {
            s2.Send(8);
            s2.Send(90);
            s1.Send(7);
            s1.Send(60);
        });

        Transaction.RunVoid(() =>
        {
            s2.Send(8);
            s1.Send(7);
            s2.Send(90);
            s1.Send(60);
        });

        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [60, 9, 90, 90, 90], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestOrElseSimultaneous2()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        Stream<int> s2 = s.Map(static x => 2 * x);
        List<int> @out = [];
        IListener l = s.OrElse(s2).ListenStrong(@out.Add);
        s.Send(7);
        s.Send(9);
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [7, 9], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestOrElseLeftBias()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        Stream<int> s2 = s.Map(static x => 2 * x);
        List<int> @out = [];
        IListener l = s2.OrElse(s).ListenStrong(@out.Add);
        s.Send(7);
        s.Send(9);
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [14, 18], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestMergeNonSimultaneous()
    {
        StreamSink<int> s1 = Stream.CreateSink<int>();
        StreamSink<int> s2 = Stream.CreateSink<int>();
        List<int> @out = [];
        IListener l = s1.Merge(s2: s2, f: static (x, y) => x + y).ListenStrong(@out.Add);
        s1.Send(7);
        s2.Send(9);
        s1.Send(8);
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [7, 9, 8], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestMergeSimultaneous()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        Stream<int> s2 = s.Map(static x => 2 * x);
        List<int> @out = [];
        IListener l = s.Merge(s2: s2, f: static (x, y) => x + y).ListenStrong(@out.Add);
        s.Send(7);
        s.Send(9);
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [21, 27], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestCoalesce()
    {
        StreamSink<int> s = Stream.CreateSink<int>(static (x, y) => x + y);
        List<int> @out = [];
        IListener l = s.ListenStrong(@out.Add);

        Transaction.RunVoid(() =>
        {
            s.Send(2);
        });

        Transaction.RunVoid(() =>
        {
            s.Send(8);
            s.Send(40);
        });

        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [2, 48], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestCoalesce2()
    {
        StreamSink<int> s = Stream.CreateSink<int>(static (x, y) => x + y);
        List<int> @out = [];
        IListener l = s.ListenStrong(@out.Add);

        Transaction.RunVoid(() =>
        {
            s.Send(1);
            s.Send(2);
            s.Send(3);
            s.Send(4);
            s.Send(5);
        });

        Transaction.RunVoid(() =>
        {
            s.Send(6);
            s.Send(7);
            s.Send(8);
            s.Send(9);
            s.Send(10);
        });

        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [15, 40], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestFilter()
    {
        StreamSink<char> s = Stream.CreateSink<char>();
        List<char> @out = [];
        IListener l = s.Filter(char.IsUpper).ListenStrong(@out.Add);
        s.Send('H');
        s.Send('o');
        s.Send('I');
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: ['H', 'I'], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestFilterSome()
    {
        StreamSink<Maybe<string>> s = Stream.CreateSink<Maybe<string>>();
        List<string> @out = [];
        IListener l = s.FilterSome().ListenStrong(@out.Add);
        s.Send(Maybe.Some("tomato"));
        s.Send(Maybe.None);
        s.Send(Maybe.Some("peach"));
        s.Send(Maybe.None);
        s.Send(Maybe.Some("pear"));
        l.Unlisten();

        await Assert.That(@out)
            .IsEquivalentTo(expected: ["tomato", "peach", "pear"], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestChoose()
    {
        StreamSink<string> s = Stream.CreateSink<string>();
        List<int> @out = [];

        IListener l =
            s.Choose(static v => int.TryParse(s: v, result: out int n) ? Maybe.Some(n) : Maybe.None)
                .ListenStrong(@out.Add);

        s.Send("1");
        s.Send("tomato");
        s.Send("2");
        s.Send(string.Empty);
        s.Send("3");
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestChooseMatchesMapThenFilterSome()
    {
        StreamSink<string> s = Stream.CreateSink<string>();
        List<int> chosen = [];
        List<int> mapped = [];

        Func<string, Maybe<int>> f = static v => int.TryParse(s: v, result: out int n) ? Maybe.Some(n) : Maybe.None;

        IListener l1 = s.Choose(f).ListenStrong(chosen.Add);
        IListener l2 = s.Map(f).FilterSome().ListenStrong(mapped.Add);

        s.Send("1");
        s.Send("tomato");
        s.Send("2");

        l1.Unlisten();
        l2.Unlisten();

        await Assert.That(chosen).IsEquivalentTo(expected: mapped, ordering: CollectionOrdering.Matching);
        await Assert.That(chosen).IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestChooseNoneFiresNothing()
    {
        StreamSink<string> s = Stream.CreateSink<string>();
        List<int> @out = [];
        IListener l = s.Choose(static _ => Maybe<int>.None).ListenStrong(@out.Add);
        s.Send("1");
        s.Send("2");
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: Array.Empty<int>(), ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestLoopStream()
    {
        StreamSink<int> sa = Stream.CreateSink<int>();

        (StreamLoop<int> sb, Stream<int> sb2, Stream<int> sc) =
            Transaction.Run(() =>
            {
                StreamLoop<int> sbLocal = Stream.CreateLoop<int>();
                Stream<int> scLocal = sa.Map(static x => x % 10).Merge(s2: sbLocal, f: static (x, y) => x * y);
                Stream<int> sbOut = sa.Map(static x => x / 10).Filter(static x => x != 0);
                sbLocal.Loop(sbOut);
                return (sbLocal, sbOut, scLocal);
            });

        List<int> @out = [];
        List<int> out2 = [];
        List<int> out3 = [];
        IListener l = sb.ListenStrong(@out.Add);
        IListener l2 = sb2.ListenStrong(out2.Add);
        IListener l3 = sc.ListenStrong(out3.Add);
        sa.Send(2);
        sa.Send(52);
        l3.Unlisten();
        l2.Unlisten();
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [5], ordering: CollectionOrdering.Matching);
        await Assert.That(out2).IsEquivalentTo(expected: [5], ordering: CollectionOrdering.Matching);
        await Assert.That(out3).IsEquivalentTo(expected: [2, 10], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestLoopCell()
    {
        CellSink<int> ca = Cell.CreateSink(22);

        (CellLoop<int> cb, Cell<int> cb2, Cell<int> cc) =
            Transaction.Run(() =>
            {
                CellLoop<int> cbLocal = Cell.CreateLoop<int>();
                Cell<int> ccLocal = ca.Map(static x => x % 10).Lift(c2: cbLocal, f: static (x, y) => x * y);
                Cell<int> cbOut = ca.Map(static x => x / 10);
                cbLocal.Loop(cbOut);
                return (cbLocal, cbOut, ccLocal);
            });

        List<int> @out = [];
        List<int> out2 = [];
        List<int> out3 = [];
        IListener l = cb.ListenStrong(@out.Add);
        IListener l2 = cb2.ListenStrong(out2.Add);
        IListener l3 = cc.ListenStrong(out3.Add);
        ca.Send(2);
        ca.Send(52);
        l3.Unlisten();
        l2.Unlisten();
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [2, 0, 5], ordering: CollectionOrdering.Matching);
        await Assert.That(out2).IsEquivalentTo(expected: [2, 0, 5], ordering: CollectionOrdering.Matching);
        await Assert.That(out3).IsEquivalentTo(expected: [4, 0, 10], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestGate()
    {
        StreamSink<char?> sc = Stream.CreateSink<char?>();
        BehaviorSink<bool> cGate = Behavior.CreateSink(true);
        List<char?> @out = [];
        IListener l = sc.Gate(cGate).ListenStrong(@out.Add);
        sc.Send('H');
        cGate.Send(false);
        sc.Send('O');
        cGate.Send(true);
        sc.Send('I');
        l.Unlisten();

        // char?[] rather than char[]: this collection holds char?, and TUnit compares element
        // types where NUnit coerced them.
        await Assert.That(@out)
            .IsEquivalentTo(expected: new char?[] { 'H', 'I' }, ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestCalm()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];
        IListener l = s.Calm().ListenStrong(@out.Add);
        s.Send(2);
        s.Send(2);
        s.Send(2);
        s.Send(4);
        s.Send(2);
        s.Send(4);
        s.Send(4);
        s.Send(2);
        s.Send(2);
        s.Send(2);
        s.Send(2);
        s.Send(2);
        s.Send(4);
        s.Send(2);
        s.Send(4);
        s.Send(4);
        s.Send(2);
        s.Send(2);
        s.Send(2);
        s.Send(2);
        s.Send(2);
        s.Send(4);
        s.Send(2);
        s.Send(4);
        s.Send(4);
        s.Send(2);
        s.Send(2);
        s.Send(2);
        s.Send(2);
        s.Send(2);
        s.Send(4);
        s.Send(2);
        s.Send(4);
        s.Send(4);
        s.Send(2);
        s.Send(2);
        s.Send(2);
        s.Send(2);
        s.Send(2);
        s.Send(4);
        s.Send(2);
        s.Send(4);
        s.Send(4);
        s.Send(2);
        s.Send(2);
        l.Unlisten();

        await Assert.That(@out)
            .IsEquivalentTo(
                expected: [2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2],
                ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestCalm2()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];
        IListener l = s.Calm().ListenStrong(@out.Add);
        s.Send(2);
        s.Send(4);
        s.Send(2);
        s.Send(4);
        s.Send(4);
        s.Send(2);
        s.Send(2);
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [2, 4, 2, 4, 2], ordering: CollectionOrdering.Matching);
    }

    // Calm remembers the last value it let through, and that memory has to survive the end of a
    // transaction. The existing Calm tests only send out of one. Thus, they never use a firing
    // that comes from some sources in one transaction. They also never test if the
    // remembered value that committed at the end of one transaction is what the next one compares
    // against. The test checks the two here. The cell suppresses the second transaction only when
    // the first committed correctly, and the fourth only when the third did.
    [Test]
    public async Task TestCalmRemembersAcrossTransactions()
    {
        StreamSink<int> a = Stream.CreateSink<int>();
        StreamSink<int> b = Stream.CreateSink<int>();
        Stream<int> merged = a.Merge(s2: b, f: static (x, y) => x + y);

        List<int> @out = [];
        IListener l = merged.Calm().ListenStrong(@out.Add);

        Transaction.RunVoid(() =>
        {
            a.Send(1);
            b.Send(1);
        });

        // 2 again, from one source this time. The cell must suppress it.
        a.Send(2);

        Transaction.RunVoid(() =>
        {
            a.Send(1);
            b.Send(2);
        });

        // 3 again - suppressed.
        a.Send(3);

        b.Send(4);

        l.Unlisten();

        await Assert.That(@out).IsEquivalentTo(expected: [2, 3, 4], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestCollect()
    {
        StreamSink<int> sa = Stream.CreateSink<int>();
        List<int> @out = [];

        Stream<int> sum =
            sa.Collect(
                initialState: (Value: 100, Test: true),
                f: static (a, s) =>
                {
                    int outputValue = s.Value + (s.Test ? a * 3 : a);
                    return (ReturnValue: outputValue, State: (Value: outputValue, Test: outputValue % 2 == 0));
                });

        IListener l = sum.ListenStrong(@out.Add);
        sa.Send(5);
        sa.Send(7);
        sa.Send(1);
        sa.Send(2);
        sa.Send(3);
        l.Unlisten();

        await Assert.That(@out)
            .IsEquivalentTo(expected: [115, 122, 125, 127, 130], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestAccum()
    {
        StreamSink<int> sa = Stream.CreateSink<int>();
        List<int> @out = [];
        Cell<int> sum = sa.Accum(initialState: 100, f: static (a, s) => a + s);
        IListener l = sum.ListenStrong(@out.Add);
        sa.Send(5);
        sa.Send(7);
        sa.Send(1);
        sa.Send(2);
        sa.Send(3);
        l.Unlisten();

        await Assert.That(@out)
            .IsEquivalentTo(expected: [100, 105, 112, 113, 115, 118], ordering: CollectionOrdering.Matching);
    }

    // Collect carries state between firings, and that state has to survive the end of a
    // transaction. TestCollect only sends out of one. Thus, it never covers a firing that comes
    // from some sources in one transaction. It also never tests if the state that
    // committed at the end of one transaction is what the next one folds over. The count in the
    // state shows the two: it can only get to 3 when each transaction committed.
    [Test]
    public async Task TestCollectStateSurvivesTransactions()
    {
        StreamSink<int> a = Stream.CreateSink<int>();
        StreamSink<int> b = Stream.CreateSink<int>();
        Stream<int> merged = a.Merge(s2: b, f: static (x, y) => x + y);

        List<string> @out = [];

        IListener l =
            merged
                .Collect(
                    initialState: (Total: 0, Count: 0),
                    f: static (v, s) =>
                        (ReturnValue: s.Total + v + "/" + (s.Count + 1),
                            State: (Total: s.Total + v, Count: s.Count + 1)))
                .ListenStrong(@out.Add);

        Transaction.RunVoid(() =>
        {
            a.Send(1);
            b.Send(2);
        });

        a.Send(10);

        Transaction.RunVoid(() =>
        {
            a.Send(1);
            b.Send(1);
        });

        l.Unlisten();

        await Assert.That(@out)
            .IsEquivalentTo(expected: ["3/1", "13/2", "15/3"], ordering: CollectionOrdering.Matching);
    }

    // Accum shares Collect's state carrying, so the same boundary applies to it.
    [Test]
    public async Task TestAccumStateSurvivesTransactions()
    {
        StreamSink<int> a = Stream.CreateSink<int>();
        StreamSink<int> b = Stream.CreateSink<int>();
        Stream<int> merged = a.Merge(s2: b, f: static (x, y) => x + y);

        List<int> @out = [];
        IListener l = merged.Accum(initialState: 0, f: static (v, s) => s + v).ListenStrong(@out.Add);

        Transaction.RunVoid(() =>
        {
            a.Send(1);
            b.Send(2);
        });

        a.Send(10);

        Transaction.RunVoid(() =>
        {
            a.Send(1);
            b.Send(1);
        });

        l.Unlisten();

        await Assert.That(@out).IsEquivalentTo(expected: [0, 3, 13, 15], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestOnce()
    {
        StreamSink<char> s = Stream.CreateSink<char>();
        List<char> @out = [];
        IListener l = s.Once().ListenStrong(@out.Add);
        s.Send('A');
        s.Send('B');
        s.Send('C');
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: ['A'], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestHold()
    {
        StreamSink<char> s = Stream.CreateSink<char>();
        Cell<char> c = s.Hold(' ');
        List<char> @out = [];
        IListener l = c.ListenStrong(@out.Add);
        s.Send('C');
        s.Send('B');
        s.Send('A');
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [' ', 'C', 'B', 'A'], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestHoldImplicitDelay()
    {
        StreamSink<char> s = Stream.CreateSink<char>();
        Cell<char> c = s.Hold(' ');
        List<char> @out = [];
        IListener l = s.Snapshot(c).ListenStrong(@out.Add);
        s.Send('C');
        s.Send('B');
        s.Send('A');
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [' ', 'C', 'B'], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestDefer()
    {
        StreamSink<char> s = Stream.CreateSink<char>();
        Cell<char> c = s.Hold(' ');
        List<char> @out = [];
        IListener l = Operational.Defer(s).Snapshot(c).ListenStrong(@out.Add);
        s.Send('C');
        s.Send('B');
        s.Send('A');
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: ['C', 'B', 'A'], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestSnapshotLatestNoImplicitDelay()
    {
        StreamSink<char> s = Stream.CreateSink<char>();
        Cell<char> c = s.Hold(' ');
        List<char> @out = [];
        IListener l = s.SnapshotLatest(c).ListenStrong(@out.Add);
        s.Send('C');
        s.Send('B');
        s.Send('A');
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: ['C', 'B', 'A'], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestSnapshotLatestSimultaneousUpdate()
    {
        CellSink<int> c1 = Cell.CreateSink(1);
        CellSink<int> c2 = Cell.CreateSink(10);
        List<string> snapshot = [];
        List<string> latest = [];

        IListener l1 =
            c1.Updates().Snapshot(c: c2, f: static (a, b) => $"{a},{b}").ListenStrong(snapshot.Add);

        IListener l2 =
            c1.Updates().SnapshotLatest(c: c2, f: static (a, b) => $"{a},{b}").ListenStrong(latest.Add);

        // The cell updates after the stream fires.
        Transaction.RunVoid(() =>
        {
            c1.Send(2);
            c2.Send(20);
        });

        // The cell updates before the stream fires.
        Transaction.RunVoid(() =>
        {
            c2.Send(30);
            c1.Send(3);
        });

        // Only the cell updates. This gives no firing.
        c2.Send(40);

        // Only the stream fires. This gives the current value of the cell.
        c1.Send(4);

        l1.Unlisten();
        l2.Unlisten();

        await Assert.That(snapshot)
            .IsEquivalentTo(expected: ["2,10", "3,20", "4,40"], ordering: CollectionOrdering.Matching);

        await Assert.That(latest)
            .IsEquivalentTo(expected: ["2,20", "3,30", "4,40"], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestSnapshotLatestMatchesLiftAndMerge()
    {
        CellSink<int> c1 = Cell.CreateSink(0);
        CellSink<int> c2 = Cell.CreateSink(0);
        List<string> expected = [];
        List<string> actual = [];

        IListener l1 = Reference(c1: c1, c2: c2, f: static (a, b) => $"{a},{b}").ListenStrong(expected.Add);

        IListener l2 =
            c1.Updates().SnapshotLatest(c: c2, f: static (a, b) => $"{a},{b}").ListenStrong(actual.Add);

        Random random = new(1234);

        for (int i = 1; i <= 500; i++)
        {
            int value = i;

            // 0 updates c1, 1 updates c2, 2 updates c1 then c2, and 3 updates c2 then c1.
            int choice = random.Next(4);

            Transaction.RunVoid(() =>
            {
                if (choice is 0 or 2)
                {
                    c1.Send(value);
                }

                if (choice is 1 or 2 or 3)
                {
                    c2.Send(-value);
                }

                if (choice is 3)
                {
                    c1.Send(value);
                }
            });
        }

        l1.Unlisten();
        l2.Unlisten();

        await Assert.That(actual.Count).IsGreaterThan(0);
        await Assert.That(actual).IsEquivalentTo(expected: expected, ordering: CollectionOrdering.Matching);

        return;

        // Before SnapshotLatest, this composition gave this result. The lifted cell fires
        // in each transaction that updates an input, with the new values. The merge keeps only the
        // transactions in which c1 also fires.
        static Stream<string> Reference(Cell<int> c1, Cell<int> c2, Func<int, int, string> f) =>
            c1.Updates()
                .Map(static _ => (fromC1: true, value: string.Empty))
                .Merge(
                    s2: c1.Lift(c2: c2, f: f).Updates().Map(static v => (fromC1: false, value: v)),
                    f: static (_, r) => r with { fromC1 = true })
                .Filter(static t => t.fromC1)
                .Map(static t => t.value);
    }

    [Test]
    public async Task TestSnapshotLatestManyMatchesLift()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        CellSink<int> c1 = Cell.CreateSink(0);
        CellSink<int> c2 = Cell.CreateSink(0);
        CellSink<int> c3 = Cell.CreateSink(0);
        CellSink<int> c4 = Cell.CreateSink(0);
        CellSink<int> c5 = Cell.CreateSink(0);
        List<IListener> listeners = [];

        // This test compares each overload with the one-cell overload on a lifted cell of the same
        // inputs. TestSnapshotLatestMatchesLiftAndMerge compares the one-cell overload with the Lift and
        // Merge composition. The behavior overloads get the same cells as behaviors.
        List<string> lift2 =
            ListenTo(
                s.SnapshotLatest(
                    c: c1.Lift(c2: c2, f: static (v1, v2) => $"{v1},{v2}"),
                    f: static (a, v) => $"{a}:{v}"));

        List<string> actual2 = ListenTo(s.SnapshotLatest(c1: c1, c2: c2, f: static (a, v1, v2) => $"{a}:{v1},{v2}"));

        List<string> actual2B =
            ListenTo(
                s.SnapshotLatest(
                    b1: c1.AsBehavior(),
                    b2: c2.AsBehavior(),
                    f: static (a, v1, v2) => $"{a}:{v1},{v2}"));

        List<string> lift3 =
            ListenTo(
                s.SnapshotLatest(
                    c: c1.Lift(c2: c2, c3: c3, f: static (v1, v2, v3) => $"{v1},{v2},{v3}"),
                    f: static (a, v) => $"{a}:{v}"));

        List<string> actual3 =
            ListenTo(s.SnapshotLatest(c1: c1, c2: c2, c3: c3, f: static (a, v1, v2, v3) => $"{a}:{v1},{v2},{v3}"));

        List<string> actual3B =
            ListenTo(
                s.SnapshotLatest(
                    b1: c1.AsBehavior(),
                    b2: c2.AsBehavior(),
                    b3: c3.AsBehavior(),
                    f: static (a, v1, v2, v3) => $"{a}:{v1},{v2},{v3}"));

        List<string> lift4 =
            ListenTo(
                s.SnapshotLatest(
                    c: c1.Lift(c2: c2, c3: c3, c4: c4, f: static (v1, v2, v3, v4) => $"{v1},{v2},{v3},{v4}"),
                    f: static (a, v) => $"{a}:{v}"));

        List<string> actual4 =
            ListenTo(
                s.SnapshotLatest(
                    c1: c1,
                    c2: c2,
                    c3: c3,
                    c4: c4,
                    f: static (a, v1, v2, v3, v4) => $"{a}:{v1},{v2},{v3},{v4}"));

        List<string> actual4B =
            ListenTo(
                s.SnapshotLatest(
                    b1: c1.AsBehavior(),
                    b2: c2.AsBehavior(),
                    b3: c3.AsBehavior(),
                    b4: c4.AsBehavior(),
                    f: static (a, v1, v2, v3, v4) => $"{a}:{v1},{v2},{v3},{v4}"));

        List<string> lift5 =
            ListenTo(
                s.SnapshotLatest(
                    c: c1.Lift(
                        c2: c2,
                        c3: c3,
                        c4: c4,
                        c5: c5,
                        f: static (v1, v2, v3, v4, v5) => $"{v1},{v2},{v3},{v4},{v5}"),
                    f: static (a, v) => $"{a}:{v}"));

        List<string> actual5 =
            ListenTo(
                s.SnapshotLatest(
                    c1: c1,
                    c2: c2,
                    c3: c3,
                    c4: c4,
                    c5: c5,
                    f: static (a, v1, v2, v3, v4, v5) => $"{a}:{v1},{v2},{v3},{v4},{v5}"));

        List<string> actual5B =
            ListenTo(
                s.SnapshotLatest(
                    b1: c1.AsBehavior(),
                    b2: c2.AsBehavior(),
                    b3: c3.AsBehavior(),
                    b4: c4.AsBehavior(),
                    b5: c5.AsBehavior(),
                    f: static (a, v1, v2, v3, v4, v5) => $"{a}:{v1},{v2},{v3},{v4},{v5}"));

        IReadOnlyList<Action<int>> sends = [s.Send, c1.Send, c2.Send, c3.Send, c4.Send, c5.Send];
        Random random = new(5678);

        for (int i = 1; i <= 500; i++)
        {
            // Each input gets a value in this transaction if random.Next(2) gives 0, in a random
            // sequence. Each value is different. Thus, a value in an incorrect position gives a
            // different string.
            int[] order = [.. Enumerable.Range(start: 0, count: sends.Count).OrderBy(_ => random.Next())];
            bool[] send = [.. order.Select(_ => random.Next(2) == 0)];
            int transaction = i;

            Transaction.RunVoid(() =>
            {
                for (int k = 0; k < order.Length; k++)
                {
                    if (send[k])
                    {
                        sends[order[k]](transaction * 10 + order[k]);
                    }
                }
            });
        }

        foreach (IListener listener in listeners)
        {
            listener.Unlisten();
        }

        await Assert.That(lift5.Count).IsGreaterThan(0);
        await Assert.That(actual2).IsEquivalentTo(expected: lift2, ordering: CollectionOrdering.Matching);
        await Assert.That(actual2B).IsEquivalentTo(expected: lift2, ordering: CollectionOrdering.Matching);
        await Assert.That(actual3).IsEquivalentTo(expected: lift3, ordering: CollectionOrdering.Matching);
        await Assert.That(actual3B).IsEquivalentTo(expected: lift3, ordering: CollectionOrdering.Matching);
        await Assert.That(actual4).IsEquivalentTo(expected: lift4, ordering: CollectionOrdering.Matching);
        await Assert.That(actual4B).IsEquivalentTo(expected: lift4, ordering: CollectionOrdering.Matching);
        await Assert.That(actual5).IsEquivalentTo(expected: lift5, ordering: CollectionOrdering.Matching);
        await Assert.That(actual5B).IsEquivalentTo(expected: lift5, ordering: CollectionOrdering.Matching);

        return;

        List<string> ListenTo(Stream<string> stream)
        {
            List<string> @out = [];
            listeners.Add(stream.ListenStrong(@out.Add));
            return @out;
        }
    }

    [Test]
    public async Task TestMergeTwoTypes()
    {
        StreamSink<int> s1 = Stream.CreateSink<int>();
        StreamSink<string> s2 = Stream.CreateSink<string>();
        List<(Maybe<int>, Maybe<string>)> @out = [];
        IListener l = s1.Merge(s2).ListenStrong(@out.Add);

        s1.Send(1);
        s2.Send("a");

        Transaction.RunVoid(() =>
        {
            s2.Send("b");
            s1.Send(2);
        });

        // A transaction in which no input fires gives no firing.
        Transaction.RunVoid(static () =>
        {
        });

        l.Unlisten();

        List<(Maybe<int>, Maybe<string>)> expected =
        [
            (Maybe.Some(1), Maybe<string>.None),
            (Maybe<int>.None, Maybe.Some("a")),
            (Maybe.Some(2), Maybe.Some("b"))
        ];

        await Assert.That(@out).IsEquivalentTo(expected: expected, ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestMergeManyTypesMatchesMapAndMerge()
    {
        StreamSink<int> s1 = Stream.CreateSink<int>();
        StreamSink<string> s2 = Stream.CreateSink<string>();
        StreamSink<char> s3 = Stream.CreateSink<char>();
        StreamSink<long> s4 = Stream.CreateSink<long>();
        List<IListener> listeners = [];

        // The reference maps each stream to a tuple that has a value only in its own element. The
        // same-type Merge puts simultaneous tuples together, and each element gets the value that
        // one of the two tuples has.
        List<(Maybe<int>, Maybe<string>)> expected2 =
            ListenTo(
                s1.Map(static a => (Maybe.Some(a), Maybe<string>.None))
                    .Merge(
                        s2: s2.Map(static b => (Maybe<int>.None, Maybe.Some(b))),
                        f: static (l, r) => (l.Item1.OrElse(r.Item1), l.Item2.OrElse(r.Item2))));

        List<(Maybe<int>, Maybe<string>)> merge2 = ListenTo(s1.Merge(s2));

        List<(Maybe<int>, Maybe<string>, Maybe<char>)> expected3 =
            ListenTo(
                s1.Map(static a => (Maybe.Some(a), Maybe<string>.None, Maybe<char>.None))
                    .Merge(
                        s2: s2.Map(static b => (Maybe<int>.None, Maybe.Some(b), Maybe<char>.None)),
                        f: Combine3)
                    .Merge(
                        s2: s3.Map(static c => (Maybe<int>.None, Maybe<string>.None, Maybe.Some(c))),
                        f: Combine3));

        List<(Maybe<int>, Maybe<string>, Maybe<char>)> merge3 = ListenTo(s1.Merge(s2: s2, s3: s3));

        List<(Maybe<int>, Maybe<string>, Maybe<char>, Maybe<long>)> expected4 =
            ListenTo(
                s1.Map(static a => (Maybe.Some(a), Maybe<string>.None, Maybe<char>.None, Maybe<long>.None))
                    .Merge(
                        s2: s2.Map(static b => (Maybe<int>.None, Maybe.Some(b), Maybe<char>.None, Maybe<long>.None)),
                        f: Combine4)
                    .Merge(
                        s2: s3.Map(static c => (Maybe<int>.None, Maybe<string>.None, Maybe.Some(c), Maybe<long>.None)),
                        f: Combine4)
                    .Merge(
                        s2: s4.Map(static d => (Maybe<int>.None, Maybe<string>.None, Maybe<char>.None, Maybe.Some(d))),
                        f: Combine4));

        List<(Maybe<int>, Maybe<string>, Maybe<char>, Maybe<long>)> merge4 =
            ListenTo(s1.Merge(s2: s2, s3: s3, s4: s4));

        IReadOnlyList<Action<int>> sends =
        [
            s1.Send, v => s2.Send($"s{v}"), v => s3.Send((char)('a' + v % 26)), v => s4.Send(v * 1000L)
        ];

        Random random = new(4321);

        for (int i = 1; i <= 500; i++)
        {
            // Each input fires in this transaction if random.Next(2) gives 0, in a random sequence.
            // The values change with each transaction. Thus, a value that stays from an earlier
            // transaction gives a different tuple.
            int[] order = [.. Enumerable.Range(start: 0, count: sends.Count).OrderBy(_ => random.Next())];
            bool[] send = [.. order.Select(_ => random.Next(2) == 0)];
            int transaction = i;

            Transaction.RunVoid(() =>
            {
                for (int k = 0; k < order.Length; k++)
                {
                    if (send[k])
                    {
                        sends[order[k]](transaction * 10 + order[k]);
                    }
                }
            });
        }

        foreach (IListener listener in listeners)
        {
            listener.Unlisten();
        }

        await Assert.That(expected4.Count).IsGreaterThan(0);
        await Assert.That(merge2).IsEquivalentTo(expected: expected2, ordering: CollectionOrdering.Matching);
        await Assert.That(merge3).IsEquivalentTo(expected: expected3, ordering: CollectionOrdering.Matching);
        await Assert.That(merge4).IsEquivalentTo(expected: expected4, ordering: CollectionOrdering.Matching);

        return;

        static (Maybe<int>, Maybe<string>, Maybe<char>) Combine3(
            (Maybe<int>, Maybe<string>, Maybe<char>) l,
            (Maybe<int>, Maybe<string>, Maybe<char>) r) =>
            (l.Item1.OrElse(r.Item1), l.Item2.OrElse(r.Item2), l.Item3.OrElse(r.Item3));

        static (Maybe<int>, Maybe<string>, Maybe<char>, Maybe<long>) Combine4(
            (Maybe<int>, Maybe<string>, Maybe<char>, Maybe<long>) l,
            (Maybe<int>, Maybe<string>, Maybe<char>, Maybe<long>) r) =>
        (
            l.Item1.OrElse(r.Item1),
            l.Item2.OrElse(r.Item2),
            l.Item3.OrElse(r.Item3),
            l.Item4.OrElse(r.Item4));

        List<TOut> ListenTo<TOut>(Stream<TOut> stream)
        {
            List<TOut> @out = [];
            listeners.Add(stream.ListenStrong(@out.Add));
            return @out;
        }
    }

    [Test]
    public async Task TestWideOverloadsMatchALiftOfAList()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        CellSink<int> c1 = Cell.CreateSink(1);
        CellSink<int> c2 = Cell.CreateSink(2);
        CellSink<int> c3 = Cell.CreateSink(3);
        CellSink<int> c4 = Cell.CreateSink(4);
        CellSink<int> c5 = Cell.CreateSink(5);
        CellSink<int> c6 = Cell.CreateSink(6);
        CellSink<int> c7 = Cell.CreateSink(7);
        CellSink<int> c8 = Cell.CreateSink(8);
        IReadOnlyList<Cell<int>> cells = [c1, c2, c3, c4, c5, c6, c7, c8];
        Behavior<int> b1 = c1.AsBehavior();
        Behavior<int> b2 = c2.AsBehavior();
        Behavior<int> b3 = c3.AsBehavior();
        Behavior<int> b4 = c4.AsBehavior();
        Behavior<int> b5 = c5.AsBehavior();
        Behavior<int> b6 = c6.AsBehavior();
        Behavior<int> b7 = c7.AsBehavior();
        Behavior<int> b8 = c8.AsBehavior();
        IReadOnlyList<Behavior<int>> behaviors = [b1, b2, b3, b4, b5, b6, b7, b8];
        List<IListener> listeners = [];
        List<(string name, List<string> actual, List<string> expected)> checks = [];

        // Each reference is the Lift of a list of the first cells or behaviors. That Lift has one form
        // for each count. Thus, the references do not use the overloads that this test examines. A
        // lifted behavior has no stream of its own, thus SnapshotLatest on s reads it in each
        // transaction. Snapshot on s reads the initial value of a lifted cell until an input updates,
        // and each input starts with a different value.
        Cell<string> lift7 =
            c1.Lift(
                c2: c2,
                c3: c3,
                c4: c4,
                c5: c5,
                c6: c6,
                c7: c7,
                f: static (v1, v2, v3, v4, v5, v6, v7) => Join(v1, v2, v3, v4, v5, v6, v7));

        Check(name: "Updates of a Lift of seven cells", actual: lift7.Updates(), expected: LiftCells(7).Updates());
        Check(name: "Snapshot of a Lift of seven cells", actual: s.Snapshot(lift7), expected: s.Snapshot(LiftCells(7)));

        Cell<string> lift8 =
            c1.Lift(
                c2: c2,
                c3: c3,
                c4: c4,
                c5: c5,
                c6: c6,
                c7: c7,
                c8: c8,
                f: static (v1, v2, v3, v4, v5, v6, v7, v8) => Join(v1, v2, v3, v4, v5, v6, v7, v8));

        Check(name: "Updates of a Lift of eight cells", actual: lift8.Updates(), expected: LiftCells(8).Updates());
        Check(name: "Snapshot of a Lift of eight cells", actual: s.Snapshot(lift8), expected: s.Snapshot(LiftCells(8)));

        Check(
            name: "Lift of seven behaviors",
            actual: s.SnapshotLatest(
                b1.Lift(
                    b2: b2,
                    b3: b3,
                    b4: b4,
                    b5: b5,
                    b6: b6,
                    b7: b7,
                    f: static (v1, v2, v3, v4, v5, v6, v7) => Join(v1, v2, v3, v4, v5, v6, v7))),
            expected: s.SnapshotLatest(LiftBehaviors(7)));

        Check(
            name: "Lift of eight behaviors",
            actual: s.SnapshotLatest(
                b1.Lift(
                    b2: b2,
                    b3: b3,
                    b4: b4,
                    b5: b5,
                    b6: b6,
                    b7: b7,
                    b8: b8,
                    f: static (v1, v2, v3, v4, v5, v6, v7, v8) => Join(v1, v2, v3, v4, v5, v6, v7, v8))),
            expected: s.SnapshotLatest(LiftBehaviors(8)));

        Check(
            name: "Snapshot of five cells",
            actual: s.Snapshot(
                c1: c1,
                c2: c2,
                c3: c3,
                c4: c4,
                c5: c5,
                f: static (a, v1, v2, v3, v4, v5) => $"{a}:{Join(v1, v2, v3, v4, v5)}"),
            expected: s.Snapshot(c: LiftCells(5), f: static (a, v) => $"{a}:{v}"));

        Check(
            name: "Snapshot of five behaviors",
            actual: s.Snapshot(
                b1: b1,
                b2: b2,
                b3: b3,
                b4: b4,
                b5: b5,
                f: static (a, v1, v2, v3, v4, v5) => $"{a}:{Join(v1, v2, v3, v4, v5)}"),
            expected: s.Snapshot(b: LiftBehaviors(5), f: static (a, v) => $"{a}:{v}"));

        Check(
            name: "Snapshot of six cells",
            actual: s.Snapshot(
                c1: c1,
                c2: c2,
                c3: c3,
                c4: c4,
                c5: c5,
                c6: c6,
                f: static (a, v1, v2, v3, v4, v5, v6) => $"{a}:{Join(v1, v2, v3, v4, v5, v6)}"),
            expected: s.Snapshot(c: LiftCells(6), f: static (a, v) => $"{a}:{v}"));

        Check(
            name: "Snapshot of six behaviors",
            actual: s.Snapshot(
                b1: b1,
                b2: b2,
                b3: b3,
                b4: b4,
                b5: b5,
                b6: b6,
                f: static (a, v1, v2, v3, v4, v5, v6) => $"{a}:{Join(v1, v2, v3, v4, v5, v6)}"),
            expected: s.Snapshot(b: LiftBehaviors(6), f: static (a, v) => $"{a}:{v}"));

        Check(
            name: "Snapshot of seven cells",
            actual: s.Snapshot(
                c1: c1,
                c2: c2,
                c3: c3,
                c4: c4,
                c5: c5,
                c6: c6,
                c7: c7,
                f: static (a, v1, v2, v3, v4, v5, v6, v7) => $"{a}:{Join(v1, v2, v3, v4, v5, v6, v7)}"),
            expected: s.Snapshot(c: LiftCells(7), f: static (a, v) => $"{a}:{v}"));

        Check(
            name: "Snapshot of seven behaviors",
            actual: s.Snapshot(
                b1: b1,
                b2: b2,
                b3: b3,
                b4: b4,
                b5: b5,
                b6: b6,
                b7: b7,
                f: static (a, v1, v2, v3, v4, v5, v6, v7) => $"{a}:{Join(v1, v2, v3, v4, v5, v6, v7)}"),
            expected: s.Snapshot(b: LiftBehaviors(7), f: static (a, v) => $"{a}:{v}"));

        Check(
            name: "Snapshot of eight cells",
            actual: s.Snapshot(
                c1: c1,
                c2: c2,
                c3: c3,
                c4: c4,
                c5: c5,
                c6: c6,
                c7: c7,
                c8: c8,
                f: static (a, v1, v2, v3, v4, v5, v6, v7, v8) => $"{a}:{Join(v1, v2, v3, v4, v5, v6, v7, v8)}"),
            expected: s.Snapshot(c: LiftCells(8), f: static (a, v) => $"{a}:{v}"));

        Check(
            name: "Snapshot of eight behaviors",
            actual: s.Snapshot(
                b1: b1,
                b2: b2,
                b3: b3,
                b4: b4,
                b5: b5,
                b6: b6,
                b7: b7,
                b8: b8,
                f: static (a, v1, v2, v3, v4, v5, v6, v7, v8) => $"{a}:{Join(v1, v2, v3, v4, v5, v6, v7, v8)}"),
            expected: s.Snapshot(b: LiftBehaviors(8), f: static (a, v) => $"{a}:{v}"));

        Check(
            name: "SnapshotLatest of six cells",
            actual: s.SnapshotLatest(
                c1: c1,
                c2: c2,
                c3: c3,
                c4: c4,
                c5: c5,
                c6: c6,
                f: static (a, v1, v2, v3, v4, v5, v6) => $"{a}:{Join(v1, v2, v3, v4, v5, v6)}"),
            expected: s.SnapshotLatest(c: LiftCells(6), f: static (a, v) => $"{a}:{v}"));

        Check(
            name: "SnapshotLatest of six behaviors",
            actual: s.SnapshotLatest(
                b1: b1,
                b2: b2,
                b3: b3,
                b4: b4,
                b5: b5,
                b6: b6,
                f: static (a, v1, v2, v3, v4, v5, v6) => $"{a}:{Join(v1, v2, v3, v4, v5, v6)}"),
            expected: s.SnapshotLatest(b: LiftBehaviors(6), f: static (a, v) => $"{a}:{v}"));

        Check(
            name: "SnapshotLatest of seven cells",
            actual: s.SnapshotLatest(
                c1: c1,
                c2: c2,
                c3: c3,
                c4: c4,
                c5: c5,
                c6: c6,
                c7: c7,
                f: static (a, v1, v2, v3, v4, v5, v6, v7) => $"{a}:{Join(v1, v2, v3, v4, v5, v6, v7)}"),
            expected: s.SnapshotLatest(c: LiftCells(7), f: static (a, v) => $"{a}:{v}"));

        Check(
            name: "SnapshotLatest of seven behaviors",
            actual: s.SnapshotLatest(
                b1: b1,
                b2: b2,
                b3: b3,
                b4: b4,
                b5: b5,
                b6: b6,
                b7: b7,
                f: static (a, v1, v2, v3, v4, v5, v6, v7) => $"{a}:{Join(v1, v2, v3, v4, v5, v6, v7)}"),
            expected: s.SnapshotLatest(b: LiftBehaviors(7), f: static (a, v) => $"{a}:{v}"));

        Check(
            name: "SnapshotLatest of eight cells",
            actual: s.SnapshotLatest(
                c1: c1,
                c2: c2,
                c3: c3,
                c4: c4,
                c5: c5,
                c6: c6,
                c7: c7,
                c8: c8,
                f: static (a, v1, v2, v3, v4, v5, v6, v7, v8) => $"{a}:{Join(v1, v2, v3, v4, v5, v6, v7, v8)}"),
            expected: s.SnapshotLatest(c: LiftCells(8), f: static (a, v) => $"{a}:{v}"));

        Check(
            name: "SnapshotLatest of eight behaviors",
            actual: s.SnapshotLatest(
                b1: b1,
                b2: b2,
                b3: b3,
                b4: b4,
                b5: b5,
                b6: b6,
                b7: b7,
                b8: b8,
                f: static (a, v1, v2, v3, v4, v5, v6, v7, v8) => $"{a}:{Join(v1, v2, v3, v4, v5, v6, v7, v8)}"),
            expected: s.SnapshotLatest(b: LiftBehaviors(8), f: static (a, v) => $"{a}:{v}"));

        IReadOnlyList<Action<int>> sends =
        [
            s.Send, c1.Send, c2.Send, c3.Send, c4.Send, c5.Send, c6.Send, c7.Send, c8.Send
        ];

        Random random = new(8765);

        for (int i = 1; i <= 500; i++)
        {
            // Each input gets a value in this transaction if random.Next(2) gives 0, in a random
            // sequence. Each value is different. Thus, a value in an incorrect position gives a
            // different string.
            int[] order = [.. Enumerable.Range(start: 0, count: sends.Count).OrderBy(_ => random.Next())];
            bool[] send = [.. order.Select(_ => random.Next(2) == 0)];
            int transaction = i;

            Transaction.RunVoid(() =>
            {
                for (int k = 0; k < order.Length; k++)
                {
                    if (send[k])
                    {
                        sends[order[k]](transaction * 10 + order[k]);
                    }
                }
            });
        }

        foreach (IListener listener in listeners)
        {
            listener.Unlisten();
        }

        foreach ((string name, List<string> actual, List<string> expected) in checks)
        {
            await Assert.That(expected.Count).IsGreaterThan(0).Because(name);

            await Assert.That(actual)
                .IsEquivalentTo(expected: expected, ordering: CollectionOrdering.Matching)
                .Because(name);
        }

        return;

        static string Join(params int[] values) => string.Join(separator: ",", values: values);

        Cell<string> LiftCells(int count) =>
            cells.Take(count).Lift(f: static values => string.Join(separator: ",", values: values));

        Behavior<string> LiftBehaviors(int count) =>
            behaviors.Take(count).Lift(f: static values => string.Join(separator: ",", values: values));

        void Check(string name, Stream<string> actual, Stream<string> expected) =>
            checks.Add((name, ListenTo(actual), ListenTo(expected)));

        List<string> ListenTo(Stream<string> stream)
        {
            List<string> @out = [];
            listeners.Add(stream.ListenStrong(@out.Add));
            return @out;
        }
    }

    [Test]
    public async Task TestMergeFiveToEightTypesMatchesAMergeOfSlots()
    {
        StreamSink<int> s1 = Stream.CreateSink<int>();
        StreamSink<string> s2 = Stream.CreateSink<string>();
        StreamSink<char> s3 = Stream.CreateSink<char>();
        StreamSink<long> s4 = Stream.CreateSink<long>();
        StreamSink<bool> s5 = Stream.CreateSink<bool>();
        StreamSink<short> s6 = Stream.CreateSink<short>();
        StreamSink<byte> s7 = Stream.CreateSink<byte>();
        StreamSink<uint> s8 = Stream.CreateSink<uint>();
        List<IListener> listeners = [];

        // The reference gives each stream a list of slots with a value only in its own slot. The Merge
        // for streams of one type puts the slots of simultaneous lists together. That Merge has one
        // form for each count, thus the reference does not use the overloads that this test examines.
        IReadOnlyList<Stream<string>> texts =
        [
            s1.Map(static v => $"{v}"),
            s2.Map(static v => $"{v}"),
            s3.Map(static v => $"{v}"),
            s4.Map(static v => $"{v}"),
            s5.Map(static v => $"{v}"),
            s6.Map(static v => $"{v}"),
            s7.Map(static v => $"{v}"),
            s8.Map(static v => $"{v}")
        ];

        List<(string name, List<string> actual, List<string> expected)> checks =
        [
            (
                "Merge of five streams",
                ListenTo(
                    s1.Merge(s2: s2, s3: s3, s4: s4, s5: s5)
                        .Map(static t =>
                            Show(
                                Slot(t.Item1),
                                Slot(t.Item2),
                                Slot(t.Item3),
                                Slot(t.Item4),
                                Slot(t.Item5)))),
                ListenTo(Reference(5))),
            (
                "Merge of six streams",
                ListenTo(
                    s1.Merge(s2: s2, s3: s3, s4: s4, s5: s5, s6: s6)
                        .Map(static t =>
                            Show(
                                Slot(t.Item1),
                                Slot(t.Item2),
                                Slot(t.Item3),
                                Slot(t.Item4),
                                Slot(t.Item5),
                                Slot(t.Item6)))),
                ListenTo(Reference(6))),
            (
                "Merge of seven streams",
                ListenTo(
                    s1.Merge(s2: s2, s3: s3, s4: s4, s5: s5, s6: s6, s7: s7)
                        .Map(static t =>
                            Show(
                                Slot(t.Item1),
                                Slot(t.Item2),
                                Slot(t.Item3),
                                Slot(t.Item4),
                                Slot(t.Item5),
                                Slot(t.Item6),
                                Slot(t.Item7)))),
                ListenTo(Reference(7))),
            (
                "Merge of eight streams",
                ListenTo(
                    s1.Merge(s2: s2, s3: s3, s4: s4, s5: s5, s6: s6, s7: s7, s8: s8)
                        .Map(static t =>
                            Show(
                                Slot(t.Item1),
                                Slot(t.Item2),
                                Slot(t.Item3),
                                Slot(t.Item4),
                                Slot(t.Item5),
                                Slot(t.Item6),
                                Slot(t.Item7),
                                Slot(t.Item8)))),
                ListenTo(Reference(8)))
        ];

        IReadOnlyList<Action<int>> sends =
        [
            s1.Send,
            v => s2.Send($"s{v}"),
            v => s3.Send((char)('a' + v % 26)),
            v => s4.Send(v * 1000L),
            v => s5.Send(v % 2 == 0),
            v => s6.Send((short)v),
            v => s7.Send((byte)(v % 256)),
            v => s8.Send((uint)v)
        ];

        Random random = new(9876);

        for (int i = 1; i <= 500; i++)
        {
            // Each input fires in this transaction if random.Next(2) gives 0, in a random sequence.
            // The values change with each transaction. Thus, a value that stays from an earlier
            // transaction gives a different string.
            int[] order = [.. Enumerable.Range(start: 0, count: sends.Count).OrderBy(_ => random.Next())];
            bool[] send = [.. order.Select(_ => random.Next(2) == 0)];
            int transaction = i;

            Transaction.RunVoid(() =>
            {
                for (int k = 0; k < order.Length; k++)
                {
                    if (send[k])
                    {
                        sends[order[k]](transaction * 10 + order[k]);
                    }
                }
            });
        }

        foreach (IListener listener in listeners)
        {
            listener.Unlisten();
        }

        foreach ((string name, List<string> actual, List<string> expected) in checks)
        {
            await Assert.That(expected.Count).IsGreaterThan(0).Because(name);

            await Assert.That(actual)
                .IsEquivalentTo(expected: expected, ordering: CollectionOrdering.Matching)
                .Because(name);
        }

        return;

        Stream<string> Reference(int count) =>
            texts.Take(count)
                .Select((text, index) => text.Map(v => Slots(index: index, count: count, value: v)))
                .Merge(f: static (l, r) => [.. l.Zip(second: r, resultSelector: static (x, y) => x == "-" ? y : x)])
                .Map(static slots => Show(slots));

        static string[] Slots(int index, int count, string value) =>
        [
            .. Enumerable.Range(start: 0, count: count).Select(i => i == index ? value : "-")
        ];

        static string Slot<T>(Maybe<T> m) => m.Match(onSome: static v => $"{v}", onNone: static () => "-");

        static string Show(params string[] slots) => string.Join(separator: "|", value: slots);

        List<string> ListenTo(Stream<string> stream)
        {
            List<string> @out = [];
            listeners.Add(stream.ListenStrong(@out.Add));
            return @out;
        }
    }

    [Test]
    public async Task TestSnapshotLatestValues()
    {
        CellSink<int> c1 = Cell.CreateSink(1);
        CellSink<int> c2 = Cell.CreateSink(10);
        List<int> @out = [];

        IListener l =
            Transaction.Run(() =>
                c1.Values().SnapshotLatest(c: c2, f: static (a, b) => a + b).ListenStrong(@out.Add));

        Transaction.RunVoid(() =>
        {
            c1.Send(2);
            c2.Send(20);
        });

        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [11, 22], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestSnapshotLatestBehavior()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        BehaviorSink<int> b = Behavior.CreateSink(0);
        List<int> @out = [];
        IListener l = s.SnapshotLatest(b: b, f: static (a, v) => a + v).ListenStrong(@out.Add);

        Transaction.RunVoid(() =>
        {
            s.Send(1);
            b.Send(100);
        });

        s.Send(2);
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [101, 102], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestSnapshotLatestLift()
    {
        CellSink<int> c1 = Cell.CreateSink(1);
        CellSink<int> c2 = Cell.CreateSink(10);
        CellSink<int> c3 = Cell.CreateSink(100);
        List<int> @out = [];

        IListener l =
            c1.Updates()
                .SnapshotLatest(c2.Lift(c2: c3, f: static (b, c) => b + c))
                .ListenStrong(@out.Add);

        Transaction.RunVoid(() =>
        {
            c1.Send(2);
            c2.Send(20);
            c3.Send(200);
        });

        Transaction.RunVoid(() =>
        {
            c3.Send(300);
            c1.Send(3);
        });

        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: [220, 320], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestListen()
    {
        StreamSink<int> s = Stream.CreateSink<int>();

        List<int> @out = [];

        ((Action)(() =>
        {
            // ReSharper disable once UnusedVariable
            IWeakListener l = s.Listen(@out.Add);

            s.Send(1);
            s.Send(2);
        }))();

        Collect();
        s.Send(3);
        s.Send(4);

        await Assert.That(@out.Count).IsEqualTo(2);
    }

    [Test]
    public async Task TestListenWithMap()
    {
        StreamSink<int> s = Stream.CreateSink<int>();

        List<int> @out = [];

        ((Action)(() =>
        {
            Stream<int> s2 = s.Map(static v => v + 1);

            ((Action)(() =>
            {
                // ReSharper disable once UnusedVariable
                IWeakListener l = s2.Listen(@out.Add);

                s.Send(1);
                s.Send(2);
            }))();

            Collect();

            ((Action)(() =>
            {
                // ReSharper disable once UnusedVariable
                IWeakListener l = s2.Listen(@out.Add);

                s.Send(3);
                s.Send(4);
                s.Send(5);
            }))();
        }))();

        Collect();
        s.Send(6);
        s.Send(7);

        await Assert.That(@out.Count).IsEqualTo(5);
    }

    [Test]
    public async Task TestUnlisten()
    {
        StreamSink<int> s = Stream.CreateSink<int>();

        List<int> @out = [];

        ((Action)(() =>
        {
            // ReSharper disable once UnusedVariable
            IStrongListener l = s.ListenStrong(@out.Add);

            s.Send(1);

            l.Unlisten();

            s.Send(2);
        }))();

        s.Send(3);
        s.Send(4);

        await Assert.That(@out.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TestUnlistenWeak()
    {
        StreamSink<int> s = Stream.CreateSink<int>();

        List<int> @out = [];

        ((Action)(() =>
        {
            // ReSharper disable once UnusedVariable
            IWeakListener l = s.Listen(@out.Add);

            s.Send(1);

            l.Unlisten();

            s.Send(2);
        }))();

        s.Send(3);
        s.Send(4);

        await Assert.That(@out.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TestMultipleUnlisten()
    {
        StreamSink<int> s = Stream.CreateSink<int>();

        List<int> @out = [];

        ((Action)(() =>
        {
            // ReSharper disable once UnusedVariable
            IStrongListener l = s.ListenStrong(@out.Add);

            s.Send(1);

            l.Unlisten();
            l.Unlisten();

            s.Send(2);

            l.Unlisten();
        }))();

        s.Send(3);
        s.Send(4);

        await Assert.That(@out.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TestMultipleUnlistenWeak()
    {
        StreamSink<int> s = Stream.CreateSink<int>();

        List<int> @out = [];

        ((Action)(() =>
        {
            // ReSharper disable once UnusedVariable
            IWeakListener l = s.Listen(@out.Add);

            s.Send(1);

            l.Unlisten();
            l.Unlisten();

            s.Send(2);

            l.Unlisten();
        }))();

        s.Send(3);
        s.Send(4);

        await Assert.That(@out.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TestListenOnce()
    {
        StreamSink<char> s = Stream.CreateSink<char>();
        List<char> @out = [];
        IListener l = s.ListenOnce(@out.Add);
        s.Send('A');
        s.Send('B');
        s.Send('C');
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: ['A'], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestListenOnceStrong()
    {
        StreamSink<char> s = Stream.CreateSink<char>();
        List<char> @out = [];
        IStrongListener l = s.ListenOnceStrong(@out.Add);
        s.Send('A');
        s.Send('B');
        s.Send('C');
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(expected: ['A'], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestListenOnceStrongUnlistenBeforeTheFirstFiring()
    {
        StreamSink<char> s = Stream.CreateSink<char>();
        List<char> @out = [];
        IStrongListener l = s.ListenOnceStrong(@out.Add);
        l.Unlisten();
        s.Send('A');
        await Assert.That(@out).IsEmpty();
    }

    [Test]
    public async Task TestListenOnceAsync()
    {
        StreamSink<char> s = Stream.CreateSink<char>();

        new Thread(() =>
        {
            Thread.Sleep(250);
            s.Send('A');
            s.Send('B');
            s.Send('C');
        }).Start();

        char r = await s.ListenOnceAsync();
        await Assert.That(r).IsEqualTo('A');
    }

    [Test]
    public async Task TestListenOnceAsyncWithCleanup()
    {
        StreamSink<char> s = Stream.CreateSink<char>();

        new Thread(() =>
        {
            Thread.Sleep(250);
            s.Send('A');
            s.Send('B');
            s.Send('C');
        }).Start();

        Task<char> t = s.ListenOnceAsync();
        Collect();
        char r = await t;
        await Assert.That(r).IsEqualTo('A');
    }

    [Test]
    public async Task TestListenOnceAsyncSameThread()
    {
        StreamSink<char> s = Stream.CreateSink<char>();
        Task<char> t = s.ListenOnceAsync();
        s.Send('A');
        s.Send('B');
        s.Send('C');
        char r = await t;
        await Assert.That(r).IsEqualTo('A');
    }

    [Test]
    public async Task TestListenOnceAsyncSameThreadWithCleanup()
    {
        StreamSink<char> s = Stream.CreateSink<char>();
        Task<char> t = s.ListenOnceAsync();
        Collect();
        s.Send('A');
        s.Send('B');
        s.Send('C');
        char r = await t;
        await Assert.That(r).IsEqualTo('A');
    }

    [Test]
    public async Task TestListenAsync()
    {
        CellSink<int> a = Cell.CreateSink(1);
        Cell<int> a1 = a.Map(static x => x + 1);
        Cell<int> a2 = a.Map(static x => x * 2);

        (CellLoop<int> called, IListener l) =
            Transaction.Run(() =>
            {
                Cell<int> result = a1.Lift(c2: a2, f: static (x, y) => x + y);
                Stream<Unit> incrementStream = result.Values().MapTo(Unit.Value);
                StreamSink<Unit> decrementStream = Stream.CreateSink<Unit>();
                CellLoop<int> calledLoop = Cell.CreateLoop<int>();

                calledLoop.Loop(
                    incrementStream.MapTo(1)
                        .Merge(s2: decrementStream.MapTo(-1), f: static (x, y) => x + y)
                        .Snapshot(c: calledLoop, f: static (u, c) => c + u)
                        .Hold(0));

                IListener lLocal =
                    result.ListenStrong(_ =>
                    {
                        Task.Run(async () =>
                        {
                            await Task.Delay(900);
                            decrementStream.Send(Unit.Value);
                        });
                    });

                return (calledLoop, lLocal);
            });

        // ReSharper disable once UnusedVariable
        List<int> calledResults = [];
        IListener l2 = called.ListenStrong(calledResults.Add);

        await Task.Delay(500);
        a.Send(2);
        await Task.Delay(500);
        a.Send(3);
        await Task.Delay(2500);

        l2.Unlisten();
        l.Unlisten();
    }

    [Test]
    public async Task TestStreamLoop()
    {
        StreamSink<int> streamSink = Stream.CreateSink<int>();

        Stream<int> s =
            Transaction.Run(() =>
            {
                StreamLoop<int> sl = new();
                Cell<int> c = sl.Map(static v => v + 2).Hold(0);
                Stream<int> s2 = streamSink.Snapshot(c: c, f: static (x, y) => x + y);
                sl.Loop(s2);
                return s2;
            });

        List<int> @out = [];
        IListener l = s.ListenStrong(@out.Add);
        streamSink.Send(3);
        streamSink.Send(4);
        streamSink.Send(7);
        streamSink.Send(8);
        l.Unlisten();

        await Assert.That(@out).IsEquivalentTo(expected: [3, 9, 18, 28], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestStreamLoopDefer()
    {
        StreamSink<int> streamSink = Stream.CreateSink<int>();

        Stream<int> stream =
            Transaction.Run(() =>
            {
                StreamLoop<int> streamLoop = new();

                Stream<int> streamLocal =
                    Operational.Defer(streamSink.OrElse(streamLoop).Filter(static v => v < 5).Map(static v => v + 1));

                streamLoop.Loop(streamLocal);
                return streamLocal;
            });

        List<int> @out = [];
        IListener l = stream.ListenStrong(@out.Add);
        streamSink.Send(2);
        l.Unlisten();

        await Assert.That(@out).IsEquivalentTo(expected: [3, 4, 5], ordering: CollectionOrdering.Matching);
    }

    // Node ranks index directly into the prioritized queue's backing array, which starts at
    // 1000 entries. A chain this long pushes ranks over that boundary and over some
    // regrowth operations. That queue is static. Thus, an error here did not only fail the deep
    // graph. It left the queue unusable for each subsequent transaction in the process, which is
    // what the trailing shallow chain checks.
    [Test]
    public async Task TestDeepChainGrowsPrioritizedQueue()
    {
        foreach (int depth in new[] { 999, 1000, 1001, 2000, 5000 })
        {
            StreamSink<int> s = Stream.CreateSink<int>();
            Stream<int> stream = s;

            for (int i = 0; i < depth; i++)
            {
                stream = stream.Map(static v => v + 1);
            }

            List<int> @out = [];
            IListener l = stream.ListenStrong(@out.Add);
            s.Send(0);
            l.Unlisten();

            await Assert.That(@out)
                .IsEquivalentTo(expected: [depth], ordering: CollectionOrdering.Matching)
                .Because($"chain of depth {depth}");
        }

        StreamSink<int> shallowSink = Stream.CreateSink<int>();
        List<int> shallowOut = [];
        IListener shallowListener = shallowSink.Map(static v => v + 1).ListenStrong(shallowOut.Add);
        shallowSink.Send(1);
        shallowListener.Unlisten();

        await Assert.That(shallowOut).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);
    }

    private static void Collect()
    {
        // Each generation, not only generation 0. These tests are correct only if a GC reclaims a
        // listener when no code roots it. A generation-0 collection reclaims only what stays in
        // generation 0. A full suite in one process allocates a sufficient quantity, thus the GC
        // usually moves the listener to a higher generation before the test asks. An object in a
        // higher generation survives the collection and continues to fire. That is how TestListen
        // was correct locally and on the branch build, and was incorrect on the PR build of the same
        // commit. It reported four sends where it wanted two.
        //
        // The finalizer step and the second collection are for the sweep mechanism of
        // StreamListenerManager, which asks for a sweep when a GC finalizes it. No code here waits on
        // that sweep, because Send removes the dead targets itself. A run of it keeps this test from
        // a collection that it leaves for whatever runs next.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
