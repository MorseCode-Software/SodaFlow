using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SodaFlow.Collections;

/// <summary>
///     A large collection with keys, as a flat graph in SodaFlow: one cell that holds the full
///     snapshot, one stream of resolved changes, and a cell from those changes that sends a value
///     only at a change of the shape.
/// </summary>
/// <remarks>
///     <para>
///         An observer of one item costs one hash lookup for each active observer and for each
///         transaction, and that cost does not change with the size of the collection. This code
///         makes an observer at the first read and keeps it in a weak cache. Thus only the items
///         with an observer have graph nodes.
///     </para>
///     <para>
///         No cell is in the value of a second cell. A <c>Cell&lt;Collection&gt;</c> whose value
///         holds its own cell builds graph nodes in the fold that makes each new value. Those are
///         new nodes for each structural change and for each observer, and only a switch removes
///         them. One change stream gives the two views here.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class RootCollection<TKey, TIdentity, TState>
    : ReactiveCollection<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly Lazy<ReactiveCollection<TKey, TIdentity, TState>> orderedByArrival;

    private RootCollection(
        IEqualityComparer<TKey> keyEqualityComparer,
        Stream<ItemChange<TKey, TIdentity, TState>> itemChangesStream,
        Cell<CollectionSnapshot<TKey, TIdentity, TState>> snapshotCell,
        Cell<IReadOnlyDictionary<TKey, TIdentity>> shapeCell)
    {
        this.KeyEqualityComparer = keyEqualityComparer;
        this.ItemChangesStream = itemChangesStream;
        this.SnapshotCell = snapshotCell;
        this.ShapeCell = shapeCell;

        // This code builds the order at its first use, thus a collection that no code lists has no
        // cost for an ordered key set. It is the sequence of the arrival of the items and not an
        // order of their values, thus this code never compares two keys and TKey needs no
        // compare operation. Use SortByKey for an order of the keys.
        this.orderedByArrival =
            new Lazy<ReactiveCollection<TKey, TIdentity, TState>>(
                valueFactory: () => CollectionViewUtility.CreateRootImpl(this),
                mode: LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal IEqualityComparer<TKey> KeyEqualityComparer { get; }

    /// <inheritdoc />
    public override Stream<ItemChange<TKey, TIdentity, TState>> ItemChangesStream { get; }

    /// <inheritdoc />
    public override Cell<OrderedKeys<TKey, TIdentity, TState>> KeysCell => this.orderedByArrival.Value.KeysCell;

    /// <inheritdoc />
    public override Stream<CollectionViewChange<TKey, TIdentity, TState>> KeyChangesStream =>
        this.orderedByArrival.Value.KeyChangesStream;

    /// <inheritdoc />
    /// <remarks>On the collection this is the full store, and it sends a value at each
    /// change.</remarks>
    public override Cell<CollectionSnapshot<TKey, TIdentity, TState>> SnapshotCell { get; }

    /// <inheritdoc />
    public override Cell<IReadOnlyDictionary<TKey, TIdentity>> ShapeCell { get; }

    /// <inheritdoc />
    /// <remarks>A root owns the store, so this is itself.</remarks>
    internal override RootCollection<TKey, TIdentity, TState> Root => this;

    /// <summary>
    ///     Builds the collection that each public <c>Create</c> factory returns. Those factories
    ///     are on <see cref="ReactiveCollection{TKey,TIdentity,TState}" /> and on the class beside
    ///     it that is not generic.
    /// </summary>
    /// <param name="keySelector">Makes the key of an item from its immutable part.</param>
    /// <param name="keyEqualityComparer">The equality comparer for keys.</param>
    /// <param name="initialEntries">The initial contents of the collection, read at their first use.</param>
    /// <param name="editStreams">Each stream that edits the collection.</param>
    /// <returns>The collection.</returns>
    internal static ReactiveCollection<TKey, TIdentity, TState> CreateImpl(
        Func<TIdentity, TKey> keySelector,
        IEqualityComparer<TKey> keyEqualityComparer,
        Cell<IEnumerable<Item<TIdentity, TState>>> initialEntries,
        params Stream<CollectionEdit<TKey, TIdentity, TState>>[] editStreams) =>
        TransactionInternal.Apply((trans, _) =>
        {
            ImmutableDictionary<TKey, TIdentity>.Builder identities =
                ImmutableDictionary.CreateBuilder<TKey, TIdentity>(keyEqualityComparer);

            Dictionary<TKey, TState> states = new(keyEqualityComparer);

            // This code gives a number to each item in the sequence of the enumeration, and the
            // collection lists the items in that sequence.
            ImmutableDictionary<TKey, long>.Builder arrivals =
                ImmutableDictionary.CreateBuilder<TKey, long>(keyEqualityComparer);

            Lazy<CollectionSnapshot<TKey, TIdentity, TState>> initial =
                initialEntries.SampleLazyImpl()
                    .MapImpl(initialEntries =>
                    {
                        foreach (Item<TIdentity, TState> item in initialEntries)
                        {
                            TKey key = keySelector(item.Identity);

                            // This uses ContainsKey and not TryAdd. A dictionary in
                            // netstandard2.0 and in net472 has no TryAdd, and a builder has
                            // none.
                            if (identities.ContainsKey(key))
                            {
                                throw new ArgumentException($"Duplicate key '{key}' in the initial items.");
                            }

                            identities.Add(key: key, value: item.Identity);
                            states.Add(key: key, value: item.State);
                            arrivals.Add(key: key, value: arrivals.Count);
                        }

                        return new CollectionSnapshot<TKey, TIdentity, TState>(
                            identities: identities.ToImmutable(),
                            states: ImmutableStateMap<TState>.Create(keyEqualityComparer)
                                .With(updated: states, removed: Array.Empty<TKey>()),
                            arrivals: arrivals.ToImmutable(),
                            nextArrival: arrivals.Count);
                    });

            Stream<CollectionEdit<TKey, TIdentity, TState>> editsStream = MergeEdits(editStreams);

            // The resolution of an edit uses the state that it resolves against, and the
            // resolution of the edits makes that state. Thus this code has an explicit loop.
            LoopedCell<CollectionSnapshot<TKey, TIdentity, TState>> snapshotLoopCell = new();

            Stream<ItemChange<TKey, TIdentity, TState>> itemChangesStream =
                editsStream
                    .SnapshotImpl(
                        c: snapshotLoopCell,
                        f: (edit, before) =>
                            Resolve(
                                keyEqualityComparer: keyEqualityComparer,
                                keySelector: keySelector,
                                edit: edit,
                                before: before))
                    .FilterSomeInternal();

            Cell<CollectionSnapshot<TKey, TIdentity, TState>> snapshotCell =
                itemChangesStream
                    .MapImpl(static change => change.After)
                    .HoldLazyImpl(initial);

            snapshotLoopCell.Loop(trans: trans, c: snapshotCell);

            // This comes from its own hold on the same stream, and not from a Calm on a map of
            // the snapshot cell. The two holds read the same transaction, thus the two views always
            // agree. This cell also sends a value only at the condition above: a change of the item
            // count, or a change of a key.
            Cell<IReadOnlyDictionary<TKey, TIdentity>> shapeCell =
                itemChangesStream
                    .FilterImpl(static change => change.IsStructural)
                    .MapImpl(static change => change.After.Identities)
                    .HoldLazyImpl(initial.MapImpl(static initial => initial.Identities));

            return new RootCollection<TKey, TIdentity, TState>(
                keyEqualityComparer: keyEqualityComparer,
                itemChangesStream: itemChangesStream,
                snapshotCell: snapshotCell,
                shapeCell: shapeCell);
        });

    /// <inheritdoc />
    /// <remarks>
    ///     The cost is low, thus code can make one for each bound view. It filters on one hash
    ///     lookup and does not read the other items.
    ///     The key can be missing now. A removal sends <paramref name="onAbsent" />, and a
    ///     subsequent add with the same key sends <paramref name="onPresent" /> again. Thus a view
    ///     that binds to a key can continue after the item.
    ///     A weak cache holds one for each key, thus N observers of one key share a node, and the
    ///     node goes out of memory with the last observer. Two projections of the same key are two
    ///     cells, thus the C# surface and the F# surface never give each other an incorrect
    ///     cell.
    /// </remarks>
    internal override Cell<TProjected> CreateIdentityCell<TProjected>(
        TKey key,
        Func<TIdentity, TProjected> onPresent,
        Func<TProjected> onAbsent) =>
        TransactionInternal.RunImpl(() =>
            this.ItemChangesStream
                .MapImpl(change => change.ProjectIdentityChangeFor(key: key, onPresent: onPresent, onAbsent: onAbsent))
                .FilterSomeInternal()
                .HoldLazyImpl(
                    this.SnapshotCell.SampleLazyImpl()
                        .MapImpl(snapshot =>
                            snapshot.TryGetIdentity(key: key, identity: out TIdentity? identity)
                                ? onPresent(identity)
                                : onAbsent())));

    /// <summary>
    ///     Merges the input streams into one stream. Edits from different streams in the same
    ///     transaction become one change event and one cell update.
    ///     <see cref="CollectionEdit{TKey,TIdentity,TState}.CombineWith" /> refuses an ambiguous
    ///     condition, and no code resolves it with the sequence of the merge, because SodaFlow does
    ///     not give that sequence.
    /// </summary>
    private static Stream<CollectionEdit<TKey, TIdentity, TState>> MergeEdits(
        IEnumerable<Stream<CollectionEdit<TKey, TIdentity, TState>>> editStreams) =>
        editStreams.Aggregate(
            seed: StreamInternal.NeverImpl<CollectionEdit<TKey, TIdentity, TState>>(),
            func: static (mergedStream, editStream) =>
                mergedStream.MergeImpl(
                    s: editStream,
                    f: static (left, right) => left.CombineWith(right)));

    private static MaybeInternal<ItemChange<TKey, TIdentity, TState>> Resolve(
        IEqualityComparer<TKey> keyEqualityComparer,
        Func<TIdentity, TKey> keySelector,
        CollectionEdit<TKey, TIdentity, TState> edit,
        CollectionSnapshot<TKey, TIdentity, TState> before)
    {
        Dictionary<TKey, TState> newStates = new(keyEqualityComparer);
        HashSet<TKey> added = new(keyEqualityComparer);
        HashSet<TKey> removed = new(keyEqualityComparer);

        foreach (TKey key in edit.Removes.Where(before.ContainsKey))
        {
            removed.Add(key);
        }

        foreach (Item<TIdentity, TState> item in edit.Adds)
        {
            TKey key = keySelector(item.Identity);

            if (before.ContainsKey(key) && !removed.Contains(key))
            {
                throw new InvalidOperationException(
                    $"Key '{key}' already exists. Re-keying is a remove followed by an add.");
            }

            added.Add(key);
            removed.Remove(key);
            newStates[key] = item.State;
        }

        foreach (KeyValuePair<TKey, Func<TState, TState>> update in edit.Updates)
        {
            if (removed.Contains(update.Key))
            {
                throw new InvalidOperationException(
                    $"Key '{update.Key}' is updated and removed in the same transaction.");
            }

            if (!newStates.TryGet(key: update.Key, value: out TState? current)
                && !before.States.TryGetState(key: update.Key, state: out current))
            {
                throw new KeyNotFoundException($"Cannot update key '{update.Key}': no such item in the collection.");
            }

            newStates[update.Key] = update.Value(current);
        }

        if (newStates.Count == 0 && removed.Count == 0)
        {
            return MaybeInternal<ItemChange<TKey, TIdentity, TState>>.None;
        }

        // Only a structural edit changes the identity map, and it builds the next version from
        // this version and does not copy it. Thus an add costs one write and not one read of the
        // full collection.
        ImmutableDictionary<TKey, TIdentity> identities =
            added.Count > 0 || removed.Count > 0
                ? before.WithIdentities(
                    added: edit.Adds.Select(item =>
                        new KeyValuePair<TKey, TIdentity>(key: keySelector(item.Identity), value: item.Identity)),
                    removed: removed)
                : before.IdentitiesImpl;

        // This code gives a number at each structural edit that changes the identity map, in the
        // sequence of the adds in that edit, which is the sequence of their arrival at the end of
        // the collection. Edits from more than one stream in one transaction come together in the
        // sequence of the streams, and their adds come together in that sequence also.
        (ImmutableDictionary<TKey, long> arrivals, long nextArrival) =
            added.Count > 0 || removed.Count > 0
                ? before.WithArrivals(added: edit.Adds.Select(item => keySelector(item.Identity)), removed: removed)
                : (before.ArrivalsImpl, before.NextArrival);

        CollectionSnapshot<TKey, TIdentity, TState> after =
            new(
                identities: identities,
                states: before.StatesImpl.With(
                    updated: newStates,
                    removed: removed),
                arrivals: arrivals,
                nextArrival: nextArrival);

        return MaybeInternal.Some(
            new ItemChange<TKey, TIdentity, TState>(
                before: before,
                after: after,
                newStates: newStates,
                added: added,
                removed: removed));
    }

    internal override Cell<TProjected> CreateStateCell<TProjected>(
        TKey key,
        Func<TState, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        Cell<TProjected> stateCell =
            TransactionInternal.RunImpl(() =>
                // This reads the new value from the event and does not sample the snapshot cell.
                // A sample of a cell in a transaction gives the value from before the
                // transaction.
                //
                // The seed is lazy for the same cause. This code can build this cell in the
                // transaction that adds its key, for example a row from a structural change. The
                // change stream then sent its value before this construction, thus the seed is the
                // only source for the cell. A sample here, before the lazy step, reads the
                // snapshot from before the transaction, which does not have the key, and the cell
                // then has no value until the next edit to that key. A lazy sample runs after the
                // transaction is stable and gives the correct value.
                //
                // The projection is in this map and not in a second map after it, thus the
                // optional type of a wrapper costs no more nodes.
                this.ItemChangesStream
                    .MapImpl(change => change.ProjectChangeFor(key: key, onPresent: onPresent, onAbsent: onAbsent))
                    .FilterSomeInternal()
                    .HoldLazyImpl(
                        this.SnapshotCell.SampleLazyImpl()
                            .MapImpl(snapshot =>
                                snapshot.States.TryGetState(key: key, state: out TState? state)
                                    ? onPresent(state)
                                    : onAbsent())));

        return stateCell;
    }
}
