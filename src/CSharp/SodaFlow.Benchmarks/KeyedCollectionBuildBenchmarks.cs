using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to stand a large keyed collection up and bind a screenful of it, in each of
///     the three shapes.
/// </summary>
/// <remarks>
///     <para>
///         This is the half of the comparison where the collection is expected to win outright,
///         and the reason it exists. A cell per mutable value per object is
///         <c>ItemCount × 3</c> graph nodes built whether or not anything ever reads them; a
///         reactive collection is one graph plus a hash array mapped trie, and a cell only for the
///         <see cref="ObserverCount" /> keys somebody asked about.
///     </para>
///     <para>
///         Read the allocation column as carefully as the time column. The two are measuring the
///         same thing here — nodes that exist — and allocation is the one that keeps costing after
///         the benchmark ends.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net80)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class KeyedCollectionBuildBenchmarks
{
    /// <summary>
    ///     How many rows a screenful is. Fixed rather than a parameter: the point of the collection
    ///     is that this number and <see cref="ItemCount" /> are independent, and varying both would
    ///     say that less clearly than holding one still.
    /// </summary>
    private const int ObserverCount = 20;

    /// <summary>How many items the collection holds.</summary>
    [Params(1_000, 10_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>A cell sink per mutable value on every object, built up front.</summary>
    [Benchmark(Description = "build, sinks per field", Baseline = true)]
    public void BuildSinkPerField() => Observe(SinkPerFieldShape.Build(this.ItemCount), this.ItemCount);

    /// <summary>The same cells, wired to one edit stream instead of poked directly.</summary>
    [Benchmark(Description = "build, cells per field from a stream")]
    public void BuildStreamFedCells() => Observe(StreamFedCellShape.Build(this.ItemCount), this.ItemCount);

    /// <summary>One graph and a trie, with cells only for the keys observed.</summary>
    [Benchmark(Description = "build, reactive collection")]
    public void BuildReactiveCollection() =>
        Observe(ReactiveCollectionShape.Build(this.ItemCount), this.ItemCount);

    /// <summary>
    ///     Binds a screenful and then releases it, so what is measured is building the collection
    ///     and everything a view would attach to it, and nothing is left listening afterwards.
    /// </summary>
    private static void Observe(IKeyedCollectionShape shape, int itemCount)
    {
        List<IListener> listeners =
            [.. ItemSeed.ObservedKeys(itemCount, ObserverCount).Select(shape.Observe)];

        foreach (IListener listener in listeners)
        {
            listener.Unlisten();
        }
    }
}
