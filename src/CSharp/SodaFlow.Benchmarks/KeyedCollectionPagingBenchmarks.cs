using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to turn a page of a sorted collection, re-derived against chained.
/// </summary>
/// <remarks>
///     <para>
///         This is a criteria change, which <see cref="KeyedCollectionViewBenchmarks" /> reports as
///         the case a chain loses: changing a predicate rebuilds that stage, and a filter's rebuild
///         files every surviving key into a fresh ordered set, so it is Θ(n) with an allocation per
///         node where re-deriving sorts an array.
///     </para>
///     <para>
///         A slice is the exception, and that is what this measures. Its rebuild constructs a
///         <c>RangeKeys</c> over the ordering the stage above it already holds - a lazy view with a
///         start and an end, which costs nothing to build however large the collection is. The
///         ordering is not touched, because an offset cannot reorder anything. Re-deriving the same
///         page has to sort the collection again to find out what is in it.
///     </para>
///     <para>
///         The page turned to alternates between the first and the second, which is what a reader
///         clicking through does. Both shapes hold the same page of the same ordering and the setup
///         checks that before either is timed.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class KeyedCollectionPagingBenchmarks
{
    // Populated for real in the setup; built small here so the fields never have to be nullable.
    private IKeyedPagingShape rederived = RederivedPageShape.Build(1);
    private IKeyedPagingShape chained = ChainedPageShape.Build(1);

    private int pageCount;

    /// <summary>How many items the collection holds.</summary>
    [Params(1_000, 10_000, 100_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>Builds both shapes, and refuses to run if they disagree about the page.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.rederived = RederivedPageShape.Build(this.ItemCount);
        this.chained = ChainedPageShape.Build(this.ItemCount);

        if (!this.rederived.Keys.SequenceEqual(this.chained.Keys))
        {
            throw new InvalidOperationException(
                "The two shapes disagree about the first page, so timing them against each other "
                + $"would compare different work. Re-derived: [{Describe(this.rederived.Keys)}]. "
                + $"Chained: [{Describe(this.chained.Keys)}].");
        }

        // And again after a turn, because the first page is the one case where an offset of zero
        // could hide an off-by-one in either direction.
        this.rederived.TurnTo(ViewSeed.Limit);
        this.chained.TurnTo(ViewSeed.Limit);

        if (!this.rederived.Keys.SequenceEqual(this.chained.Keys))
        {
            throw new InvalidOperationException(
                "The two shapes disagree about the second page. Re-derived: "
                + $"[{Describe(this.rederived.Keys)}]. Chained: [{Describe(this.chained.Keys)}].");
        }

        this.rederived.TurnTo(0);
        this.chained.TurnTo(0);
    }

    /// <summary>Turning the page by re-sorting the collection and re-windowing it.</summary>
    [Benchmark(Description = "turn the page, re-derived", Baseline = true)]
    public void TurnRederived() => this.rederived.TurnTo(this.NextOffset());

    /// <summary>The same turn, by moving a slice's offset.</summary>
    [Benchmark(Description = "turn the page, chained")]
    public void TurnChained() => this.chained.TurnTo(this.NextOffset());

    private static string Describe(IEnumerable<int> keys) => string.Join(", ", keys);

    /// <summary>
    ///     The first page and the second, alternating. A page that only ever advanced would run off
    ///     the end of the collection partway through the benchmark and start measuring a window with
    ///     nothing in it.
    /// </summary>
    private int NextOffset()
    {
        this.pageCount++;

        return ViewSeed.Limit * (this.pageCount % 2);
    }
}
