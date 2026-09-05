using System;
using System.Collections.Generic;
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
        await Assert.That(@out).IsEquivalentTo([5], CollectionOrdering.Matching);
        s.Send(6);
        await Assert.That(@out).IsEquivalentTo([5], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo(["7", "5"], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([7, 9, 8], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([60, 9, 90, 90, 90], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([7, 9], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([14, 18], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([7, 9, 8], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([21, 27], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([2, 48], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([15, 40], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo(['H', 'I'], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo(["tomato", "peach", "pear"], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([1, 2, 3], CollectionOrdering.Matching);
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

        await Assert.That(chosen).IsEquivalentTo(mapped, CollectionOrdering.Matching);
        await Assert.That(chosen).IsEquivalentTo([1, 2], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo(Array.Empty<int>(), CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([5], CollectionOrdering.Matching);
        await Assert.That(out2).IsEquivalentTo([5], CollectionOrdering.Matching);
        await Assert.That(out3).IsEquivalentTo([2, 10], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([2, 0, 5], CollectionOrdering.Matching);
        await Assert.That(out2).IsEquivalentTo([2, 0, 5], CollectionOrdering.Matching);
        await Assert.That(out3).IsEquivalentTo([4, 0, 10], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo(new char?[] { 'H', 'I' }, CollectionOrdering.Matching);
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

        await Assert.That(@out).IsEquivalentTo([2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([2, 4, 2, 4, 2], CollectionOrdering.Matching);
    }

    // Calm remembers the last value it let through, and that memory has to survive the end of a
    // transaction. The existing Calm tests only ever send outside one, so they never exercise a
    // firing that arrives with several sources feeding it in a single transaction, nor whether
    // the remembered value committed at the end of one transaction is what the next compares
    // against. Both are checked here: the second transaction is suppressed only if the first
    // committed correctly, and the fourth only if the third did.
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

        // 2 again, from a single source this time - must be suppressed.
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

        await Assert.That(@out).IsEquivalentTo([2, 3, 4], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([115, 122, 125, 127, 130], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([100, 105, 112, 113, 115, 118], CollectionOrdering.Matching);
    }

    // Collect carries state between firings, and that state has to survive the end of a
    // transaction. TestCollect only ever sends outside one, so it never covers a firing that
    // arrives with several sources feeding it in a single transaction, nor whether the state
    // committed at the end of one transaction is what the next one folds over. The count in the
    // state makes both visible: it can only reach 3 if every transaction committed.
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

        await Assert.That(@out).IsEquivalentTo(["3/1", "13/2", "15/3"], CollectionOrdering.Matching);
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

        await Assert.That(@out).IsEquivalentTo([0, 3, 13, 15], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo(['A'], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([' ', 'C', 'B', 'A'], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo([' ', 'C', 'B'], CollectionOrdering.Matching);
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
        await Assert.That(@out).IsEquivalentTo(['C', 'B', 'A'], CollectionOrdering.Matching);
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

        GC.Collect(generation: 0, mode: GCCollectionMode.Forced);
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

            GC.Collect(generation: 0, mode: GCCollectionMode.Forced);

            ((Action)(() =>
            {
                // ReSharper disable once UnusedVariable
                IWeakListener l = s2.Listen(@out.Add);

                s.Send(3);
                s.Send(4);
                s.Send(5);
            }))();
        }))();

        GC.Collect(generation: 0, mode: GCCollectionMode.Forced);
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
        await Assert.That(@out).IsEquivalentTo(['A'], CollectionOrdering.Matching);
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
        GC.Collect(generation: 0, mode: GCCollectionMode.Forced);
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
        GC.Collect(generation: 0, mode: GCCollectionMode.Forced);
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

        await Assert.That(@out).IsEquivalentTo([3, 9, 18, 28], CollectionOrdering.Matching);
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

        await Assert.That(@out).IsEquivalentTo([3, 4, 5], CollectionOrdering.Matching);
    }

    // Node ranks index directly into the prioritized queue's backing array, which starts at
    // 1000 entries. A chain this long pushes ranks past that boundary and past several
    // regrowth operations. Because that queue is static, getting this wrong did not just fail the deep
    // graph - it left the queue unusable for every later transaction in the process, which is
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

            await Assert.That(@out).IsEquivalentTo([depth], CollectionOrdering.Matching).Because($"chain of depth {depth}");
        }

        StreamSink<int> shallowSink = Stream.CreateSink<int>();
        List<int> shallowOut = [];
        IListener shallowListener = shallowSink.Map(static v => v + 1).ListenStrong(shallowOut.Add);
        shallowSink.Send(1);
        shallowListener.Unlisten();

        await Assert.That(shallowOut).IsEquivalentTo([2], CollectionOrdering.Matching);
    }
}
