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
    private readonly CellSink<ImmutableDictionary<int, Entry<ItemIdentity, ItemState>>> items;
    private readonly CellSink<int> threshold;
    private readonly Cell<IReadOnlyList<int>> view;

    // Load-bearing: the map above is only evaluated because something is listening to it, and a
    // benchmark measuring a projection nobody asked for would measure nothing.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;

    private RederivedViewShape(
        CellSink<ImmutableDictionary<int, Entry<ItemIdentity, ItemState>>> items,
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
        ImmutableDictionary<int, Entry<ItemIdentity, ItemState>>.Builder builder =
            ImmutableDictionary.CreateBuilder<int, Entry<ItemIdentity, ItemState>>();

        for (int number = 0; number < itemCount; number++)
        {
            builder.Add(
                number,
                new Entry<ItemIdentity, ItemState>(ItemSeed.Identity(number), ItemSeed.State(number)));
        }

        return Transaction.Run(() =>
        {
            CellSink<ImmutableDictionary<int, Entry<ItemIdentity, ItemState>>> items =
                Cell.CreateSink(builder.ToImmutable());

            CellSink<int> threshold = Cell.CreateSink(ViewSeed.InitialThreshold);

            Cell<IReadOnlyList<int>> view = items.Lift<
                ImmutableDictionary<int, Entry<ItemIdentity, ItemState>>,
                int,
                IReadOnlyList<int>>(
                threshold,
                static (map, limit) =>
                [
                    .. map.Values
                        .Where(entry => ViewSeed.Passes(entry.State, limit))
                        .OrderByDescending(static entry => entry.State.Score)
                        .ThenBy(static entry => entry.Identity.Number)
                        .Take(ViewSeed.Limit)
                        .Select(static entry => entry.Identity.Number)
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
                new Entry<ItemIdentity, ItemState>(ItemSeed.Identity(key), state)));

    public void AddAndRemove(int key, ItemState state)
    {
        this.items.Send(
            this.items.Sample().Add(
                key,
                new Entry<ItemIdentity, ItemState>(ItemSeed.Identity(key), state)));

        this.items.Send(this.items.Sample().Remove(key));
    }

    public void SetThreshold(int threshold) => this.threshold.Send(threshold);
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
    /// <param name="sortByIdentity">
    ///     Whether to order by the identity rather than the state. The seed gives every item a
    ///     score equal to its number, so both orders put the same keys in the same places and the
    ///     only thing that differs is which half the sort reads - and therefore whether a state
    ///     edit can move anything.
    /// </param>
    internal static ChainedViewShape Build(int itemCount, bool sortByIdentity)
    {
        List<Entry<ItemIdentity, ItemState>> entries = new(itemCount);

        for (int number = 0; number < itemCount; number++)
        {
            entries.Add(new Entry<ItemIdentity, ItemState>(
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
                    entries,
                    edits);

            CellSink<int> threshold = Cell.CreateSink(ViewSeed.InitialThreshold);

            IReactiveCollection<int, ItemIdentity, ItemState> filtered =
                collection.Filter(threshold, static (limit, _, state) => ViewSeed.Passes(state, limit));

            IReactiveCollection<int, ItemIdentity, ItemState> view =
                (sortByIdentity
                    ? filtered.SortByIdDescending(static identity => identity.Number)
                    : filtered.SortByDescending(static (_, state) => state.Score))
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
                new Entry<ItemIdentity, ItemState>(ItemSeed.Identity(key), state)));

        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Remove(key));
    }

    public void SetThreshold(int threshold) => this.threshold.Send(threshold);
}
