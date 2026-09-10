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
    private IKeyedCollectionViewShape chained = ChainedViewShape.Build(1, ChainStyle.ByState);
    private IKeyedCollectionViewShape chainedByIdentitySort = ChainedViewShape.Build(1, ChainStyle.SortByIdentity);

    private IKeyedCollectionViewShape chainedByIdentityThroughout =
        ChainedViewShape.Build(1, ChainStyle.ByIdentity);

    private IKeyedCollectionViewShape selectiveByState =
        ChainedViewShape.Build(1, ChainStyle.SelectiveByState);

    private IKeyedCollectionViewShape selectiveByIdentity =
        ChainedViewShape.Build(1, ChainStyle.SelectiveByIdentity);

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
        this.chained = ChainedViewShape.Build(this.ItemCount, ChainStyle.ByState);
        this.chainedByIdentitySort = ChainedViewShape.Build(this.ItemCount, ChainStyle.SortByIdentity);
        this.chainedByIdentityThroughout = ChainedViewShape.Build(this.ItemCount, ChainStyle.ByIdentity);
        this.selectiveByState = ChainedViewShape.Build(this.ItemCount, ChainStyle.SelectiveByState);
        this.selectiveByIdentity = ChainedViewShape.Build(this.ItemCount, ChainStyle.SelectiveByIdentity);

        // These two are checked against each other rather than against the re-derived view, whose
        // predicate keeps everything: what has to match is that asking the state and asking the
        // identity select the same half.
        if (!this.selectiveByState.Keys.SequenceEqual(this.selectiveByIdentity.Keys))
        {
            throw new InvalidOperationException(
                "The two selective filters disagree about what they keep, so timing them against "
                + "each other would compare different work. By state: "
                + $"[{Describe(this.selectiveByState.Keys)}]. By identity: "
                + $"[{Describe(this.selectiveByIdentity.Keys)}].");
        }

        if (!this.rederived.Keys.SequenceEqual(this.chainedByIdentityThroughout.Keys))
        {
            throw new InvalidOperationException(
                "The chain reading only identities disagrees with the re-derived view, which it "
                + "should not: its filter admits everything, as the threshold one does to begin "
                + "with, and its sort orders by a number equal to the score. Chained on identity "
                + $"throughout: [{Describe(this.chainedByIdentityThroughout.Keys)}].");
        }

        if (!this.rederived.Keys.SequenceEqual(this.chainedByIdentitySort.Keys))
        {
            throw new InvalidOperationException(
                "The identity-ordered chain disagrees with the re-derived view, which it should "
                + "not: the seed gives every item a score equal to its number, so ordering by "
                + "either puts the same keys in the same places. Chained by identity: "
                + $"[{Describe(this.chainedByIdentitySort.Keys)}].");
        }

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

    /// <summary>
    ///     The same edit again, through a chain whose sort reads the identity rather than the
    ///     state — so nothing it holds can have moved, and it is allowed to say so.
    /// </summary>
    [Benchmark(Description = "edit an item, chained on an identity sort")]
    public void EditChainedByIdentitySort() => this.chainedByIdentitySort.Replace(EditedKey, this.NextState());

    /// <summary>
    ///     And again, through a chain where neither stage reads the state. Membership cannot have
    ///     changed and neither can any position, so between them the two stages do nothing but
    ///     look up an index and forward the update — which is the floor, because a stage cannot
    ///     know whether something below it sorts on what just changed.
    /// </summary>
    [Benchmark(Description = "edit an item, chained on identity throughout")]
    public void EditChainedByIdentityThroughout() =>
        this.chainedByIdentityThroughout.Replace(EditedKey, this.NextState());

    /// <summary>
    ///     An edit to an item a selective filter does not keep, tested against the state — which
    ///     costs a membership test and a predicate test to conclude there is nothing to do.
    /// </summary>
    [Benchmark(Description = "edit an excluded item, state filter")]
    public void EditExcludedByState() =>
        this.selectiveByState.Replace(ExcludedKey, this.NextExcludedState());

    /// <summary>
    ///     The same edit, against a filter that selects from the identity — which cannot have
    ///     changed, so one failed index lookup settles it.
    /// </summary>
    [Benchmark(Description = "edit an excluded item, identity filter")]
    public void EditExcludedByIdentity() =>
        this.selectiveByIdentity.Replace(ExcludedKey, this.NextExcludedState());

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
    ///     The same change again, through the identity-ordered chain. A rebuild reads every key it
    ///     keeps, and an order projecting from the identity reads one map where the other reads
    ///     two — so this is the rebuild half of what an identity sort buys, which the re-filing
    ///     benchmarks above cannot see.
    /// </summary>
    [Benchmark(Description = "change the threshold, chained on an identity sort")]
    public void SetThresholdChainedByIdentitySort() => this.chainedByIdentitySort.SetThreshold(this.NextThreshold());

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

    /// <summary>
    ///     An odd key, which both selective filters exclude and neither can be made to admit: the
    ///     identity one because a number cannot change, and the state one because
    ///     <see cref="NextExcludedState" /> keeps the score odd.
    /// </summary>
    private static int ExcludedKey => 1;

    private static string Describe(IEnumerable<int> keys) => string.Join(", ", keys);

    /// <summary>
    ///     Two states, alternating, both scoring odd. If the score's parity moved, the state filter
    ///     would admit the item and do a stage's worth of real work while the identity filter did
    ///     none - which would be a difference in what they were asked, not in what they cost to
    ///     ask.
    /// </summary>
    private ItemState NextExcludedState()
    {
        this.editCount++;

        return new ItemState("edited", 1 + (2 * (this.editCount % 2)), false);
    }

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
