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
    ///     Carried because an identity carries more than its key, and because the three shapes
    ///     must hold the same value for the compare. Nothing here reads it.
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    internal string Code { get; }
}

/// <summary>
///     The mutable half. Three fields, because "a cell per mutable value" only differs from "a cell
///     per object" when there is more than one.
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
///     The three ways to hold a large keyed collection in an FRP graph. Each one is built to the
///     same interface, thus a benchmark can give each of them the same three questions.
/// </summary>
/// <remarks>
///     <para>
///         <b>Sinks per field</b> is the shape that a reader selects first. Each mutable value on each
///         object gets its own <see cref="CellSink{T}" />, and an edit is a send into the one it
///         concerns. Nothing fans out, thus an edit is <c>O(1)</c>. It is the quickest thing in
///         these benchmarks by an order of magnitude. What it costs is <c>items × fields</c> cells
///         held when nothing is watching them.
///     </para>
///     <para>
///         It is also the shape that is the most difficult to use. You must say that next to the
///         numbers, because the numbers alone flatter it. A sink is how an event from out of
///         the graph gets in, and SodaFlow enforces that, and does not only recommend it.
///         <c>Send</c> throws "Send may not be called inside a callback" when code calls it in a
///         transaction. Thus, this shape holds only while each mutable value in the collection
///         is one that other code hands over in full. Put any logic between the source and the value.
///         Examples are a balance from a total, a status from two other fields, and anything
///         downstream of a different cell. You then cannot send it, and you are in the second shape.
///         That one costs three milliseconds for each edit at ten thousand items.
///     </para>
///     <para>
///         <b>Cells per field, fed from one edit stream</b> is what that becomes. That occurs when the
///         edits come as events and not as method calls. Each item filters the shared stream for its
///         own key, and its field cells hang off that. It composes, and each edit in the
///         collection now evaluates one filter per item, plus the cells behind whichever one
///         matched. That is the shape this collection exists to replace, written as charitably as it
///         can be. It has one filter for each item, and not one for each field, which is what a
///         careful hand writes.
///     </para>
///     <para>
///         <b>ReactiveCollection</b> resolves the edit one time against a snapshot and fans out only to
///         the keys somebody is actually observing.
///     </para>
///     <para>
///         The benchmark asks all three to replace the full state of one item, thus the work is the same
///         work. Observers monitor the full state too. In the first two shapes that means a lift of
///         the three field cells back together. That is what an object with a cell for each value
///         costs a reader.
///     </para>
/// </remarks>
internal interface IKeyedCollectionShape
{
    /// <summary>Starts to monitor the state of one item, as a bound row does.</summary>
    IListener Observe(int key);

    /// <summary>Replaces the state of one item, which is one edit, at each path into the graph.</summary>
    void Replace(int key, ItemState state);
}

/// <summary>Shared by the three shapes, thus they start from the same contents.</summary>
internal static class ItemSeed
{
    internal static ItemIdentity Identity(int number) =>
        new(number: number, code: "C" + number.ToString(CultureInfo.InvariantCulture));

    internal static ItemState State(int number) =>
        new(name: "item " + number.ToString(CultureInfo.InvariantCulture), score: number, isFrozen: false);

    /// <summary>The keys that the benchmarks monitor, at equal distances, thus none of them cluster.</summary>
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

        // A read of the full state puts the three cells together again, which is the cost
        // this shape hands to each reader.
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

        // One transaction, so this is one edit rather than three, as the other two shapes
        // send it.
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

        // Held because an object with this shape holds it. Nothing here reads it.
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        internal ItemIdentity Identity { get; }

        internal CellSink<string> Name { get; }

        internal CellSink<int> Score { get; }

        internal CellSink<bool> IsFrozen { get; }
    }
}

/// <summary>
///     A cell per mutable value per object, with one filter per object picking that object's edits
///     out of a shared stream. Each edit evaluates each one of those filters.
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

            // One filter over the shared stream, for each item, shared by the three cells of this item.
            // It is the charitable version of this shape, because a filter for each field is three times
            // this. It is the line the benchmark is about. The graph now has a node for each item that
            // wakes for each edit in the collection, and for an edit that nothing is observing.
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
