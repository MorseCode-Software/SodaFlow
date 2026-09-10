using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What a per-item observer costs depending on what it is bound to, and what it would cost if
///     observing through a view answered for the view rather than for the collection.
/// </summary>
/// <remarks>
///     <para>
///         <c>StateCell</c> on a filtered view currently hands back the collection's own cell, so a
///         key the filter excluded still has its state. That is the last thing a view exposes that
///         is not the view's own, and whether to close it is open. Closing it means a view's cell is
///         the collection's cell lifted against that view's membership, and the cost of that is
///         what this measures - before the decision rather than after it.
///     </para>
///     <para>
///         The arm to watch is the last pair. An observer bound to its own item wakes when that item
///         changes. One lifted against a view's keys wakes when <i>anything</i> the view holds moves,
///         because the view's keys are one cell and reordering replaces it - so twenty observers
///         wake for an edit to an item none of them are watching. That is a different shape of cost
///         from "one more node per observer", and it is the number the decision turns on.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class KeyedCollectionObservationBenchmarks
{
    // Populated for real in the setup; built small here so the fields never have to be nullable.
    private ObservationShape onRoot = ObservationShape.Build(ObservationShape.ObserverCount, ObservationStyle.OnRoot);

    private ObservationShape throughView =
        ObservationShape.Build(ObservationShape.ObserverCount, ObservationStyle.ThroughView);

    private ObservationShape viewScoped =
        ObservationShape.Build(ObservationShape.ObserverCount, ObservationStyle.ViewScoped);

    private ObservationShape viewScopedPerKey =
        ObservationShape.Build(ObservationShape.ObserverCount, ObservationStyle.ViewScopedPerKey);

    private ObservationShape viewNative =
        ObservationShape.Build(ObservationShape.ObserverCount, ObservationStyle.ViewNative);

    private int editCount;

    /// <summary>How many items the collection holds.</summary>
    [Params(1_000, 10_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>Builds all three arrangements over the same collection shape.</summary>
    [GlobalSetup]
    public void Setup()
    {
        ObservationShape.VerifyPremises(this.ItemCount);

        this.onRoot = ObservationShape.Build(this.ItemCount, ObservationStyle.OnRoot);
        this.throughView = ObservationShape.Build(this.ItemCount, ObservationStyle.ThroughView);
        this.viewScoped = ObservationShape.Build(this.ItemCount, ObservationStyle.ViewScoped);
        this.viewScopedPerKey =
            ObservationShape.Build(this.ItemCount, ObservationStyle.ViewScopedPerKey);
        this.viewNative = ObservationShape.Build(this.ItemCount, ObservationStyle.ViewNative);
    }

    /// <summary>An edit to a watched item, observed on the collection.</summary>
    [Benchmark(Description = "edit a watched item, observed on the collection", Baseline = true)]
    public void EditWatchedOnRoot() => this.onRoot.Replace(this.WatchedKey, this.NextState());

    /// <summary>
    ///     The same, observed through a view - which today is the same cell, so this should match
    ///     the baseline and is here to say so.
    /// </summary>
    [Benchmark(Description = "edit a watched item, observed through a view")]
    public void EditWatchedThroughView() => this.throughView.Replace(this.WatchedKey, this.NextState());

    /// <summary>The same again, with each observer lifted against the view's membership.</summary>
    [Benchmark(Description = "edit a watched item, observed with membership")]
    public void EditWatchedViewScoped() => this.viewScoped.Replace(this.WatchedKey, this.NextState());

    /// <summary>The same again, with membership held per observer and calmed.</summary>
    [Benchmark(Description = "edit a watched item, observed with membership per key")]
    public void EditWatchedViewScopedPerKey() =>
        this.viewScopedPerKey.Replace(this.WatchedKey, this.NextState());

    /// <summary>The same again, through the view's own cell, which is what the library builds.</summary>
    [Benchmark(Description = "edit a watched item, observed by the view itself")]
    public void EditWatchedViewNative() => this.viewNative.Replace(this.WatchedKey, this.NextState());

    /// <summary>
    ///     An edit to an item in the view that nobody watches. Observers bound to their own items
    ///     have nothing to do here.
    /// </summary>
    [Benchmark(Description = "edit an unwatched item, observed on the collection")]
    public void EditUnwatchedOnRoot() =>
        this.onRoot.Replace(ObservationShape.UnobservedKeyInView, this.NextState());

    /// <summary>
    ///     The same edit, with each observer lifted against the view's membership - which the edit
    ///     moves, because it reorders the sort beneath the filter.
    /// </summary>
    [Benchmark(Description = "edit an unwatched item, observed with membership")]
    public void EditUnwatchedViewScoped() =>
        this.viewScoped.Replace(ObservationShape.UnobservedKeyInView, this.NextState());

    /// <summary>
    ///     The edit that decides it. Nobody watches this item and nobody's membership moves, so an
    ///     observer that asks only about its own key has nothing to do - if the calming works, this
    ///     costs what observing the collection costs.
    /// </summary>
    [Benchmark(Description = "edit an unwatched item, observed with membership per key")]
    public void EditUnwatchedViewScopedPerKey() =>
        this.viewScopedPerKey.Replace(ObservationShape.UnobservedKeyInView, this.NextState());

    /// <summary>
    ///     The edit the whole exercise is about: nobody watches this item and nobody's membership
    ///     moves, so an observer that filters itself out of a change naming another key should cost
    ///     what observing the collection costs.
    /// </summary>
    [Benchmark(Description = "edit an unwatched item, observed by the view itself")]
    public void EditUnwatchedViewNative() =>
        this.viewNative.Replace(ObservationShape.UnobservedKeyInView, this.NextState());

    /// <summary>A key an observer is bound to, and which the filter keeps.</summary>
    private int WatchedKey => ObservationShape.ObservedKeys(this.ItemCount)[0];

    /// <summary>
    ///     Two states, alternating, so the item moves within the sort and back rather than climbing
    ///     out of it as the benchmark runs.
    /// </summary>
    private ItemState NextState()
    {
        this.editCount++;

        return new ItemState("edited", int.MaxValue - (this.editCount % 2), false);
    }
}
