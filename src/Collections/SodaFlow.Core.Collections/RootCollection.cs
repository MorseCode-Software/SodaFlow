using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SodaFlow.Collections;

/// <summary>
///     A large keyed collection exposed to SodaFlow as a flat graph: one cell holding the whole
///     snapshot, one stream of resolved changes, and a derived cell that fires only when the shape
///     changes.
/// </summary>
/// <remarks>
///     <para>
///         Per-item observation costs one hash lookup per active observer per transaction, and is
///         independent of collection size. Observers are created on demand and cached weakly, so
///         only the items actually being watched carry any graph nodes.
///     </para>
///     <para>
///         Note that no cell is nested inside another cell's value. A <c>Cell&lt;Collection&gt;</c>
///         whose value carried its own inner cell would construct graph nodes inside the fold that
///         produces each new value — new nodes per structural change, per observer, flattened back
///         out only by a switch. The two views here are derived from one change stream instead.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class RootCollection<TKey, TIdentity, TState>
    : ReactiveCollection<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly Lazy<ReactiveCollection<TKey, TIdentity, TState>> orderedByKey;

    private RootCollection(
        Stream<ItemChange<TKey, TIdentity, TState>> itemChangesStream,
        Cell<CollectionSnapshot<TKey, TIdentity, TState>> snapshotCell,
        Cell<IReadOnlyDictionary<TKey, TIdentity>> shapeCell)
    {
        this.ItemChangesStream = itemChangesStream;
        this.SnapshotCell = snapshotCell;
        this.ShapeCell = shapeCell;

        // The ordering is built on first use. A collection nobody sorts or lists never pays for a
        // sorted key set, and TKey only has to be comparable if something actually asks for keys in
        // order.
        this.orderedByKey = new Lazy<ReactiveCollection<TKey, TIdentity, TState>>(
            () => CollectionViewUtility.CreateRootImpl(this, Comparer<TKey>.Default),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public override Stream<ItemChange<TKey, TIdentity, TState>> ItemChangesStream { get; }

    /// <inheritdoc />
    public override Cell<IOrderedKeys<TKey, TIdentity, TState>> KeysCell => this.orderedByKey.Value.KeysCell;

    /// <inheritdoc />
    public override Stream<CollectionViewChange<TKey, TIdentity, TState>> KeyChangesStream =>
        this.orderedByKey.Value.KeyChangesStream;

    /// <inheritdoc />
    /// <remarks>On the collection itself this is the whole store, and it fires on every change.</remarks>
    public override Cell<CollectionSnapshot<TKey, TIdentity, TState>> SnapshotCell { get; }

    /// <inheritdoc />
    public override Cell<IReadOnlyDictionary<TKey, TIdentity>> ShapeCell { get; }

    /// <inheritdoc />
    /// <remarks>A root owns the store, so this is itself.</remarks>
    internal override ReactiveCollection<TKey, TIdentity, TState> Root => this;

    /// <summary>
    ///     Builds the collection both of the public factories on
    ///     <see cref="ReactiveCollection{TKey,TIdentity,TState}" /> return.
    /// </summary>
    /// <param name="keySelector">Derives an item's key from its immutable portion.</param>
    /// <param name="initialEntries">The collection's initial contents.</param>
    /// <param name="emptyStateMap">The empty map to build the initial contents on.</param>
    /// <param name="editStreams">Every stream that will ever edit the collection.</param>
    /// <returns>The collection.</returns>
    internal static ReactiveCollection<TKey, TIdentity, TState> CreateImpl(
        Func<TIdentity, TKey> keySelector,
        IEnumerable<Item<TIdentity, TState>> initialEntries,
        IStateMap<TKey, TState> emptyStateMap,
        params Stream<CollectionEdit<TKey, TIdentity, TState>>[] editStreams)
    {
        ImmutableDictionary<TKey, TIdentity>.Builder identities =
            ImmutableDictionary.CreateBuilder<TKey, TIdentity>();

        Dictionary<TKey, TState> states = new();

        foreach (Item<TIdentity, TState> item in initialEntries)
        {
            TKey key = keySelector(item.Identity);

            // ContainsKey rather than TryAdd, which netstandard2.0 and net472 do not have on a
            // dictionary and which a builder does not have at all.
            if (identities.ContainsKey(key))
            {
                throw new ArgumentException($"Duplicate key '{key}' in the initial items.");
            }

            identities.Add(key, item.Identity);
            states.Add(key, item.State);
        }

        CollectionSnapshot<TKey, TIdentity, TState> initial =
            new(identities.ToImmutable(), emptyStateMap.With(states, Array.Empty<TKey>()));

        Stream<CollectionEdit<TKey, TIdentity, TState>> editsStream = MergeEdits(editStreams);

        return TransactionInternal.Apply((trans, _) =>
        {
            // The resolution of an edit depends on the state it is resolved against, and that state
            // is produced by resolving edits: an explicit loop.
            LoopedCell<CollectionSnapshot<TKey, TIdentity, TState>> snapshotLoopCell = new();

            Stream<ItemChange<TKey, TIdentity, TState>> itemChangesStream = editsStream
                .SnapshotImpl(
                    snapshotLoopCell,
                    (edit, before) => Resolve(keySelector, edit, before))
                .FilterSomeInternal();

            Cell<CollectionSnapshot<TKey, TIdentity, TState>> snapshotCell = itemChangesStream
                .MapImpl(static change => change.After)
                .HoldImpl(initial);

            snapshotLoopCell.Loop(trans, snapshotCell);

            // Derived by its own hold off the same stream rather than by calming a map of the
            // snapshot cell — both holds see the same transaction, so the two views can never
            // disagree, and this one fires on exactly the stated condition: the item count changed,
            // or a key changed.
            Cell<IReadOnlyDictionary<TKey, TIdentity>> shapeCell = itemChangesStream
                .FilterImpl(static change => change.IsStructural)
                .MapImpl(static change => change.After.Identities)
                .HoldImpl(initial.Identities);

            return new RootCollection<TKey, TIdentity, TState>(
                itemChangesStream,
                snapshotCell,
                shapeCell);
        });
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Cheap enough to create per bound view: it filters on a single hash lookup and never
    ///     touches the rest of the collection.
    ///     The key need not exist yet. Removal fires <paramref name="onAbsent" /> and a later add
    ///     under the same key fires <paramref name="onPresent" /> again, so a view bound to a key
    ///     can outlive the item.
    ///     Cached weakly per key, so N observers of one key share a node and the node goes away
    ///     when the last observer does. Two projections of the same key are two cells, which is
    ///     what keeps the C# and F# surfaces from handing each other the wrong one.
    /// </remarks>
    internal override Cell<TProjected> CreateIdentityCell<TProjected>(
        TKey key,
        Func<TIdentity, TProjected> onPresent,
        Func<TProjected> onAbsent) =>
        TransactionInternal.RunImpl(() =>
            this.ItemChangesStream
                .MapImpl(change => change.ProjectIdentityChangeFor(key, onPresent, onAbsent))
                .FilterSomeInternal()
                .HoldLazyImpl(this.SnapshotCell.SampleLazyImpl().MapImpl(
                    snapshot => snapshot.TryGetIdentity(key, out TIdentity identity)
                        ? onPresent(identity)
                        : onAbsent())));

    /// <summary>
    ///     Merges the input streams into one. Edits arriving from different streams in the same
    ///     transaction combine into a single change event and a single cell update; the ambiguous
    ///     case is rejected inside
    ///     <see cref="CollectionEdit{TKey,TIdentity,TState}.CombineWith" /> rather than resolved by merge
    ///     order, which SodaFlow does not define.
    /// </summary>
    private static Stream<CollectionEdit<TKey, TIdentity, TState>> MergeEdits(
        IEnumerable<Stream<CollectionEdit<TKey, TIdentity, TState>>> editStreams) =>
        editStreams.Aggregate(
            StreamInternal.NeverImpl<CollectionEdit<TKey, TIdentity, TState>>(),
            static (mergedStream, editStream) => mergedStream.MergeImpl(
                s: editStream,
                f: static (left, right) => left.CombineWith(right)));

    private static MaybeInternal<ItemChange<TKey, TIdentity, TState>> Resolve(
        Func<TIdentity, TKey> keySelector,
        CollectionEdit<TKey, TIdentity, TState> edit,
        CollectionSnapshot<TKey, TIdentity, TState> before)
    {
        Dictionary<TKey, TState> newStates = new();
        HashSet<TKey> added = new();
        HashSet<TKey> removed = new();

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

            if (!newStates.TryGet(update.Key, out TState current) &&
                !before.States.TryGetState(update.Key, out current))
            {
                throw new KeyNotFoundException(
                    $"Cannot update key '{update.Key}': no such item in the collection.");
            }

            newStates[update.Key] = update.Value(current);
        }

        if (newStates.Count == 0 && removed.Count == 0)
        {
            return MaybeInternal<ItemChange<TKey, TIdentity, TState>>.None;
        }

        // Only a structural edit moves the identity map, and it moves it by building the next
        // version from this one rather than copying it - so an add costs one write rather than a
        // pass over the collection.
        ImmutableDictionary<TKey, TIdentity> identities =
            added.Count > 0 || removed.Count > 0
                ? before.WithIdentities(
                    edit.Adds.Select(item =>
                        new KeyValuePair<TKey, TIdentity>(keySelector(item.Identity), item.Identity)),
                    removed)
                : before.IdentitiesImpl;

        CollectionSnapshot<TKey, TIdentity, TState> after = new(
            identities,
            before.States.With(newStates, removed));

        return MaybeInternal.Some(
            new ItemChange<TKey, TIdentity, TState>(before, after, newStates, added, removed));
    }

    internal override Cell<TProjected> CreateStateCell<TProjected>(
        TKey key,
        Func<TState, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        Cell<TProjected> stateCell = TransactionInternal.RunImpl(() =>
            // Read the new value off the event rather than snapshotting the snapshot cell: a cell
            // sampled during a transaction still holds its pre-transaction value.
            //
            // The seed is lazy for the same reason. This cell may well be built during the very
            // transaction that adds its key - a row constructed in response to a structural change
            // - and by then the change stream has already fired, so the seed is all the cell has to
            // go on. An eager sample here would read the pre-transaction snapshot, in which the key
            // does not yet exist, and the cell would sit at no value until the next edit touching
            // that key. A lazy sample is forced after the transaction settles and yields the
            // correct value.
            //
            // The projection happens inside this map rather than in one chained after it, so a
            // wrapper's choice of optional type costs no extra node.
            this.ItemChangesStream
                .MapImpl(change => change.ProjectChangeFor(key, onPresent, onAbsent))
                .FilterSomeInternal()
                .HoldLazyImpl(this.SnapshotCell.SampleLazyImpl().MapImpl(
                    snapshot => snapshot.States.TryGetState(key, out TState state)
                        ? onPresent(state)
                        : onAbsent())));

        return stateCell;
    }
}
