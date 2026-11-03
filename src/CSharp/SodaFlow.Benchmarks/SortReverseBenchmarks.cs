using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;
using SodaFlow.Collections;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What a change of direction on a sort costs, against a change of sort
///     value, and against the reverse of the sorted set by itself.
/// </summary>
/// <remarks>
///     <para>
///         A sort stage that gets an order over the same value in the opposite
///         direction does not sort each key again. It reads the set that it holds
///         from its end, through a view that costs nothing to make, and then it
///         reports a reset. The first arm measures that toggle through the stage.
///         The second arm puts a <c>Take</c> after the sort, which is the shape
///         of a paged screen. The third arm changes the sort value, which rebuilds
///         the stage, and it is the cost that the reverse prevents.
///     </para>
///     <para>
///         Before the view, the stage copied the set into a new sorted set. The
///         fourth arm is that copy, on a set of the same size with the same
///         recipe, and no stage around it. It was 85 percent of the first arm, and
///         it stays here as the figure that the view removed.
///     </para>
///     <para>
///         The listener in the first three arms reads nothing, thus those arms
///         measure the stage and not a consumer. A consumer that reads the full
///         list after the toggle pays a walk of the keys. The view walks them in
///         runs of equal sort values, where the copy walked a plain set. The fifth
///         arm toggles and then reads each key, which is what a bound list does at
///         a reset.
///     </para>
///     <para>
///         An order with more than one level reverses when each level changes
///         direction. The sixth arm toggles each of two levels. Before an order kept
///         its levels, that toggle rebuilt the stage at the cost of the third arm.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class SortReverseBenchmarks
{
    /// <summary>
    ///     One delegate for the two directions, because the stage reverses only when
    ///     the two orders hold the same selector instance.
    /// </summary>
    private static readonly Func<ItemIdentity, ItemState, int> ByScore = static (_, state) => state.Score;

    private static readonly Func<ItemIdentity, int> ByNumber = static identity => identity.Number;

    private static readonly KeyOrder<int, ItemIdentity, ItemState> ScoreAscending =
        KeyOrder<int, ItemIdentity, ItemState>.By(
            selector: ByScore,
            sortComparer: Comparer<int>.Default,
            keyComparer: Comparer<int>.Default,
            isDescending: false);

    private static readonly KeyOrder<int, ItemIdentity, ItemState> ScoreDescending =
        KeyOrder<int, ItemIdentity, ItemState>.By(
            selector: ByScore,
            sortComparer: Comparer<int>.Default,
            keyComparer: Comparer<int>.Default,
            isDescending: true);

    private static readonly KeyOrder<int, ItemIdentity, ItemState> NumberAscending =
        KeyOrder<int, ItemIdentity, ItemState>.ByIdentity(
            selector: ByNumber,
            sortComparer: Comparer<int>.Default,
            keyComparer: Comparer<int>.Default,
            isDescending: false);

    /// <summary>
    ///     The two-level orders. Each score is unique, thus the second level never
    ///     decides. The arm measures the identification of the order and the read
    ///     of the set, and not the second level.
    /// </summary>
    private static readonly KeyOrder<int, ItemIdentity, ItemState> ScoreThenNumberAscending =
        ScoreAscending.ThenByIdentity(selector: ByNumber, sortComparer: Comparer<int>.Default, isDescending: false);

    private static readonly KeyOrder<int, ItemIdentity, ItemState> ScoreThenNumberDescending =
        ScoreDescending.ThenByIdentity(selector: ByNumber, sortComparer: Comparer<int>.Default, isDescending: true);

    private int directionCount;

    private int pagedDirectionCount;

    private int readDirectionCount;

    private int twoLevelDirectionCount;

    private int setDirectionCount;

    private int valueCount;

    // Populated fully in the setup. Built small here so the fields never have to be nullable.
    private SortedViewShape sorted = SortedViewShape.Build(itemCount: 1, limit: 0, order: ScoreAscending);

    private SortedViewShape paged = SortedViewShape.Build(itemCount: 1, limit: 1, order: ScoreAscending);

    private SortedViewShape resorted = SortedViewShape.Build(itemCount: 1, limit: 0, order: ScoreAscending);

    private SortedViewShape read = SortedViewShape.Build(itemCount: 1, limit: 0, order: ScoreAscending);

    private SortedViewShape twoLevel = SortedViewShape.Build(itemCount: 1, limit: 0, order: ScoreThenNumberAscending);

    private ImmutableSortedSet<Entry> set = ImmutableSortedSet<Entry>.Empty;

    /// <summary>How many items the collection holds.</summary>
    [Params(10_000, 100_000)]
    public int ItemCount { get; [UsedImplicitly] set; }

    /// <summary>
    ///     Builds the shapes, and refuses to run if a toggle does not reverse the
    ///     view.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.sorted = SortedViewShape.Build(itemCount: this.ItemCount, limit: 0, order: ScoreAscending);
        this.paged = SortedViewShape.Build(itemCount: this.ItemCount, limit: ViewSeed.Limit, order: ScoreAscending);
        this.resorted = SortedViewShape.Build(itemCount: this.ItemCount, limit: 0, order: ScoreAscending);
        this.read = SortedViewShape.Build(itemCount: this.ItemCount, limit: 0, order: ScoreAscending);
        this.twoLevel = SortedViewShape.Build(itemCount: this.ItemCount, limit: 0, order: ScoreThenNumberAscending);

        this.set =
            Enumerable
                .Range(start: 0, count: this.ItemCount)
                .Select(static number => new Entry(key: number, score: number))
                .ToImmutableSortedSet(new EntryComparer(isDescending: false));

        CheckToggleReverses(shape: this.sorted, descending: ScoreDescending, ascending: ScoreAscending);

        CheckToggleReverses(
            shape: this.twoLevel,
            descending: ScoreThenNumberDescending,
            ascending: ScoreThenNumberAscending);
    }

    /// <summary>
    ///     Toggles a shape to <paramref name="descending" /> and back, and throws an
    ///     exception if the view does not start at the last key between the two.
    /// </summary>
    private static void CheckToggleReverses(
        SortedViewShape shape,
        KeyOrder<int, ItemIdentity, ItemState> descending,
        KeyOrder<int, ItemIdentity, ItemState> ascending)
    {
        if (shape.FirstKey != 0)
        {
            throw new InvalidOperationException(
                $"The ascending sort starts at key {shape.FirstKey}, and the seed scores each item with "
                + "its own number, so it should start at 0.");
        }

        shape.Order(descending);

        if (shape.FirstKey != shape.Count - 1)
        {
            throw new InvalidOperationException(
                $"The descending sort starts at key {shape.FirstKey} and not {shape.Count - 1}, so the "
                + "toggle did not reverse the view.");
        }

        shape.Order(ascending);
    }

    /// <summary>A change of direction on the same sort value, which reverses the set.</summary>
    [Benchmark(Description = "toggle the direction", Baseline = true)]
    public void ToggleDirection() => this.sorted.Order(NextDirection(ref this.directionCount));

    /// <summary>The same toggle, with a window of twenty keys after the sort.</summary>
    [Benchmark(Description = "toggle the direction, then take a page")]
    public void ToggleDirectionPaged() => this.paged.Order(NextDirection(ref this.pagedDirectionCount));

    /// <summary>A change of sort value, which files each key again.</summary>
    [Benchmark(Description = "change the sort value")]
    public void ChangeSortValue()
    {
        this.valueCount++;
        this.resorted.Order(this.valueCount % 2 == 0 ? ScoreAscending : NumberAscending);
    }

    /// <summary>The toggle, and then a read of each key, as a bound list does at a reset.</summary>
    [Benchmark(Description = "toggle the direction, then read every key")]
    public int ToggleDirectionAndRead()
    {
        this.read.Order(NextDirection(ref this.readDirectionCount));

        return this.read.Sum();
    }

    /// <summary>A change of direction at each of two levels, which also reverses the set.</summary>
    [Benchmark(Description = "toggle the direction of two levels")]
    public void ToggleTwoLevels() =>
        this.twoLevel.Order(
            ++this.twoLevelDirectionCount % 2 == 1 ? ScoreThenNumberDescending : ScoreThenNumberAscending);

    /// <summary>The copy that the stage made before the view, with no stage around it.</summary>
    [Benchmark(Description = "copy the set reversed, the former recipe")]
    public ImmutableSortedSet<Entry> ReverseSetAlone()
    {
        this.setDirectionCount++;

        this.set =
            this.set
                .ToBuilder()
                .Reverse()
                .ToImmutableSortedSet(new EntryComparer(isDescending: this.setDirectionCount % 2 == 1));

        return this.set;
    }

    /// <summary>
    ///     Descending and ascending, alternating. A toggle that does not alternate
    ///     gives the stage an order equivalent to the one it holds, and the stage
    ///     then does nothing.
    /// </summary>
    private static KeyOrder<int, ItemIdentity, ItemState> NextDirection(ref int count)
    {
        count++;

        return count % 2 == 1 ? ScoreDescending : ScoreAscending;
    }

    /// <summary>
    ///     An entry of the set in the fourth arm. It has the two parts of the entry
    ///     that a sort stage holds: a key and a sort value.
    /// </summary>
    // Public, because the arm returns the set so that the runtime does not remove the work.
    // ReSharper disable once MemberCanBeInternal
    public sealed class Entry
    {
        internal Entry(int key, int score)
        {
            this.Key = key;
            this.Score = score;
        }

        internal int Key { get; }

        internal int Score { get; }
    }

    private sealed class EntryComparer : IComparer<Entry>
    {
        private readonly bool isDescending;

        internal EntryComparer(bool isDescending) => this.isDescending = isDescending;

        public int Compare(Entry? x, Entry? y)
        {
            if (x is null || y is null)
            {
                throw new ArgumentNullException(x is null ? nameof(x) : nameof(y));
            }

            int result = x.Score.CompareTo(y.Score);

            if (this.isDescending)
            {
                result = -result;
            }

            return result != 0 ? result : x.Key.CompareTo(y.Key);
        }
    }
}

/// <summary>
///     A collection sorted by an order cell, with an optional <c>Take</c> after
///     the sort, and a listener on the view.
/// </summary>
internal sealed class SortedViewShape
{
    // Load-bearing, as in the other shapes.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;
    private readonly CellSink<KeyOrder<int, ItemIdentity, ItemState>> order;
    private readonly ReactiveCollection<int, ItemIdentity, ItemState> view;

    private SortedViewShape(
        CellSink<KeyOrder<int, ItemIdentity, ItemState>> order,
        ReactiveCollection<int, ItemIdentity, ItemState> view,
        IListener listener)
    {
        this.order = order;
        this.view = view;
        this.listener = listener;
    }

    internal int FirstKey => this.view.KeysCell.Sample()[0];

    internal int Count => this.view.KeysCell.Sample().Count;

    /// <summary>Reads each key one time, and answers with their sum, thus the runtime keeps the work.</summary>
    internal int Sum() => this.view.KeysCell.Sample().Sum();

    internal void Order(KeyOrder<int, ItemIdentity, ItemState> order) => this.order.Send(order);

    /// <summary>
    ///     Builds the shape, sorted by <paramref name="order" />. A
    ///     <paramref name="limit" /> of zero means no <c>Take</c> after the sort.
    /// </summary>
    internal static SortedViewShape Build(int itemCount, int limit, KeyOrder<int, ItemIdentity, ItemState> order)
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

            CellSink<KeyOrder<int, ItemIdentity, ItemState>> orderCell = Cell.CreateSink(order);

            ReactiveCollection<int, ItemIdentity, ItemState> sorted = collection.SortBy(orderCell);

            ReactiveCollection<int, ItemIdentity, ItemState> view = limit > 0 ? sorted.Take(limit) : sorted;

            return new SortedViewShape(
                order: orderCell,
                view: view,
                listener: view.KeyChangesStream.ListenStrong(static _ =>
                {
                }));
        });
    }
}
