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
///         This is a criteria change. <see cref="KeyedCollectionViewBenchmarks" /> reports it as the
///         condition a chain loses. A change to a predicate rebuilds that stage, and the rebuild of a filter
///         files each surviving key into a new ordered set. Thus, it is Θ(n) with an allocation for each
///         node, where a re-derivation sorts an array.
///     </para>
///     <para>
///         A slice is the exception, and that is what this measures. Its rebuild makes a <c>RangeKeys</c>
///         over the ordering that the stage above it holds. That is a lazy view with a start and an end,
///         which costs nothing to build, at each size of the collection. The ordering is not touched, because
///         an offset cannot reorder anything. Re-deriving the same page has to sort the collection again to
///         find out what is in it.
///     </para>
///     <para>
///         The page turned to alternates between the first and the second, which is what a reader clicking
///         through does. The two shapes hold the same page of the same ordering and the setup checks that
///         before it times the two.
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
    private ChainedPageShape chained = ChainedPageShape.Build(1);
    private int editCount;

    private int pageCount;

    // Populated fully in the setup. built small here so the fields never have to be nullable.
    private RederivedPageShape rederived = RederivedPageShape.Build(1);

    /// <summary>How many items the collection holds.</summary>
    [Params(1_000, 10_000, 100_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>
    ///     A key the second page holds. The seed scores each item with its own number, and the sort
    ///     is descending. Thus, position p holds key <c>ItemCount - 1 - p</c>, and the second page
    ///     runs from <c>ItemCount - 21</c> down to <c>ItemCount - 40</c>. This sits in the middle of
    ///     it.
    /// </summary>
    private int InPageKey => this.ItemCount - 30;

    /// <summary>
    ///     The lowest-scoring key, which sorts last and so is as far out of the second page as a
    ///     key can be.
    /// </summary>
    private static int OutsidePageKey => 0;

    /// <summary>Builds the two shapes, and refuses to run if they disagree about the page.</summary>
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

        // And again after a turn. The first page is the one condition where an offset of zero can
        // hide an off-by-one in one of the two directions.
        this.rederived.TurnTo(ViewSeed.Limit);
        this.chained.TurnTo(ViewSeed.Limit);

        if (!this.rederived.Keys.SequenceEqual(this.chained.Keys))
        {
            throw new InvalidOperationException(
                "The two shapes disagree about the second page. Re-derived: "
                + $"[{Describe(this.rederived.Keys)}]. Chained: [{Describe(this.chained.Keys)}].");
        }

        // Left on the second page and not the first. Thus, the edit benchmarks below use a non-zero
        // offset, which is the primary point of a slice, and the condition that a <c>Take</c> cannot
        // replace.

        // The edit arms are dependent on one key in that page and the other out of it. That is
        // arithmetic on the seed, and not something the code enforces. Checked, because an
        // in-page key that fell out of the window measures the cheap path below the
        // expensive path's name.
        if (!this.chained.Keys.Contains(this.InPageKey))
        {
            throw new InvalidOperationException(
                $"Key {this.InPageKey} was meant to be inside the second page, which holds "
                + $"[{Describe(this.chained.Keys)}].");
        }

        if (this.chained.Keys.Contains(OutsidePageKey))
        {
            throw new InvalidOperationException(
                $"Key {OutsidePageKey} was meant to be outside the second page, which holds "
                + $"[{Describe(this.chained.Keys)}].");
        }
    }

    /// <summary>Turning the page by re-sorting the collection and re-windowing it.</summary>
    [Benchmark(Description = "turn the page, re-derived", Baseline = true)]
    public void TurnRederived() => this.rederived.TurnTo(this.NextOffset());

    /// <summary>The same turn, by moving a slice's offset.</summary>
    [Benchmark(Description = "turn the page, chained")]
    public void TurnChained() => this.chained.TurnTo(this.NextOffset());

    /// <summary>An edit to an item the page holds, which the window has to forward.</summary>
    [Benchmark(Description = "edit an item in the page, re-derived")]
    public void EditInPageRederived() =>
        this.rederived.Replace(key: this.InPageKey, state: this.NextStateFor(this.InPageKey));

    /// <summary>The same edit, through the slice.</summary>
    [Benchmark(Description = "edit an item in the page, chained")]
    public void EditInPageChained() =>
        this.chained.Replace(key: this.InPageKey, state: this.NextStateFor(this.InPageKey));

    /// <summary>
    ///     An edit to an item the page does not hold, which the window has to conclude changes
    ///     nothing it shows.
    /// </summary>
    [Benchmark(Description = "edit an item outside the page, re-derived")]
    public void EditOutsidePageRederived() =>
        this.rederived.Replace(key: OutsidePageKey, state: this.NextStateFor(OutsidePageKey));

    /// <summary>The same edit, through the slice.</summary>
    [Benchmark(Description = "edit an item outside the page, chained")]
    public void EditOutsidePageChained() =>
        this.chained.Replace(key: OutsidePageKey, state: this.NextStateFor(OutsidePageKey));

    private static string Describe(IEnumerable<int> keys) => string.Join(separator: ", ", values: keys);

    /// <summary>
    ///     The first page and the second, alternating. A page that only advances goes over
    ///     the end of the collection partway through the benchmark and start measuring a window with
    ///     nothing in it.
    /// </summary>
    private int NextOffset()
    {
        this.pageCount++;

        return ViewSeed.Limit * (this.pageCount % 2);
    }

    /// <summary>
    ///     A new state for a key that leaves its sort value alone. Thus, nothing can move, and this
    ///     measures the cost of the window for each edit, and not the cost to move the key again.
    /// </summary>
    /// <remarks>
    ///     Only the name changes. An edit that moved the item is a different question, measured by
    ///     <see cref="KeyedCollectionViewBenchmarks" />. A mix of the two here leaves the two arms
    ///     with visibly different quantities of work. That changes with the position of the item, and
    ///     the compare stops to be about the window.
    /// </remarks>
    private ItemState NextStateFor(int key)
    {
        this.editCount++;

        return new ItemState(
            name: this.editCount % 2 == 0 ? "edited" : "re-edited",
            score: key,
            isFrozen: false);
    }
}
