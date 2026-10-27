using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;
using SodaFlow.Collections;
using SodaFlow.Functional;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to make a state cell for each key of a collection.
/// </summary>
/// <remarks>
///     <para>
///         The collection keeps each cell that it gives in a weak cache. An add to
///         that cache can first remove the entries whose cells the garbage
///         collector removed. That removal reads each entry. Thus, its frequency
///         sets the cost: a removal at each add makes the cost of n cells
///         quadratic.
///     </para>
///     <para>
///         Both benchmarks make the collection, and the second one also makes a
///         cell for each key and keeps each cell. Thus, no removal finds an entry to
///         remove, and the difference between the two is the cost of the cells and
///         of the cache.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class PerKeyCellBenchmarks
{
    private readonly List<Item<ItemIdentity, ItemState>> items = [];

    /// <summary>How many items the collection holds, and how many cells the benchmark makes.</summary>
    [Params(100, 1_000, 10_000)]
    public int KeyCount { get; [UsedImplicitly] set; }

    /// <summary>Makes the items that each collection starts with.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.items.Clear();

        for (int number = 0; number < this.KeyCount; number++)
        {
            this.items.Add(
                new Item<ItemIdentity, ItemState>(
                    identity: ItemSeed.Identity(number),
                    state: ItemSeed.State(number)));
        }
    }

    /// <summary>Makes the collection, with no cell.</summary>
    /// <returns>The collection, as an object because its item types are internal.</returns>
    [Benchmark(Baseline = true, Description = "collection")]
    public object Collection() => this.CreateCollection();

    /// <summary>Makes the collection and a state cell for each key, and keeps each cell.</summary>
    /// <returns>The cells, as an object because their item type is internal.</returns>
    [Benchmark(Description = "collection, and a state cell for each key")]
    public object CollectionAndCells()
    {
        ReactiveCollection<int, ItemIdentity, ItemState> collection = this.CreateCollection();
        List<Cell<Maybe<ItemState>>> cells = new(this.KeyCount);

        for (int key = 0; key < this.KeyCount; key++)
        {
            cells.Add(collection.StateCell(key));
        }

        return cells;
    }

    private ReactiveCollection<int, ItemIdentity, ItemState> CreateCollection() =>
        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: static identity => identity.Number,
            initialItems: this.items,
            Stream.Never<CollectionEdit<int, ItemIdentity, ItemState>>());
}
