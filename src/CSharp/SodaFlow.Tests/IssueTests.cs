using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public sealed class IssueTests
{
    [Test]
    public async Task Issue151_PoolDoubleSubtraction_Broken()
    {
        Exception? actual = null;

        try
        {
            CellSink<int> threshold = Cell.CreateSink(10);
            StreamSink<int> addPoolSink = Stream.CreateSink<int>();

            Transaction.Run(() =>
            {
                StreamLoop<int> submitPooledAmount = new();

                // Ways that the pool is modified.
                Stream<Func<int, int>> poolAddByInput = addPoolSink.Map(static i => (Func<int, int>)(x => x + i));

                Stream<Func<int, int>>
                    poolRemoveByUsage = submitPooledAmount.Map(static i => (Func<int, int>)(x => x - i));

                // The current level of the pool
                Cell<int> poolLocal =
                    poolAddByInput
                        .Merge(s2: poolRemoveByUsage, f: static (f, g) => x => g(f(x)))
                        .Accum(initialState: 0, f: static (f, x) => f(x));

                // The current input changes combined with the pool as a stream
                Stream<int> inputByAdded =
                    poolAddByInput
                        .Snapshot(
                            c1: poolLocal,
                            c2: threshold,
                            f: static (f, x, t) =>
                                f(x) >= t
                                    ? Maybe.Some(f(x))
                                    : Maybe.None)
                        .FilterSome();

                // Simple rising edge on pool threshold satisfaction.
                Stream<int> inputBySatisfaction =
                    poolLocal.Updates()
                        .Snapshot(
                            c1: poolLocal,
                            c2: threshold,
                            f: static (neu, alt, t) =>
                                neu >= t && alt < t
                                    ? Maybe.Some(neu)
                                    : Maybe.None)
                        .FilterSome();

                submitPooledAmount.Loop(inputByAdded.Merge(s2: inputBySatisfaction, f: Math.Max));

                return (submitPooledAmount, poolLocal);
            });
        }
        catch (Exception e)
        {
            actual = e;
        }

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual?.Message).IsEqualTo("A dependency cycle was detected.");
    }

    [Test]
    public async Task Issue151_PoolDoubleSubtraction_Fixed()
    {
        CellSink<int> threshold = Cell.CreateSink(10);
        StreamSink<int> addPoolSink = Stream.CreateSink<int>();

        (Stream<int> input, Cell<int> pool) =
            Transaction.Run(() =>
            {
                StreamLoop<int> submitPooledAmount = new();

                // Ways that the pool is modified.
                Stream<Func<int, int>> poolAddByInput = addPoolSink.Map(static i => (Func<int, int>)(x => x + i));

                Stream<Func<int, int>> poolRemoveByUsage =
                    Operational.Defer(submitPooledAmount.Map(static i => (Func<int, int>)(x => x - i)));

                // The current level of the pool
                Cell<int> poolLocal =
                    poolAddByInput
                        .Merge(s2: poolRemoveByUsage, f: static (f, g) => x => g(f(x)))
                        .Accum(initialState: 0, f: static (f, x) => f(x));

                // The current input changes combined with the pool as a stream
                Stream<int> inputByAdded =
                    poolAddByInput
                        .Snapshot(
                            c1: poolLocal,
                            c2: threshold,
                            f: static (f, x, t) =>
                                f(x) >= t
                                    ? Maybe.Some(f(x))
                                    : Maybe.None)
                        .FilterSome();

                // Simple rising edge on pool threshold satisfaction.
                Stream<int> inputBySatisfaction =
                    poolLocal.Updates()
                        .Snapshot(
                            c1: poolLocal,
                            c2: threshold,
                            f: static (neu, alt, t) =>
                                neu >= t && alt < t
                                    ? Maybe.Some(neu)
                                    : Maybe.None)
                        .FilterSome();

                submitPooledAmount.Loop(inputByAdded.Merge(s2: inputBySatisfaction, f: Math.Max));

                return (submitPooledAmount, poolLocal);
            });

        List<int> submissions = [];

        using (input.ListenStrong(submissions.Add))
        {
            // Add amount which can be immediately used based on threshold.
            // Pool should remain zero after the transaction is complete.
            addPoolSink.Send(10);
        }

        await Assert.That(submissions.Count).IsEqualTo(1);
        await Assert.That(submissions[0]).IsEqualTo(10);
        await Assert.That(pool.Sample()).IsEqualTo(0);
    }
}
