using System;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What a selective filter costs per edit as the collection grows, asked of the state and of
///     the identity.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="KeyedCollectionViewBenchmarks" /> asks the same question at a thousand items
///         and ten thousand, with everything else it measures. This asks only this question, and up to
///         a million. The two paths are different by two trie lookups, a trie lookup costs
///         <c>O(log32 n)</c>, and after a given size no structure stays in cache. If
///         the difference grows with the collection, this is where it shows.
///     </para>
///     <para>
///         Two shapes and not the six next door, and that is deliberate. A chain at a million items
///         holds an ordered key set for each stage, and six of them measure the garbage collector.
///     </para>
///     <para>
///         The third arm has no chain at all, and it is here because the first run of this
///         benchmark is not readable without it. An edit pays for the transaction, the send operation,
///         the trie write, and the change object before it reads any stage. That floor is approximately
///         two fifths of what an excluded-key edit costs. Against the full number a
///         stage-level difference reads as noise, and the honest-looking result is that there
///         is none. Against the cost of the chain, the same measurement is a constant few percent.
///         Subtract the floor before comparing anything.
///     </para>
///     <para>
///         The two filters keep the same half. The seed gives each item a score equal to its number,
///         thus even scores and even numbers are the same items. The sort below is over the identity
///         in the two, which leaves the filter as the only thing that is different.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class KeyedCollectionScaleBenchmarks
{
    private ChainedViewShape byIdentity = ChainedViewShape.Build(itemCount: 1, style: ChainStyle.SelectiveByIdentity);

    private ChainedViewShape byState = ChainedViewShape.Build(itemCount: 1, style: ChainStyle.SelectiveByState);

    private int editCount;

    // Populated fully in the setup. built small here so the fields never have to be nullable.
    private RootOnlyViewShape rootOnly = RootOnlyViewShape.Build(1);

    /// <summary>How many items the collection holds.</summary>
    [Params(10_000, 100_000, 1_000_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>An even key, which the two filters keep.</summary>
    private static int InViewKey => 0;

    /// <summary>An odd key, which no filter keeps.</summary>
    private static int ExcludedKey => 1;

    /// <summary>Builds the two chains, and refuses to run if they keep different items.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.rootOnly = RootOnlyViewShape.Build(this.ItemCount);
        this.byState = ChainedViewShape.Build(itemCount: this.ItemCount, style: ChainStyle.SelectiveByState);
        this.byIdentity = ChainedViewShape.Build(itemCount: this.ItemCount, style: ChainStyle.SelectiveByIdentity);

        if (!this.byState.Keys.SequenceEqual(this.byIdentity.Keys))
        {
            throw new InvalidOperationException(
                "The two selective filters disagree about what they keep, so timing them against "
                + "each other would compare different work.");
        }
    }

    /// <summary>
    ///     An edit with no view stages at all, which each arm below pays before it does anything
    ///     of its own. The baseline, because the difference between the arms is the question here,
    ///     and this is how much of each of them is not that.
    /// </summary>
    [Benchmark(Description = "edit, no chain", Baseline = true)]
    public void EditNoChain() => this.rootOnly.Replace(key: InViewKey, state: this.NextInViewState());

    /// <summary>An edit to an item the filter keeps, tested against the state.</summary>
    [Benchmark(Description = "edit an item in view, state filter")]
    public void EditInViewByState() => this.byState.Replace(key: InViewKey, state: this.NextInViewState());

    /// <summary>The same edit, against a filter that selects from the identity.</summary>
    [Benchmark(Description = "edit an item in view, identity filter")]
    public void EditInViewByIdentity() => this.byIdentity.Replace(key: InViewKey, state: this.NextInViewState());

    /// <summary>
    ///     An edit to an item the filter does not keep, tested against the state. That is a membership
    ///     test and a predicate test, to conclude that there is nothing to do.
    /// </summary>
    [Benchmark(Description = "edit an excluded item, state filter")]
    public void EditExcludedByState() => this.byState.Replace(key: ExcludedKey, state: this.NextExcludedState());

    /// <summary>
    ///     The same edit, against a filter that selects from the identity, which cannot
    ///     changed, thus one index lookup that misses gives the answer.
    /// </summary>
    [Benchmark(Description = "edit an excluded item, identity filter")]
    public void EditExcludedByIdentity() => this.byIdentity.Replace(key: ExcludedKey, state: this.NextExcludedState());

    /// <summary>
    ///     Two states, alternating, the two scoring even. Thus, the state filter keeps the item before
    ///     and after, and this measures that decision, and not a change of mind.
    /// </summary>
    private ItemState NextInViewState()
    {
        this.editCount++;

        return new ItemState(name: "edited", score: 2 * (this.editCount % 2), isFrozen: false);
    }

    /// <summary>
    ///     Two states, alternating, the two scoring odd. With a change to the parity, the state filter
    ///     admits the item. It then does the full work of a stage, while the identity filter does
    ///     none. That is a difference in the question, and not in the cost of the question.
    /// </summary>
    private ItemState NextExcludedState()
    {
        this.editCount++;

        return new ItemState(name: "edited", score: 1 + 2 * (this.editCount % 2), isFrozen: false);
    }
}
