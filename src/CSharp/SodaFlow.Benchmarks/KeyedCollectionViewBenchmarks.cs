using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to keep a filtered, sorted, windowed view of a collection up to date as the collection
///     changes. It compares a full re-derivation at each change against a chain of stages that adjust.
/// </summary>
/// <remarks>
///     <para>
///         The view is the same in the two. It holds the unfrozen items with a score at a threshold or above
///         it, in order by score descending, and the first twenty of those. The setup checks that the two
///         give the same keys before it times the two.
///     </para>
///     <para>
///         This measures three things, and they do not all point in the same direction.
///     </para>
///     <para>
///         An <b>edit</b> is where the chain must win, and win by more as the collection grows. Re-deriving
///         sorts everything that passed the filter, thus it is <c>O(n log n)</c> in the collection. The chain
///         files one key again in one sorted set, which is <c>O(log n)</c>, and then re-windows twenty.
///     </para>
///     <para>
///         An <b>add and remove</b> is the same story for structural change, and this benchmark is what made
///         it true. The identity map in <c>CollectionSnapshot</c> was a plain dictionary, which makes its
///         next version only with a copy. Thus, a structural edit was <c>O(n)</c>, and a low cost for the
///         change in the stages below it made no difference. That showed here as a chain that won by less as
///         the collection grew, and not by more. The map is a trie now, and this is flat.
///     </para>
///     <para>
///         A <b>threshold change</b> is the condition the chain loses. A change to a criteria rebuilds that
///         stage and each stage below it. A rebuild files each surviving key into a new ordered set. Where a
///         re-derivation sorts an array, the chain builds a tree that stays. That costs an allocation for
///         each node, where the sort costs none. The two are Θ(n), thus parity is the ceiling and this does
///         not get to it. This measures it for this cause. It does not flatter this code. It is also the
///         number to show a reader who asks why to debounce a search box, and not to filter at each
///         keystroke.
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
    private ChainedViewShape chained = ChainedViewShape.Build(itemCount: 1, style: ChainStyle.ByState);

    private ChainedViewShape chainedByIdentitySort =
        ChainedViewShape.Build(itemCount: 1, style: ChainStyle.SortByIdentity);

    private ChainedViewShape chainedByIdentityThroughout =
        ChainedViewShape.Build(itemCount: 1, style: ChainStyle.ByIdentity);

    private int editCount;

    // Populated fully in the setup. built small here so the fields never have to be nullable.
    private RederivedViewShape rederived = RederivedViewShape.Build(1);

    private ChainedViewShape selectiveByIdentity =
        ChainedViewShape.Build(itemCount: 1, style: ChainStyle.SelectiveByIdentity);

    private ChainedViewShape
        selectiveByState = ChainedViewShape.Build(itemCount: 1, style: ChainStyle.SelectiveByState);

    private int thresholdCount;

    /// <summary>How many items the collection holds.</summary>
    [Params(1_000, 10_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>
    ///     The key the two shapes edit. Which one it is hardly matters, because
    ///     <see cref="NextState" /> scores it to the top of the range in each condition. Thus, the
    ///     edit gets to the window, and no filter removes it before one of the two shapes has to do
    ///     anything about it.
    /// </summary>
    private static int EditedKey => 0;

    /// <summary>
    ///     A key no seeded item uses, thus an add of it cannot collide and a remove of it cannot keep the
    ///     collection short.
    /// </summary>
    private static int AddedKey => -1;

    /// <summary>
    ///     Scored to the top of the range, thus the added item enters the window. Each stage of the
    ///     chain then has to do something about it. Scored below the threshold, the filter drops it,
    ///     and the sort and the window never see it. That measures a chain that refuses the work, and
    ///     not a chain that does it.
    /// </summary>
    private static ItemState AddedState => new(name: "added", score: int.MaxValue, isFrozen: false);

    /// <summary>
    ///     An odd key, which the two selective filters exclude and no filter can accept. The identity
    ///     one excludes it because a number cannot change. The state one excludes it because
    ///     <see cref="NextExcludedState" /> keeps the score odd.
    /// </summary>
    private static int ExcludedKey => 1;

    /// <summary>
    ///     Builds the two shapes, and refuses to run if they disagree about what the view contains.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.rederived = RederivedViewShape.Build(this.ItemCount);
        this.chained = ChainedViewShape.Build(itemCount: this.ItemCount, style: ChainStyle.ByState);

        this.chainedByIdentitySort =
            ChainedViewShape.Build(itemCount: this.ItemCount, style: ChainStyle.SortByIdentity);

        this.chainedByIdentityThroughout =
            ChainedViewShape.Build(itemCount: this.ItemCount, style: ChainStyle.ByIdentity);

        this.selectiveByState = ChainedViewShape.Build(itemCount: this.ItemCount, style: ChainStyle.SelectiveByState);

        this.selectiveByIdentity =
            ChainedViewShape.Build(itemCount: this.ItemCount, style: ChainStyle.SelectiveByIdentity);

        // The setup checks these two against each other, and not against the re-derived view, whose
        // predicate keeps everything. What has to agree is that a question to the state and a
        // question to the identity select the same half.
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

    /// <summary>One item's state changes, which can move it in the view or out of it.</summary>
    [Benchmark(Description = "edit an item, re-derived", Baseline = true)]
    public void EditRederived() => this.rederived.Replace(key: EditedKey, state: this.NextState());

    /// <summary>The same edit, through the chain.</summary>
    [Benchmark(Description = "edit an item, chained")]
    public void EditChained() => this.chained.Replace(key: EditedKey, state: this.NextState());

    /// <summary>
    ///     The same edit again, through a chain whose sort reads the identity and not the state.
    ///     Thus, nothing it holds moved, and it can say so.
    /// </summary>
    [Benchmark(Description = "edit an item, chained on an identity sort")]
    public void EditChainedByIdentitySort() =>
        this.chainedByIdentitySort.Replace(key: EditedKey, state: this.NextState());

    /// <summary>
    ///     And again, through a chain where no stage reads the state. Membership cannot change, and
    ///     no position can change. Thus, between them the two stages only look up an index and
    ///     forward the update. That is the floor, because a stage cannot know if something below it
    ///     sorts on the value that changed.
    /// </summary>
    [Benchmark(Description = "edit an item, chained on identity throughout")]
    public void EditChainedByIdentityThroughout() =>
        this.chainedByIdentityThroughout.Replace(key: EditedKey, state: this.NextState());

    /// <summary>
    ///     An edit to an item a selective filter does not keep, tested against the state. That costs
    ///     a membership test and a predicate test, to conclude that there is nothing to do.
    /// </summary>
    [Benchmark(Description = "edit an excluded item, state filter")]
    public void EditExcludedByState() =>
        this.selectiveByState.Replace(key: ExcludedKey, state: this.NextExcludedState());

    /// <summary>
    ///     The same edit, against a filter that selects from the identity, which does not change. Thus,
    ///     one index lookup that misses gives the answer.
    /// </summary>
    [Benchmark(Description = "edit an excluded item, identity filter")]
    public void EditExcludedByIdentity() =>
        this.selectiveByIdentity.Replace(key: ExcludedKey, state: this.NextExcludedState());

    /// <summary>An item enters the collection and leaves it again.</summary>
    [Benchmark(Description = "add and remove an item, re-derived")]
    public void AddAndRemoveRederived() => this.rederived.AddAndRemove(key: AddedKey, state: AddedState);

    /// <summary>The same pair of structural edits, through the chain.</summary>
    [Benchmark(Description = "add and remove an item, chained")]
    public void AddAndRemoveChained() => this.chained.AddAndRemove(key: AddedKey, state: AddedState);

    /// <summary>The filter's criteria changes, which re-derives everything in each condition.</summary>
    [Benchmark(Description = "change the threshold, re-derived")]
    public void SetThresholdRederived() => this.rederived.SetThreshold(this.NextThreshold());

    /// <summary>The same change, through the chain, which rebuilds the stage and reports a reset.</summary>
    [Benchmark(Description = "change the threshold, chained")]
    public void SetThresholdChained() => this.chained.SetThreshold(this.NextThreshold());

    /// <summary>
    ///     The same change again, through the identity-ordered chain. A rebuild reads each key it
    ///     keeps, and an order that projects from the identity reads one map where the other reads
    ///     two. Thus, this is the rebuild half of what an identity sort buys, and the benchmarks
    ///     above on a second file operation cannot see it.
    /// </summary>
    [Benchmark(Description = "change the threshold, chained on an identity sort")]
    public void SetThresholdChainedByIdentitySort() => this.chainedByIdentitySort.SetThreshold(this.NextThreshold());

    private static string Describe(IEnumerable<int> keys) => string.Join(separator: ", ", values: keys);

    /// <summary>
    ///     Two states, alternating, the two scoring odd. With a change to the parity of the score,
    ///     the state filter admits the item. It then does the full work of a stage, while the
    ///     identity filter does none. That is a difference in the question, and not in what they cost
    ///     to answer.
    /// </summary>
    private ItemState NextExcludedState()
    {
        this.editCount++;

        return new ItemState(name: "edited", score: 1 + 2 * (this.editCount % 2), isFrozen: false);
    }

    /// <summary>
    ///     Two states, alternating. A score that only climbs moves the edited item
    ///     through the ordering as the benchmark ran, thus the measurement drifts with it.
    /// </summary>
    private ItemState NextState()
    {
        this.editCount++;

        return new ItemState(name: "edited", score: int.MaxValue - this.editCount % 2, isFrozen: false);
    }

    /// <summary>
    ///     Two thresholds, alternating, and the two are sufficiently low. Thus, the filter passes almost
    ///     everything, and this measures the rebuild and not an empty collection.
    /// </summary>
    private int NextThreshold()
    {
        this.thresholdCount++;

        return ViewSeed.InitialThreshold + this.thresholdCount % 2;
    }
}
