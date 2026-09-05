using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public class LoopTests
{
    [Test]
    public async Task ImperativeStreamLoop()
    {
        StreamSink<int> s = Stream.CreateSink<int>();

        Stream<int> result =
            Transaction.Run(() =>
            {
                StreamLoop<int> l = new();
                Stream<int> resultLocal = s.Snapshot(c: l.Hold(0), f: static (n, o) => n + o);
                l.Loop(resultLocal);
                return resultLocal;
            });

        List<int> @out = [];

        using (result.ListenStrong(@out.Add))
        {
            s.Send(1);
            s.Send(2);
            s.Send(3);
        }

        await Assert.That(@out).IsEquivalentTo(new[] { 1, 3, 6 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ImperativeStreamLoopFailsWhenLoopedTwice()
    {
        InvalidOperationException? actual = null;

        try
        {
            StreamSink<int> s = Stream.CreateSink<int>();

            Transaction.RunVoid(() =>
            {
                StreamLoop<int> l = new();
                Stream<int> resultLocal = s.Snapshot(c: l.Hold(0), f: static (n, o) => n + o);
                l.Loop(resultLocal);
                l.Loop(resultLocal);
            });
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("Loop was looped more than once.");
    }

    [Test]
    public async Task ImperativeStreamLoopFailsWithoutTransaction()
    {
        InvalidOperationException? actual = null;

        try
        {
            // ReSharper disable once ObjectCreationAsStatement
            new StreamLoop<int>();
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("Loop must be created within an explicit transaction.");
    }

    [Test]
    public async Task ImperativeStreamLoopFailsWhenNotLooped()
    {
        InvalidOperationException? actual = null;

        try
        {
            // ReSharper disable once ObjectCreationAsStatement
            Transaction.RunVoid(static () => new StreamLoop<int>());
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("Loop was not looped.");
    }

    [Test]
    public async Task ImperativeStreamLoopFailsWhenLoopedInSeparateTransaction()
    {
        InvalidOperationException? actual = null;

        StreamLoop<int>? l = null;

        ManualResetEvent waitHandle = new(false);

        new Thread(() =>
            Transaction.RunVoid(() =>
            {
                l = new StreamLoop<int>();
                waitHandle.Set();
                Thread.Sleep(500);
            })).Start();

        waitHandle.WaitOne();

        try
        {
            StreamSink<int> s = Stream.CreateSink<int>();

            Transaction.RunVoid(() =>
            {
                Thread.Sleep(250);
                // ReSharper disable once NullableWarningSuppressionIsUsed - l will be non-null because it is set before
                // the waitHandle is signaled.
                Stream<int> resultLocal = s.Snapshot(c: l!.Hold(0), f: static (n, o) => n + o);
                // ReSharper disable once NullableWarningSuppressionIsUsed - l will be non-null because it is set before
                // the waitHandle is signaled.
                l!.Loop(resultLocal);
            });
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        Thread.Sleep(500);

        await Assert.That(actual).IsNotNull();

        await Assert.That(actual?.Message).IsEqualTo("Loop must be looped in the same transaction that it was created in.");
    }

    [Test]
    public async Task FunctionalStreamLoop()
    {
        StreamSink<int> s = Stream.CreateSink<int>();

        Stream<int> result =
            Stream.Loop<int>().WithoutCaptures(l => s.Snapshot(c: l.Hold(0), f: static (n, o) => n + o));

        List<int> @out = [];

        using (result.ListenStrong(@out.Add))
        {
            s.Send(1);
            s.Send(2);
            s.Send(3);
        }

        await Assert.That(@out).IsEquivalentTo(new[] { 1, 3, 6 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task FunctionalStreamLoopWithCaptures()
    {
        StreamSink<int> s = Stream.CreateSink<int>();

        (Stream<int> result, Stream<int> s2) =
            Stream.Loop<int>()
                .WithCaptures(l =>
                    (Stream: s.Snapshot(c: l.Hold(0), f: static (n, o) => n + o), Captures: s.Map(static v => 2 * v)));

        List<int> @out = [];
        List<int> out2 = [];

        using (result.ListenStrong(@out.Add))
        using (s2.ListenStrong(out2.Add))

        {
            s.Send(1);
            s.Send(2);
            s.Send(3);
        }

        await Assert.That(@out).IsEquivalentTo(new[] { 1, 3, 6 }, CollectionOrdering.Matching);
        await Assert.That(out2).IsEquivalentTo(new[] { 2, 4, 6 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ImperativeBehaviorLoop()
    {
        BehaviorSink<int> s = Behavior.CreateSink(0);

        Behavior<int> result =
            Transaction.Run(() =>
            {
                BehaviorLoop<int> l = new();

                Behavior<int> resultLocal =
                    Operational.Updates(s).Snapshot(b: l, f: static (n, o) => n + o).Hold(0).AsBehavior();

                l.Loop(resultLocal);
                return resultLocal;
            });

        List<int> @out = [];

        using (Transaction.Run(() => Operational.Value(result).ListenStrong(@out.Add)))
        {
            s.Send(1);
            s.Send(2);
            s.Send(3);
        }

        await Assert.That(@out).IsEquivalentTo(new[] { 0, 1, 3, 6 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ImperativeBehaviorLoopFailsWhenLoopedTwice()
    {
        InvalidOperationException? actual = null;

        try
        {
            BehaviorSink<int> s = Behavior.CreateSink(0);

            Transaction.RunVoid(() =>
            {
                BehaviorLoop<int> l = new();

                Behavior<int> resultLocal =
                    Operational.Updates(s).Snapshot(b: l, f: static (n, o) => n + o).Hold(0).AsBehavior();

                l.Loop(resultLocal);
                l.Loop(resultLocal);
            });
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("Loop was looped more than once.");
    }

    [Test]
    public async Task ImperativeBehaviorLoopFailsWithoutTransaction()
    {
        InvalidOperationException? actual = null;

        try
        {
            // ReSharper disable once ObjectCreationAsStatement
            new BehaviorLoop<int>();
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("Loop must be created within an explicit transaction.");
    }

    [Test]
    public async Task ImperativeBehaviorLoopFailsWhenNotLooped()
    {
        InvalidOperationException? actual = null;

        try
        {
            // ReSharper disable once ObjectCreationAsStatement
            Transaction.RunVoid(static () => new BehaviorLoop<int>());
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("Loop was not looped.");
    }

    [Test]
    public async Task ImperativeBehaviorLoopFailsWhenLoopedInSeparateTransaction()
    {
        InvalidOperationException? actual = null;

        BehaviorLoop<int>? l = null;

        ManualResetEvent waitHandle = new(false);

        new Thread(() =>
            Transaction.RunVoid(() =>
            {
                l = new BehaviorLoop<int>();
                waitHandle.Set();
                Thread.Sleep(500);
            })).Start();

        waitHandle.WaitOne();

        try
        {
            BehaviorSink<int> s = Behavior.CreateSink(0);

            Transaction.RunVoid(() =>
            {
                Thread.Sleep(250);

                Behavior<int> resultLocal =
                    // ReSharper disable once NullableWarningSuppressionIsUsed - l will be non-null because it is set
                    // before the waitHandle is signaled.
                    Operational.Updates(s).Snapshot(b: l!, f: static (n, o) => n + o).Hold(0).AsBehavior();

                // ReSharper disable once NullableWarningSuppressionIsUsed - l will be non-null because it is set before
                // the waitHandle is signaled.
                l!.Loop(resultLocal);
            });
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        Thread.Sleep(500);

        await Assert.That(actual).IsNotNull();

        await Assert.That(actual?.Message).IsEqualTo("Loop must be looped in the same transaction that it was created in.");
    }

    [Test]
    public async Task FunctionalBehaviorLoop()
    {
        BehaviorSink<int> s = Behavior.CreateSink(0);

        Behavior<int> result =
            Behavior.Loop<int>()
                .WithoutCaptures(l =>
                    Operational.Updates(s).Snapshot(b: l, f: static (n, o) => n + o).Hold(0).AsBehavior());

        List<int> @out = [];

        using (Transaction.Run(() => Operational.Value(result).ListenStrong(@out.Add)))
        {
            s.Send(1);
            s.Send(2);
            s.Send(3);
        }

        await Assert.That(@out).IsEquivalentTo(new[] { 0, 1, 3, 6 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task FunctionalBehaviorLoopWithCaptures()
    {
        BehaviorSink<int> s = Behavior.CreateSink(0);

        (Behavior<int> result, Behavior<int> s2) =
            Behavior.Loop<int>()
                .WithCaptures(l =>
                    (Behavior: Operational.Updates(s).Snapshot(b: l, f: static (n, o) => n + o).Hold(0).AsBehavior(),
                        Captures: s.Map(static v => 2 * v)));

        List<int> @out = [];
        List<int> out2 = [];

        using (Transaction.Run(() => Operational.Value(result).ListenStrong(@out.Add)))
        using (Transaction.Run(() => Operational.Value(s2).ListenStrong(out2.Add)))

        {
            s.Send(1);
            s.Send(2);
            s.Send(3);
        }

        await Assert.That(@out).IsEquivalentTo(new[] { 0, 1, 3, 6 }, CollectionOrdering.Matching);
        await Assert.That(out2).IsEquivalentTo(new[] { 0, 2, 4, 6 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ImperativeCellLoop()
    {
        CellSink<int> s = Cell.CreateSink(0);

        Cell<int> result =
            Transaction.Run(() =>
            {
                CellLoop<int> l = new();
                Cell<int> resultLocal = s.Updates().Snapshot(c: l, f: static (n, o) => n + o).Hold(0);
                l.Loop(resultLocal);
                return resultLocal;
            });

        List<int> @out = [];

        using (Transaction.Run(() => result.Values().ListenStrong(@out.Add)))
        {
            s.Send(1);
            s.Send(2);
            s.Send(3);
        }

        await Assert.That(@out).IsEquivalentTo(new[] { 0, 1, 3, 6 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ImperativeCellLoopFailsWhenLoopedTwice()
    {
        InvalidOperationException? actual = null;

        try
        {
            CellSink<int> s = Cell.CreateSink(0);

            Transaction.RunVoid(() =>
            {
                CellLoop<int> l = new();
                Cell<int> resultLocal = s.Updates().Snapshot(c: l, f: static (n, o) => n + o).Hold(0);
                l.Loop(resultLocal);
                l.Loop(resultLocal);
            });
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("Loop was looped more than once.");
    }

    [Test]
    public async Task ImperativeCellLoopFailsWithoutTransaction()
    {
        InvalidOperationException? actual = null;

        try
        {
            // ReSharper disable once ObjectCreationAsStatement
            new CellLoop<int>();
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("Loop must be created within an explicit transaction.");
    }

    [Test]
    public async Task ImperativeCellLoopFailsWhenNotLooped()
    {
        InvalidOperationException? actual = null;

        try
        {
            // ReSharper disable once ObjectCreationAsStatement
            Transaction.RunVoid(static () => new CellLoop<int>());
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("Loop was not looped.");
    }

    [Test]
    public async Task ImperativeCellLoopFailsWhenLoopedInSeparateTransaction()
    {
        InvalidOperationException? actual = null;

        CellLoop<int>? l = null;

        ManualResetEvent waitHandle = new(false);

        new Thread(() =>
            Transaction.RunVoid(() =>
            {
                l = new CellLoop<int>();
                waitHandle.Set();
                Thread.Sleep(500);
            })).Start();

        waitHandle.WaitOne();

        try
        {
            CellSink<int> s = Cell.CreateSink(0);

            Transaction.RunVoid(() =>
            {
                Thread.Sleep(250);
                // ReSharper disable once NullableWarningSuppressionIsUsed - l will be non-null because it is set before
                // the waitHandle is signaled.
                Cell<int> resultLocal = s.Updates().Snapshot(c: l!, f: static (n, o) => n + o).Hold(0);
                // ReSharper disable once NullableWarningSuppressionIsUsed - l will be non-null because it is set before
                // the waitHandle is signaled.
                l!.Loop(resultLocal);
            });
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        Thread.Sleep(500);

        await Assert.That(actual).IsNotNull();

        await Assert.That(actual?.Message).IsEqualTo("Loop must be looped in the same transaction that it was created in.");
    }

    [Test]
    public async Task FunctionalCellLoop()
    {
        CellSink<int> s = Cell.CreateSink(0);

        Cell<int> result =
            Cell.Loop<int>().WithoutCaptures(l => s.Updates().Snapshot(c: l, f: static (n, o) => n + o).Hold(0));

        List<int> @out = [];

        using (Transaction.Run(() => result.ListenStrong(@out.Add)))
        {
            s.Send(1);
            s.Send(2);
            s.Send(3);
        }

        await Assert.That(@out).IsEquivalentTo(new[] { 0, 1, 3, 6 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task FunctionalCellLoopWithCaptures()
    {
        CellSink<int> s = Cell.CreateSink(0);

        (Cell<int> result, Cell<int> s2) =
            Cell.Loop<int>()
                .WithCaptures(l =>
                    (Cell: s.Updates().Snapshot(c: l, f: static (n, o) => n + o).Hold(0),
                        Captures: s.Map(static v => 2 * v)));

        List<int> @out = [];
        List<int> out2 = [];

        using (Transaction.Run(() => result.ListenStrong(@out.Add)))
        using (Transaction.Run(() => s2.ListenStrong(out2.Add)))

        {
            s.Send(1);
            s.Send(2);
            s.Send(3);
        }

        await Assert.That(@out).IsEquivalentTo(new[] { 0, 1, 3, 6 }, CollectionOrdering.Matching);
        await Assert.That(out2).IsEquivalentTo(new[] { 0, 2, 4, 6 }, CollectionOrdering.Matching);
    }

    // Desired behavior:
    //A list of items of type TestObject are held in a cell.  TestObject contains a cell of type int named Output, which
    //is calculated from other values. Any time a new TestObject is created, it will have the values for the cells from
    //which Output is calculated.  The sum of all Output values in the list should always be 50 or greater.
    // Internal rather than private, and the change is not cosmetic: NUnit only ran tests in public
    // and internal types, so while this was private its one test never ran at all. TUnit's generator
    // finds it and emits metadata for it, which does not compile against a private type - so the
    // choice was to let it run or to say plainly that it does not.
    internal sealed class DependencyCycleTest
    {
        // Switch over the sum of the Output cells in the list.
        // This won't work because we would need to recurse to keep the list correct when the sum is very low (only one
        // item can be added per transaction). The current implementation throws an exception stating that a dependency
        // cycle was detected, and I think this is the correct behavior.
        [Test]
        public async Task TestSwitchCLoop()
        {
            Exception? actual = null;

            try
            {
                CellStreamSink<IReadOnlyList<TestObject>> streamSink =
                    Cell.CreateStreamSink<IReadOnlyList<TestObject>>();

                Cell<IReadOnlyList<TestObject>> cell =
                    Transaction.Run(() =>
                    {
                        CellLoop<IReadOnlyList<TestObject>> cellLoop = new();

                        Cell<IReadOnlyList<TestObject>> cellLocal =
                            streamSink
                                .Map(static v => (Func<IReadOnlyList<TestObject>, IReadOnlyList<TestObject>>)(_ => v))
                                .Merge(
                                    s2: cellLoop
                                        .Map(static oo => oo.Select(static o => o.Output).Lift(static vv => vv.Sum()))
                                        .SwitchC()
                                        .Updates()
                                        .Filter(static sum => sum < 50)
                                        .MapTo(
                                            (Func<IReadOnlyList<TestObject>, IReadOnlyList<TestObject>>)(static v =>
                                            [
                                                .. v, new TestObject()
                                            ])),
                                    f: static (f, g) => v => g(f(v)))
                                .Snapshot(c: cellLoop, f: static (f, v) => f(v))
                                .Hold([.. Enumerable.Range(start: 1, count: 10).Select(static _ => new TestObject())]);

                        cellLoop.Loop(cellLocal);
                        return cellLocal;
                    });

                cell.Sample()[2].Input1.Send(1);
                cell.Sample()[1].Input1.Send(-20);
                streamSink.Send([]);
            }
            catch (AggregateException e)
            {
                actual =
                    e.InnerExceptions.FirstOrDefault(static ex => ex.Message == "A dependency cycle was detected.");
            }
            catch (Exception e)
            {
                actual = e;
            }

            await Assert.That(actual).IsNotNull();
            await Assert.That(actual?.Message).IsEqualTo("A dependency cycle was detected.");
        }

        // Switch over the sum of the Output cell value streams in the list.
        // This won't work both because we miss the first Values stream event when the list changes and also because we
        // would need to recurse to keep the list correct when the sum is very low (only one item can be added per
        // transaction).
        [Test]
        public async Task TestSwitchSValuesLoop()
        {
            CellStreamSink<IReadOnlyList<TestObject>> streamSink =
                Cell.CreateStreamSink<IReadOnlyList<TestObject>>();

            Cell<IReadOnlyList<TestObject>> cell =
                Transaction.Run(() =>
                {
                    CellLoop<IReadOnlyList<TestObject>> cellLoop = new();

                    Cell<IReadOnlyList<TestObject>> cellLocal =
                        streamSink.Map(static v => (Func<IReadOnlyList<TestObject>, IReadOnlyList<TestObject>>)(_ => v))
                            .Merge(
                                s2: cellLoop
                                    .Map(static oo =>
                                        oo.Select(static o => o.Output).Lift(static vv => vv.Sum()).Values())
                                    .SwitchS()
                                    .Filter(static sum => sum < 50)
                                    .MapTo(
                                        (Func<IReadOnlyList<TestObject>, IReadOnlyList<TestObject>>)(static v =>
                                        [
                                            .. v, new TestObject()
                                        ])),
                                f: static (f, g) => v => g(f(v)))
                            .Snapshot(c: cellLoop, f: static (f, v) => f(v))
                            .Hold([.. Enumerable.Range(start: 1, count: 10).Select(static _ => new TestObject())]);

                    cellLoop.Loop(cellLocal);
                    return cellLocal;
                });

            List<int> objectCounts = [-1];
            cell.ListenStrong(vv => objectCounts.Add(vv.Count));
            objectCounts.Add(-1);
            cell.Sample()[2].Input1.Send(1);
            objectCounts.Add(-1);
            cell.Sample()[1].Input1.Send(-20);
            objectCounts.Add(-1);
            streamSink.Send([]);
            objectCounts.Add(-1);

            // Ideal result, likely not achievable.
            //await Assert.That(objectCounts).IsEquivalentTo(new[] { -1, 10, -1, 11, -1, 15, -1, 10, -1 });

            // Glitchy result, also not returned by this method.
            //await Assert.That(objectCounts).IsEquivalentTo(new[] { -1, 10, -1, 11, -1, 12, 13, 14, 15, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, -1 });

            // Incorrect result we will see.
            await Assert.That(objectCounts).IsEquivalentTo(new[] { -1, 10, -1, 11, -1, 12, -1, 0, -1 });
        }

        // Switch over the sum of the Output cells in the list, deferring the firings from the Values stream.
        // This will work because it allows the Values to recurse by firing each step in a new transaction immediately
        // following the transaction for the previous step. The only drawback to this method is that each step of the
        // recursion is in a new transaction, so it exhibits "glitchy" behavior where the intermediate invalid states
        // are externally visible.
        [Test]
        public async Task TestSwitchCDeferredLoop()
        {
            CellStreamSink<IReadOnlyList<TestObject>> streamSink =
                Cell.CreateStreamSink<IReadOnlyList<TestObject>>();

            Cell<IReadOnlyList<TestObject>> cell =
                Transaction.Run(() =>
                {
                    CellLoop<IReadOnlyList<TestObject>> cellLoop = new();

                    Cell<IReadOnlyList<TestObject>> cellLocal =
                        streamSink
                            .OrElse(
                                Operational
                                    .Defer(
                                        cellLoop.Map(static oo =>
                                                oo.Select(static o => o.Output).Lift(static vv => vv.Sum()))
                                            .SwitchC()
                                            .Values())
                                    .Filter(static sum => sum < 50)
                                    .Snapshot(
                                        c: cellLoop,
                                        f: static (_, items) =>
                                            (IReadOnlyList<TestObject>)[.. items, new TestObject()]))
                            .Hold([.. Enumerable.Range(start: 1, count: 10).Select(static _ => new TestObject())]);

                    cellLoop.Loop(cellLocal);
                    return cellLocal;
                });

            List<int> objectCounts = [-1];
            cell.ListenStrong(vv => objectCounts.Add(vv.Count));
            objectCounts.Add(-1);
            cell.Sample()[2].Input1.Send(1);
            objectCounts.Add(-1);
            cell.Sample()[1].Input1.Send(-20);
            objectCounts.Add(-1);
            streamSink.Send([]);
            objectCounts.Add(-1);

            // Ideal result, likely not achievable.
            //await Assert.That(objectCounts).IsEquivalentTo(new[] { -1, 10, -1, 11, -1, 15, -1, 10, -1 });

            // Glitchy result, but correct otherwise.
            await Assert.That(objectCounts).IsEquivalentTo(new[] { -1, 10, -1, 11, -1, 12, 13, 14, 15, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, -1 });
        }

        // Switch over the sum of the Output cells in the list, deferring the firings from the Values stream, and use a
        // better API.
        // This is identical to the previous solution except that it uses a special version of SwitchC which defers the
        // Values stream firings. Using this API, we can better capture the intent of the SwitchC call and allow the
        // type system to eventually check for valid usages of the looped cell.
        // Note that when the types are modified, the SwitchCWithDeferredValues() call will actually become
        // SwitchC().DeferredValues() with SwitchC() on the cell loop returning a special type containing the
        // DeferredValues() and DeferredUpdates() methods.
        [Test]
        public async Task TestSwitchCDeferredLoopWithBetterApi()
        {
            CellStreamSink<IReadOnlyList<TestObject>> streamSink =
                Cell.CreateStreamSink<IReadOnlyList<TestObject>>();

            Cell<IReadOnlyList<TestObject>> cell =
                Transaction.Run(() =>
                {
                    CellLoop<IReadOnlyList<TestObject>> cellLoop = new();

                    Cell<IReadOnlyList<TestObject>> cellLocal =
                        streamSink
                            .OrElse(
                                cellLoop.Map(static oo => oo.Select(static o => o.Output).Lift(static vv => vv.Sum()))
                                    .SwitchCWithDeferredValues()
                                    .Filter(static sum => sum < 50)
                                    .Snapshot(
                                        c: cellLoop,
                                        f: static (_, items) =>
                                            (IReadOnlyList<TestObject>)[.. items, new TestObject()]))
                            .Hold([.. Enumerable.Range(start: 1, count: 10).Select(static _ => new TestObject())]);

                    cellLoop.Loop(cellLocal);
                    return cellLocal;
                });

            List<int> objectCounts = [-1];
            cell.ListenStrong(vv => objectCounts.Add(vv.Count));
            objectCounts.Add(-1);
            cell.Sample()[2].Input1.Send(1);
            objectCounts.Add(-1);
            cell.Sample()[1].Input1.Send(-20);
            objectCounts.Add(-1);
            streamSink.Send([]);
            objectCounts.Add(-1);

            // Ideal result, likely not achievable.
            //await Assert.That(objectCounts).IsEquivalentTo(new[] { -1, 10, -1, 11, -1, 15, -1, 10, -1 });

            // Glitchy result, but correct otherwise.
            await Assert.That(objectCounts).IsEquivalentTo(new[] { -1, 10, -1, 11, -1, 12, 13, 14, 15, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, -1 });
        }

        private sealed class TestObject
        {
            public TestObject()
            {
                this.Input1 = Cell.CreateStreamSink<int>();
                Cell<int> input1Cell = this.Input1.Hold(3);

                this.Input2 = Cell.CreateStreamSink<int>();
                Cell<int> input2Cell = this.Input2.Hold(2);

                this.Output = input1Cell.Lift(c2: input2Cell, f: static (i1, i2) => i1 + i2);
            }

            public StreamSink<int> Input1 { get; }
            private StreamSink<int> Input2 { get; }
            public Cell<int> Output { get; }
        }
    }
}

file static class TestExtensions
{
    public static Stream<T> SwitchCWithDeferredValues<T>(this Cell<Cell<T>> cca) =>
        Operational.Defer(cca.SwitchC().Values());
}
