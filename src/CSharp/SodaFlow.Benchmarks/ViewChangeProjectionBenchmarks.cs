using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;
using SodaFlow.Collections;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What one edit of many keys costs with many per-key cells on one view,
///     against the same cells on the collection.
/// </summary>
/// <remarks>
///     <para>
///         A per-key cell on a view hangs off the change stream of the view, and
///         at each change it asks the change for its key. The change answered
///         that with a scan of its operations. Thus, an edit of <i>n</i> keys with
///         <i>m</i> cells on the view cost <i>n × m</i> comparisons, and almost
///         each one found nothing. The root answers the same question with a
///         dictionary lookup. The difference between the two arms is that cost.
///     </para>
///     <para>
///         The collection holds twenty thousand items. A stage lists at most two
///         thousand operations at that size before it reports a reset. Thus, each
///         edit here stays a list of operations. The edited keys are the first
///         <see cref="EditedKeyCount" />, and the observed keys are at equal
///         distances across the collection. Thus, few of the cells find their key
///         in the change. That is the usual case for a bound page of rows.
///     </para>
///     <para>
///         The view is alive in the two arms, with a listener on its change
///         stream. What differs is only where the per-key cells hang.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class ViewChangeProjectionBenchmarks
{
    private const int ItemCount = 20_000;

    private int nextScore;

    // Populated fully in the setup. Built small here, thus the fields never have to be nullable.
    private Shape onCollection = Shape.Build(editedKeyCount: 1, observerCount: 1, throughView: false);
    private Shape throughView = Shape.Build(editedKeyCount: 1, observerCount: 1, throughView: true);

    /// <summary>How many keys one edit updates, in one transaction.</summary>
    [Params(10, 100, 1_000)]
    public int EditedKeyCount { get; [UsedImplicitly] set; }

    /// <summary>How many keys have a state cell with a listener.</summary>
    [Params(20, 100)]
    public int ObserverCount { get; [UsedImplicitly] set; }

    /// <summary>Stands the two shapes up.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.onCollection =
            Shape.Build(editedKeyCount: this.EditedKeyCount, observerCount: this.ObserverCount, throughView: false);

        this.throughView =
            Shape.Build(editedKeyCount: this.EditedKeyCount, observerCount: this.ObserverCount, throughView: true);
    }

    /// <summary>Releases the listeners that hold the two graphs up.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.onCollection.Release();
        this.throughView.Release();
    }

    /// <summary>One edit of the keys, with the cells on the collection.</summary>
    [Benchmark(Baseline = true, Description = "edit many keys, cells on the collection")]
    public void EditOnCollection() => this.onCollection.Edit(++this.nextScore);

    /// <summary>One edit of the keys, with the cells on a view.</summary>
    [Benchmark(Description = "edit many keys, cells through a view")]
    public void EditThroughView() => this.throughView.Edit(++this.nextScore);

    /// <summary>
    ///     A collection, a filter over it that keeps each item, the per-key cells with
    ///     their listeners, and one edit that updates the first keys.
    /// </summary>
    private sealed class Shape
    {
        private readonly CollectionEdit<int, ItemIdentity, ItemState> edit;
        private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;
        private readonly IReadOnlyList<IListener> listeners;
        private readonly NextState next;

        private Shape(
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits,
            CollectionEdit<int, ItemIdentity, ItemState> edit,
            NextState next,
            IReadOnlyList<IListener> listeners)
        {
            this.edits = edits;
            this.edit = edit;
            this.next = next;
            this.listeners = listeners;
        }

        /// <summary>
        ///     Sends the edit. A different score each time, thus nothing can stop at
        ///     equality.
        /// </summary>
        internal void Edit(int score)
        {
            this.next.State = new ItemState(name: "edited", score: score, isFrozen: false);
            this.edits.Send(this.edit);
        }

        internal void Release()
        {
            foreach (IListener listener in this.listeners)
            {
                listener.Unlisten();
            }
        }

        internal static Shape Build(int editedKeyCount, int observerCount, bool throughView)
        {
            List<Item<ItemIdentity, ItemState>> items = new(ItemCount);

            for (int number = 0; number < ItemCount; number++)
            {
                items.Add(
                    new Item<ItemIdentity, ItemState>(
                        identity: ItemSeed.Identity(number),
                        state: ItemSeed.State(number)));
            }

            return Transaction.Run(() =>
            {
                StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
                    Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

                ReactiveCollection<int, ItemIdentity, ItemState> collection =
                    ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                        keySelector: static identity => identity.Number,
                        initialItems: items,
                        edits);

                // A filter that reads the state, thus the stage tests each updated key again and
                // reports an update for it. The seed freezes no item, thus the view holds each key.
                ReactiveCollection<int, ItemIdentity, ItemState> view =
                    collection.Filter(static (_, state) => !state.IsFrozen);

                ReactiveCollection<int, ItemIdentity, ItemState> observed = throughView ? view : collection;

                List<IListener> listeners =
                [
                    .. ItemSeed.ObservedKeys(itemCount: ItemCount, observerCount: observerCount)
                        .Select(key =>
                            observed.StateCell(key)
                                .Updates()
                                .ListenStrong(static _ =>
                                {
                                })),
                    view.KeyChangesStream.ListenStrong(static _ =>
                    {
                    })
                ];

                NextState next = new();

                return new Shape(
                    edits: edits,
                    edit: BuildEdit(keyCount: editedKeyCount, next: next),
                    next: next,
                    listeners: listeners);
            });
        }

        /// <summary>
        ///     Built one time, thus the measurement does not include the construction
        ///     of the edit. Each transform reads the state that the last call to
        ///     <see cref="Edit" /> set.
        /// </summary>
        private static CollectionEdit<int, ItemIdentity, ItemState> BuildEdit(int keyCount, NextState next)
        {
            Dictionary<int, Func<ItemState, ItemState>> updates = new(keyCount);

            for (int key = 0; key < keyCount; key++)
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
