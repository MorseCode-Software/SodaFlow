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

public sealed class LoopTests
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

        await Assert.That(@out).IsEquivalentTo(expected: [1, 3, 6], ordering: CollectionOrdering.Matching);
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

        InvalidOperationException? actualOnOtherThread = null;

        Thread thread =
            new(() =>
            {
                try
                {
                    Transaction.RunVoid(() =>
                    {
                        l = new StreamLoop<int>();
                        waitHandle.Set();
                        Thread.Sleep(500);
                    });
                }
                catch (InvalidOperationException e)
                {
                    // This transaction makes the loop and never closes it, thus it must fail at its end.
                    // A catch here keeps it from the end of the test host, and lets the assertion below read it.
                    actualOnOtherThread = e;
                }
            });

        thread.Start();

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

        thread.Join();

        await Assert.That(actual).IsNotNull();

        await Assert.That(actual?.Message)
            .IsEqualTo("Loop must be looped in the same transaction that it was created in.");

        await Assert.That(actualOnOtherThread).IsNotNull();

        await Assert.That(actualOnOtherThread?.Message).IsEqualTo("Loop was not looped.");
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

        await Assert.That(@out).IsEquivalentTo(expected: [0, 1, 3, 6], ordering: CollectionOrdering.Matching);
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

        InvalidOperationException? actualOnOtherThread = null;

        Thread thread =
            new(() =>
            {
                try
                {
                    Transaction.RunVoid(() =>
                    {
                        l = new BehaviorLoop<int>();
                        waitHandle.Set();
                        Thread.Sleep(500);
                    });
                }
                catch (InvalidOperationException e)
                {
                    // This transaction makes the loop and never closes it, thus it must fail at its end.
                    // A catch here keeps it from the end of the test host, and lets the assertion below read it.
                    actualOnOtherThread = e;
                }
            });

        thread.Start();

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

        thread.Join();

        await Assert.That(actual).IsNotNull();

        await Assert.That(actual?.Message)
            .IsEqualTo("Loop must be looped in the same transaction that it was created in.");

        await Assert.That(actualOnOtherThread).IsNotNull();

        await Assert.That(actualOnOtherThread?.Message).IsEqualTo("Loop was not looped.");
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

        await Assert.That(@out).IsEquivalentTo(expected: [0, 1, 3, 6], ordering: CollectionOrdering.Matching);
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

        InvalidOperationException? actualOnOtherThread = null;

        Thread thread =
            new(() =>
            {
                try
                {
                    Transaction.RunVoid(() =>
                    {
                        l = new CellLoop<int>();
                        waitHandle.Set();
                        Thread.Sleep(500);
                    });
                }
                catch (InvalidOperationException e)
                {
                    // This transaction makes the loop and never closes it, thus it must fail at its end.
                    // A catch here keeps it from the end of the test host, and lets the assertion below read it.
                    actualOnOtherThread = e;
                }
            });

        thread.Start();

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

        thread.Join();

        await Assert.That(actual).IsNotNull();

        await Assert.That(actual?.Message)
            .IsEqualTo("Loop must be looped in the same transaction that it was created in.");

        await Assert.That(actualOnOtherThread).IsNotNull();

        await Assert.That(actualOnOtherThread?.Message).IsEqualTo("Loop was not looped.");
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

    [Test]
    public async Task FunctionalCellLoopClosesALoopedCaptureWithTheCloser()
    {
        CellSink<int> s = Cell.CreateSink(0);

        // The capture reads the placeholder, thus it is a LoopedCell in the block. The second
        // function runs when the loop is closed, and the closer it gets gives the capture back as a
        // Cell.
        (Cell<int> result, Cell<int> doubled) =
            Cell.Loop<int>()
                .WithCaptures(
                    f: l =>
                        (Cell: s.Updates().Snapshot(c: l, f: static (n, o) => n + o).Hold(0),
                            Captures: l.Map(static v => 2 * v)),
                    close: static (doubledLoop, closer) => closer.Close(doubledLoop));

        List<int> @out = [];

        using (Transaction.Run(() => doubled.ListenStrong(@out.Add)))
        {
            s.Send(1);
            s.Send(2);
            s.Send(3);
        }

        await Assert.That(result.Sample()).IsEqualTo(6);
        await Assert.That(@out).IsEquivalentTo(expected: [0, 2, 6, 12], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TheCloserConvertsEachLoopedMemberOfTheCaptures()
    {
        CellSink<int> s = Cell.CreateSink(0);

        (Cell<int> result, (Cell<int> plusOne, int tag)) =
            Cell.Loop<int>()
                .WithCaptures(
                    f: l =>
                        (Cell: s.Updates().Snapshot(c: l, f: static (n, o) => n + o).Hold(0),
                            Captures: (PlusOne: l.Map(static v => v + 1), Tag: 7)),
                    close: static (captures, closer) => (closer.Close(captures.PlusOne), captures.Tag));

        s.Send(5);

        await Assert.That(tag).IsEqualTo(7);
        await Assert.That(plusOne.Sample()).IsEqualTo(6);
        await Assert.That(result.Sample()).IsEqualTo(5);
    }

    [Test]
    public async Task TheCloseFunctionRunsAfterTheLoopIsClosed()
    {
        CellSink<int> s = Cell.CreateSink(3);

        // A read of the converted capture in the close function has an answer, which shows that
        // the loop is closed when the function runs.
        (Cell<int> result, int readInClose) =
            Cell.Loop<int>()
                .WithCaptures(
                    f: l =>
                        (Cell: s.Updates().Snapshot(c: l, f: static (n, o) => n + o).Hold(0),
                            Captures: l.Map(static v => v + 1)),
                    close: static (plusOne, closer) => closer.Close(plusOne).Sample());

        await Assert.That(readInClose).IsEqualTo(1);
        await Assert.That(result.Sample()).IsEqualTo(0);
    }

    [Test]
    public async Task TheCloserThrowsForACaptureOfALoopThatIsStillOpen()
    {
        InvalidOperationException? actual = null;

        try
        {
            // The inner loop is closed when its closer runs, but the capture also reads the outer
            // placeholder, which is not. The closer reads the value, thus the call throws.
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed - Testing for side effect only.
            Cell.Loop<int>()
                .WithoutCaptures(static outer =>
                    Cell.Loop<int>()
                        .WithCaptures(
                            f: inner =>
                                (Cell: Cell.Constant(1), Captures: inner.Lift(c2: outer, f: static (a, b) => a + b)),
                            close: static (sum, closer) => closer.Close(sum))
                        .Cell);
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        await Assert.That(actual?.Message).IsEqualTo("BehaviorLoop was sampled before it was looped.");
    }

    [Test]
    public async Task TheCloserOfTheLastLoopConvertsACaptureOfBoth()
    {
        // The same capture, converted with the closer of the outer loop, which is the last loop it
        // depends on.
        (Cell<int> outerResult, Cell<int> sum) =
            Cell.Loop<int>()
                .WithCaptures(
                    f: static outer =>
                    {
                        (Cell<int> innerResult, LoopedCell<int> sumLoop) =
                            Cell.Loop<int>()
                                .WithCaptures(inner =>
                                    (Cell: Cell.Constant(1), Captures: inner.Lift(c2: outer, f: static (a, b) => a + b)));

                        return (Cell: innerResult.Map(static v => v + 1), Captures: sumLoop);
                    },
                    close: static (sumLoop, closer) => closer.Close(sumLoop));

        await Assert.That(outerResult.Sample()).IsEqualTo(2);
        await Assert.That(sum.Sample()).IsEqualTo(3);
    }

    [Test]
    public async Task AsCellReadBeforeTheCloseThrows()
    {
        InvalidOperationException? actual = null;

        try
        {
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed - Testing for side effect only.
            Cell.Loop<int>().WithoutCaptures(static l => Cell.Constant(l.AsCell().Sample()));
        }
        catch (InvalidOperationException e)
        {
            actual = e;
        }

        await Assert.That(actual?.Message).IsEqualTo("BehaviorLoop was sampled before it was looped.");
    }

    [Test]
    public async Task AsCellReadAfterTheCloseGivesTheValue()
    {
        (Cell<int> result, (Cell<int> asserted, int tag)) =
            Cell.Loop<int>().WithCaptures(static l => (Cell: Cell.Constant(4), Captures: (l.AsCell(), 1)));

        await Assert.That(asserted.Sample()).IsEqualTo(4);
        await Assert.That(result.Sample()).IsEqualTo(4);
        await Assert.That(tag).IsEqualTo(1);
    }

    [Test]
    public async Task FunctionalBehaviorLoopClosesALoopedCaptureWithTheCloser()
    {
        BehaviorSink<int> s = Behavior.CreateSink(1);

        (Behavior<int> result, Behavior<int> doubled) =
            Behavior.Loop<int>()
                .WithCaptures(
                    f: l =>
                        (Behavior: Operational.Updates(s)
                                .Snapshot(b: l, f: static (n, o) => n + o)
                                .Hold(0)
                                .AsBehavior(),
                            Captures: l.Map(static v => 2 * v)),
                    close: static (doubledLoop, closer) => closer.Close(doubledLoop));

        s.Send(2);

        await Assert.That(result.Sample()).IsEqualTo(2);
        await Assert.That(doubled.Sample()).IsEqualTo(4);
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
    public static Stream<T> SwitchCWithDeferredValues<T>(this LoopedCell<Cell<T>> cca) =>
        Operational.Defer(cca.SwitchC().Values());
}
