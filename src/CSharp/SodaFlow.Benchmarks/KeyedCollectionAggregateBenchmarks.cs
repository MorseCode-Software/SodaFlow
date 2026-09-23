using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to keep a total of one state value across the full collection current as the collection
///     changes.
/// </summary>
/// <remarks>
///     <para>
///         Each other benchmark here measures something that depends on a screenful. An aggregate depends on
///         each item by definition. Thus, it is the honest test of the value of the collection for more than
///         windows.
///     </para>
///     <para>
///         The answer is that it is, but not by holding a cell over the store. Mapping the snapshot cell
///         reads each item on each edit. Thus, a total costs the collection at each edit, and the size of the
///         change has no effect. A fold over the change stream costs what changed. The change carries the new
///         states, the snapshot the transaction started from holds the previous ones, and the difference
///         between them is the full update.
///     </para>
///     <para>
///         What this does not measure is a first computation. The two shapes sum the collection one time to
///         start. Only the second avoids a second sum.
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

    private IncrementalAggregateShape incremental = IncrementalAggregateShape.Build(1);

    // Populated fully in the setup. built small here so the fields never have to be nullable.
    private RederivedAggregateShape rederived = RederivedAggregateShape.Build(1);

    /// <summary>How many items the collection holds.</summary>
    [Params(1_000, 10_000, 100_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>The key the two shapes edit. Which one it is has no effect on either.</summary>
    private static int EditedKey => 0;

    /// <summary>
    ///     A key no seeded item uses, so the setup's structural check cannot collide with one.
    /// </summary>
    private static int AddedKey => -1;

    /// <summary>
    ///     Builds the two shapes and refuses to run unless they agree on the total, before an edit and
    ///     after one. A fold that drifts is the defect this shape invites, and a total that drifts has no
    ///     lower cost than a correct one.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.rederived = RederivedAggregateShape.Build(this.ItemCount);
        this.incremental = IncrementalAggregateShape.Build(this.ItemCount);

        Agree("before any edit");

        // One state, handed to the two. Two calls to NextState give them different ones. That is
        // a defect in the check, and not in one of the two shapes. It was, the first time this ran.
        ItemState edited = this.NextState();

        this.rederived.Replace(key: EditedKey, state: edited);
        this.incremental.Replace(key: EditedKey, state: edited);

        Agree("after one edit");

        // A structural change too. An add and a remove get to the added and removed halves of the
        // fold, and nothing else does. Nothing timed below goes near them.
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

    /// <summary>One edit, with the total recomputed from the full store.</summary>
    [Benchmark(Description = "total after an edit, re-derived", Baseline = true)]
    public void EditRederived() => this.rederived.Replace(key: EditedKey, state: this.NextState());

    /// <summary>The same edit, with the total adjusted by what changed.</summary>
    [Benchmark(Description = "total after an edit, folded")]
    public void EditIncremental() => this.incremental.Replace(key: EditedKey, state: this.NextState());

    /// <summary>
    ///     Two states, alternating, so the total oscillates between two values rather than climbing
    ///     as the benchmark runs. A total with no limit measures arithmetic on larger numbers. It
    ///     also stops to compare between the arms at a different count of the runs.
    /// </summary>
    private ItemState NextState()
    {
        this.editCount++;

        return new ItemState(name: "edited", score: this.editCount % 2, isFrozen: false);
    }
}
