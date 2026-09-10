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
///         and ten thousand, alongside everything else it measures. This asks only this question,
///         and up to a million — because the two paths differ by two trie lookups, a trie lookup
///         costs O(log32 n), and past a certain size neither structure is in cache any more. If
///         the gap grows with the collection, this is where it shows.
///     </para>
///     <para>
///         Two shapes rather than the six next door, and that is deliberate: a chain at a million
///         items holds an ordered key set per stage, and six of them would measure the garbage
///         collector.
///     </para>
///     <para>
///         The third arm has no chain at all, and it is here because the first run of this
///         benchmark could not be read without it. An edit pays for the transaction, the send, the
///         trie write and the change object before any stage is consulted, and that floor is
///         roughly two fifths of what an excluded-key edit costs. Against the whole number a
///         stage-level difference reads as noise, and the honest-looking conclusion is that there
///         is none; against the chain's own cost the same measurement is a steady few percent.
///         Subtract the floor before comparing anything.
///     </para>
///     <para>
///         Both filters keep the same half. The seed gives every item a score equal to its number,
///         so even scores and even numbers are the same items, and the sort below is over the
///         identity in both — leaving the filter as the only thing that differs.
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
    // Populated for real in the setup; built small here so the fields never have to be nullable.
    private IKeyedCollectionViewShape rootOnly = RootOnlyViewShape.Build(1);
    private IKeyedCollectionViewShape byState = ChainedViewShape.Build(1, ChainStyle.SelectiveByState);
    private IKeyedCollectionViewShape byIdentity = ChainedViewShape.Build(1, ChainStyle.SelectiveById);

    private int editCount;

    /// <summary>How many items the collection holds.</summary>
    [Params(10_000, 100_000, 1_000_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>Stands both chains up, and refuses to run if they keep different items.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.rootOnly = RootOnlyViewShape.Build(this.ItemCount);
        this.byState = ChainedViewShape.Build(this.ItemCount, ChainStyle.SelectiveByState);
        this.byIdentity = ChainedViewShape.Build(this.ItemCount, ChainStyle.SelectiveById);

        if (!this.byState.Keys.SequenceEqual(this.byIdentity.Keys))
        {
            throw new InvalidOperationException(
                "The two selective filters disagree about what they keep, so timing them against "
                + "each other would compare different work.");
        }
    }

    /// <summary>
    ///     An edit with no view stages at all, which every arm below pays before it does anything
    ///     of its own. The baseline, because the difference between the arms is what is being
    ///     asked about and this is how much of each of them is not that.
    /// </summary>
    [Benchmark(Description = "edit, no chain", Baseline = true)]
    public void EditNoChain() => this.rootOnly.Replace(InViewKey, this.NextInViewState());

    /// <summary>An edit to an item the filter keeps, tested against the state.</summary>
    [Benchmark(Description = "edit an item in view, state filter")]
    public void EditInViewByState() => this.byState.Replace(InViewKey, this.NextInViewState());

    /// <summary>The same edit, against a filter that selects from the identity.</summary>
    [Benchmark(Description = "edit an item in view, identity filter")]
    public void EditInViewById() => this.byIdentity.Replace(InViewKey, this.NextInViewState());

    /// <summary>
    ///     An edit to an item the filter does not keep, tested against the state — a membership
    ///     test and a predicate test to conclude there is nothing to do.
    /// </summary>
    [Benchmark(Description = "edit an excluded item, state filter")]
    public void EditExcludedByState() =>
        this.byState.Replace(ExcludedKey, this.NextExcludedState());

    /// <summary>
    ///     The same edit, against a filter that selects from the identity — which cannot have
    ///     changed, so one failed index lookup settles it.
    /// </summary>
    [Benchmark(Description = "edit an excluded item, identity filter")]
    public void EditExcludedById() =>
        this.byIdentity.Replace(ExcludedKey, this.NextExcludedState());

    /// <summary>An even key, which both filters keep.</summary>
    private static int InViewKey => 0;

    /// <summary>An odd key, which neither filter keeps.</summary>
    private static int ExcludedKey => 1;

    /// <summary>
    ///     Two states, alternating, both scoring even — so the state filter keeps the item before
    ///     and after, and is measured deciding that rather than acting on a change of mind.
    /// </summary>
    private ItemState NextInViewState()
    {
        this.editCount++;

        return new ItemState("edited", 2 * (this.editCount % 2), false);
    }

    /// <summary>
    ///     Two states, alternating, both scoring odd. If the parity moved, the state filter would
    ///     admit the item and do a stage's worth of real work while the identity filter did none —
    ///     a difference in what they were asked rather than in what asking cost.
    /// </summary>
    private ItemState NextExcludedState()
    {
        this.editCount++;

        return new ItemState("edited", 1 + (2 * (this.editCount % 2)), false);
    }
}
