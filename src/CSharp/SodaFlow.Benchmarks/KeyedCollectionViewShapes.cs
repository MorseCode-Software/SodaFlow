using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using SodaFlow.Collections;

namespace SodaFlow.Benchmarks;

/// <summary>
///     The two ways to keep "the top twenty unfrozen accounts by balance" up to date as the
///     collection underneath it changes.
/// </summary>
/// <remarks>
///     <para>
///         <b>Re-derived</b> is what a lift over the whole collection gives you: hold every item in
///         one cell, and map it through <c>Where</c>, <c>OrderByDescending</c> and <c>Take</c>. It
///         is three lines, it is obviously correct, and it does all of that work again for every
///         edit, however small. The version here is the charitable one — the items live in an
///         immutable dictionary so that applying the edit itself is O(log32 n) rather than a copy
///         of the whole list, which leaves the re-derivation as the thing actually being measured.
///     </para>
///     <para>
///         <b>Chained</b> is <c>Filter</c>, then <c>SortByDescending</c>, then <c>Take</c>. Each
///         stage keeps its own ordered key set and applies the operations from the stage above, so
///         an edit re-files one key rather than re-sorting a collection.
///     </para>
///     <para>
///         Both are asked for the same answer, and <see cref="IKeyedCollectionViewShape.Keys" />
///         exists so the benchmark can check in its setup that they give it. A comparison between
///         two things computing different results would not be worth running.
///     </para>
/// </remarks>
internal interface IKeyedCollectionViewShape
{
    /// <summary>The view's keys, in order, as they stand.</summary>
    IReadOnlyList<int> Keys { get; }

    /// <summary>Replaces one item's state, which may move it within the view or out of it.</summary>
    void Replace(int key, ItemState state);

    /// <summary>
    ///     Adds an item and removes it again, which is two structural edits that leave the
    ///     collection the size it started. A benchmark that only added would measure a collection
    ///     growing under it.
    /// </summary>
    void AddAndRemove(int key, ItemState state);

    /// <summary>Changes what the filter is filtering on, which rebuilds the stage.</summary>
    void SetThreshold(int threshold);
}

/// <summary>Shared by the two view shapes, so they are asked for the same thing.</summary>
internal static class ViewSeed
{
    /// <summary>How many rows the view keeps — a screenful, as in the other benchmarks.</summary>
    internal const int Limit = 20;

    /// <summary>
    ///     Low enough that nearly everything passes, so the filter is not quietly doing the
    ///     <c>Take</c>'s job for it.
    /// </summary>
    internal const int InitialThreshold = 0;

    internal static bool Passes(ItemState state, int threshold) =>
        !state.IsFrozen && state.Score >= threshold;
}

/// <summary>
///     One cell holding every item, mapped through <c>Where</c>, <c>OrderByDescending</c> and
///     <c>Take</c> — re-derived in full on every edit.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class RederivedViewShape : IKeyedCollectionViewShape
{
    private readonly CellSink<ImmutableDictionary<int, Item<ItemIdentity, ItemState>>> items;
    private readonly CellSink<int> threshold;
    private readonly Cell<IReadOnlyList<int>> view;

    // Load-bearing: the map above is only evaluated because something is listening to it, and a
    // benchmark measuring a projection nobody asked for would measure nothing.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;

    private RederivedViewShape(
        CellSink<ImmutableDictionary<int, Item<ItemIdentity, ItemState>>> items,
        CellSink<int> threshold,
        Cell<IReadOnlyList<int>> view,
        IListener listener)
    {
        this.items = items;
        this.threshold = threshold;
        this.view = view;
        this.listener = listener;
    }

    public IReadOnlyList<int> Keys => this.view.Sample();

    internal static RederivedViewShape Build(int itemCount)
    {
        ImmutableDictionary<int, Item<ItemIdentity, ItemState>>.Builder builder =
            ImmutableDictionary.CreateBuilder<int, Item<ItemIdentity, ItemState>>();

        for (int number = 0; number < itemCount; number++)
        {
            builder.Add(
                number,
                new Item<ItemIdentity, ItemState>(ItemSeed.Identity(number), ItemSeed.State(number)));
        }

        return Transaction.Run(() =>
        {
            CellSink<ImmutableDictionary<int, Item<ItemIdentity, ItemState>>> items =
                Cell.CreateSink(builder.ToImmutable());

            CellSink<int> threshold = Cell.CreateSink(ViewSeed.InitialThreshold);

            Cell<IReadOnlyList<int>> view = items.Lift<
                ImmutableDictionary<int, Item<ItemIdentity, ItemState>>,
                int,
                IReadOnlyList<int>>(
                threshold,
                static (map, limit) =>
                [
                    .. map.Values
                        .Where(item => ViewSeed.Passes(item.State, limit))
                        .OrderByDescending(static item => item.State.Score)
                        .ThenBy(static item => item.Identity.Number)
                        .Take(ViewSeed.Limit)
                        .Select(static item => item.Identity.Number)
                ]);

            return new RederivedViewShape(
                items,
                threshold,
                view,
                view.Updates().ListenStrong(static _ => { }));
        });
    }

    public void Replace(int key, ItemState state) =>
        this.items.Send(
            this.items.Sample().SetItem(
                key,
                new Item<ItemIdentity, ItemState>(ItemSeed.Identity(key), state)));

    public void AddAndRemove(int key, ItemState state)
    {
        this.items.Send(
            this.items.Sample().Add(
                key,
                new Item<ItemIdentity, ItemState>(ItemSeed.Identity(key), state)));

        this.items.Send(this.items.Sample().Remove(key));
    }

    public void SetThreshold(int threshold) => this.threshold.Send(threshold);
}

/// <summary>Which halves of an item the chain's two stages read.</summary>
internal enum ChainStyle
{
    /// <summary>Filter on the score, sort on the score.</summary>
    ByState,

    /// <summary>Filter on the score, sort on the number.</summary>
    SortByIdentity,

    /// <summary>
    ///     Neither stage reads the state, so nothing a state edit carries can reach either of them
    ///     beyond the update they are obliged to forward.
    /// </summary>
    ByIdentity,

    /// <summary>
    ///     A filter that keeps half of what it sees, tested against the state, over an identity
    ///     sort. Paired with <see cref="SelectiveByIdentity" />, which keeps the same half by asking the
    ///     identity instead.
    /// </summary>
    SelectiveByState,

    /// <summary>The same half, selected from the identity.</summary>
    SelectiveByIdentity,
}

/// <summary>
///     <c>Filter</c>, then <c>SortByDescending</c>, then <c>Take</c> — each stage keeping its own
///     ordered key set and adjusting it.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class ChainedViewShape : IKeyedCollectionViewShape
{
    private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;
    private readonly CellSink<int> threshold;
    private readonly IReactiveCollection<int, ItemIdentity, ItemState> view;

    // Load-bearing, as in the other shape.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;

    private ChainedViewShape(
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        CellSink<int> threshold,
        IReactiveCollection<int, ItemIdentity, ItemState> view,
        IListener listener)
    {
        this.edits = edits;
        this.threshold = threshold;
        this.view = view;
        this.listener = listener;
    }

    public IReadOnlyList<int> Keys => [.. this.view.KeysCell.Sample()];

    /// <param name="itemCount">How many items the collection holds.</param>
    /// <param name="style">
    ///     Which halves the two stages read. Every arrangement holds the same keys in the same
    ///     places: the seed gives every item a score equal to its number, and the initial threshold
    ///     admits all of them - so what differs between them is only which half each stage reads,
    ///     and therefore how much of a state edit it can ignore.
    /// </param>
    internal static ChainedViewShape Build(int itemCount, ChainStyle style)
    {
        List<Item<ItemIdentity, ItemState>> items = new(itemCount);

        for (int number = 0; number < itemCount; number++)
        {
            items.Add(new Item<ItemIdentity, ItemState>(
                ItemSeed.Identity(number),
                ItemSeed.State(number)));
        }

        return Transaction.Run(() =>
        {
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
                Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

            ReactiveCollection<int, ItemIdentity, ItemState> collection =
                ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    static identity => identity.Number,
                    items,
                    edits);

            CellSink<int> threshold = Cell.CreateSink(ViewSeed.InitialThreshold);

            // The identity filter admits everything, as the threshold one does at its initial
            // value. What is being measured is not what the predicate answers - the ordinary
            // filter looks the item up and asks either way - but whether it has to ask at all.
            // The two selective arrangements keep the same items - the seed gives every item a
            // score equal to its number, so even scores and even numbers are the same half - and
            // differ only in which half they had to read to find that out.
            IReactiveCollection<int, ItemIdentity, ItemState> filtered = style switch
            {
                ChainStyle.ByIdentity => collection.FilterByIdentity(static _ => true),
                ChainStyle.SelectiveByIdentity =>
                    collection.FilterByIdentity(static identity => identity.Number % 2 == 0),
                ChainStyle.SelectiveByState =>
                    collection.Filter(static (_, state) => state.Score % 2 == 0),
                _ => collection.Filter(
                    threshold,
                    static (limit, _, state) => ViewSeed.Passes(state, limit)),
            };

            IReactiveCollection<int, ItemIdentity, ItemState> view =
                (style == ChainStyle.ByState
                    ? filtered.SortByDescending(static (_, state) => state.Score)
                    : filtered.SortByIdentityDescending(static identity => identity.Number))
                .Take(ViewSeed.Limit);

            return new ChainedViewShape(
                edits,
                threshold,
                view,
                view.ChangesStream.ListenStrong(static _ => { }));
        });
    }

    public void Replace(int key, ItemState state) =>
        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Update(key, _ => state));

    public void AddAndRemove(int key, ItemState state)
    {
        this.edits.Send(
            CollectionEdit<int, ItemIdentity, ItemState>.Add(
                new Item<ItemIdentity, ItemState>(ItemSeed.Identity(key), state)));

        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Remove(key));
    }

    public void SetThreshold(int threshold) => this.threshold.Send(threshold);
}

/// <summary>
///     The collection with no view stages at all, listening to its own change stream — the floor
///     an edit cannot go below however little the stages above it choose to do.
/// </summary>
/// <remarks>
///     <para>
///         This exists because without it the view benchmarks cannot be read. An edit through a
///         chain pays for the transaction, the send, the trie write to the state map, the snapshot
///         and the change object before any stage is consulted, and at ten thousand items that is
///         2.8 of the 6.5 microseconds an excluded-key edit costs. Report the 6.5 and a
///         stage-level difference of a fifth of a microsecond reads as noise; subtract the floor
///         and the same difference is six percent of what the chain actually does.
///     </para>
///     <para>
///         Subtract this from the arms beside it and what is left is what the chain actually
///         costs.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class RootOnlyViewShape : IKeyedCollectionViewShape
{
    private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;
    private readonly ReactiveCollection<int, ItemIdentity, ItemState> collection;

    // Load-bearing, as in the other shapes: something must be listening or nothing is evaluated.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;

    private RootOnlyViewShape(
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        ReactiveCollection<int, ItemIdentity, ItemState> collection,
        IListener listener)
    {
        this.edits = edits;
        this.collection = collection;
        this.listener = listener;
    }

    public IReadOnlyList<int> Keys => [.. this.collection.KeysCell.Sample()];

    internal static RootOnlyViewShape Build(int itemCount)
    {
        List<Item<ItemIdentity, ItemState>> items = new(itemCount);

        for (int number = 0; number < itemCount; number++)
        {
            items.Add(new Item<ItemIdentity, ItemState>(
                ItemSeed.Identity(number),
                ItemSeed.State(number)));
        }

        return Transaction.Run(() =>
        {
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
                Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

            ReactiveCollection<int, ItemIdentity, ItemState> collection =
                ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    static identity => identity.Number,
                    items,
                    edits);

            return new RootOnlyViewShape(
                edits,
                collection,
                collection.ChangesStream.ListenStrong(static _ => { }));
        });
    }

    public void Replace(int key, ItemState state) =>
        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Update(key, _ => state));

    public void AddAndRemove(int key, ItemState state)
    {
        this.edits.Send(
            CollectionEdit<int, ItemIdentity, ItemState>.Add(
                new Item<ItemIdentity, ItemState>(ItemSeed.Identity(key), state)));

        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Remove(key));
    }

    /// <inheritdoc />
    /// <remarks>Nothing here filters, so there is no threshold to change.</remarks>
    public void SetThreshold(int threshold)
    {
    }
}

/// <summary>A page of a sorted collection, the two ways of keeping one.</summary>
/// <remarks>
///     Both hold the same page of the same ordering, and the benchmark checks that in its setup
///     before timing either.
/// </remarks>
internal interface IKeyedPagingShape
{
    /// <summary>The page's keys, in order, as they stand.</summary>
    IReadOnlyList<int> Keys { get; }

    /// <summary>Moves the window to a new offset.</summary>
    void TurnTo(int offset);

    /// <summary>Replaces one item's state.</summary>
    void Replace(int key, ItemState state);
}

/// <summary>
///     One cell holding every item, re-sorted and re-windowed on every page turn and every edit.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class RederivedPageShape : IKeyedPagingShape
{
    private readonly CellSink<ImmutableDictionary<int, Item<ItemIdentity, ItemState>>> items;
    private readonly CellSink<int> offset;
    private readonly Cell<IReadOnlyList<int>> page;

    // Load-bearing, as in the other shapes: without a listener the projection is never evaluated.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;

    private RederivedPageShape(
        CellSink<ImmutableDictionary<int, Item<ItemIdentity, ItemState>>> items,
        CellSink<int> offset,
        Cell<IReadOnlyList<int>> page,
        IListener listener)
    {
        this.items = items;
        this.offset = offset;
        this.page = page;
        this.listener = listener;
    }

    public IReadOnlyList<int> Keys => this.page.Sample();

    internal static RederivedPageShape Build(int itemCount)
    {
        ImmutableDictionary<int, Item<ItemIdentity, ItemState>>.Builder builder =
            ImmutableDictionary.CreateBuilder<int, Item<ItemIdentity, ItemState>>();

        for (int number = 0; number < itemCount; number++)
        {
            builder.Add(
                number,
                new Item<ItemIdentity, ItemState>(ItemSeed.Identity(number), ItemSeed.State(number)));
        }

        return Transaction.Run(() =>
        {
            CellSink<ImmutableDictionary<int, Item<ItemIdentity, ItemState>>> items =
                Cell.CreateSink(builder.ToImmutable());

            CellSink<int> offset = Cell.CreateSink(0);

            Cell<IReadOnlyList<int>> page = items.Lift<
                ImmutableDictionary<int, Item<ItemIdentity, ItemState>>,
                int,
                IReadOnlyList<int>>(
                offset,
                static (map, at) =>
                [
                    .. map.Values
                        .OrderByDescending(static item => item.State.Score)
                        .Skip(at)
                        .Take(ViewSeed.Limit)
                        .Select(static item => item.Identity.Number)
                ]);

            return new RederivedPageShape(
                items,
                offset,
                page,
                page.Updates().ListenStrong(static _ => { }));
        });
    }

    public void TurnTo(int offset) => this.offset.Send(offset);

    public void Replace(int key, ItemState state) =>
        this.items.Send(
            this.items.Sample().SetItem(
                key,
                new Item<ItemIdentity, ItemState>(ItemSeed.Identity(key), state)));
}

/// <summary>
///     <c>SortByDescending</c> then <c>Slice</c>, where turning the page changes the slice's offset
///     cell and nothing else.
/// </summary>
/// <remarks>
///     A criteria change rebuilds the stage that owns the criteria, and for most stages that is the
///     expensive path - a filter files every surviving key into a fresh ordered set. A slice's
///     rebuild is a <c>RangeKeys</c> over the ordering it already had, which is a lazy view and
///     costs nothing to construct. That is the asymmetry the page-turn benchmarks exist to show.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class ChainedPageShape : IKeyedPagingShape
{
    private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;
    private readonly CellSink<int> offset;
    private readonly IReactiveCollection<int, ItemIdentity, ItemState> page;

    // Load-bearing, as above.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;

    private ChainedPageShape(
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        CellSink<int> offset,
        IReactiveCollection<int, ItemIdentity, ItemState> page,
        IListener listener)
    {
        this.edits = edits;
        this.offset = offset;
        this.page = page;
        this.listener = listener;
    }

    public IReadOnlyList<int> Keys => [.. this.page.KeysCell.Sample()];

    internal static ChainedPageShape Build(int itemCount)
    {
        List<Item<ItemIdentity, ItemState>> items = new(itemCount);

        for (int number = 0; number < itemCount; number++)
        {
            items.Add(new Item<ItemIdentity, ItemState>(
                ItemSeed.Identity(number),
                ItemSeed.State(number)));
        }

        return Transaction.Run(() =>
        {
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
                Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

            ReactiveCollection<int, ItemIdentity, ItemState> collection =
                ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    static identity => identity.Number,
                    items,
                    edits);

            CellSink<int> offset = Cell.CreateSink(0);

            IReactiveCollection<int, ItemIdentity, ItemState> page = collection
                .SortByDescending(static (_, state) => state.Score)
                .Slice(offset, Cell.Constant(ViewSeed.Limit));

            return new ChainedPageShape(
                edits,
                offset,
                page,
                page.ChangesStream.ListenStrong(static _ => { }));
        });
    }

    public void TurnTo(int offset) => this.offset.Send(offset);

    public void Replace(int key, ItemState state) =>
        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Update(key, _ => state));
}
