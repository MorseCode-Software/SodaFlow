using System.Collections.Generic;
using System.Linq;
using SodaFlow.Collections;

namespace SodaFlow.Benchmarks;

/// <summary>
///     A running total of one state value across the whole collection, the two ways of keeping one.
/// </summary>
/// <remarks>
///     <para>
///         Unlike a view, an aggregate genuinely depends on every item, so there is no window to
///         hide behind and no arrangement in which the answer is cheap to produce from scratch.
///         What differs is whether it is produced from scratch.
///     </para>
///     <para>
///         The naive shape reads the whole store on every edit, which is what a <c>Map</c> over the
///         snapshot cell gives you and what most people write first. The incremental shape folds the
///         change stream instead: an edit carries the keys that changed and their new states, and
///         the snapshot the transaction started from still holds the old ones, so the delta costs
///         one subtraction and one addition per changed key however large the collection is.
///     </para>
///     <para>
///         Both are checked against each other in the setup, and again after an edit, because a
///         running total that drifts is exactly the bug this shape invites and a drifting total is
///         no cheaper to compute than a correct one.
///     </para>
/// </remarks>
internal interface IKeyedAggregateShape
{
    /// <summary>The total as it stands.</summary>
    long Total { get; }

    /// <summary>Replaces one item's state.</summary>
    void Replace(int key, ItemState state);

    /// <summary>
    ///     Adds an item and removes it again, which is the only thing that exercises the added and
    ///     removed halves of an incremental fold. Not timed; the setup uses it to check the fold
    ///     agrees with the sum after a structural change as well as after a state change.
    /// </summary>
    void AddAndRemove(int key, ItemState state);
}

/// <summary>Shared by the two aggregate shapes, so they are asked for the same thing.</summary>
file static class AggregateSeed
{
    /// <summary>The value being totalled, from one item.</summary>
    internal static long ValueOf(ItemState state) => state.Score;

    /// <summary>The initial contents both shapes are built on.</summary>
    internal static List<Entry<ItemIdentity, ItemState>> Entries(int itemCount)
    {
        List<Entry<ItemIdentity, ItemState>> entries = new(itemCount);

        for (int number = 0; number < itemCount; number++)
        {
            entries.Add(new Entry<ItemIdentity, ItemState>(
                ItemSeed.Identity(number),
                ItemSeed.State(number)));
        }

        return entries;
    }
}

/// <summary>
///     The total recomputed from the whole store on every edit, by mapping the snapshot cell.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class RederivedAggregateShape : IKeyedAggregateShape
{
    private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;
    private readonly Cell<long> total;

    // Load-bearing: a map nobody listens to is never evaluated, and this benchmark would then
    // measure a sum that is never taken.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;

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

    internal static RederivedAggregateShape Build(int itemCount)
    {
        List<Entry<ItemIdentity, ItemState>> entries = AggregateSeed.Entries(itemCount);

        return Transaction.Run(() =>
        {
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
                Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

            ReactiveCollection<int, ItemIdentity, ItemState> collection =
                ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    static identity => identity.Number,
                    entries,
                    edits);

            // Through Pairs rather than Keys plus a lookup each. The first version of this
            // benchmark did the latter, and it made the baseline slower than it had any need to be
            // - on a hundred thousand items, summing one field cost more than sorting the whole
            // collection did next door. The gap being measured here is meant to be the one between
            // re-reading and folding, not the one between walking a trie and searching it.
            Cell<long> total = collection.SnapshotCell.Map(static snapshot =>
            {
                long sum = 0;

                // A loop rather than Sum, because this arm is the thing being measured and the
                // comparison should be against the fastest reasonable way to write it. LINQ costs
                // a delegate call per item here, which would flatter the other arm for a reason
                // that has nothing to do with folding.
                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (KeyValuePair<int, ItemState> pair in snapshot.States.Pairs)
                {
                    sum += AggregateSeed.ValueOf(pair.Value);
                }

                return sum;
            });

            return new RederivedAggregateShape(
                edits,
                total,
                total.Updates().ListenStrong(static _ => { }));
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
}

/// <summary>
///     The total folded from the change stream, adjusted by what actually changed.
/// </summary>
/// <remarks>
///     The old states come from snapshotting the collection's own snapshot cell, which during the
///     transaction that produced the change still holds the version the transaction started from.
///     That is the whole trick, and it is why no separate copy of the previous values has to be
///     kept alongside.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class IncrementalAggregateShape : IKeyedAggregateShape
{
    private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;
    private readonly Cell<long> total;

    // Load-bearing, as above.
    // ReSharper disable once NotAccessedField.Local
    private readonly IListener listener;

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

    internal static IncrementalAggregateShape Build(int itemCount)
    {
        List<Entry<ItemIdentity, ItemState>> entries = AggregateSeed.Entries(itemCount);

        long initial = entries.Sum(static entry => AggregateSeed.ValueOf(entry.State));

        return Transaction.Run(() =>
        {
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
                Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

            ReactiveCollection<int, ItemIdentity, ItemState> collection =
                ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    static identity => identity.Number,
                    entries,
                    edits);

            Cell<long> total = collection.ItemChangesStream
                .Snapshot(collection.SnapshotCell, static (change, before) => DeltaOf(change, before))
                .Accum(initial, static (delta, running) => running + delta);

            return new IncrementalAggregateShape(
                edits,
                total,
                total.Updates().ListenStrong(static _ => { }));
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

    /// <summary>
    ///     How much the total moved, from the keys that changed and the states they held before.
    /// </summary>
    /// <remarks>
    ///     An added key has no state in <paramref name="before" />, so it contributes only its new
    ///     value; a removed key is absent from <c>NewStates</c>, so it contributes only the negation
    ///     of its old one. Neither needs a special case beyond looking.
    /// </remarks>
    private static long DeltaOf(
        CollectionChange<int, ItemIdentity, ItemState> change,
        CollectionSnapshot<int, ItemIdentity, ItemState> before)
    {
        long delta = 0;

        foreach (KeyValuePair<int, ItemState> pair in change.NewStates)
        {
            if (before.States.TryGetState(pair.Key, out ItemState old))
            {
                delta -= AggregateSeed.ValueOf(old);
            }

            delta += AggregateSeed.ValueOf(pair.Value);
        }

        foreach (int key in change.Removed)
        {
            if (before.States.TryGetState(key, out ItemState old))
            {
                delta -= AggregateSeed.ValueOf(old);
            }
        }

        return delta;
    }
}
