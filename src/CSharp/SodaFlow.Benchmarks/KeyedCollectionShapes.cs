using System.Collections.Generic;
using System.Globalization;
using SodaFlow.Collections;

namespace SodaFlow.Benchmarks;

/// <summary>The immutable half of a benchmark item. The key is its <see cref="Number" />.</summary>
internal sealed class ItemIdentity
{
    internal ItemIdentity(int number, string code)
    {
        this.Number = number;
        this.Code = code;
    }

    internal int Number { get; }

    /// <summary>
    ///     Carried because a real identity carries more than its key, and because the three shapes
    ///     must hold the same thing to be compared. Nothing here reads it.
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    internal string Code { get; }
}

/// <summary>
///     The mutable half. Three fields, because "a cell per mutable value" only differs from "a cell
///     per object" once there is more than one.
/// </summary>
internal sealed class ItemState
{
    internal ItemState(string name, int score, bool isFrozen)
    {
        this.Name = name;
        this.Score = score;
        this.IsFrozen = isFrozen;
    }

    internal string Name { get; }

    internal int Score { get; }

    internal bool IsFrozen { get; }
}

/// <summary>
///     The three ways to hold a large keyed collection in an FRP graph, built to the same interface
///     so a benchmark can ask each of them the same three questions.
/// </summary>
/// <remarks>
///     <para>
///         <b>Sinks per field</b> is the shape people reach for first: every mutable value on every
///         object gets its own <see cref="CellSink{T}" />, and an edit is a send straight into the
///         one it concerns. Nothing fans out, so an edit is O(1) — and it is the quickest thing in
///         these benchmarks by an order of magnitude. What it costs is <c>items × fields</c> cells
///         held whether anything is watching them or not.
///     </para>
///     <para>
///         It is also the shape you are least likely to be able to use, which is worth saying next
///         to the numbers rather than leaving them to flatter it. A sink is how an event from
///         <i>outside</i> the graph gets in, and SodaFlow enforces that rather than advising it:
///         <c>Send</c> throws "Send may not be called inside a callback" when it is reached from
///         within a transaction. So this shape holds only while every mutable value in the
///         collection is one the outside world hands over whole. Put any logic between the source
///         and the value — a balance derived from a running total, a status computed from two other
///         fields, anything downstream of another cell at all — and you cannot send it, and you are
///         in the second shape. Which is the one that costs three milliseconds an edit at ten
///         thousand items.
///     </para>
///     <para>
///         <b>Cells per field, fed from one edit stream</b> is what that turns into as soon as the
///         edits arrive as events rather than as method calls: each item filters the shared stream
///         for its own key, and its field cells hang off that. It composes — and every edit in the
///         collection now evaluates one filter per item, plus the cells behind whichever one
///         matched. That is the shape this collection exists to replace, written as charitably as
///         it can be: one filter per item rather than one per field, which is what a careful hand
///         would write.
///     </para>
///     <para>
///         <b>ReactiveCollection</b> resolves the edit once against a snapshot and fans out only to
///         the keys somebody is actually observing.
///     </para>
///     <para>
///         All three are asked to replace one item's whole state, so the work compared is the same
///         work. Observers watch the whole state too: in the first two shapes that means lifting
///         the three field cells back together, which is what an object with a cell per value costs
///         a reader.
///     </para>
/// </remarks>
internal interface IKeyedCollectionShape
{
    /// <summary>Starts watching one item's state, as a bound row would.</summary>
    IListener Observe(int key);

    /// <summary>Replaces one item's state, which is one edit however it is delivered.</summary>
    void Replace(int key, ItemState state);
}

/// <summary>Shared by the three shapes, so they start from identical contents.</summary>
internal static class ItemSeed
{
    internal static ItemIdentity Identity(int number) =>
        new(number: number, code: "C" + number.ToString(CultureInfo.InvariantCulture));

    internal static ItemState State(int number) =>
        new(name: "item " + number.ToString(CultureInfo.InvariantCulture), score: number, isFrozen: false);

    /// <summary>The keys the benchmarks observe: evenly spread, so none of them cluster.</summary>
    internal static IReadOnlyList<int> ObservedKeys(int itemCount, int observerCount)
    {
        List<int> keys = new(observerCount);

        for (int index = 0; index < observerCount; index++)
        {
            keys.Add(index * (itemCount / observerCount));
        }

        return keys;
    }
}

/// <summary>
///     A cell sink per mutable value per object, edited by sending straight into them.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class SinkPerFieldShape : IKeyedCollectionShape
{
    private readonly Dictionary<int, Item> items;

    private SinkPerFieldShape(Dictionary<int, Item> items) => this.items = items;

    public IListener Observe(int key)
    {
        Item item = this.items[key];

        // Reading the whole state means putting the three cells back together, which is the cost
        // this shape hands to every reader.
        return Transaction.Run(() =>
            item.Name
                .Lift(
                    c2: item.Score,
                    c3: item.IsFrozen,
                    f: static (name, score, isFrozen) => new ItemState(name: name, score: score, isFrozen: isFrozen))
                .Updates()
                .ListenStrong(static _ =>
                {
                }));
    }

    public void Replace(int key, ItemState state)
    {
        Item item = this.items[key];

        // One transaction, so this is one edit rather than three, exactly as the other two shapes
        // deliver it.
        Transaction.RunVoid(() =>
        {
            item.Name.Send(state.Name);
            item.Score.Send(state.Score);
            item.IsFrozen.Send(state.IsFrozen);
        });
    }

    internal static SinkPerFieldShape Build(int itemCount)
    {
        Dictionary<int, Item> items = new(itemCount);

        for (int number = 0; number < itemCount; number++)
        {
            items.Add(key: number, value: new Item(identity: ItemSeed.Identity(number), state: ItemSeed.State(number)));
        }

        return new SinkPerFieldShape(items);
    }

    private sealed class Item
    {
        internal Item(ItemIdentity identity, ItemState state)
        {
            this.Identity = identity;
            this.Name = Cell.CreateSink(state.Name);
            this.Score = Cell.CreateSink(state.Score);
            this.IsFrozen = Cell.CreateSink(state.IsFrozen);
        }

        // Held because a real object would hold it; nothing here reads it.
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        internal ItemIdentity Identity { get; }

        internal CellSink<string> Name { get; }

        internal CellSink<int> Score { get; }

        internal CellSink<bool> IsFrozen { get; }
    }
}

/// <summary>
///     A cell per mutable value per object, with one filter per object picking that object's edits
///     out of a shared stream. Every edit evaluates every one of those filters.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class StreamFedCellShape : IKeyedCollectionShape
{
    private readonly StreamSink<Edit> edits;
    private readonly Dictionary<int, Item> items;

    private StreamFedCellShape(StreamSink<Edit> edits, Dictionary<int, Item> items)
    {
        this.edits = edits;
        this.items = items;
    }

    public IListener Observe(int key)
    {
        Item item = this.items[key];

        return Transaction.Run(() =>
            item.Name
                .Lift(
                    c2: item.Score,
                    c3: item.IsFrozen,
                    f: static (name, score, isFrozen) => new ItemState(name: name, score: score, isFrozen: isFrozen))
                .Updates()
                .ListenStrong(static _ =>
                {
                }));
    }

    public void Replace(int key, ItemState state) => this.edits.Send(new Edit(key: key, state: state));

    internal static StreamFedCellShape Build(int itemCount) =>
        Transaction.Run(() =>
        {
            StreamSink<Edit> edits = Stream.CreateSink<Edit>();
            Dictionary<int, Item> items = new(itemCount);

            for (int number = 0; number < itemCount; number++)
            {
                items.Add(
                    key: number,
                    value: new Item(edits: edits, identity: ItemSeed.Identity(number), state: ItemSeed.State(number)));
            }

            return new StreamFedCellShape(edits: edits, items: items);
        });

    private sealed class Edit
    {
        internal Edit(int key, ItemState state)
        {
            this.Key = key;
            this.State = state;
        }

        internal int Key { get; }

        internal ItemState State { get; }
    }

    private sealed class Item
    {
        internal Item(Stream<Edit> edits, ItemIdentity identity, ItemState state)
        {
            this.Identity = identity;

            // One filter over the shared stream, per item, shared by this item's three cells -
            // the charitable version of this shape, since a filter per field would be three times
            // this. It is still the line the benchmark is about: the graph now has a node per item
            // that wakes for every edit in the collection, whether or not anything is observing
            // it.
            Stream<Edit> mine = edits.Filter(edit => edit.Key == identity.Number);

            this.Name = mine.Map(static edit => edit.State.Name).Hold(state.Name);
            this.Score = mine.Map(static edit => edit.State.Score).Hold(state.Score);
            this.IsFrozen = mine.Map(static edit => edit.State.IsFrozen).Hold(state.IsFrozen);
        }

        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        internal ItemIdentity Identity { get; }

        internal Cell<string> Name { get; }

        internal Cell<int> Score { get; }

        internal Cell<bool> IsFrozen { get; }
    }
}

/// <summary>
///     One snapshot cell, one change stream, and a per-item cell built only for the keys somebody
///     asks about.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class ReactiveCollectionShape : IKeyedCollectionShape
{
    private readonly ReactiveCollection<int, ItemIdentity, ItemState> collection;
    private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;

    private ReactiveCollectionShape(
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        ReactiveCollection<int, ItemIdentity, ItemState> collection)
    {
        this.edits = edits;
        this.collection = collection;
    }

    public IListener Observe(int key) =>
        Transaction.Run(() =>
            this.collection.StateCell(key)
                .Updates()
                .ListenStrong(static _ =>
                {
                }));

    public void Replace(int key, ItemState state) =>
        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Update(key: key, transform: _ => state));

    internal static ReactiveCollectionShape Build(int itemCount)
    {
        List<Item<ItemIdentity, ItemState>> items = new(itemCount);

        for (int number = 0; number < itemCount; number++)
        {
            items.Add(
                new Item<ItemIdentity, ItemState>(
                    identity: ItemSeed.Identity(number),
                    state: ItemSeed.State(number)));
        }

        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        return new ReactiveCollectionShape(
            edits: edits,
            collection: ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                keySelector: static identity => identity.Number,
                initialItems: items,
                edits));
    }
}
