using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SodaFlow.Functional;

namespace SodaFlow.Tests.Performance;

internal static class Program
{
    // ReSharper disable once UnusedMember.Global
    public static void Main3(string[] args)
    {
        CellSink<bool> c = Cell.CreateSink(false);

        Console.WriteLine("Press any key");
        Console.ReadKey();

        ((Action)(() =>
        {
            // ReSharper disable once CollectionNeverQueried.Local
            List<Cell<bool>> cc = [];

            for (int i = 0; i < 5000; i++)
            {
                cc.Add(c.Map(static v => !v));
            }

            Console.WriteLine("Press any key");
            Console.ReadKey();
        }))();

        Console.WriteLine("Press any key");
        Console.ReadKey();
    }

    // ReSharper disable once UnusedMember.Global
    public static void Main2(string[] args)
    {
        Console.WriteLine("Press any key");
        Console.ReadKey();

        //CellSink<IReadOnlyList<SmallTestObject>> s = ((Func<CellSink<IReadOnlyList<SmallTestObject>>>)(() =>
        //   new CellSink<IReadOnlyList<SmallTestObject>>(new SmallTestObject[0])))();
        CellSink<IReadOnlyList<SmallTestObject>> s =
            ((Func<CellSink<IReadOnlyList<SmallTestObject>>>)(static () =>
                Cell.CreateSink<IReadOnlyList<SmallTestObject>>(
                    [.. Enumerable.Range(start: 0, count: 500).Select(static _ => new SmallTestObject())])))();

        // ReSharper disable once UnusedVariable
        Cell<IReadOnlyList<bool>> s2 = s.Map(static oo => oo.Select(static o => o.S).Lift()).SwitchC();

        ((Action)(() =>
        {
            for (int i = 0; i < 5; i++)
            {
                s.Send([.. Enumerable.Range(start: 0, count: 500).Select(static _ => new SmallTestObject())]);
            }
        }))();

        s.Send([]);

        Console.WriteLine("Press any key");
        Console.ReadKey();

        ((Action)(() =>
        {
            for (int i = 0; i < 5; i++)
            {
                s.Send([.. Enumerable.Range(start: 0, count: 500).Select(static _ => new SmallTestObject())]);
            }
        }))();

        s.Send([]);

        Console.WriteLine("Press any key");
        Console.ReadKey();
    }

    public static void Main(string[] args)
    {
        Console.WriteLine("Press any key");
        Console.ReadKey();

        (StreamSink<Unit> toggleAllSelectedStream,
                Cell<IReadOnlyList<(TestObject Object, bool IsSelected)>> objectsAndIsSelected,
                Stream<bool> selectAllStream, CellSink<IReadOnlyList<TestObject>> objects) =
            Transaction.Run(static () =>
            {
                CellLoop<bool?> allSelectedCellLoop = Cell.CreateLoop<bool?>();
                StreamSink<Unit> toggleAllSelectedStreamLocal = Stream.CreateSink<Unit>();

                Stream<bool> selectAllStreamLocal =
                    toggleAllSelectedStreamLocal.Snapshot(allSelectedCellLoop).Map(static a => a != true);

                IReadOnlyList<TestObject> o2 =
                [
                    .. Enumerable.Range(start: 0, count: 10000)
                        .Select(_ => new TestObject(selectAllStream: selectAllStreamLocal))
                ];

                CellSink<IReadOnlyList<TestObject>> objectsLocal =
                    Cell.CreateSink((IReadOnlyList<TestObject>)[]);

                Cell<IReadOnlyList<(TestObject Object, bool IsSelected)>> objectsAndIsSelectedLocal =
                    objectsLocal
                        .Map(static oo =>
                            oo.Select(static o => o.IsSelected.Map(s => (Object: o, IsSelected: s))).Lift())
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

                return (toggleAllSelectedStreamLocal, objectsAndIsSelectedLocal, selectAllStreamLocal,
                    objectsLocal);
            });

        // ReSharper disable once UnusedVariable
        IListener l =
            Transaction.Run(() =>
                objectsAndIsSelected.Map(static oo => oo.Count(static o => o.IsSelected))
                    .Updates()
                    .ListenStrong(static v => Console.WriteLine($"{v} selected")));

        Console.WriteLine("Press any key");
        Console.ReadKey();

        Stopwatch sw = new();
        sw.Start();

        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        Thread.Sleep(500);
        SendMore(cellSink: objects, selectAllStream: selectAllStream);
        objects.Sample()[2].IsSelectedStreamSink.Send(true);

        Transaction.RunVoid(() =>
        {
            objects.Sample()[3].IsSelectedStreamSink.Send(true);
            objects.Sample()[4].IsSelectedStreamSink.Send(true);
        });

        Transaction.RunVoid(() =>
        {
            objects.Send(
            [
                .. Enumerable.Range(start: 0, count: 2500)
                    .Select(_ => new TestObject(selectAllStream: selectAllStream))
            ]);

            toggleAllSelectedStream.Send(Unit.Value);
        });

        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        toggleAllSelectedStream.Send(Unit.Value);
        objects.Send([]);

        sw.Stop();

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine($"Elapsed: {sw.ElapsedMilliseconds}ms");

        Console.WriteLine();
        Console.WriteLine("Press any key");
        Console.ReadKey();
    }

    private static void SendMore(CellSink<IReadOnlyList<TestObject>> cellSink, Stream<bool> selectAllStream) =>
        Transaction.RunVoid(() =>
            cellSink.Send(
            [
                .. Enumerable.Range(start: 0, count: 20000)
                    .Select(_ => new TestObject(selectAllStream: selectAllStream))
            ]));

    private sealed class SmallTestObject
    {
        public CellSink<bool> S { get; } = Cell.CreateSink(false);
    }

    private sealed class TestObject
    {
        public TestObject(Stream<bool> selectAllStream)
        {
            this.IsSelectedStreamSink = Stream.CreateSink<bool>();
            this.IsSelected = selectAllStream.OrElse(this.IsSelectedStreamSink).Hold(false);
        }

        public StreamSink<bool> IsSelectedStreamSink { get; }
        public Cell<bool> IsSelected { get; }
    }
}
