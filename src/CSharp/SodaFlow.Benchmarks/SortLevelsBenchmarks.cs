using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;
using SodaFlow.Collections;

namespace SodaFlow.Benchmarks;

/// <summary>
///     Measures a sort with one, two, and three levels: a sort of the full set again,
///     and one state edit that moves one key.
/// </summary>
/// <remarks>
///     <para>
///         The first level has many equal values, thus the next level decides most
///         comparisons. Two of the levels are <see cref="int" /> values, which an
///         order that holds its sort values as objects must box. The string level
///         uses the ordinal comparer, thus the cost of the comparison does not hide
///         the cost of the order.
///     </para>
///     <para>
///         Each sort of the full set changes the first level, thus the stage cannot
///         keep its entries and makes each of them again.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class SortLevelsBenchmarks
{
    private const int Buckets = 100;

    private const int OtherBuckets = 101;

    private static readonly Func<ItemIdentity, ItemState, int> ByBucket = static (_, state) => state.Score % Buckets;

    private static readonly Func<ItemIdentity, ItemState, int> ByOtherBucket =
        static (_, state) => state.Score % OtherBuckets;

    private static readonly Func<ItemIdentity, ItemState, string> ByName = static (_, state) => state.Name;

    private static readonly Func<ItemIdentity, int> ByNumber = static identity => identity.Number;

    private static readonly KeyOrder<int, ItemIdentity, ItemState>[] OneLevel =
        [KeyOrder<int, ItemIdentity, ItemState>.By(ByBucket), KeyOrder<int, ItemIdentity, ItemState>.By(ByOtherBucket)];

    private static readonly KeyOrder<int, ItemIdentity, ItemState>[] TwoLevels =
        [OneLevel[0].ThenByIdentity(ByNumber), OneLevel[1].ThenByIdentity(ByNumber)];

    private static readonly KeyOrder<int, ItemIdentity, ItemState>[] ThreeLevels =
    [
        OneLevel[0]
            .ThenBy(selector: ByName, sortComparer: StringComparer.Ordinal, isDescending: false)
            .ThenByIdentity(ByNumber),
        OneLevel[1]
            .ThenBy(selector: ByName, sortComparer: StringComparer.Ordinal, isDescending: false)
            .ThenByIdentity(ByNumber)
    ];

    private int oneLevelSorts;

    private int twoLevelSorts;

    private int threeLevelSorts;

    private int oneLevelMoves;

    private int twoLevelMoves;

    private int threeLevelMoves;

    // Populated fully in the setup. Built small here so the fields never have to be nullable.
    private SortLevelsShape oneLevel = SortLevelsShape.Build(itemCount: 1, order: OneLevel[0]);

    private SortLevelsShape twoLevels = SortLevelsShape.Build(itemCount: 1, order: TwoLevels[0]);

    private SortLevelsShape threeLevels = SortLevelsShape.Build(itemCount: 1, order: ThreeLevels[0]);

    private SortLevelsShape oneLevelEdited = SortLevelsShape.Build(itemCount: 1, order: OneLevel[0]);

    private SortLevelsShape twoLevelsEdited = SortLevelsShape.Build(itemCount: 1, order: TwoLevels[0]);

    private SortLevelsShape threeLevelsEdited = SortLevelsShape.Build(itemCount: 1, order: ThreeLevels[0]);

    [Params(10_000, 100_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    [GlobalSetup]
    public void Setup()
    {
        this.oneLevel = SortLevelsShape.Build(itemCount: this.ItemCount, order: OneLevel[0]);
        this.twoLevels = SortLevelsShape.Build(itemCount: this.ItemCount, order: TwoLevels[0]);
        this.threeLevels = SortLevelsShape.Build(itemCount: this.ItemCount, order: ThreeLevels[0]);
        this.oneLevelEdited = SortLevelsShape.Build(itemCount: this.ItemCount, order: OneLevel[0]);
        this.twoLevelsEdited = SortLevelsShape.Build(itemCount: this.ItemCount, order: TwoLevels[0]);
        this.threeLevelsEdited = SortLevelsShape.Build(itemCount: this.ItemCount, order: ThreeLevels[0]);

        CheckSecondLevelDecides(shape: this.twoLevels);
        CheckSecondLevelDecides(shape: this.threeLevels);
    }

    // The scores are the numbers, thus the first bucket holds the numbers 0, 100, 200, and so on. A
    // sort that ignores the level after the first puts them in the sequence of the keys, which is the
    // same sequence. Thus, the check moves one of them to the end of its bucket and reads the result.
    private static void CheckSecondLevelDecides(SortLevelsShape shape)
    {
        if (shape.Count < 2 * Buckets)
        {
            return;
        }

        if (shape.KeyAt(index: 0) != 0 || shape.KeyAt(index: 1) != Buckets)
        {
            throw new InvalidOperationException(
                $"The sort starts with the keys {shape.KeyAt(index: 0)} and {shape.KeyAt(index: 1)}, and "
                + $"not with 0 and {Buckets}, so a level after the first does not decide.");
        }
    }

    [Benchmark(Description = "sort the full set again, one level", Baseline = true)]
    public void SortOneLevel() => this.oneLevel.Order(OneLevel[++this.oneLevelSorts % 2]);

    [Benchmark(Description = "sort the full set again, two levels")]
    public void SortTwoLevels() => this.twoLevels.Order(TwoLevels[++this.twoLevelSorts % 2]);

    [Benchmark(Description = "sort the full set again, three levels")]
    public void SortThreeLevels() => this.threeLevels.Order(ThreeLevels[++this.threeLevelSorts % 2]);

    [Benchmark(Description = "move one key with a state edit, one level")]
    public void MoveOneLevel() => this.oneLevelEdited.Move(step: ++this.oneLevelMoves);

    [Benchmark(Description = "move one key with a state edit, two levels")]
    public void MoveTwoLevels() => this.twoLevelsEdited.Move(step: ++this.twoLevelMoves);

    [Benchmark(Description = "move one key with a state edit, three levels")]
    public void MoveThreeLevels() => this.threeLevelsEdited.Move(step: ++this.threeLevelMoves);
}

/// <summary>A sorted view with a listener, an order that a benchmark changes, and edits.</summary>
internal sealed class SortLevelsShape
{
    private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;

    // Load-bearing, as in the other shapes.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;
    private readonly CellSink<KeyOrder<int, ItemIdentity, ItemState>> order;
    private readonly ReactiveCollection<int, ItemIdentity, ItemState> view;

    private SortLevelsShape(
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order,
        ReactiveCollection<int, ItemIdentity, ItemState> view,
        IListener listener)
    {
        this.edits = edits;
        this.order = order;
        this.view = view;
        this.listener = listener;
    }

    internal int Count => this.view.KeysCell.Sample().Count;

    internal int KeyAt(int index) => this.view.KeysCell.Sample()[index];

    internal void Order(KeyOrder<int, ItemIdentity, ItemState> order) => this.order.Send(order);

    /// <summary>Gives a new score to the key in the middle, which moves it to a different bucket.</summary>
    /// <param name="step">A number that is different at each call.</param>
    internal void Move(int step)
    {
        int key = this.Count / 2;

        this.edits.Send(
            CollectionEdit<int, ItemIdentity, ItemState>.Update(
                key: key,
                transform: state => new ItemState(name: state.Name, score: key + step, isFrozen: state.IsFrozen)));
    }

    internal static SortLevelsShape Build(int itemCount, KeyOrder<int, ItemIdentity, ItemState> order)
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
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
                Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

            ReactiveCollection<int, ItemIdentity, ItemState> collection =
                ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    keySelector: static identity => identity.Number,
                    initialItems: items,
                    edits);

            CellSink<KeyOrder<int, ItemIdentity, ItemState>> orderCell = Cell.CreateSink(order);

            ReactiveCollection<int, ItemIdentity, ItemState> view = collection.SortBy(orderCell);

            return new SortLevelsShape(
                edits: edits,
                order: orderCell,
                view: view,
                listener: view.KeyChangesStream.ListenStrong(static _ =>
                {
                }));
        });
    }
}
