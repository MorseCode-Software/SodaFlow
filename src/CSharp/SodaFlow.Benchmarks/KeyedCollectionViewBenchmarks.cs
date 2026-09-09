using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to keep a filtered, sorted, windowed view of a collection up to date as the
///     collection changes — re-derived in full each time, against a chain of stages that adjust.
/// </summary>
/// <remarks>
///     <para>
///         The view is the same in both: unfrozen items scoring at least a threshold, ordered by
///         score descending, the first twenty. Both are checked in the setup to be producing the
///         same keys before either is timed.
///     </para>
///     <para>
///         Three things are measured, and they do not all point the same way.
///     </para>
///     <para>
///         An <b>edit</b> is where the chain should win and win by more as the collection grows.
///         Re-deriving sorts everything that passed the filter, so it is O(n log n) in the
///         collection; the chain re-files one key in one sorted set, which is O(log n), and then
///         re-windows twenty.
///     </para>
///     <para>
///         An <b>add and remove</b> is the same story for structural change, and this benchmark is
///         what made it true. The identity map in <c>CollectionSnapshot</c> was a plain dictionary,
///         which can only produce its next version by being copied, so a structural edit was O(n)
///         however cheaply the stages below it absorbed the change — and it showed up here as a
///         chain that won by less as the collection grew rather than more. The map is a trie now,
///         and this is flat.
///     </para>
///     <para>
///         A <b>threshold change</b> is the case the chain loses. Changing a criteria rebuilds that
///         stage and every stage below it, and a rebuild files every surviving key into a fresh
///         ordered set — so where re-deriving sorts an array, the chain builds a persistent tree,
///         which costs an allocation per node where the sort costs none. Both are Θ(n), so parity
///         is the ceiling and this does not reach it. It is measured here precisely because it does
///         not flatter the design, and it is the number to point at when telling someone to
///         debounce a search box rather than filtering on every keystroke.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class KeyedCollectionViewBenchmarks
{
    // Populated for real in the setup; built small here so the fields never have to be nullable.
    private IKeyedCollectionViewShape rederived = RederivedViewShape.Build(1);
    private IKeyedCollectionViewShape chained = ChainedViewShape.Build(1);

    private int editCount;
    private int thresholdCount;

    /// <summary>How many items the collection holds.</summary>
    [Params(1_000, 10_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>
    ///     Builds both shapes, and refuses to run if they disagree about what the view contains.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.rederived = RederivedViewShape.Build(this.ItemCount);
        this.chained = ChainedViewShape.Build(this.ItemCount);

        if (!this.rederived.Keys.SequenceEqual(this.chained.Keys))
        {
            throw new InvalidOperationException(
                "The two shapes disagree about the view, so timing them against each other would "
                + $"compare different work. Re-derived: [{Describe(this.rederived.Keys)}]. "
                + $"Chained: [{Describe(this.chained.Keys)}].");
        }
    }

    /// <summary>One item's state changes, which may move it within the view or out of it.</summary>
    [Benchmark(Description = "edit an item, re-derived", Baseline = true)]
    public void EditRederived() => this.rederived.Replace(EditedKey, this.NextState());

    /// <summary>The same edit, through the chain.</summary>
    [Benchmark(Description = "edit an item, chained")]
    public void EditChained() => this.chained.Replace(EditedKey, this.NextState());

    /// <summary>An item enters the collection and leaves it again.</summary>
    [Benchmark(Description = "add and remove an item, re-derived")]
    public void AddAndRemoveRederived() => this.rederived.AddAndRemove(AddedKey, AddedState);

    /// <summary>The same pair of structural edits, through the chain.</summary>
    [Benchmark(Description = "add and remove an item, chained")]
    public void AddAndRemoveChained() => this.chained.AddAndRemove(AddedKey, AddedState);

    /// <summary>The filter's criteria changes, which re-derives everything either way.</summary>
    [Benchmark(Description = "change the threshold, re-derived")]
    public void SetThresholdRederived() => this.rederived.SetThreshold(this.NextThreshold());

    /// <summary>The same change, through the chain, which rebuilds the stage and reports a reset.</summary>
    [Benchmark(Description = "change the threshold, chained")]
    public void SetThresholdChained() => this.chained.SetThreshold(this.NextThreshold());

    /// <summary>
    ///     The key both shapes edit. Which one hardly matters, because <see cref="NextState" />
    ///     scores it to the top of the range either way, so the edit lands inside the window rather
    ///     than being filtered away before either shape has to do anything about it.
    /// </summary>
    private static int EditedKey => 0;

    /// <summary>
    ///     A key no seeded item uses, so adding it cannot collide and removing it cannot leave the
    ///     collection short.
    /// </summary>
    private static int AddedKey => -1;

    /// <summary>
    ///     Scored to the top of the range, so the added item actually enters the window and every
    ///     stage of the chain has to do something about it. Scored below the threshold instead, the
    ///     filter would drop it and the sort and the window would never hear of it - which would
    ///     measure the chain declining to work rather than the chain working.
    /// </summary>
    private static ItemState AddedState => new("added", int.MaxValue, false);

    private static string Describe(IEnumerable<int> keys) => string.Join(", ", keys);

    /// <summary>
    ///     Two states, alternating. A score that only ever climbed would migrate the edited item
    ///     through the ordering as the benchmark ran, so what was measured would drift with it.
    /// </summary>
    private ItemState NextState()
    {
        this.editCount++;

        return new ItemState("edited", int.MaxValue - (this.editCount % 2), false);
    }

    /// <summary>
    ///     Two thresholds, alternating, both low enough that the filter still passes nearly
    ///     everything — so what is measured is the rebuild rather than the collection emptying.
    /// </summary>
    private int NextThreshold()
    {
        this.thresholdCount++;

        return ViewSeed.InitialThreshold + (this.thresholdCount % 2);
    }
}
