using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SodaFlow.Functional;

namespace SodaFlow.Benchmarks;

/// <summary>
///     The cost of one send through each of several small graphs.
/// </summary>
/// <remarks>
///     The graphs are a sink with one listener, a fan-out of eight maps, a chain
///     of eight maps, a FilterSome, and a Choose. Each stream that fires reads the listeners of its node one time. These
///     benchmarks measure that read with no cell in the graph. The fan-out and
///     the chain each fire nine streams for one send. Thus, they show the cost
///     of each read more clearly than a lift does. The FilterSome and the Choose
///     are one node each. The cost of each above the sink with one listener is
///     the cost of that node.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See ListenOnceBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class StreamSendBenchmarks
{
    private const int Width = 8;

    private static readonly Action<int> Handler = static _ =>
    {
    };

    private readonly StreamSink<int> single = Stream.CreateSink<int>();
    private readonly StreamSink<int> fanOut = Stream.CreateSink<int>();
    private readonly StreamSink<int> chain = Stream.CreateSink<int>();
    private readonly StreamSink<Maybe<int>> filterSome = Stream.CreateSink<Maybe<int>>();
    private readonly StreamSink<int> choose = Stream.CreateSink<int>();
    private readonly List<IListener> listeners = [];

    private int value;

    /// <summary>Builds the three graphs, each with a listener on each last stream.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.listeners.Add(this.single.Listen(Handler));

        for (int i = 0; i < Width; i++)
        {
            int offset = i;
            this.listeners.Add(this.fanOut.Map(x => x + offset).Listen(Handler));
        }

        Stream<int> last = this.chain;

        for (int i = 0; i < Width; i++)
        {
            last = last.Map(static x => x + 1);
        }

        this.listeners.Add(last.Listen(Handler));
        this.listeners.Add(this.filterSome.FilterSome().Listen(Handler));
        this.listeners.Add(this.choose.Choose(static x => Maybe.SomeIf(condition: (x & 1) == 0, value: x)).Listen(Handler));
    }

    /// <summary>Removes the listeners.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (IListener listener in this.listeners)
        {
            listener.Dispose();
        }

        this.listeners.Clear();
    }

    /// <summary>Sends a value to a sink with one listener.</summary>
    [Benchmark(Baseline = true, Description = "send to a sink with one listener")]
    public void Single() => this.single.Send(++this.value);

    /// <summary>Sends a value to a sink with eight mapped streams, each with a listener.</summary>
    [Benchmark(Description = "send to a fan-out of eight maps")]
    public void FanOut() => this.fanOut.Send(++this.value);

    /// <summary>Sends a value through a chain of eight maps to one listener.</summary>
    [Benchmark(Description = "send through a chain of eight maps")]
    public void Chain() => this.chain.Send(++this.value);

    /// <summary>Sends a Maybe that holds a value through a FilterSome to one listener.</summary>
    [Benchmark(Description = "send through a FilterSome")]
    public void ThroughFilterSome() => this.filterSome.Send(Maybe.Some(++this.value));

    /// <summary>Sends a value through a Choose that gives a value for each second value.</summary>
    [Benchmark(Description = "send through a Choose, half of them chosen")]
    public void ThroughChoose() => this.choose.Send(++this.value);
}
