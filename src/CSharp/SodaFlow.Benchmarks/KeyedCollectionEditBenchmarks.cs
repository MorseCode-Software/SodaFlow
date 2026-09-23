using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What one edit costs after the construction of the collection, with a screenful of it bound, in each of
///     the three shapes.
/// </summary>
/// <remarks>
///     <para>
///         This measures two edits, and the difference between them is the full argument. An edit to an
///         <i>observed</i> key has work to do in each shape. An edit to an <i>unobserved</i> key is what
///         almost each edit is, when twenty rows are bound out of ten thousand items. Such an edit must cost
///         nothing downstream, and what it actually costs is what separates these three.
///     </para>
///     <para>
///         Expect sinks per field to win on time here, and to win by more as <see cref="ItemCount" /> grows.
///         A send into one cell goes to the listeners of that cell and to nothing else. That is a true result
///         and not one to hide.
///     </para>
///     <para>
///         It is also a result about a shape most collections cannot have. A sink takes events from out of
///         the graph and nothing else, and <c>Send</c> throws in a transaction. Thus, a cell for each field
///         with a sink behind it needs each mutable value to come in full from other code. No logic comes
///         between the two. One derived field, and it is the second shape. See
///         <see cref="IKeyedCollectionShape" />, and read this row as the floor rather than as the
///         alternative.
///     </para>
///     <para>
///         Cells per field fed from a stream is the one to monitor as the collection grows. Each edit
///         evaluates one filter for each item, thus its cost grows with <see cref="ItemCount" />. It makes no
///         difference if something observes the collection, and the unobserved condition costs almost what
///         the observed one costs.
///     </para>
///     <para>
///         The reactive collection resolves the edit one time and then evaluates one hash lookup per
///         <i>observer</i>. That grows with the twenty bound rows and it does not change with the ten
///         thousand items, which is the property this library is for.
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

    private int nextScore;
    private Bound reactiveCollection = Bound.Of(shape: ReactiveCollectionShape.Build(1), itemCount: 1);

    // Populated fully in the setup. built small here so the fields never have to be nullable.
    private Bound sinkPerField = Bound.Of(shape: SinkPerFieldShape.Build(1), itemCount: 1);
    private Bound streamFedCells = Bound.Of(shape: StreamFedCellShape.Build(1), itemCount: 1);

    /// <summary>How many items the collection holds.</summary>
    [Params(1_000, 10_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>Stands all three shapes up with a screenful of each one bound.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.sinkPerField = Bound.Of(shape: SinkPerFieldShape.Build(this.ItemCount), itemCount: this.ItemCount);
        this.streamFedCells = Bound.Of(shape: StreamFedCellShape.Build(this.ItemCount), itemCount: this.ItemCount);

        this.reactiveCollection =
            Bound.Of(shape: ReactiveCollectionShape.Build(this.ItemCount), itemCount: this.ItemCount);
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
    public void EditObservedSinkPerField() => this.Edit(bound: this.sinkPerField, observed: true);

    /// <summary>An edit to a key a bound row is watching, fed through one stream.</summary>
    [Benchmark(Description = "edit an observed item, cells per field from a stream")]
    public void EditObservedStreamFedCells() => this.Edit(bound: this.streamFedCells, observed: true);

    /// <summary>An edit to a key a bound row is watching, through the collection.</summary>
    [Benchmark(Description = "edit an observed item, reactive collection")]
    public void EditObservedReactiveCollection() => this.Edit(bound: this.reactiveCollection, observed: true);

    /// <summary>An edit to a key nothing is watching, in the sinks-per-field shape.</summary>
    [Benchmark(Description = "edit an unobserved item, sinks per field")]
    public void EditUnobservedSinkPerField() => this.Edit(bound: this.sinkPerField, observed: false);

    /// <summary>
    ///     An edit to a key nothing is watching, fed through one stream — which evaluates
    ///     one filter per item in the collection.
    /// </summary>
    [Benchmark(Description = "edit an unobserved item, cells per field from a stream")]
    public void EditUnobservedStreamFedCells() => this.Edit(bound: this.streamFedCells, observed: false);

    /// <summary>
    ///     An edit to a key nothing is watching, through the collection — one resolution and one
    ///     hash lookup per observer.
    /// </summary>
    [Benchmark(Description = "edit an unobserved item, reactive collection")]
    public void EditUnobservedReactiveCollection() => this.Edit(bound: this.reactiveCollection, observed: false);

    private void Edit(Bound bound, bool observed)
    {
        // A different score each time, so nothing anywhere gets to short-circuit on equality.
        this.nextScore++;

        int key = observed ? bound.ObservedKey : bound.UnobservedKey;

        bound.Shape.Replace(key: key, state: new ItemState(name: "edited", score: this.nextScore, isFrozen: false));
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
            IReadOnlyList<int> observedKeys = ItemSeed.ObservedKeys(itemCount: itemCount, observerCount: ObserverCount);
            List<IListener> listeners = [.. observedKeys.Select(shape.Observe)];

            // The first observed key, and the one after it. The equal distances guarantee that it is
            // not observed as long as there are more items than rows.
            int observedKey = observedKeys[0];

            return new Bound(
                shape: shape,
                listeners: listeners,
                observedKey: observedKey,
                unobservedKey: itemCount > ObserverCount ? observedKey + 1 : observedKey);
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
