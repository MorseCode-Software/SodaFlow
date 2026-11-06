using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SodaFlow.Collections;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to build a graph: one row for each common shape, over a count
///     of nodes.
/// </summary>
/// <remarks>
///     <para>
///         The other benchmarks measure a send, a sample, or a view change through
///         a graph that the setup built. This one measures the setup. A view model
///         builds its graph one time. Thus, the cost shows at the start of a screen,
///         and in a test suite that builds many graphs.
///     </para>
///     <para>
///         Each arm builds <see cref="Count" /> nodes in one transaction, which is
///         how a view model builds. It returns the nodes so that the runtime keeps
///         the work. The figures are for the full build; divide by the count for
///         one node. The constant and the sink arms open no transaction, because
///         neither needs one.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class GraphBuildBenchmarks
{
    /// <summary>How many nodes each arm builds.</summary>
    private const int Count = 1_000;

    /// <summary>A chain of maps, each on the one before it.</summary>
    [Benchmark(Description = "chain of maps", Baseline = true)]
    public Stream<int> ChainOfMaps()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();

        return Transaction.Run(() =>
        {
            Stream<int> s = sink;

            for (int i = 0; i < Count; i++)
            {
                s = s.Map(static v => v + 1);
            }

            return s;
        });
    }

    /// <summary>Maps that all listen to one sink.</summary>
    [Benchmark(Description = "fan-out of maps from one sink")]
    public List<Stream<int>> FanOutOfMaps()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<Stream<int>> keep = new(Count);

        Transaction.RunVoid(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                keep.Add(sink.Map(static v => v + 1));
            }
        });

        return keep;
    }

    /// <summary>A chain of maps, with a hold on each.</summary>
    [Benchmark(Description = "map and hold, in a chain")]
    public List<Cell<int>> MapAndHold()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<Cell<int>> keep = new(Count);

        Transaction.RunVoid(() =>
        {
            Stream<int> s = sink;

            for (int i = 0; i < Count; i++)
            {
                s = s.Map(static v => v + 1);
                keep.Add(s.Hold(0));
            }
        });

        return keep;
    }

    /// <summary>Lifts of the same two cells.</summary>
    [Benchmark(Description = "lift of two cells")]
    public List<Cell<int>> LiftOfTwo()
    {
        CellSink<int> a = Cell.CreateSink(1);
        CellSink<int> b = Cell.CreateSink(2);
        List<Cell<int>> keep = new(Count);

        Transaction.RunVoid(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                keep.Add(a.Lift(c2: b, f: static (x, y) => x + y));
            }
        });

        return keep;
    }

    /// <summary>Lifts of the same six cells.</summary>
    [Benchmark(Description = "lift of six cells")]
    public List<Cell<int>> LiftOfSix()
    {
        CellSink<int> a = Cell.CreateSink(1);
        CellSink<int> b = Cell.CreateSink(2);
        CellSink<int> c = Cell.CreateSink(3);
        CellSink<int> d = Cell.CreateSink(4);
        CellSink<int> e = Cell.CreateSink(5);
        CellSink<int> f = Cell.CreateSink(6);
        List<Cell<int>> keep = new(Count);

        Transaction.RunVoid(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                keep.Add(
                    a.Lift(
                        c2: b,
                        c3: c,
                        c4: d,
                        c5: e,
                        c6: f,
                        f: static (v1, v2, v3, v4, v5, v6) => v1 + v2 + v3 + v4 + v5 + v6));
            }
        });

        return keep;
    }

    /// <summary>Maps of one cell.</summary>
    [Benchmark(Description = "map of one cell")]
    public List<Cell<int>> MapOfOneCell()
    {
        CellSink<int> a = Cell.CreateSink(1);
        List<Cell<int>> keep = new(Count);

        Transaction.RunVoid(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                keep.Add(a.Map(static v => v + 1));
            }
        });

        return keep;
    }

    /// <summary>Accumulators on one sink.</summary>
    [Benchmark(Description = "accum on one stream")]
    public List<Cell<int>> AccumOnOneStream()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<Cell<int>> keep = new(Count);

        Transaction.RunVoid(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                keep.Add(sink.Accum(initialState: 0, f: static (v, total) => total + v));
            }
        });

        return keep;
    }

    /// <summary>Merges of the same two sinks.</summary>
    [Benchmark(Description = "merge of two streams")]
    public List<Stream<int>> MergeOfTwoStreams()
    {
        StreamSink<int> a = Stream.CreateSink<int>();
        StreamSink<int> b = Stream.CreateSink<int>();
        List<Stream<int>> keep = new(Count);

        Transaction.RunVoid(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                keep.Add(a.OrElse(b));
            }
        });

        return keep;
    }

    /// <summary>Switches over a constant cell that holds one cell.</summary>
    [Benchmark(Description = "switch of a constant cell of one cell")]
    public List<Cell<int>> SwitchOfAConstantCell()
    {
        CellSink<int> inner = Cell.CreateSink(1);
        List<Cell<int>> keep = new(Count);

        Transaction.RunVoid(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                keep.Add(Cell.Constant<Cell<int>>(inner).SwitchC());
            }
        });

        return keep;
    }

    /// <summary>Filter views of one collection of ten items.</summary>
    /// <remarks>
    ///     The item types are internal to this assembly, thus the public arm returns
    ///     the views as objects.
    /// </remarks>
    [Benchmark(Description = "filter view of a collection of ten")]
    public List<object> FilterViewOfACollection()
    {
        List<Item<ItemIdentity, ItemState>> items = new(10);

        for (int number = 0; number < 10; number++)
        {
            items.Add(
                new Item<ItemIdentity, ItemState>(
                    identity: ItemSeed.Identity(number),
                    state: ItemSeed.State(number)));
        }

        List<object> keep = new(Count);

        Transaction.RunVoid(() =>
        {
            ReactiveCollection<int, ItemIdentity, ItemState> collection =
                ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    keySelector: static identity => identity.Number,
                    initialItems: items,
                    Stream.Never<CollectionEdit<int, ItemIdentity, ItemState>>());

            for (int i = 0; i < Count; i++)
            {
                keep.Add(collection.Filter(static (_, state) => state.Score > 5));
            }
        });

        return keep;
    }

    /// <summary>Strong listeners on one sink.</summary>
    [Benchmark(Description = "ListenStrong on one stream")]
    public List<IListener> ListenStrongOnOneStream()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<IListener> keep = new(Count);

        Transaction.RunVoid(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                keep.Add(
                    sink.ListenStrong(static _ =>
                    {
                    }));
            }
        });

        return keep;
    }

    /// <summary>Strong listeners on one cell.</summary>
    [Benchmark(Description = "ListenStrong on one cell")]
    public List<IListener> ListenStrongOnOneCell()
    {
        CellSink<int> a = Cell.CreateSink(1);
        List<IListener> keep = new(Count);

        Transaction.RunVoid(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                keep.Add(
                    a.ListenStrong(static _ =>
                    {
                    }));
            }
        });

        return keep;
    }

    /// <summary>Constant cells.</summary>
    [Benchmark(Description = "constant cell")]
    public List<Cell<int>> ConstantCell()
    {
        List<Cell<int>> keep = new(Count);

        for (int i = 0; i < Count; i++)
        {
            keep.Add(Cell.Constant(i));
        }

        return keep;
    }

    /// <summary>Sinks alone.</summary>
    [Benchmark(Description = "sink")]
    public List<StreamSink<int>> Sink()
    {
        List<StreamSink<int>> keep = new(Count);

        for (int i = 0; i < Count; i++)
        {
            keep.Add(Stream.CreateSink<int>());
        }

        return keep;
    }
}
