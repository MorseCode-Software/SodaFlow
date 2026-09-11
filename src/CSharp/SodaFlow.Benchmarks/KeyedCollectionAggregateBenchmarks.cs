using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to keep a total of one state value across the whole collection current as the
///     collection changes.
/// </summary>
/// <remarks>
///     <para>
///         Every other benchmark here measures something that depends on a screenful. An aggregate
///         depends on every item by definition, which makes it the honest test of whether the
///         collection is useful for anything but windows.
///     </para>
///     <para>
///         The answer is that it is, but not by holding a cell over the store. Mapping the snapshot
///         cell reads every item on every edit, so a total costs the collection each time however
///         little of it moved. Folding the change stream costs what changed: the change carries the
///         new states, the snapshot the transaction started from still holds the old ones, and the
///         difference between them is the whole update.
///     </para>
///     <para>
///         What this does not measure is a first computation. Both shapes sum the collection once to
///         start; only the second avoids doing it again.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class KeyedCollectionAggregateBenchmarks
{
    private int editCount;

    private IKeyedAggregateShape incremental = IncrementalAggregateShape.Build(1);

    // Populated for real in the setup; built small here so the fields never have to be nullable.
    private IKeyedAggregateShape rederived = RederivedAggregateShape.Build(1);

    /// <summary>How many items the collection holds.</summary>
    [Params(1_000, 10_000, 100_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>The key both shapes edit. Which one it is does not matter to either.</summary>
    private static int EditedKey => 0;

    /// <summary>
    ///     A key no seeded item uses, so the setup's structural check cannot collide with one.
    /// </summary>
    private static int AddedKey => -1;

    /// <summary>
    ///     Builds both shapes and refuses to run unless they agree on the total, before an edit and
    ///     after one. A fold that drifts is the bug this shape invites, and a drifting total is no
    ///     cheaper to compute than a correct one.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.rederived = RederivedAggregateShape.Build(this.ItemCount);
        this.incremental = IncrementalAggregateShape.Build(this.ItemCount);

        Agree("before any edit");

        // One state, handed to both. Calling NextState twice gives them different ones, which is
        // a bug in the check rather than in either shape - and was, the first time this ran.
        ItemState edited = this.NextState();

        this.rederived.Replace(key: EditedKey, state: edited);
        this.incremental.Replace(key: EditedKey, state: edited);

        Agree("after one edit");

        // A structural change too, because adding and removing is the only thing that reaches the
        // added and removed halves of the fold, and nothing timed below goes near them.
        ItemState added = new(name: "added", score: 1234, isFrozen: false);

        this.rederived.AddAndRemove(key: AddedKey, state: added);
        this.incremental.AddAndRemove(key: AddedKey, state: added);

        Agree("after an add and a remove");

        return;

        void Agree(string when)
        {
            if (this.rederived.Total != this.incremental.Total)
            {
                throw new InvalidOperationException(
                    $"The two totals disagree {when}, so timing them against each other would "
                    + $"compare different work. Re-derived: {this.rederived.Total}. Incremental: "
                    + $"{this.incremental.Total}.");
            }
        }
    }

    /// <summary>One edit, with the total recomputed from the whole store.</summary>
    [Benchmark(Description = "total after an edit, re-derived", Baseline = true)]
    public void EditRederived() => this.rederived.Replace(key: EditedKey, state: this.NextState());

    /// <summary>The same edit, with the total adjusted by what changed.</summary>
    [Benchmark(Description = "total after an edit, folded")]
    public void EditIncremental() => this.incremental.Replace(key: EditedKey, state: this.NextState());

    /// <summary>
    ///     Two states, alternating, so the total oscillates between two values rather than climbing
    ///     as the benchmark runs. A total that grew without bound would eventually measure
    ///     arithmetic on larger numbers, and would also stop being comparable between the arms if
    ///     they ran a different number of times.
    /// </summary>
    private ItemState NextState()
    {
        this.editCount++;

        return new ItemState(name: "edited", score: this.editCount % 2, isFrozen: false);
    }
}
