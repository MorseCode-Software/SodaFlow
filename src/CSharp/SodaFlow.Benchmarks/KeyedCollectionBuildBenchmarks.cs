using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to build a large keyed collection and bind a screenful of it, in each of the three
///     shapes.
/// </summary>
/// <remarks>
///     <para>
///         This is the half of the compare where the collection must win outright, and the cause of it. A
///         cell per mutable value per object is <c>ItemCount × 3</c> graph nodes that the code builds, and no
///         code has to read them. A reactive collection is one graph plus a hash array mapped trie, and a
///         cell only for the <see cref="ObserverCount" /> keys somebody asked about.
///     </para>
///     <para>
///         Read the allocation column as carefully as the time column. The two measure the same thing here,
///         which is the count of nodes. The allocation is the one that keeps a cost after the benchmark ends.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class KeyedCollectionBuildBenchmarks
{
    /// <summary>
    ///     How many rows a screenful is. Fixed rather than a parameter: the point of the collection
    ///     is that this number and <see cref="ItemCount" /> do not change together. A change to the two says
    ///     that less clearly than a constant value for one of them.
    /// </summary>
    private const int ObserverCount = 20;

    /// <summary>How many items the collection holds.</summary>
    [Params(1_000, 10_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>A cell sink per mutable value on each object, built up front.</summary>
    [Benchmark(Description = "build, sinks per field", Baseline = true)]
    public void BuildSinkPerField() =>
        Observe(shape: SinkPerFieldShape.Build(this.ItemCount), itemCount: this.ItemCount);

    /// <summary>The same cells, wired to one edit stream, and not poked directly.</summary>
    [Benchmark(Description = "build, cells per field from a stream")]
    public void BuildStreamFedCells() =>
        Observe(shape: StreamFedCellShape.Build(this.ItemCount), itemCount: this.ItemCount);

    /// <summary>One graph and a trie, with cells only for the keys observed.</summary>
    [Benchmark(Description = "build, reactive collection")]
    public void BuildReactiveCollection() =>
        Observe(shape: ReactiveCollectionShape.Build(this.ItemCount), itemCount: this.ItemCount);

    /// <summary>
    ///     Binds a screenful and then releases it. Thus, this measures the construction of the collection
    ///     and everything a view attaches to it, and nothing is left listening afterwards.
    /// </summary>
    private static void Observe(IKeyedCollectionShape shape, int itemCount)
    {
        List<IListener> listeners =
        [
            .. ItemSeed.ObservedKeys(itemCount: itemCount, observerCount: ObserverCount).Select(shape.Observe)
        ];

        foreach (IListener listener in listeners)
        {
            listener.Unlisten();
        }
    }
}
