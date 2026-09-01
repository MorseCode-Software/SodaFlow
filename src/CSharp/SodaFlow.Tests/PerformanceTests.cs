using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests
{
    [TestFixture]
    public class PerformanceTests
    {
        [Test]
        public void TestMerge()
        {
            StreamSink<Unit> s = Stream.CreateSink<Unit>();

            TestObject[] obj =
                Transaction.Run(() =>
                {
                    StreamLoop<bool> loop = Stream.CreateLoop<bool>();
                    CellStreamSink<int> s1 = Cell.CreateStreamSink<int>();
                    CellStreamSink<int> s2 = Cell.CreateStreamSink<int>();

                    TestObject[] l =
                        Enumerable.Range(start: 0, count: 5000)
                            .Select(_ => new TestObject(s: loop, s1: s1, s2: s2))
                            .ToArray();

                    loop.Loop(s.Snapshot(l.Select(o => o.Cell).Lift()).Map(o => o.All(v => v == 0)));
                    return l;
                });

            int[] values = obj.Select(v => v.CurrentValue).ToArray();
            CollectionAssert.AreEqual(expected: Enumerable.Range(start: 1, count: 5000).Select(_ => 0), actual: values);
        }

        private class TestObject
        {
            // ReSharper disable once NotAccessedField.Local
            private readonly IListener l;
            private Lazy<int> currentValue = new Lazy<int>(() => default);

            public TestObject(Stream<bool> s, Stream<int> s1, Stream<int> s2)
            {
                (Cell<int> cell, IStrongListener l) =
                    Transaction.Run(() =>
                    {
                        Cell<int> cellLocal = s.Map(v => v ? 1 : 0).OrElse(s1).OrElse(s2).Hold(0);

                        Cell<int> cell2 =
                            s1.Snapshot(c: cellLocal, f: (left, right) => left + right)
                                .Filter(v => v > 5)
                                .OrElse(
                                    s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: (left, right) => left + right))
                                        .Map(v => v + 1))
                                .Hold(3);

                        Cell<int> cell3 =
                            s1.Snapshot(c: cellLocal, f: (left, right) => left + right)
                                .Filter(v => v > 5)
                                .OrElse(
                                    s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: (left, right) => left + right))
                                        .Map(v => v + 1))
                                .Hold(3);

                        Cell<int> cell4 =
                            s1.Snapshot(c: cellLocal, f: (left, right) => left + right)
                                .Filter(v => v > 5)
                                .OrElse(
                                    s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: (left, right) => left + right))
                                        .Map(v => v + 1))
                                .Hold(3);

                        Cell<int> cell5 =
                            s1.Snapshot(c: cellLocal, f: (left, right) => left + right)
                                .Filter(v => v > 5)
                                .OrElse(
                                    s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: (left, right) => left + right))
                                        .Map(v => v + 1))
                                .Hold(3);

                        Cell<int> cell6 =
                            s1.Snapshot(c: cellLocal, f: (left, right) => left + right)
                                .Filter(v => v > 5)
                                .OrElse(
                                    s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: (left, right) => left + right))
                                        .Map(v => v + 1))
                                .Hold(3);

                        Cell<int> cell7 =
                            s1.Snapshot(c: cellLocal, f: (left, right) => left + right)
                                .Filter(v => v > 5)
                                .OrElse(
                                    s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: (left, right) => left + right))
                                        .Map(v => v + 1))
                                .Hold(3);

                        Cell<int> cell8 =
                            s1.Snapshot(c: cellLocal, f: (left, right) => left + right)
                                .Filter(v => v > 5)
                                .OrElse(
                                    s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: (left, right) => left + right))
                                        .Map(v => v + 1))
                                .Hold(3);

                        Cell<int> cell9 =
                            s1.Snapshot(c: cellLocal, f: (left, right) => left + right)
                                .Filter(v => v > 5)
                                .OrElse(
                                    s.Snapshot(s1.Hold(0).Lift(c2: s2.Hold(1), f: (left, right) => left + right))
                                        .Map(v => v + 1))
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
        public void TestRunConstruct()
        {
            CellSink<IReadOnlyList<TestObject2>> objects =
                Transaction.Run(() =>
                {
                    IReadOnlyList<TestObject2> o2 =
                        Enumerable.Range(start: 0, count: 10000)
                            .Select(n =>
                                new TestObject2(
                                    id: n,
                                    initialIsSelected: n < 1500,
                                    selectAllStream: Stream.Never<bool>()))
                            .ToArray();

                    CellSink<IReadOnlyList<TestObject2>> objectsLocal = Cell.CreateSink(o2);

                    return objectsLocal;
                });

            Transaction.Run(() =>
            {
                objects.Send(
                    Enumerable.Range(start: 0, count: 20000)
                        .Select(n =>
                            new TestObject2(id: n, initialIsSelected: n < 500, selectAllStream: Stream.Never<bool>()))
                        .ToArray());

                return Unit.Value;
            });
        }

        [Test]
        public void TestRunConstruct2()
        {
            (var objectsAndIsSelected, Stream<bool> selectAllStream, CellSink<IReadOnlyList<TestObject2>> objects) =
                Transaction.Run(() =>
                {
                    CellLoop<bool?> allSelectedCellLoop = Cell.CreateLoop<bool?>();
                    StreamSink<Unit> toggleAllSelectedStreamLocal = Stream.CreateSink<Unit>();

                    Stream<bool> selectAllStreamLocal =
                        toggleAllSelectedStreamLocal.Snapshot(allSelectedCellLoop).Map(a => a != true);

                    IReadOnlyList<TestObject2> o2 =
                        Enumerable.Range(start: 0, count: 10000)
                            .Select(n =>
                                new TestObject2(
                                    id: n,
                                    initialIsSelected: n < 1500,
                                    selectAllStream: selectAllStreamLocal))
                            .ToArray();

                    CellSink<IReadOnlyList<TestObject2>> objectsLocal = Cell.CreateSink(o2);

                    var objectsAndIsSelectedLocal =
                        objectsLocal
                            .Map(oo => oo.Select(o => o.IsSelected.Map(s => new { Object = o, IsSelected = s })).Lift())
                            .SwitchC();

                    bool defaultValue = o2.Count < 1;

                    Cell<bool?> allSelected =
                        objectsAndIsSelectedLocal.Map(oo =>
                            !oo.Any()
                                ? defaultValue
                                : oo.All(o => o.IsSelected)
                                    ? true
                                    : oo.All(o => !o.IsSelected)
                                        ? (bool?)false
                                        : null);

                    allSelectedCellLoop.Loop(allSelected);

                    return (objectsAndIsSelectedLocal, selectAllStreamLocal, objectsLocal);
                });

            List<int> @out = new List<int>();

            using (Transaction.Run(() =>
                       objectsAndIsSelected.Map(oo => oo.Count(o => o.IsSelected))
                           .Values()
                           .ListenStrong(@out.Add)))
            {
                Transaction.Run(() =>
                {
                    objects.Send(
                        Enumerable.Range(start: 0, count: 20000)
                            .Select(n =>
                                new TestObject2(id: n, initialIsSelected: n < 500, selectAllStream: selectAllStream))
                            .ToArray());

                    return Unit.Value;
                });
            }

            CollectionAssert.AreEqual(expected: new[] { 1500, 500 }, actual: @out);
        }

        private class TestObject2
        {
            public TestObject2(int id, bool initialIsSelected, Stream<bool> selectAllStream)
            {
                this.Id = id;
                this.IsSelectedStreamSink = Stream.CreateSink<bool>();
                this.IsSelected = selectAllStream.OrElse(this.IsSelectedStreamSink).Hold(initialIsSelected);
            }

            public int Id { get; }
            public StreamSink<bool> IsSelectedStreamSink { get; }
            public Cell<bool> IsSelected { get; }
        }
    }
}