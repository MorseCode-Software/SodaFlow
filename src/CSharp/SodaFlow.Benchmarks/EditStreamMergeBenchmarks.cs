using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;
using SodaFlow.Collections;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What one edit costs on its way into a collection, as the number of edit
///     streams the collection was made with grows.
/// </summary>
/// <remarks>
///     <para>
///         A collection merges its edit streams into one before it resolves an
///         edit. The shape of that merge sets how many nodes an edit crosses
///         before the resolution. A chain of pairwise merges costs an edit on the
///         first stream one node for each other stream, and a tree costs it about
///         log₂ of the count; one node that takes every stream costs it one.
///         The collection holds a thousand items and one observed key, thus the
///         work after the merge is the same in each arm and small.
///     </para>
///     <para>
///         The first stream and the last stream are the two ends of a chain. An
///         edit on the first crosses every merge, and an edit on the last crosses
///         one. The two arms show the spread, and a shape where the two agree has
///         no spread.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class EditStreamMergeBenchmarks
{
    private const int ItemCount = 1_000;

    private const int EditedKey = 0;

    private int nextScore;

    // Populated fully in the setup. Built small here, thus the fields never have to be nullable.
    private Shape shape = Shape.Build(1);

    /// <summary>How many edit streams the collection was made with.</summary>
    [Params(1, 2, 8, 32)]
    public int StreamCount { get; [UsedImplicitly] set; }

    [GlobalSetup]
    public void Setup() => this.shape = Shape.Build(this.StreamCount);

    [GlobalCleanup]
    public void Cleanup() => this.shape.Dispose();

    [Benchmark(Description = "edit through the first stream")]
    public void EditThroughFirstStream() => this.shape.Edit(streamIndex: 0, score: ++this.nextScore);

    [Benchmark(Description = "edit through the last stream")]
    public void EditThroughLastStream() =>
        this.shape.Edit(streamIndex: this.StreamCount - 1, score: ++this.nextScore);

    private sealed class Shape
    {
        private readonly IListener listener;
        private readonly IReadOnlyList<StreamSink<CollectionEdit<int, ItemIdentity, ItemState>>> sinks;

        private Shape(
            IReadOnlyList<StreamSink<CollectionEdit<int, ItemIdentity, ItemState>>> sinks,
            IListener listener)
        {
            this.sinks = sinks;
            this.listener = listener;
        }

        internal void Edit(int streamIndex, int score) =>
            this.sinks[streamIndex]
                .Send(
                    CollectionEdit<int, ItemIdentity, ItemState>.Update(
                        key: EditedKey,
                        transform: state => new ItemState(name: state.Name, score: score, isFrozen: state.IsFrozen)));

        internal void Dispose() => this.listener.Unlisten();

        internal static Shape Build(int streamCount)
        {
            List<Item<ItemIdentity, ItemState>> items = new(ItemCount);

            for (int number = 0; number < ItemCount; number++)
            {
                items.Add(
                    new Item<ItemIdentity, ItemState>(
                        identity: ItemSeed.Identity(number),
                        state: ItemSeed.State(number)));
            }

            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>>[] sinks =
                new StreamSink<CollectionEdit<int, ItemIdentity, ItemState>>[streamCount];

            for (int index = 0; index < streamCount; index++)
            {
                sinks[index] = Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();
            }

            return Transaction.Run(() =>
            {
                ReactiveCollection<int, ItemIdentity, ItemState> collection =
                    ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                        keySelector: static identity => identity.Number,
                        initialItems: items,
                        // ReSharper disable once CoVariantArrayConversion - The array is only read.
                        editStreams: sinks);

                IListener listener =
                    collection.StateCell(EditedKey)
                        .Updates()
                        .Listen(static _ =>
                        {
                        });

                return new Shape(sinks: sinks, listener: listener);
            });
        }
    }
}
