using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using SodaFlow.Collections;

namespace SodaFlow.Benchmarks;

/// <summary>
///     The two ways to keep "the top twenty unfrozen accounts by balance" up to date as the collection below
///     it changes.
/// </summary>
/// <remarks>
///     <para>
///         <b>Re-derived</b> is what a lift over the full collection gives. It holds each item in one cell,
///         and maps it through <c>Where</c>, <c>OrderByDescending</c> and <c>Take</c>. It is three lines, it
///         is obviously correct, and it does all of that work again for each edit. The size of the edit makes
///         no difference. The version here is the charitable one. The items live in an immutable dictionary,
///         thus the edit
///         itself is <c>O(log32 n)</c> and not a copy of the full list. That leaves the re-derivation as the
///         thing this measures.
///     </para>
///     <para>
///         <b>Chained</b> is <c>Filter</c>, then <c>SortByDescending</c>, then <c>Take</c>. Each stage keeps
///         its own ordered key set and uses the operations from the stage above. Thus, an edit files one key
///         again, and does not sort a collection again.
///     </para>
///     <para>
///         The benchmark asks the two for the same answer, and <see cref="IKeyedCollectionViewShape.Keys" />
///         exists so the benchmark can check in its setup that they give it. A compare between two things
///         that give different results is not worth a run.
///     </para>
/// </remarks>
file interface IKeyedCollectionViewShape
{
    /// <summary>The keys of the view, in order, at this moment.</summary>
    // ReSharper disable once UnusedMemberInSuper.Global - Defines shape expected for implementers
    IReadOnlyList<int> Keys { get; }

    /// <summary>Replaces one item's state, which can move it in the view or out of it.</summary>
    // ReSharper disable once UnusedMemberInSuper.Global - Defines shape expected for implementers
    void Replace(int key, ItemState state);

    /// <summary>
    ///     Adds an item and removes it again, which is two structural edits that keep the collection
    ///     at the size it started. A benchmark that only adds measures a collection that grows below
    ///     it.
    /// </summary>
    // ReSharper disable once UnusedMemberInSuper.Global - Defines shape expected for implementers
    void AddAndRemove(int key, ItemState state);

    /// <summary>Changes what the filter is filtering on, which rebuilds the stage.</summary>
    // ReSharper disable once UnusedMemberInSuper.Global - Defines shape expected for implementers
    void SetThreshold(int threshold);
}

/// <summary>Shared by the two view shapes, thus the benchmark asks them the same question.</summary>
internal static class ViewSeed
{
    /// <summary>How many rows the view keeps — a screenful, as in the other benchmarks.</summary>
    internal const int Limit = 20;

    /// <summary>
    ///     Low, thus almost everything passes, so the filter is not quietly doing the
    ///     work of the <c>Take</c> for it.
    /// </summary>
    internal const int InitialThreshold = 0;

    internal static bool Passes(ItemState state, int threshold) => !state.IsFrozen && state.Score >= threshold;
}

/// <summary>
///     One cell holding each item, mapped through <c>Where</c>, <c>OrderByDescending</c> and
///     <c>Take</c> — re-derived in full on each edit.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class RederivedViewShape : IKeyedCollectionViewShape
{
    private readonly CellSink<ImmutableDictionary<int, Item<ItemIdentity, ItemState>>> items;

    // Load-bearing: the map above is only evaluated because something is listening to it, and a
    // benchmark that measures a projection nobody asked for measures nothing.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;
    private readonly CellSink<int> threshold;
    private readonly Cell<IReadOnlyList<int>> view;

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

    public void Replace(int key, ItemState state) =>
        this.items.Send(
            this.items.Sample()
                .SetItem(
                    key: key,
                    value: new Item<ItemIdentity, ItemState>(identity: ItemSeed.Identity(key), state: state)));

    public void AddAndRemove(int key, ItemState state)
    {
        this.items.Send(
            this.items.Sample()
                .Add(
                    key: key,
                    value: new Item<ItemIdentity, ItemState>(identity: ItemSeed.Identity(key), state: state)));

        this.items.Send(this.items.Sample().Remove(key));
    }

    public void SetThreshold(int threshold) => this.threshold.Send(threshold);

    internal static RederivedViewShape Build(int itemCount)
    {
        ImmutableDictionary<int, Item<ItemIdentity, ItemState>>.Builder builder =
            ImmutableDictionary.CreateBuilder<int, Item<ItemIdentity, ItemState>>();

        for (int number = 0; number < itemCount; number++)
        {
            builder.Add(
                key: number,
                value: new Item<ItemIdentity, ItemState>(
                    identity: ItemSeed.Identity(number),
                    state: ItemSeed.State(number)));
        }

        return Transaction.Run(() =>
        {
            CellSink<ImmutableDictionary<int, Item<ItemIdentity, ItemState>>> items =
                Cell.CreateSink(builder.ToImmutable());

            CellSink<int> threshold = Cell.CreateSink(ViewSeed.InitialThreshold);

            Cell<IReadOnlyList<int>> view =
                items.Lift<
                    ImmutableDictionary<int, Item<ItemIdentity, ItemState>>,
                    int,
                    IReadOnlyList<int>>(
                    c2: threshold,
                    f: static (map, limit) =>
                    [
                        .. map.Values
                            .Where(item => ViewSeed.Passes(state: item.State, threshold: limit))
                            .OrderByDescending(static item => item.State.Score)
                            .ThenBy(static item => item.Identity.Number)
                            .Take(ViewSeed.Limit)
                            .Select(static item => item.Identity.Number)
                    ]);

            return new RederivedViewShape(
                items: items,
                threshold: threshold,
                view: view,
                listener: view.Updates()
                    .ListenStrong(static _ =>
                    {
                    }));
        });
    }
}

/// <summary>Which halves of an item the chain's two stages read.</summary>
internal enum ChainStyle
{
    /// <summary>Filter on the score, sort on the score.</summary>
    ByState,

    /// <summary>Filter on the score, sort on the number.</summary>
    SortByIdentity,

    /// <summary>
    ///     No stage reads the state, thus nothing a state edit carries can get to one of them
    ///     more than the update they have to forward.
    /// </summary>
    ByIdentity,

    /// <summary>
    ///     A filter that keeps half of what it sees, tested against the state, over an identity
    ///     sort. Paired with <see cref="SelectiveByIdentity" />, which keeps the same half by asking the
    ///     the identity as an alternative.
    /// </summary>
    SelectiveByState,

    /// <summary>The same half, selected from the identity.</summary>
    SelectiveByIdentity
}

/// <summary>
///     <c>Filter</c>, then <c>SortByDescending</c>, then <c>Take</c> — each stage keeping its own
///     ordered key set and adjusting it.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class ChainedViewShape : IKeyedCollectionViewShape
{
    private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;

    // Load-bearing, as in the other shape.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;
    private readonly CellSink<int> threshold;
    private readonly ReactiveCollection<int, ItemIdentity, ItemState> view;

    private ChainedViewShape(
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        CellSink<int> threshold,
        ReactiveCollection<int, ItemIdentity, ItemState> view,
        IListener listener)
    {
        this.edits = edits;
        this.threshold = threshold;
        this.view = view;
        this.listener = listener;
    }

    public IReadOnlyList<int> Keys => [.. this.view.KeysCell.Sample()];

    public void Replace(int key, ItemState state) =>
        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Update(key: key, transform: _ => state));

    public void AddAndRemove(int key, ItemState state)
    {
        this.edits.Send(
            CollectionEdit<int, ItemIdentity, ItemState>.Add(
                new Item<ItemIdentity, ItemState>(identity: ItemSeed.Identity(key), state: state)));

        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Remove(key));
    }

    public void SetThreshold(int threshold) => this.threshold.Send(threshold);

    /// <param name="itemCount">How many items the collection holds.</param>
    /// <param name="style">
    ///     Which halves the two stages read. Each configuration holds the same keys in the same
    ///     places. The seed gives each item a score equal to its number, and the initial threshold
    ///     admits all of them. Thus, what differs between them is only which half each stage reads,
    ///     and how much of a state edit it can ignore.
    /// </param>
    internal static ChainedViewShape Build(int itemCount, ChainStyle style)
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

            CellSink<int> threshold = Cell.CreateSink(ViewSeed.InitialThreshold);

            // The identity filter admits everything, as the threshold one does at its initial
            // value. This does not measure what the predicate answers. The ordinary filter looks the
            // item up and tests in each condition, thus the question is if it has to test at all. The
            // two selective configurations keep the same items. The seed gives each item a score equal to
            // its number, thus even scores and even numbers are the same half. They are different only in
            // which half they had to read to find that.
            ReactiveCollection<int, ItemIdentity, ItemState> filtered =
                style switch
                {
                    ChainStyle.ByIdentity => collection.FilterByIdentity(static _ => true),
                    ChainStyle.SelectiveByIdentity =>
                        collection.FilterByIdentity(static identity => identity.Number % 2 == 0),
                    ChainStyle.SelectiveByState =>
                        collection.Filter(static (_, state) => state.Score % 2 == 0),
                    _ => collection.Filter(
                        criteriaCell: threshold,
                        predicate: static (limit, _, state) => ViewSeed.Passes(state: state, threshold: limit))
                };

            ReactiveCollection<int, ItemIdentity, ItemState> view =
                (style == ChainStyle.ByState
                    ? filtered.SortByDescending(static (_, state) => state.Score)
                    : filtered.SortByIdentityDescending(static identity => identity.Number))
                .Take(ViewSeed.Limit);

            return new ChainedViewShape(
                edits: edits,
                threshold: threshold,
                view: view,
                listener: view.KeyChangesStream.ListenStrong(static _ =>
                {
                }));
        });
    }
}

/// <summary>
///     The collection with no view stages, and a listener on its own change stream. This is the floor that an
///     edit cannot go below.
/// </summary>
/// <remarks>
///     <para>
///         This exists because without it no reader can read the view benchmarks. An edit through a chain
///         pays for the transaction, the send operation, the trie write to the state map, the snapshot, and
///         the change object. All of that comes before it reads any stage. At ten thousand items that is 2.8
///         of the 6.5 microseconds an excluded-key edit costs. Report the 6.5 and a stage-level difference of
///         a fifth of a microsecond reads as noise. Subtract the floor and the same difference is six percent
///         of what the chain actually does.
///     </para>
///     <para>
///         Subtract this from the arms beside it and what is left is what the chain actually costs.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class RootOnlyViewShape : IKeyedCollectionViewShape
{
    private readonly ReactiveCollection<int, ItemIdentity, ItemState> collection;
    private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;

    // Load-bearing, as in the other shapes: a listener is necessary, or nothing evaluates.
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

    public void Replace(int key, ItemState state) =>
        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Update(key: key, transform: _ => state));

    public void AddAndRemove(int key, ItemState state)
    {
        this.edits.Send(
            CollectionEdit<int, ItemIdentity, ItemState>.Add(
                new Item<ItemIdentity, ItemState>(identity: ItemSeed.Identity(key), state: state)));

        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Remove(key));
    }

    /// <inheritdoc />
    /// <remarks>Nothing here filters, so there is no threshold to change.</remarks>
    public void SetThreshold(int threshold)
    {
    }

    internal static RootOnlyViewShape Build(int itemCount)
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

            return new RootOnlyViewShape(
                edits: edits,
                collection: collection,
                listener: collection.KeyChangesStream.ListenStrong(static _ =>
                {
                }));
        });
    }
}

/// <summary>A page of a sorted collection, the two ways of keeping one.</summary>
/// <remarks>
///     The two hold the same page of the same ordering, and the benchmark checks that in its setup
///     before timing either.
/// </remarks>
file interface IKeyedPagingShape
{
    /// <summary>The keys of the page, in order, at this moment.</summary>
    // ReSharper disable once UnusedMemberInSuper.Global - Defines shape expected for implementers
    IReadOnlyList<int> Keys { get; }

    /// <summary>Moves the window to a new offset.</summary>
    // ReSharper disable once UnusedMemberInSuper.Global - Defines shape expected for implementers
    void TurnTo(int offset);

    /// <summary>Replaces one item's state.</summary>
    // ReSharper disable once UnusedMemberInSuper.Global - Defines shape expected for implementers
    void Replace(int key, ItemState state);
}

/// <summary>
///     One cell holding each item, re-sorted and re-windowed on each page turn and each edit.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class RederivedPageShape : IKeyedPagingShape
{
    private readonly CellSink<ImmutableDictionary<int, Item<ItemIdentity, ItemState>>> items;

    // Load-bearing, as in the other shapes: without a listener the projection is never evaluated.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;
    private readonly CellSink<int> offset;
    private readonly Cell<IReadOnlyList<int>> page;

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

    public void TurnTo(int offset) => this.offset.Send(offset);

    public void Replace(int key, ItemState state) =>
        this.items.Send(
            this.items.Sample()
                .SetItem(
                    key: key,
                    value: new Item<ItemIdentity, ItemState>(identity: ItemSeed.Identity(key), state: state)));

    internal static RederivedPageShape Build(int itemCount)
    {
        ImmutableDictionary<int, Item<ItemIdentity, ItemState>>.Builder builder =
            ImmutableDictionary.CreateBuilder<int, Item<ItemIdentity, ItemState>>();

        for (int number = 0; number < itemCount; number++)
        {
            builder.Add(
                key: number,
                value: new Item<ItemIdentity, ItemState>(
                    identity: ItemSeed.Identity(number),
                    state: ItemSeed.State(number)));
        }

        return Transaction.Run(() =>
        {
            CellSink<ImmutableDictionary<int, Item<ItemIdentity, ItemState>>> items =
                Cell.CreateSink(builder.ToImmutable());

            CellSink<int> offset = Cell.CreateSink(0);

            Cell<IReadOnlyList<int>> page =
                items.Lift<
                    ImmutableDictionary<int, Item<ItemIdentity, ItemState>>,
                    int,
                    IReadOnlyList<int>>(
                    c2: offset,
                    f: static (map, at) =>
                    [
                        .. map.Values
                            .OrderByDescending(static item => item.State.Score)
                            .Skip(at)
                            .Take(ViewSeed.Limit)
                            .Select(static item => item.Identity.Number)
                    ]);

            return new RederivedPageShape(
                items: items,
                offset: offset,
                page: page,
                listener: page.Updates()
                    .ListenStrong(static _ =>
                    {
                    }));
        });
    }
}

/// <summary>
///     <c>SortByDescending</c> then <c>Slice</c>, where turning the page changes the slice's offset
///     cell and nothing else.
/// </summary>
/// <remarks>
///     A criteria change rebuilds the stage that owns the criteria. For most stages that is the
///     expensive path, because a filter files each surviving key into a new ordered set. The
///     rebuild of a slice is a <c>RangeKeys</c> over the ordering it had, which is a lazy view and
///     costs nothing to make. That is the asymmetry the page-turn benchmarks show.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class ChainedPageShape : IKeyedPagingShape
{
    private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;

    // Load-bearing, as above.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;
    private readonly CellSink<int> offset;
    private readonly ReactiveCollection<int, ItemIdentity, ItemState> page;

    private ChainedPageShape(
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        CellSink<int> offset,
        ReactiveCollection<int, ItemIdentity, ItemState> page,
        IListener listener)
    {
        this.edits = edits;
        this.offset = offset;
        this.page = page;
        this.listener = listener;
    }

    public IReadOnlyList<int> Keys => [.. this.page.KeysCell.Sample()];

    public void TurnTo(int offset) => this.offset.Send(offset);

    public void Replace(int key, ItemState state) =>
        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Update(key: key, transform: _ => state));

    internal static ChainedPageShape Build(int itemCount)
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

            CellSink<int> offset = Cell.CreateSink(0);

            ReactiveCollection<int, ItemIdentity, ItemState> page =
                collection
                    .SortByDescending(static (_, state) => state.Score)
                    .Slice(offsetCell: offset, limitCell: Cell.Constant(ViewSeed.Limit));

            return new ChainedPageShape(
                edits: edits,
                offset: offset,
                page: page,
                listener: page.KeyChangesStream.ListenStrong(static _ =>
                {
                }));
        });
    }
}
