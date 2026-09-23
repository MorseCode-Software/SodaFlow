using System.Collections.Generic;
using System.Linq;
using SodaFlow.Collections;

namespace SodaFlow.Benchmarks;

/// <summary>
///     A total of one state value across the full collection, the two ways of keeping one.
/// </summary>
/// <remarks>
///     <para>
///         Unlike a view, an aggregate genuinely depends on each item, so there is no window to hide behind.
///         There is also no configuration where the answer is cheap to make from scratch. The difference is
///         if the code makes it from scratch.
///     </para>
///     <para>
///         The naive shape reads the full store on each edit. That is what a <c>Map</c> over the snapshot
///         cell gives, and what most readers write first. The incremental shape folds the change stream. An
///         edit carries the keys that changed and their new states, and the snapshot the transaction started
///         from holds the previous ones. Thus, the delta costs one subtraction and one sum for each changed
///         key, at each size of the collection.
///     </para>
///     <para>
///         The setup checks the two against each other, and again after an edit. A total that drifts is the
///         defect this shape invites. A total that drifts has no lower cost than a correct one.
///     </para>
/// </remarks>
file interface IKeyedAggregateShape
{
    /// <summary>The total as it stands.</summary>
    // ReSharper disable once UnusedMemberInSuper.Global - Defines shape expected for implementers
    long Total { get; }

    /// <summary>Replaces one item's state.</summary>
    // ReSharper disable once UnusedMemberInSuper.Global - Defines shape expected for implementers
    void Replace(int key, ItemState state);

    /// <summary>
    ///     Adds an item and removes it again, which is the only thing that exercises the added and
    ///     removed halves of an incremental fold. Not timed. The setup uses it to check the fold
    ///     agrees with the sum after a structural change and after a state change.
    /// </summary>
    // ReSharper disable once UnusedMemberInSuper.Global - Defines shape expected for implementers
    void AddAndRemove(int key, ItemState state);
}

/// <summary>Shared by the two aggregate shapes, thus the benchmark asks them the same question.</summary>
file static class AggregateSeed
{
    /// <summary>The value in the total, from one item.</summary>
    internal static long ValueOf(ItemState state) => state.Score;

    /// <summary>The initial contents the two shapes are built on.</summary>
    internal static List<Item<ItemIdentity, ItemState>> Entries(int itemCount)
    {
        List<Item<ItemIdentity, ItemState>> items = new(itemCount);

        for (int number = 0; number < itemCount; number++)
        {
            items.Add(
                new Item<ItemIdentity, ItemState>(
                    identity: ItemSeed.Identity(number),
                    state: ItemSeed.State(number)));
        }

        return items;
    }
}

/// <summary>
///     The total recomputed from the full store on each edit, by mapping the snapshot cell.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class RederivedAggregateShape : IKeyedAggregateShape
{
    private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;

    // Load-bearing: a map nobody listens to is never evaluated, and this benchmark then
    // measure a sum that is never taken.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;
    private readonly Cell<long> total;

    private RederivedAggregateShape(
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        Cell<long> total,
        IListener listener)
    {
        this.edits = edits;
        this.total = total;
        this.listener = listener;
    }

    public long Total => this.total.Sample();

    public void Replace(int key, ItemState state) =>
        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Update(key: key, transform: _ => state));

    public void AddAndRemove(int key, ItemState state)
    {
        this.edits.Send(
            CollectionEdit<int, ItemIdentity, ItemState>.Add(
                new Item<ItemIdentity, ItemState>(identity: ItemSeed.Identity(key), state: state)));

        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Remove(key));
    }

    internal static RederivedAggregateShape Build(int itemCount)
    {
        List<Item<ItemIdentity, ItemState>> items = AggregateSeed.Entries(itemCount);

        return Transaction.Run(() =>
        {
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
                Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

            ReactiveCollection<int, ItemIdentity, ItemState> collection =
                ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    keySelector: static identity => identity.Number,
                    initialItems: items,
                    edits);

            // Through Pairs, and not Keys with a lookup for each. The first version of this benchmark
            // did the second one, which made the baseline slower than necessary. On a hundred thousand
            // items, a sum of one field cost more than a sort of the full collection did next door. The
            // difference that this measures is the one between a re-read and a fold. It is not the one
            // between a walk of a trie and a search of it.
            Cell<long> total =
                collection.SnapshotCell.Map(static snapshot =>
                {
                    long sum = 0;

                    // A loop and not Sum, because this arm is the thing that the benchmark measures. The
                    // compare must be against the fastest reasonable procedure to write it. LINQ costs
                    // a delegate call per item here, which flatters the other arm for a cause
                    // that has nothing to do with folding.
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    foreach (KeyValuePair<int, ItemState> pair in snapshot.States.Pairs)
                    {
                        sum += AggregateSeed.ValueOf(pair.Value);
                    }

                    return sum;
                });

            return new RederivedAggregateShape(
                edits: edits,
                total: total,
                listener: total.Updates()
                    .ListenStrong(static _ =>
                    {
                    }));
        });
    }
}

/// <summary>
///     The total folded from the change stream, adjusted by what actually changed.
/// </summary>
/// <remarks>
///     A snapshot of the snapshot cell of the collection gives the previous states. During the
///     transaction that made the change, that cell holds the version the transaction started from.
///     That is the trick, and it is why no second copy of the previous values has to be
///     kept alongside.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class IncrementalAggregateShape : IKeyedAggregateShape
{
    private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;

    // Load-bearing, as above.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;
    private readonly Cell<long> total;

    private IncrementalAggregateShape(
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        Cell<long> total,
        IListener listener)
    {
        this.edits = edits;
        this.total = total;
        this.listener = listener;
    }

    public long Total => this.total.Sample();

    public void Replace(int key, ItemState state) =>
        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Update(key: key, transform: _ => state));

    public void AddAndRemove(int key, ItemState state)
    {
        this.edits.Send(
            CollectionEdit<int, ItemIdentity, ItemState>.Add(
                new Item<ItemIdentity, ItemState>(identity: ItemSeed.Identity(key), state: state)));

        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Remove(key));
    }

    internal static IncrementalAggregateShape Build(int itemCount)
    {
        List<Item<ItemIdentity, ItemState>> items = AggregateSeed.Entries(itemCount);

        long initial = items.Sum(static item => AggregateSeed.ValueOf(item.State));

        return Transaction.Run(() =>
        {
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
                Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

            ReactiveCollection<int, ItemIdentity, ItemState> collection =
                ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    keySelector: static identity => identity.Number,
                    initialItems: items,
                    edits);

            Cell<long> total =
                collection.ItemChangesStream
                    .Snapshot(
                        c: collection.SnapshotCell,
                        f: static (change, before) => DeltaOf(change: change, before: before))
                    .Accum(initialState: initial, f: static (delta, running) => running + delta);

            return new IncrementalAggregateShape(
                edits: edits,
                total: total,
                listener: total.Updates()
                    .ListenStrong(static _ =>
                    {
                    }));
        });
    }

    /// <summary>
    ///     How much the total moved, from the keys that changed and the states they held before.
    /// </summary>
    /// <remarks>
    ///     An added key has no state in <paramref name="before" />, so it contributes only its new
    ///     value. A removed key is missing from <c>NewStates</c>, thus it contributes only the negation
    ///     of its previous one. Nothing special is necessary for the two, more than a look.
    /// </remarks>
    private static long DeltaOf(
        ItemChange<int, ItemIdentity, ItemState> change,
        CollectionSnapshot<int, ItemIdentity, ItemState> before)
    {
        long delta = 0;

        foreach (KeyValuePair<int, ItemState> pair in change.NewStates)
        {
            if (before.States.TryGetState(key: pair.Key, state: out ItemState? old))
            {
                delta -= AggregateSeed.ValueOf(old);
            }

            delta += AggregateSeed.ValueOf(pair.Value);
        }

        foreach (int key in change.Removed)
        {
            if (before.States.TryGetState(key: key, state: out ItemState? old))
            {
                delta -= AggregateSeed.ValueOf(old);
            }
        }

        return delta;
    }
}
