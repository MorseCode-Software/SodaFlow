using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace SodaFlow.Benchmarks;

/// <summary>
///     The cost of a derived node that links to its inputs only while something
///     listens to it.
/// </summary>
/// <remarks>
///     <para>
///         A bind and an unbind is a listen on the updates of a cell or on a
///         stream, a dispose, and an empty transaction. An unlisten keeps the
///         links until an input fires with no listener.
///     </para>
///     <para>
///         The setup listens to the unobserved maps and releases them. The first
///         send unlinks them, thus the next sends run none of them. The observed
///         maps keep their listeners. A sample of an unlinked map reads its source,
///         and keeps the value for the version of the source.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See ListenOnceBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class DemandLinkBenchmarks
{
    private const int Width = 100;

    private static readonly Action<int> Handler = static _ =>
    {
    };

    private static readonly Action Nothing = static () =>
    {
    };

    private readonly CellSink<int> mapSource = Cell.CreateSink(0);
    private readonly CellSink<int> liftLeft = Cell.CreateSink(0);
    private readonly CellSink<int> liftRight = Cell.CreateSink(0);
    private readonly StreamSink<int> mergeLeft = Stream.CreateSink<int>();
    private readonly StreamSink<int> mergeRight = Stream.CreateSink<int>();
    private readonly CellSink<int> unobserved = Cell.CreateSink(0);
    private readonly CellSink<int> observed = Cell.CreateSink(0);
    private readonly CellSink<int> sampled = Cell.CreateSink(0);
    private readonly List<Cell<int>> kept = [];
    private readonly List<IListener> listeners = [];

    private Cell<int> map = Cell.Constant(0);
    private Cell<int> lift = Cell.Constant(0);
    private Stream<int> merge = Stream.Never<int>();
    private Cell<int> sampledMap = Cell.Constant(0);
    private int value;

    /// <summary>Builds the graphs, and binds and releases the unobserved maps.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.map = this.mapSource.Map(static x => x + 1);
        this.lift = this.liftLeft.Lift(c2: this.liftRight, f: static (x, y) => x + y);
        this.merge = this.mergeLeft.OrElse(this.mergeRight);

        for (int i = 0; i < Width; i++)
        {
            int offset = i;
            Cell<int> released = this.unobserved.Map(x => x + offset);
            this.kept.Add(released);
            released.Updates().Listen(Handler).Dispose();

            this.listeners.Add(this.observed.Map(x => x + offset).Updates().Listen(Handler));
        }

        this.sampledMap = this.sampled.Map(static x => x * 2);
        this.sampledMap.Updates().Listen(Handler).Dispose();
        Transaction.RunVoid(Nothing);
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
        GC.KeepAlive(this.kept);
    }

    /// <summary>Binds and unbinds the updates of a map of a cell.</summary>
    [Benchmark(Baseline = true, Description = "bind and unbind a map")]
    public void BindAndUnbindAMap()
    {
        this.map.Updates().Listen(Handler).Dispose();
        Transaction.RunVoid(Nothing);
    }

    /// <summary>Binds and unbinds the updates of a lift of two cells.</summary>
    [Benchmark(Description = "bind and unbind a lift")]
    public void BindAndUnbindALift()
    {
        this.lift.Updates().Listen(Handler).Dispose();
        Transaction.RunVoid(Nothing);
    }

    /// <summary>Binds and unbinds the updates of a sink.</summary>
    [Benchmark(Description = "bind and unbind a sink")]
    public void BindAndUnbindASink()
    {
        this.mapSource.Updates().Listen(Handler).Dispose();
        Transaction.RunVoid(Nothing);
    }

    /// <summary>
    ///     Sends into the source of a map, then binds and unbinds the map. A source
    ///     that fires while the map has no listener unlinks the map each time.
    /// </summary>
    [Benchmark(Description = "send, then bind and unbind a map")]
    public void SendThenBindAndUnbindAMap()
    {
        this.mapSource.Send(++this.value);
        this.map.Updates().Listen(Handler).Dispose();
        Transaction.RunVoid(Nothing);
    }

    /// <summary>Binds and unbinds a merge of two streams.</summary>
    [Benchmark(Description = "bind and unbind a merge")]
    public void BindAndUnbindAMerge()
    {
        this.merge.Listen(Handler).Dispose();
        Transaction.RunVoid(Nothing);
    }

    /// <summary>Sends into a cell with a hundred maps that were bound and then released.</summary>
    [Benchmark(Description = "send to 100 released maps")]
    public void SendToReleasedMaps() => this.unobserved.Send(++this.value);

    /// <summary>Sends into a cell with a hundred maps, each with a listener on its updates.</summary>
    [Benchmark(Description = "send to 100 observed maps")]
    public void SendToObservedMaps() => this.observed.Send(++this.value);

    /// <summary>Samples a released map whose source did not change.</summary>
    /// <returns>The value of the map.</returns>
    [Benchmark(Description = "sample a released map")]
    public int SampleAReleasedMap() => this.sampledMap.Sample();

    /// <summary>Sends into the source of a released map, then samples the map.</summary>
    /// <returns>The value of the map.</returns>
    [Benchmark(Description = "send, then sample a released map")]
    public int SendThenSampleAReleasedMap()
    {
        this.sampled.Send(++this.value);
        return this.sampledMap.Sample();
    }
}
