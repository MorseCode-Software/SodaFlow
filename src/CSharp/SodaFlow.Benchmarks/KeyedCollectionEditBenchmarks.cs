using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What one edit costs once the collection is standing and a screenful of it is bound, in each
///     of the three shapes.
/// </summary>
/// <remarks>
///     <para>
///         Two edits are measured, and the difference between them is the whole argument. An edit
///         to an <i>observed</i> key has work to do in every shape. An edit to an
///         <i>unobserved</i> key — which is what almost every edit is, when twenty rows are bound
///         out of ten thousand items — should ideally cost nothing at all downstream, and what it
///         actually costs is what separates these three.
///     </para>
///     <para>
///         Expect sinks per field to win on time here and to keep winning as
///         <see cref="ItemCount" /> grows: a send into one cell fans out to that cell's listeners
///         and to nothing else. That is a real result and not one to hide. What it costs is
///         everything in <see cref="KeyedCollectionBuildBenchmarks" />, plus the fact that an edit
///         can only arrive as a method call on a reference to the right sink — which is why the
///         second shape exists.
///     </para>
///     <para>
///         Cells per field fed from a stream is the one to watch scale. Every edit evaluates one
///         filter per item, so its cost is proportional to <see cref="ItemCount" /> whether
///         anything is observing or not, and the unobserved case costs very nearly what the
///         observed one does.
///     </para>
///     <para>
///         The reactive collection resolves the edit once and then evaluates one hash lookup per
///         <i>observer</i>. That is proportional to the twenty bound rows and independent of the
///         ten thousand items, which is the property the whole design is for.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class KeyedCollectionEditBenchmarks
{
    /// <summary>How many rows a screenful is. See KeyedCollectionBuildBenchmarks.</summary>
    private const int ObserverCount = 20;

    // Populated for real in the setup; built small here so the fields never have to be nullable.
    private Bound sinkPerField = Bound.Of(SinkPerFieldShape.Build(1), 1);
    private Bound streamFedCells = Bound.Of(StreamFedCellShape.Build(1), 1);
    private Bound reactiveCollection = Bound.Of(ReactiveCollectionShape.Build(1), 1);

    private int nextScore;

    /// <summary>How many items the collection holds.</summary>
    [Params(1_000, 10_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>Stands all three shapes up with a screenful of each one bound.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.sinkPerField = Bound.Of(SinkPerFieldShape.Build(this.ItemCount), this.ItemCount);
        this.streamFedCells = Bound.Of(StreamFedCellShape.Build(this.ItemCount), this.ItemCount);
        this.reactiveCollection = Bound.Of(ReactiveCollectionShape.Build(this.ItemCount), this.ItemCount);
    }

    /// <summary>Releases the listeners holding the three graphs up.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.sinkPerField.Release();
        this.streamFedCells.Release();
        this.reactiveCollection.Release();
    }

    /// <summary>An edit to a key a bound row is watching, in the sinks-per-field shape.</summary>
    [Benchmark(Description = "edit an observed item, sinks per field", Baseline = true)]
    public void EditObservedSinkPerField() => this.Edit(this.sinkPerField, observed: true);

    /// <summary>An edit to a key a bound row is watching, fed through one stream.</summary>
    [Benchmark(Description = "edit an observed item, cells per field from a stream")]
    public void EditObservedStreamFedCells() => this.Edit(this.streamFedCells, observed: true);

    /// <summary>An edit to a key a bound row is watching, through the collection.</summary>
    [Benchmark(Description = "edit an observed item, reactive collection")]
    public void EditObservedReactiveCollection() => this.Edit(this.reactiveCollection, observed: true);

    /// <summary>An edit to a key nothing is watching, in the sinks-per-field shape.</summary>
    [Benchmark(Description = "edit an unobserved item, sinks per field")]
    public void EditUnobservedSinkPerField() => this.Edit(this.sinkPerField, observed: false);

    /// <summary>
    ///     An edit to a key nothing is watching, fed through one stream — which still evaluates
    ///     one filter per item in the collection.
    /// </summary>
    [Benchmark(Description = "edit an unobserved item, cells per field from a stream")]
    public void EditUnobservedStreamFedCells() => this.Edit(this.streamFedCells, observed: false);

    /// <summary>
    ///     An edit to a key nothing is watching, through the collection — one resolution and one
    ///     hash lookup per observer.
    /// </summary>
    [Benchmark(Description = "edit an unobserved item, reactive collection")]
    public void EditUnobservedReactiveCollection() => this.Edit(this.reactiveCollection, observed: false);

    private void Edit(Bound bound, bool observed)
    {
        // A different score each time, so nothing anywhere gets to short-circuit on equality.
        this.nextScore++;

        int key = observed ? bound.ObservedKey : bound.UnobservedKey;

        bound.Shape.Replace(key, new ItemState("edited", this.nextScore, false));
    }

    /// <summary>
    ///     A shape with a screenful bound to it, and the two keys the benchmarks edit: one a row is
    ///     watching, and one nothing is.
    /// </summary>
    private sealed class Bound
    {
        private readonly IReadOnlyList<IListener> listeners;

        private Bound(
            IKeyedCollectionShape shape,
            IReadOnlyList<IListener> listeners,
            int observedKey,
            int unobservedKey)
        {
            this.Shape = shape;
            this.listeners = listeners;
            this.ObservedKey = observedKey;
            this.UnobservedKey = unobservedKey;
        }

        internal IKeyedCollectionShape Shape { get; }

        internal int ObservedKey { get; }

        internal int UnobservedKey { get; }

        internal static Bound Of(IKeyedCollectionShape shape, int itemCount)
        {
            IReadOnlyList<int> observedKeys = ItemSeed.ObservedKeys(itemCount, ObserverCount);
            List<IListener> listeners = [.. observedKeys.Select(shape.Observe)];

            // The first observed key, and the one after it, which the even spread guarantees is
            // not observed as long as there are more items than rows.
            int observedKey = observedKeys[0];

            return new Bound(
                shape,
                listeners,
                observedKey,
                itemCount > ObserverCount ? observedKey + 1 : observedKey);
        }

        internal void Release()
        {
            foreach (IListener listener in this.listeners)
            {
                listener.Unlisten();
            }
        }
    }
}
