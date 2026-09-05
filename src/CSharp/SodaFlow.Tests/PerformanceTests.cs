using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using SodaFlow.Functional;

namespace SodaFlow.Tests;

public class PerformanceTests
{
    [Test]
    public async Task TestMerge()
    {
        StreamSink<Unit> s = Stream.CreateSink<Unit>();

        TestObject[] obj =
            Transaction.Run(() =>
            {
                StreamLoop<bool> loop = Stream.CreateLoop<bool>();
                CellStreamSink<int> s1 = Cell.CreateStreamSink<int>();
                CellStreamSink<int> s2 = Cell.CreateStreamSink<int>();

                TestObject[] l =
                [
                    .. Enumerable.Range(start: 0, count: 5000)
                        .Select(_ => new TestObject(s: loop, s1: s1, s2: s2))
                ];

                loop.Loop(s.Snapshot(l.Select(static o => o.Cell).Lift()).Map(static o => o.All(static v => v == 0)));
                return l;
            });

        int[] values = [.. obj.Select(static v => v.CurrentValue)];

        await Assert.That(values).IsEquivalentTo(Enumerable.Range(start: 1, count: 5000).Select(static _ => 0), CollectionOrdering.Matching);
    }

    private sealed class TestObject
    {
        // ReSharper disable once NotAccessedField.Local
        private readonly IListener l;
        private Lazy<int> currentValue = new(static () => 0);

        public TestObject(Stream<bool> s, Stream<int> s1, Stream<int> s2)
        {
            (Cell<int> cell, IStrongListener l) =
                Transaction.Run(() =>
                {
                    Cell<int> cellLocal = s.Map(static v => v ? 1 : 0).OrElse(s1).OrElse(s2).Hold(0);

                    // ReSharper disable once UnusedVariable - Keeps Cell in scope.
                    Cell<int> cell2 =
                        s1.Snapshot(c: cellLocal, f: static (left, right) => left + right)
                            .Filter(static v => v > 5)
                            .OrElse(
                                s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: static (left, right) => left + right))
                                    .Map(static v => v + 1))
                            .Hold(3);

                    // ReSharper disable once UnusedVariable - Keeps Cell in scope.
                    Cell<int> cell3 =
                        s1.Snapshot(c: cellLocal, f: static (left, right) => left + right)
                            .Filter(static v => v > 5)
                            .OrElse(
                                s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: static (left, right) => left + right))
                                    .Map(static v => v + 1))
                            .Hold(3);

                    // ReSharper disable once UnusedVariable - Keeps Cell in scope.
                    Cell<int> cell4 =
                        s1.Snapshot(c: cellLocal, f: static (left, right) => left + right)
                            .Filter(static v => v > 5)
                            .OrElse(
                                s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: static (left, right) => left + right))
                                    .Map(static v => v + 1))
                            .Hold(3);

                    // ReSharper disable once UnusedVariable - Keeps Cell in scope.
                    Cell<int> cell5 =
                        s1.Snapshot(c: cellLocal, f: static (left, right) => left + right)
                            .Filter(static v => v > 5)
                            .OrElse(
                                s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: static (left, right) => left + right))
                                    .Map(static v => v + 1))
                            .Hold(3);

                    // ReSharper disable once UnusedVariable - Keeps Cell in scope.
                    Cell<int> cell6 =
                        s1.Snapshot(c: cellLocal, f: static (left, right) => left + right)
                            .Filter(static v => v > 5)
                            .OrElse(
                                s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: static (left, right) => left + right))
                                    .Map(static v => v + 1))
                            .Hold(3);

                    // ReSharper disable once UnusedVariable - Keeps Cell in scope.
                    Cell<int> cell7 =
                        s1.Snapshot(c: cellLocal, f: static (left, right) => left + right)
                            .Filter(static v => v > 5)
                            .OrElse(
                                s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: static (left, right) => left + right))
                                    .Map(static v => v + 1))
                            .Hold(3);

                    // ReSharper disable once UnusedVariable - Keeps Cell in scope.
                    Cell<int> cell8 =
                        s1.Snapshot(c: cellLocal, f: static (left, right) => left + right)
                            .Filter(static v => v > 5)
                            .OrElse(
                                s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: static (left, right) => left + right))
                                    .Map(static v => v + 1))
                            .Hold(3);

                    // ReSharper disable once UnusedVariable - Keeps Cell in scope.
                    Cell<int> cell9 =
                        s1.Snapshot(c: cellLocal, f: static (left, right) => left + right)
                            .Filter(static v => v > 5)
                            .OrElse(
                                s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: static (left, right) => left + right))
                                    .Map(static v => v + 1))
                            .Hold(3);

                    this.currentValue = cellLocal.SampleLazy();
                    IStrongListener lLocal = cellLocal.Updates().ListenStrong(v => this.CurrentValue = v);

                    return (cellLocal, lLocal);
                });

            this.Cell = cell;
            this.l = l;
        }

        public Cell<int> Cell { get; }

        public int CurrentValue
        {
            get => this.currentValue.Value;
            private set => this.currentValue = new Lazy<int>(() => value);
        }
    }

    [Test]
    public async Task TestRunConstruct()
    {
        CellSink<IReadOnlyList<TestObject2>> objects =
            Transaction.Run(static () =>
            {
                IReadOnlyList<TestObject2> o2 =
                [
                    .. Enumerable.Range(start: 0, count: 10000)
                        .Select(static n =>
                            new TestObject2(
                                initialIsSelected: n < 1500,
                                selectAllStream: Stream.Never<bool>()))
                ];

                CellSink<IReadOnlyList<TestObject2>> objectsLocal = Cell.CreateSink(o2);

                return objectsLocal;
            });

        Transaction.Run(() =>
        {
            objects.Send(
            [
                .. Enumerable.Range(start: 0, count: 20000)
                    .Select(static n =>
                        new TestObject2(initialIsSelected: n < 500, selectAllStream: Stream.Never<bool>()))
            ]);

            return Unit.Value;
        });
    }

    [Test]
    public async Task TestRunConstruct2()
    {
        (var objectsAndIsSelected, Stream<bool> selectAllStream, CellSink<IReadOnlyList<TestObject2>> objects) =
            Transaction.Run(static () =>
            {
                CellLoop<bool?> allSelectedCellLoop = Cell.CreateLoop<bool?>();
                StreamSink<Unit> toggleAllSelectedStreamLocal = Stream.CreateSink<Unit>();

                Stream<bool> selectAllStreamLocal =
                    toggleAllSelectedStreamLocal.Snapshot(allSelectedCellLoop).Map(static a => a != true);

                IReadOnlyList<TestObject2> o2 =
                [
                    .. Enumerable.Range(start: 0, count: 10000)
                        .Select(n =>
                            new TestObject2(
                                initialIsSelected: n < 1500,
                                selectAllStream: selectAllStreamLocal))
                ];

                CellSink<IReadOnlyList<TestObject2>> objectsLocal = Cell.CreateSink(o2);

                var objectsAndIsSelectedLocal =
                    objectsLocal
                        .Map(static oo =>
                            oo.Select(static o => o.IsSelected.Map(s => new { Object = o, IsSelected = s })).Lift())
                        .SwitchC();

                bool defaultValue = o2.Count < 1;

                Cell<bool?> allSelected =
                    objectsAndIsSelectedLocal.Map(oo =>
                        !oo.Any()
                            ? defaultValue
                            : oo.All(static o => o.IsSelected)
                                ? true
                                : oo.All(static o => !o.IsSelected)
                                    ? (bool?)false
                                    : null);

                allSelectedCellLoop.Loop(allSelected);

                return (objectsAndIsSelectedLocal, selectAllStreamLocal, objectsLocal);
            });

        List<int> @out = [];

        using (Transaction.Run(() =>
                   objectsAndIsSelected.Map(static oo => oo.Count(static o => o.IsSelected))
                       .Values()
                       .ListenStrong(@out.Add)))
        {
            Transaction.Run(() =>
            {
                objects.Send(
                [
                    .. Enumerable.Range(start: 0, count: 20000)
                        .Select(n =>
                            new TestObject2(initialIsSelected: n < 500, selectAllStream: selectAllStream))
                ]);

                return Unit.Value;
            });
        }

        await Assert.That(@out).IsEquivalentTo(new[] { 1500, 500 }, CollectionOrdering.Matching);
    }

    private sealed class TestObject2
    {
        public TestObject2(bool initialIsSelected, Stream<bool> selectAllStream)
        {
            this.IsSelectedStreamSink = Stream.CreateSink<bool>();
            this.IsSelected = selectAllStream.OrElse(this.IsSelectedStreamSink).Hold(initialIsSelected);
        }

        private StreamSink<bool> IsSelectedStreamSink { get; }
        public Cell<bool> IsSelected { get; }
    }
}
