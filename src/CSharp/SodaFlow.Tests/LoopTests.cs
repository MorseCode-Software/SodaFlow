using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public sealed class LoopTests
{
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

        await Assert.That(@out).IsEquivalentTo(expected: [1, 3, 6], ordering: CollectionOrdering.Matching);
    }

    // The loop closes onto a stream of higher rank than the chain, thus the close increases the rank of
    // each map in the chain. That walk used one stack frame for each node, and a chain of 10,000 maps
    // stopped the test process with a stack overflow. This chain is longer, so that a smaller stack
    // or a smaller frame does not hide a return of that walk.
    [Test]
    public async Task FunctionalStreamLoopOverALongChain()
    {
        const int length = 50_000;
        StreamSink<int> s = Stream.CreateSink<int>();

        (_, Stream<int> tail) =
            Stream.Loop<int>()
                .WithCaptures(l =>
                {
                    Stream<int> chain = l;

                    for (int i = 0; i < length; i++)
                    {
                        chain = chain.Map(static v => v + 1);
                    }

                    return (Stream: s.Map(static v => v), Captures: chain);
                });

        List<int> @out = [];

        using (tail.ListenStrong(@out.Add))
        {
            s.Send(0);
        }

        await Assert.That(@out).IsEquivalentTo(expected: [length], ordering: CollectionOrdering.Matching);
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

        await Assert.That(@out).IsEquivalentTo(expected: [1, 3, 6], ordering: CollectionOrdering.Matching);
        await Assert.That(out2).IsEquivalentTo(expected: [2, 4, 6], ordering: CollectionOrdering.Matching);
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

        await Assert.That(@out).IsEquivalentTo(expected: [0, 1, 3, 6], ordering: CollectionOrdering.Matching);
    }

    // A looped cell keeps its own loop stream, and is not a looped behavior. Thus, its message
    // names the cell.
    [Test]
    public async Task ALoopedCellSampledBeforeItsLoopClosesThrows()
    {
        Exception? actual = null;

        try
        {
#pragma warning disable SF1001 // The read before the close is the subject of this test.
            _ = Cell.Loop<int>().WithoutCaptures(static l => Cell.Constant(l.Sample()));
#pragma warning restore SF1001 // The read before the close is the subject of this test.
        }
        catch (Exception e)
        {
            actual = e;
        }

        await Assert.That(actual).IsTypeOf<InvalidOperationException>();
        await Assert.That(actual?.Message).IsEqualTo("LoopedCell was sampled before it was looped.");
    }

    [Test]
    public async Task ALoopedBehaviorSampledBeforeItsLoopClosesThrows()
    {
        Exception? actual = null;

        try
        {
#pragma warning disable SF1001 // The read before the close is the subject of this test.
            _ = Behavior.Loop<int>().WithoutCaptures(static l => Behavior.Constant(l.Sample()));
#pragma warning restore SF1001 // The read before the close is the subject of this test.
        }
        catch (Exception e)
        {
            actual = e;
        }

        await Assert.That(actual).IsTypeOf<InvalidOperationException>();
        await Assert.That(actual?.Message).IsEqualTo("LoopedBehavior was sampled before it was looped.");
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

        await Assert.That(@out).IsEquivalentTo(expected: [0, 1, 3, 6], ordering: CollectionOrdering.Matching);
        await Assert.That(out2).IsEquivalentTo(expected: [0, 2, 4, 6], ordering: CollectionOrdering.Matching);
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

        await Assert.That(@out).IsEquivalentTo(expected: [0, 1, 3, 6], ordering: CollectionOrdering.Matching);
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

        await Assert.That(@out).IsEquivalentTo(expected: [0, 1, 3, 6], ordering: CollectionOrdering.Matching);
        await Assert.That(out2).IsEquivalentTo(expected: [0, 2, 4, 6], ordering: CollectionOrdering.Matching);
    }

    // Desired behavior:
    //A list of items of type TestObject are held in a cell.  TestObject contains a cell of type int named Output, which
    //comes from other values. At each new TestObject, it has the values for the cells from
    //which gives Output. The sum of all Output values in the list must always be 50 or greater.
    // Public rather than private, and the change is not cosmetic: while this was private its one
    // test never ran, with NUnit or anything else. TUnit requires a public type and says so at
    // build time, thus the selection was to let the test run, or to remove it. It runs.
    public sealed class DependencyCycleTest
    {
        // Switch over the sum of the Output cells in the list.
        // This does not work. The code must recurse to keep the list correct when the sum is very low
        // (a caller can add only one item in each transaction). The current implementation throws an
        // exception that reports a dependency cycle, and that is the correct behavior.
        [Test]
        public async Task TestSwitchCLoop()
        {
            Exception? actual = null;

            try
            {
                CellStreamSink<IReadOnlyList<TestObject>> streamSink =
                    Cell.CreateStreamSink<IReadOnlyList<TestObject>>();

#pragma warning disable SF1002 // The cycle is the subject of this test.
                Cell<IReadOnlyList<TestObject>> cell =
                    Cell.Loop<IReadOnlyList<TestObject>>()
                        .WithoutCaptures(cellLoop =>
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
                                .Hold([.. Enumerable.Range(start: 1, count: 10).Select(static _ => new TestObject())]));
#pragma warning restore SF1002 // The cycle is the subject of this test.

                cell.Sample()[2].Input1.Send(1);
                cell.Sample()[1].Input1.Send(-20);
                streamSink.Send([]);
            }
            catch (AggregateException e)
            {
                actual = e.InnerExceptions.OfType<DependencyCycleException>().FirstOrDefault();
            }
            catch (Exception e)
            {
                actual = e;
            }

            await Assert.That(actual).IsTypeOf<DependencyCycleException>();
            await Assert.That(actual?.Message).IsEqualTo("A dependency cycle was detected.");
        }

        // Switch over the sum of the Output cell value streams in the list.
        // This does not work, because we miss the first Values stream event when the list changes.
        // The code must also recurse to keep the list correct when the sum is very low. A caller can
        // add only one item in each transaction.
        [Test]
        public async Task TestSwitchSValuesLoop()
        {
            CellStreamSink<IReadOnlyList<TestObject>> streamSink =
                Cell.CreateStreamSink<IReadOnlyList<TestObject>>();

            Cell<IReadOnlyList<TestObject>> cell =
                Cell.Loop<IReadOnlyList<TestObject>>()
                    .WithoutCaptures(cellLoop =>
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
                            .Hold([.. Enumerable.Range(start: 1, count: 10).Select(static _ => new TestObject())]));

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
            //await Assert.That(objectCounts).IsEquivalentTo(new[] { -1, 10, -1, 11, -1, 12, 13, 14, 15, -1,
            //0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, -1 });

            // Incorrect result we will see.
            await Assert.That(objectCounts).IsEquivalentTo([-1, 10, -1, 11, -1, 12, -1, 0, -1]);
        }

        // Switch over the sum of the Output cells in the list, deferring the firings from the Values stream.
        // This works, because it lets the Values recurse. It fires each step in a new transaction
        // immediately after the transaction of the previous step. The one drawback is that each step
        // of the recursion is in a new transaction. Thus, it shows "glitchy" behavior, where the
        // incorrect states in the middle are external.
        [Test]
        public async Task TestSwitchCDeferredLoop()
        {
            CellStreamSink<IReadOnlyList<TestObject>> streamSink =
                Cell.CreateStreamSink<IReadOnlyList<TestObject>>();

            Cell<IReadOnlyList<TestObject>> cell =
                Cell.Loop<IReadOnlyList<TestObject>>()
                    .WithoutCaptures(cellLoop =>
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
                            .Hold([.. Enumerable.Range(start: 1, count: 10).Select(static _ => new TestObject())]));

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
            await Assert.That(objectCounts)
                .IsEquivalentTo([-1, 10, -1, 11, -1, 12, 13, 14, 15, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, -1]);
        }

        // Switch over the sum of the Output cells in the list, deferring the firings from the Values stream, and use a
        // better API.
        // This is the same as the previous solution, but it uses a special version of SwitchC that
        // defers the Values stream firings. This API captures the intent of the SwitchC call better.
        // It also lets the type system check for correct uses of the looped cell.
        // When the types change, the SwitchCWithDeferredValues() call becomes
        // SwitchC().DeferredValues() with SwitchC() on the cell loop returning a special type containing the
        // DeferredValues() and DeferredUpdates() methods.
        [Test]
        public async Task TestSwitchCDeferredLoopWithBetterApi()
        {
            CellStreamSink<IReadOnlyList<TestObject>> streamSink =
                Cell.CreateStreamSink<IReadOnlyList<TestObject>>();

            Cell<IReadOnlyList<TestObject>> cell =
                Cell.Loop<IReadOnlyList<TestObject>>()
                    .WithoutCaptures(cellLoop =>
                        streamSink
                            .OrElse(
                                cellLoop.Map(static oo => oo.Select(static o => o.Output).Lift(static vv => vv.Sum()))
                                    .SwitchCWithDeferredValues()
                                    .Filter(static sum => sum < 50)
                                    .Snapshot(
                                        c: cellLoop,
                                        f: static (_, items) =>
                                            (IReadOnlyList<TestObject>)[.. items, new TestObject()]))
                            .Hold([.. Enumerable.Range(start: 1, count: 10).Select(static _ => new TestObject())]));

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
            await Assert.That(objectCounts)
                .IsEquivalentTo([-1, 10, -1, 11, -1, 12, 13, 14, 15, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, -1]);
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
