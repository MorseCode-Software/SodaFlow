using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public sealed class CellTests
{
    [Test]
    public async Task TestLoop()
    {
        (Cell<int> c, CellStreamSink<int> s) =
            Transaction.Run(static () =>
            {
                CellLoop<int> loop = Cell.CreateLoop<int>();
                Cell<int> cLocal = loop.Map(static v => v * 5);
                CellStreamSink<int> sLocal = Cell.CreateStreamSink<int>();
                loop.Loop(sLocal.Hold(3));
                return (cLocal, sLocal);
            });

        List<int> output1 = [];
        List<int> output2 = [];
        IListener l = c.ListenStrong(output1.Add);
        IListener l2 = c.Updates().ListenStrong(output2.Add);

        s.Send(5);
        s.Send(7);

        l2.Unlisten();
        l.Unlisten();

        await Assert.That(output1).IsEquivalentTo([15, 25, 35], CollectionOrdering.Matching);
        await Assert.That(output2).IsEquivalentTo([25, 35], CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestLiftSimultaneousUpdates()
    {
        List<int> @out = [];
        CellSink<int> cellSink = Cell.CreateSink(1);
        Cell<int> cell = cellSink.Map(static v => 2 * v);
        IListener l = cellSink.Lift(c2: cell, f: static (x, y) => x + y).Updates().ListenStrong(@out.Add);

        cellSink.Send(2);
        cellSink.Send(7);

        l.Unlisten();

        await Assert.That(@out).IsEquivalentTo([6, 21], CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestLiftInSwitchC()
    {
        IReadOnlyList<Test> list1 = [new(0), new(1), new(2), new(3), new(4)];
        IReadOnlyList<Test> list2 = [new(5), new(6), new(7), new(8), new(9)];

        CellSink<IReadOnlyList<Test>> v = Cell.CreateSink(list1);

        Cell<IReadOnlyList<int>> c = v.Map(static oo => oo.Select(static o => o.Value).Lift()).SwitchC();

        List<IReadOnlyList<int>> streamOutput = [];
        IListener l = c.Updates().ListenStrong(streamOutput.Add);

        List<IReadOnlyList<int>> cellOutput = [];
        IListener l2 = c.ListenStrong(cellOutput.Add);

        list1[2].Value.Send(12);
        list2[1].Value.Send(16);
        list1[4].Value.Send(14);

        Transaction.RunVoid(() =>
        {
            list2[2].Value.Send(17);
            list1[0].Value.Send(10);
            v.Send(list2);
        });

        list1[3].Value.Send(13);
        list2[3].Value.Send(18);

        l2.Unlisten();
        l.Unlisten();

        await Assert.That(streamOutput.Count).IsEqualTo(4);
        await Assert.That(cellOutput.Count).IsEqualTo(5);

        await Assert.That(cellOutput[0]).IsEquivalentTo([0, 1, 2, 3, 4], CollectionOrdering.Matching);
        await Assert.That(streamOutput[0]).IsEquivalentTo([0, 1, 12, 3, 4], CollectionOrdering.Matching);
        await Assert.That(cellOutput[1]).IsEquivalentTo([0, 1, 12, 3, 4], CollectionOrdering.Matching);
        await Assert.That(streamOutput[1]).IsEquivalentTo([0, 1, 12, 3, 14], CollectionOrdering.Matching);
        await Assert.That(cellOutput[2]).IsEquivalentTo([0, 1, 12, 3, 14], CollectionOrdering.Matching);
        await Assert.That(streamOutput[2]).IsEquivalentTo([5, 16, 17, 8, 9], CollectionOrdering.Matching);
        await Assert.That(cellOutput[3]).IsEquivalentTo([5, 16, 17, 8, 9], CollectionOrdering.Matching);
        await Assert.That(streamOutput[3]).IsEquivalentTo([5, 16, 17, 18, 9], CollectionOrdering.Matching);
        await Assert.That(cellOutput[4]).IsEquivalentTo([5, 16, 17, 18, 9], CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestMapWithSwitchC()
    {
        IReadOnlyList<Test> list1 = [new(0), new(1), new(2), new(3), new(4)];
        IReadOnlyList<Test> list2 = [new(5), new(6), new(7), new(8), new(9)];

        CellSink<IReadOnlyList<Test>> v = Cell.CreateSink(list1);

        Cell<IReadOnlyList<int>> c =
            v.Map(static oo => oo.Select(static o => o.Value).Lift()).Map(static o => o).SwitchC();

        List<IReadOnlyList<int>> streamOutput = [];
        IListener l = c.Updates().ListenStrong(streamOutput.Add);

        List<IReadOnlyList<int>> cellOutput = [];
        IListener l2 = c.ListenStrong(cellOutput.Add);

        list1[2].Value.Send(12);
        list2[1].Value.Send(16);
        list1[4].Value.Send(14);

        Transaction.RunVoid(() =>
        {
            list2[2].Value.Send(17);
            list1[0].Value.Send(10);
            v.Send(list2);
        });

        list1[3].Value.Send(13);
        list2[3].Value.Send(18);

        l2.Unlisten();
        l.Unlisten();

        await Assert.That(streamOutput.Count).IsEqualTo(4);
        await Assert.That(cellOutput.Count).IsEqualTo(5);

        await Assert.That(cellOutput[0]).IsEquivalentTo([0, 1, 2, 3, 4], CollectionOrdering.Matching);
        await Assert.That(streamOutput[0]).IsEquivalentTo([0, 1, 12, 3, 4], CollectionOrdering.Matching);
        await Assert.That(cellOutput[1]).IsEquivalentTo([0, 1, 12, 3, 4], CollectionOrdering.Matching);
        await Assert.That(streamOutput[1]).IsEquivalentTo([0, 1, 12, 3, 14], CollectionOrdering.Matching);
        await Assert.That(cellOutput[2]).IsEquivalentTo([0, 1, 12, 3, 14], CollectionOrdering.Matching);
        await Assert.That(streamOutput[2]).IsEquivalentTo([5, 16, 17, 8, 9], CollectionOrdering.Matching);
        await Assert.That(cellOutput[3]).IsEquivalentTo([5, 16, 17, 8, 9], CollectionOrdering.Matching);
        await Assert.That(streamOutput[3]).IsEquivalentTo([5, 16, 17, 18, 9], CollectionOrdering.Matching);
        await Assert.That(cellOutput[4]).IsEquivalentTo([5, 16, 17, 18, 9], CollectionOrdering.Matching);
    }

    private sealed class Test(int initialValue)
    {
        public CellSink<int> Value { get; } = Cell.CreateSink(initialValue);
    }

    [Test]
    public async Task TestLiftCellsInSwitchC()
    {
        List<int> @out = [];
        CellSink<int> s = Cell.CreateSink(0);
        Cell<Cell<int>> c = Cell.Constant(Cell.Constant(1));
        Cell<Cell<int>> r = c.Map(c2 => c2.Lift(c2: s, f: static (v1, v2) => v1 + v2));
        IListener l = r.SwitchC().ListenStrong(@out.Add);
        s.Send(2);
        s.Send(4);
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo([1, 3, 5], CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestLazyCellCreation()
    {
        List<int> @out = [];
        StreamSink<int> s = Stream.CreateSink<int>();
        Cell<Cell<int>> c = Cell.Constant(1).Map(_ => s.Hold(0));
        s.Send(1);
        IListener l = c.SwitchC().ListenStrong(@out.Add);
        s.Send(3);
        s.Send(5);
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo([1, 3, 5], CollectionOrdering.Matching);
    }

    [Test]
    public async Task CellValuesWithPrevious()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        Cell<int> c = s.Hold(0);
        List<(int Current, Maybe<int> Previous)> @out = [];

        using (Transaction.Run(() =>
               {
                   Stream<(int Current, Maybe<int> Previous)> r =
                       c.Updates()
                           .Snapshot(c: c, f: static (n, o) => (Current: n, Previous: Maybe.Some(o)))
                           .OrElse(
                               Cell.ConstantLazy(c.SampleLazy())
                                   .Values()
                                   .Map(static v => (Current: v, Previous: Maybe<int>.None)));

                   return r.ListenStrong(@out.Add);
               }))
        {
            s.Send(1);
            s.Send(2);
            s.Send(3);
            s.Send(4);
        }

        await Assert.That(@out).IsEquivalentTo([
                          (Current: 0, Previous: Maybe.None),
                          (Current: 1, Previous: Maybe.Some(0)),
                          (Current: 2, Previous: Maybe.Some(1)),
                          (Current: 3, Previous: Maybe.Some(2)),
                          (Current: 4, Previous: Maybe.Some(3))
                      ], CollectionOrdering.Matching);
    }

    [Test]
    public async Task CellValuesWithPreviousHavingInitialUpdate()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        Cell<int> c = s.Hold(0);
        List<(int Current, Maybe<int> Previous)> @out = [];

        using (Transaction.Run(() =>
               {
                   Stream<(int Current, Maybe<int> Previous)> r =
                       c.Updates()
                           .Snapshot(c: c, f: static (n, o) => (Current: n, Previous: Maybe.Some(o)))
                           .OrElse(
                               Cell.ConstantLazy(c.SampleLazy())
                                   .Values()
                                   .Map(static v => (Current: v, Previous: Maybe<int>.None)));

                   s.Send(1);
                   return r.ListenStrong(@out.Add);
               }))
        {
            s.Send(2);
            s.Send(3);
            s.Send(4);
            s.Send(5);
        }

        await Assert.That(@out).IsEquivalentTo([
                          (Current: 1, Previous: Maybe.Some(0)),
                          (Current: 2, Previous: Maybe.Some(1)),
                          (Current: 3, Previous: Maybe.Some(2)),
                          (Current: 4, Previous: Maybe.Some(3)),
                          (Current: 5, Previous: Maybe.Some(4))
                      ], CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestLoopAndSwitchCError()
    {
        InvalidOperationException? exception = null;

        try
        {
            Cell.Loop<int>()
                // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                .WithoutCaptures(static l =>
                {
                    StreamSink<Inner> s = Stream.CreateSink<Inner>();
                    Cell<Inner> cc = s.Hold(new Inner(l.SampleLazy()));
                    Cell<int> c = cc.Map(static o => o.C).SwitchC();
                    return c;
                });
        }
        catch (InvalidOperationException e)
        {
            exception = e;
        }

        await Assert.That(exception).IsNotNull();

        await Assert.That(exception?.Message).IsEqualTo("ValueFactory attempted to access the Value property of this instance.");
    }

    [Test]
    public async Task TestLoopAndSwitchC()
    {
        (Cell<int> resultCell, (Cell<Inner> innerCell, StreamSink<Inner> innerStreamSink)) =
            Cell.Loop<int>()
                .WithCaptures(static l =>
                {
                    StreamSink<Inner> s = Stream.CreateSink<Inner>();
                    Cell<Inner> cc = s.Hold(new Inner(l.SampleLazy()));
                    Cell<int> c = cc.Map(static o => o.C).SwitchC().Values().Hold(3);
                    return (Cell: c, Captures: (cc, s));
                });

        List<int> @out = [];

        using (resultCell.ListenStrong(@out.Add))
        {
            innerCell.Sample().S.Send(5);
            innerStreamSink.Send(new Inner(resultCell.SampleLazy().Map(static v => v - 1)));
            innerCell.Sample().S.Send(7);
        }

        await Assert.That(@out).IsEquivalentTo([3, 5, 4, 7], CollectionOrdering.Matching);
    }

    private sealed class Inner
    {
        public Inner(Lazy<int> initialValue)
        {
            this.S = Stream.CreateSink<int>();
            this.C = this.S.HoldLazy(initialValue);
        }

        public StreamSink<int> S { get; }
        public Cell<int> C { get; }
    }
}
