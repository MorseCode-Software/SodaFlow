using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;
using SodaFlow.Collections;
using SodaFlow.Functional;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What one edit costs against the number of per-key cells on the collection
///     or on a view.
/// </summary>
/// <remarks>
///     <para>
///         A change goes only to the per-key cells of the keys that it names. Before,
///         each per-key cell was a filter on the full change stream. Thus, each
///         change went to each cell, and the cost of an edit of one key was in
///         proportion to the number of cells.
///     </para>
///     <para>
///         The collection holds ten thousand items. The cells are on keys at equal
///         distances across it, and each edit updates keys that have a cell. Each
///         edit writes a new score, thus nothing can stop at equality. On the sorted
///         view, the new score moves the key.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class PerKeyEditBenchmarks
{
    private const int ItemCount = 10_000;

    private int nextScore = ItemCount;

    // Populated fully in the setup. Built small here, thus the fields never have to be nullable.
    private Shape onCollection = Shape.Build(where: Where.Collection, cellCount: 1, editedKeyCount: 1, listen: true);
    private Shape held = Shape.Build(where: Where.Collection, cellCount: 1, editedKeyCount: 1, listen: false);
    private Shape tenKeys = Shape.Build(where: Where.Collection, cellCount: 10, editedKeyCount: 10, listen: true);
    private Shape throughFilter = Shape.Build(where: Where.Filter, cellCount: 1, editedKeyCount: 1, listen: true);
    private Shape throughSort = Shape.Build(where: Where.Sort, cellCount: 1, editedKeyCount: 1, listen: true);

    private enum Where
    {
        Collection,
        Filter,
        Sort
    }

    /// <summary>How many keys have a state cell.</summary>
    [Params(100, 1_000, 10_000)]
    public int CellCount { get; [UsedImplicitly] set; }

    /// <summary>Stands the shapes up.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.onCollection = Shape.Build(where: Where.Collection, cellCount: this.CellCount, editedKeyCount: 1, listen: true);
        this.held = Shape.Build(where: Where.Collection, cellCount: this.CellCount, editedKeyCount: 1, listen: false);
        this.tenKeys = Shape.Build(where: Where.Collection, cellCount: this.CellCount, editedKeyCount: 10, listen: true);
        this.throughFilter = Shape.Build(where: Where.Filter, cellCount: this.CellCount, editedKeyCount: 1, listen: true);
        this.throughSort = Shape.Build(where: Where.Sort, cellCount: this.CellCount, editedKeyCount: 1, listen: true);
    }

    /// <summary>Releases the listeners and the cells.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.onCollection.Release();
        this.held.Release();
        this.tenKeys.Release();
        this.throughFilter.Release();
        this.throughSort.Release();
    }

    /// <summary>One edit of one key, with a listener on each cell of the collection.</summary>
    [Benchmark(Baseline = true, Description = "edit one key, cells on the collection")]
    public void EditOneKey() => this.onCollection.Edit(++this.nextScore);

    /// <summary>The same, with the cells held and no listener on them.</summary>
    [Benchmark(Description = "edit one key, cells held and not observed")]
    public void EditOneKeyHeld() => this.held.Edit(++this.nextScore);

    /// <summary>One edit of ten keys at equal distances across the collection.</summary>
    [Benchmark(Description = "edit ten keys, cells on the collection")]
    public void EditTenKeys() => this.tenKeys.Edit(++this.nextScore);

    /// <summary>One edit of one key, with the cells on a filter that holds each key.</summary>
    [Benchmark(Description = "edit one key, cells through a filter")]
    public void EditOneKeyThroughFilter() => this.throughFilter.Edit(++this.nextScore);

    /// <summary>One edit of one key, with the cells on a sort by score. The edit moves the key.</summary>
    [Benchmark(Description = "edit one key, cells through a sort")]
    public void EditOneKeyThroughSort() => this.throughSort.Edit(++this.nextScore);

    /// <summary>A collection, the state cells, their listeners if any, and one edit.</summary>
    private sealed class Shape
    {
        private readonly IReadOnlyList<Cell<Maybe<ItemState>>> cells;
        private readonly CollectionEdit<int, ItemIdentity, ItemState> edit;
        private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;
        private readonly IReadOnlyList<IListener> listeners;
        private readonly NextState next;

        private Shape(
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits,
            CollectionEdit<int, ItemIdentity, ItemState> edit,
            NextState next,
            IReadOnlyList<Cell<Maybe<ItemState>>> cells,
            IReadOnlyList<IListener> listeners)
        {
            this.edits = edits;
            this.edit = edit;
            this.next = next;
            this.cells = cells;
            this.listeners = listeners;
        }

        /// <summary>Sends the edit, with a different score each time.</summary>
        internal void Edit(int score)
        {
            this.next.State = new ItemState(name: "edited", score: score, isFrozen: false);
            this.edits.Send(this.edit);
        }

        internal void Release()
        {
            foreach (IListener listener in this.listeners)
            {
                listener.Dispose();
            }

            GC.KeepAlive(this.cells);
        }

        internal static Shape Build(Where where, int cellCount, int editedKeyCount, bool listen)
        {
            List<Item<ItemIdentity, ItemState>> items = new(ItemCount);

            for (int number = 0; number < ItemCount; number++)
            {
                items.Add(
                    new Item<ItemIdentity, ItemState>(
                        identity: ItemSeed.Identity(number),
                        state: ItemSeed.State(number)));
            }

            IReadOnlyList<int> keys = ItemSeed.ObservedKeys(itemCount: ItemCount, observerCount: cellCount);

            return Transaction.Run(() =>
            {
                StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
                    Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

                ReactiveCollection<int, ItemIdentity, ItemState> collection =
                    ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                        keySelector: static identity => identity.Number,
                        initialItems: items,
                        edits);

                ReactiveCollection<int, ItemIdentity, ItemState> observed =
                    where switch
                    {
                        Where.Filter => collection.Filter(static (_, state) => !state.IsFrozen),
                        Where.Sort => collection.SortBy(static (_, state) => state.Score),
                        _ => collection
                    };

                List<Cell<Maybe<ItemState>>> cells = [.. keys.Select(observed.StateCell)];

                List<IListener> listeners =
                    listen
                        ?
                        [
                            .. cells.Select(static cell =>
                                cell.Updates()
                                    .Listen(static _ =>
                                    {
                                    }))
                        ]
                        : [];

                NextState next = new();

                // The edited keys have cells, and they are at equal distances across the keys that have cells.
                int step = keys.Count / editedKeyCount;

                return new Shape(
                    edits: edits,
                    edit: BuildEdit(keys: [.. Enumerable.Range(start: 0, count: editedKeyCount).Select(index => keys[index * step])], next: next),
                    next: next,
                    cells: cells,
                    listeners: listeners);
            });
        }

        /// <summary>
        ///     Built one time, thus the measurement does not include the construction
        ///     of the edit. Each transform reads the state that the last call to
        ///     <see cref="Edit" /> set.
        /// </summary>
        private static CollectionEdit<int, ItemIdentity, ItemState> BuildEdit(IReadOnlyCollection<int> keys, NextState next)
        {
            Dictionary<int, Func<ItemState, ItemState>> updates = new(keys.Count);

            foreach (int key in keys)
            {
                updates.Add(key: key, value: _ => next.State);
            }

            return new CollectionEdit<int, ItemIdentity, ItemState>(updates: updates, adds: [], removes: []);
        }

        /// <summary>The state that the next edit writes to each of its keys.</summary>
        private sealed class NextState
        {
            internal ItemState State { get; set; } = ItemSeed.State(0);
        }
    }
}
