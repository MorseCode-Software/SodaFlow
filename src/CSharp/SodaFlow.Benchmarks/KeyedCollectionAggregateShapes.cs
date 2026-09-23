using System.Collections.Generic;
using System.Linq;
using SodaFlow.Collections;

namespace SodaFlow.Benchmarks;

/// <summary>
///     A total of one state value across the full collection, the two ways of keeping one.
/// </summary>
/// <remarks>
///     <para>
///         Unlike a view, an aggregate genuinely depends on each item, so there is no window to
///         hide behind, and no configuration where the answer is cheap to make from scratch. The
///         difference is if the code makes it from scratch.
///     </para>
///     <para>
///         The naive shape reads the full store on each edit, which is what a <c>Map</c> over the
///         snapshot cell gives, and what most readers write first. The incremental shape folds the
///         change stream: an edit carries the keys that changed and their new states, and
///         the snapshot the transaction started from holds the previous ones, thus the delta costs
///         one subtraction and one sum for each changed key, at each size of the collection.
///     </para>
///     <para>
///         The two are checked against each other in the setup, and again after an edit, because a
///         total that drifts is the bug this shape invites and a drifting total is
///         no of a lower cost than a correct one.
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

/// <summary>Shared by the two aggregate shapes, so they are asked for the same thing.</summary>
file static class AggregateSeed
{
    /// <summary>The value being totaled, from one item.</summary>
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

            // Through Pairs rather than Keys plus a lookup each. The first version of this
            // benchmark did the latter, and it made the baseline slower than necessary
            // - on a hundred thousand items, summing one field cost more than sorting the full
            // collection did next door. The difference that this measures is the one between
            // a re-read with a fold, and not the one between a walk of a trie and a search of it.
            Cell<long> total =
                collection.SnapshotCell.Map(static snapshot =>
                {
                    long sum = 0;

                    // A loop rather than Sum, because this arm is the thing being measured and the
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
