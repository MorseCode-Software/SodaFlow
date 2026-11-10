using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;
using SodaFlow.Collections;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What a change of the criteria of a filter costs, by the number of keys that
///     the change moves across the filter.
/// </summary>
/// <remarks>
///     <para>
///         A filter over a criteria cell tests each key at a new criteria. Its
///         limit is 1,000 keys, or a tenth of the keys that it holds. Up to that
///         limit, it moves each key that changed sides and reports one operation
///         for each. Over it, it builds its set again and reports a reset. It finds
///         the keys that changed sides before it edits its set. Before, it edited
///         the set key by key, and at one key over the limit it discarded those
///         edits and built the set again.
///     </para>
///     <para>
///         Each arm sends two criteria: one that moves keys out, and then the
///         first one, which moves them back. Thus, each invocation starts from the
///         same set. The first arm moves no key, and it measures the test of each
///         key. The second arm moves 100 keys, which the limit permits. The third
///         arm moves a quarter of the keys, which is over the limit, as the first
///         letter of a search is.
///     </para>
///     <para>
///         The filter on the state and the filter on the identity have one arm
///         each, because each has its own pass over the keys. The listener reads
///         nothing, thus the arms measure the stage and not a consumer.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class FilterCriteriaBenchmarks
{
    /// <summary>The number of keys that the second arm moves.</summary>
    private const int FewKeys = 100;

    // Populated fully in the setup. Built small here so the fields never have to be nullable.
    private FilteredViewShape byIdentity = FilteredViewShape.Build(itemCount: 1, byIdentity: true);

    private FilteredViewShape byState = FilteredViewShape.Build(itemCount: 1, byIdentity: false);

    /// <summary>How many items the collection holds.</summary>
    [Params(10_000, 100_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>
    ///     Builds the shapes, and refuses to run if a criteria does not move the keys
    ///     that its arm names.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.byState = FilteredViewShape.Build(itemCount: this.ItemCount, byIdentity: false);
        this.byIdentity = FilteredViewShape.Build(itemCount: this.ItemCount, byIdentity: true);

        foreach (FilteredViewShape shape in new[] { this.byState, this.byIdentity })
        {
            Expect(shape: shape, minimum: -1, count: this.ItemCount);
            Expect(shape: shape, minimum: FewKeys, count: this.ItemCount - FewKeys);
            Expect(shape: shape, minimum: this.ItemCount / 4, count: this.ItemCount - this.ItemCount / 4);
            Expect(shape: shape, minimum: 0, count: this.ItemCount);
        }
    }

    [Benchmark(Description = "filter, no key moves")]
    public void FilterNoKey() => Toggle(shape: this.byState, minimum: -1);

    [Benchmark(Description = "filter, 100 keys move")]
    public void FilterFewKeys() => Toggle(shape: this.byState, minimum: FewKeys);

    [Benchmark(Description = "filter, a quarter of the keys move")]
    public void FilterAQuarter() => Toggle(shape: this.byState, minimum: this.ItemCount / 4);

    [Benchmark(Description = "filter by identity, no key moves")]
    public void FilterByIdentityNoKey() => Toggle(shape: this.byIdentity, minimum: -1);

    [Benchmark(Description = "filter by identity, 100 keys move")]
    public void FilterByIdentityFewKeys() => Toggle(shape: this.byIdentity, minimum: FewKeys);

    [Benchmark(Description = "filter by identity, a quarter of the keys move")]
    public void FilterByIdentityAQuarter() => Toggle(shape: this.byIdentity, minimum: this.ItemCount / 4);

    /// <summary>
    ///     A criteria that keeps each key from <paramref name="minimum" /> up, and
    ///     then the criteria that keeps each key.
    /// </summary>
    private static void Toggle(FilteredViewShape shape, int minimum)
    {
        shape.Minimum(minimum);
        shape.Minimum(0);
    }

    private static void Expect(FilteredViewShape shape, int minimum, int count)
    {
        shape.Minimum(minimum);

        if (shape.Count != count)
        {
            throw new InvalidOperationException("A criteria did not give the keys that the benchmark names.");
        }
    }
}

/// <summary>
///     A collection behind a filter that keeps each key from the value of a
///     criteria cell up, with a listener on the view. The number of an item is its
///     key and its score. Thus, a filter on the state and a filter on the identity
///     keep the same keys.
/// </summary>
internal sealed class FilteredViewShape
{
    // Load-bearing, as in the other shapes.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;
    private readonly CellSink<int> minimum;
    private readonly ReactiveCollection<int, ItemIdentity, ItemState> view;

    private FilteredViewShape(
        CellSink<int> minimum,
        ReactiveCollection<int, ItemIdentity, ItemState> view,
        IListener listener)
    {
        this.minimum = minimum;
        this.view = view;
        this.listener = listener;
    }

    internal int Count => this.view.KeysCell.Sample().Count;

    internal void Minimum(int value) => this.minimum.Send(value);

    /// <summary>Builds the shape with a criteria that keeps each key.</summary>
    internal static FilteredViewShape Build(int itemCount, bool byIdentity)
    {
        List<Item<ItemIdentity, ItemState>> items = new(itemCount);

        for (int number = 0; number < itemCount; number++)
        {
            items.Add(
                new Item<ItemIdentity, ItemState>(
                    identity: ItemSeed.Identity(number),
                    state: ItemSeed.State(number)));
        }

        return Transaction.Run(() =>
        {
            ReactiveCollection<int, ItemIdentity, ItemState> collection =
                ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    keySelector: static identity => identity.Number,
                    initialItems: items,
                    Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>());

            CellSink<int> minimumCell = Cell.CreateSink(0);

            ReactiveCollection<int, ItemIdentity, ItemState> view =
                byIdentity
                    ? collection.FilterByIdentity(
                        criteriaCell: minimumCell,
                        predicate: static (minimum, identity) => identity.Number >= minimum)
                    : collection.Filter(
                        criteriaCell: minimumCell,
                        predicate: static (minimum, _, state) => state.Score >= minimum);

            return new FilteredViewShape(
                minimum: minimumCell,
                view: view,
                listener: view.KeyChangesStream.Listen(static _ =>
                {
                }));
        });
    }
}
