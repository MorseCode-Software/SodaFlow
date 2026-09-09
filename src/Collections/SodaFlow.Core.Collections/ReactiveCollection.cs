using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using JetBrains.Annotations;

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
/// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class ReactiveCollection<TKey, TId, TState> : IReactiveCollection<TKey, TId, TState>
    where TKey : notnull
    where TId : notnull
{
    /// <summary>
    ///     The per-key cells, keyed by the projected type as well as the key. What a per-item cell
    ///     holds is whatever the language wrapper asked for - Maybe in C#, option in F# - and two
    ///     wrappers over one collection must not be handed each other's cells.
    /// </summary>
    private readonly Dictionary<ProjectedKey, WeakReference<object>> stateCellCache = new();

    /// <summary>
    ///     A plain object rather than <c>System.Threading.Lock</c>, which arrived in .NET 9 and is
    ///     not available on any of this package's target frameworks.
    /// </summary>
    private readonly object cacheGate = new();

    private readonly Lazy<IReactiveCollection<TKey, TId, TState>> orderedByKey;

    private ReactiveCollection(
        Stream<CollectionChange<TKey, TId, TState>> itemChangesStream,
        Cell<CollectionSnapshot<TKey, TId, TState>> snapshotCell,
        Cell<IReadOnlyDictionary<TKey, TId>> shapeCell)
    {
        this.ItemChangesStream = itemChangesStream;
        this.SnapshotCell = snapshotCell;
        this.ShapeCell = shapeCell;

        // The ordering is built on first use. A collection nobody sorts or lists never pays for a
        // sorted key set, and TKey only has to be comparable if something actually asks for keys in
        // order.
        this.orderedByKey = new Lazy<IReactiveCollection<TKey, TId, TState>>(
            () => CollectionViewUtility.CreateRootImpl(this, Comparer<TKey>.Default),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    ///     Every resolved change as keyed deltas, carrying the new state of each key that moved.
    ///     Unordered — see <see cref="ChangesStream" /> for the positional view of the same thing.
    /// </summary>
    public Stream<CollectionChange<TKey, TId, TState>> ItemChangesStream { get; }

    /// <inheritdoc />
    public Cell<IOrderedKeys<TKey, TId, TState>> KeysCell => this.orderedByKey.Value.KeysCell;

    /// <inheritdoc />
    public Stream<CollectionViewChange<TKey, TId, TState>> ChangesStream =>
        this.orderedByKey.Value.ChangesStream;

    /// <summary>Fires on every change, structural or otherwise.</summary>
    // ReSharper disable once InheritdocConsiderUsage - the interface says what this is; the
    // summary above says when it fires, which is what a reader of the root wants.
    public Cell<CollectionSnapshot<TKey, TId, TState>> SnapshotCell { get; }

    /// <summary>The outer view: fires only when the item count changes or a key changes.</summary>
    public Cell<IReadOnlyDictionary<TKey, TId>> ShapeCell { get; }

    /// <inheritdoc />
    /// <remarks>A root owns the store, so this is itself.</remarks>
    public ReactiveCollection<TKey, TId, TState> Root => this;

    /// <summary>
    ///     Defines a collection from its initial contents and every stream that will ever edit it.
    ///     There is no imperative entry point: what can change the collection is fixed here, at
    ///     construction, and is visible in one place.
    /// </summary>
    /// <param name="keySelector">Derives an item's key from its immutable portion.</param>
    /// <param name="initialEntries">The collection's initial contents.</param>
    /// <param name="editStreams">Every stream that will ever edit the collection.</param>
    /// <returns>The collection.</returns>
    /// <remarks>
    ///     Use <see cref="CollectionEdit{TKey,TId,TState}" />'s lifting factories to turn domain
    ///     streams into edits. Where the edits depend on something derived from the collection
    ///     itself, close the circle with a stream loop at the call site rather than reaching for a
    ///     sink.
    /// </remarks>
    public static ReactiveCollection<TKey, TId, TState> Create(
        Func<TId, TKey> keySelector,
        IEnumerable<Entry<TId, TState>> initialEntries,
        params Stream<CollectionEdit<TKey, TId, TState>>[] editStreams) =>
        Create(keySelector, initialEntries, ImmutableStateMap<TKey, TState>.Empty, editStreams);

    /// <summary>
    ///     Defines a collection, choosing the storage strategy rather than taking the default hash
    ///     array mapped trie.
    /// </summary>
    /// <param name="keySelector">Derives an item's key from its immutable portion.</param>
    /// <param name="initialEntries">The collection's initial contents.</param>
    /// <param name="emptyStateMap">The empty map to build the initial contents on.</param>
    /// <param name="editStreams">Every stream that will ever edit the collection.</param>
    /// <returns>The collection.</returns>
    public static ReactiveCollection<TKey, TId, TState> Create(
        Func<TId, TKey> keySelector,
        IEnumerable<Entry<TId, TState>> initialEntries,
        IStateMap<TKey, TState> emptyStateMap,
        params Stream<CollectionEdit<TKey, TId, TState>>[] editStreams)
    {
        Dictionary<TKey, TId> identities = new();
        Dictionary<TKey, TState> states = new();

        foreach (Entry<TId, TState> entry in initialEntries)
        {
            TKey key = keySelector(entry.Identity);

            // ContainsKey rather than Dictionary.TryAdd, which netstandard2.0 and net472 do not
            // have.
            if (identities.ContainsKey(key))
            {
                throw new ArgumentException($"Duplicate key '{key}' in the initial entries.");
            }

            identities.Add(key, entry.Identity);
            states.Add(key, entry.State);
        }

        CollectionSnapshot<TKey, TId, TState> initial =
            new(identities, emptyStateMap.With(states, Array.Empty<TKey>()));

        Stream<CollectionEdit<TKey, TId, TState>> editsStream = MergeEdits(editStreams);

        return TransactionInternal.Apply((trans, _) =>
        {
            // The resolution of an edit depends on the state it is resolved against, and that state
            // is produced by resolving edits: an explicit loop.
            LoopedCell<CollectionSnapshot<TKey, TId, TState>> snapshotLoopCell = new();

            Stream<CollectionChange<TKey, TId, TState>> itemChangesStream = editsStream
                .SnapshotImpl(
                    snapshotLoopCell,
                    (edit, before) => Resolve(keySelector, edit, before))
                .FilterSomeInternal();

            Cell<CollectionSnapshot<TKey, TId, TState>> snapshotCell = itemChangesStream
                .MapImpl(static change => change.After)
                .HoldImpl(initial);

            snapshotLoopCell.Loop(trans, snapshotCell);

            // Derived by its own hold off the same stream rather than by calming a map of the
            // snapshot cell — both holds see the same transaction, so the two views can never
            // disagree, and this one fires on exactly the stated condition: the item count changed,
            // or a key changed.
            Cell<IReadOnlyDictionary<TKey, TId>> shapeCell = itemChangesStream
                .FilterImpl(static change => change.IsStructural)
                .MapImpl(static change => change.After.Identities)
                .HoldImpl(initial.Identities);

            return new ReactiveCollection<TKey, TId, TState>(
                itemChangesStream,
                snapshotCell,
                shapeCell);
        });
    }

    /// <summary>
    ///     A cell tracking one item's mutable portion, shaped by the projection the language
    ///     wrapper supplies. Cheap enough to create per bound view: it filters on a single hash
    ///     lookup and never touches the rest of the collection.
    /// </summary>
    /// <remarks>
    ///     The key need not exist yet. Removal fires <paramref name="onAbsent" /> and a later add
    ///     under the same key fires <paramref name="onPresent" /> again, so a view bound to a key
    ///     can outlive the item.
    ///     Cached weakly per key, so N observers of one key share a node and the node goes away
    ///     when the last observer does. Two projections of the same key are two cells, which is
    ///     what keeps the C# and F# surfaces from handing each other the wrong one.
    /// </remarks>
    internal Cell<TProjected> StateCellImpl<TProjected>(
        TKey key,
        Func<TState, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        ProjectedKey cacheKey = new(typeof(TProjected), key);

        lock (this.cacheGate)
        {
            if (this.stateCellCache.TryGet(cacheKey, out WeakReference<object> reference) &&
                reference.TryGetTarget(out object? cached))
            {
                return (Cell<TProjected>)cached;
            }

            return this.CreateStateCell(cacheKey, key, onPresent, onAbsent);
        }
    }

    /// <summary>
    ///     Merges the input streams into one. Edits arriving from different streams in the same
    ///     transaction combine into a single change event and a single cell update; the ambiguous
    ///     case is rejected inside
    ///     <see cref="CollectionEdit{TKey,TId,TState}.CombineWith" /> rather than resolved by merge
    ///     order, which SodaFlow does not define.
    /// </summary>
    private static Stream<CollectionEdit<TKey, TId, TState>> MergeEdits(
        IReadOnlyList<Stream<CollectionEdit<TKey, TId, TState>>> editStreams) =>
        editStreams.Aggregate(
            StreamInternal.NeverImpl<CollectionEdit<TKey, TId, TState>>(),
            static (mergedStream, editStream) => mergedStream.MergeImpl(
                s: editStream,
                f: static (left, right) => left.CombineWith(right)));

    private static MaybeInternal<CollectionChange<TKey, TId, TState>> Resolve(
        Func<TId, TKey> keySelector,
        CollectionEdit<TKey, TId, TState> edit,
        CollectionSnapshot<TKey, TId, TState> before)
    {
        Dictionary<TKey, TState> newStates = new();
        HashSet<TKey> added = new();
        HashSet<TKey> removed = new();

        foreach (TKey key in edit.Removes.Where(before.ContainsKey))
        {
            removed.Add(key);
        }

        foreach (Entry<TId, TState> entry in edit.Adds)
        {
            TKey key = keySelector(entry.Identity);

            if (before.ContainsKey(key) && !removed.Contains(key))
            {
                throw new InvalidOperationException(
                    $"Key '{key}' already exists. Re-keying is a remove followed by an add.");
            }

            added.Add(key);
            removed.Remove(key);
            newStates[key] = entry.State;
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
            return MaybeInternal<CollectionChange<TKey, TId, TState>>.None;
        }

        IReadOnlyDictionary<TKey, TId> identities = before.Identities;

        if (added.Count > 0 || removed.Count > 0)
        {
            Dictionary<TKey, TId> next = new(before.Identities.Count);

            foreach (KeyValuePair<TKey, TId> pair in before.Identities)
            {
                next.Add(pair.Key, pair.Value);
            }

            foreach (TKey key in removed)
            {
                next.Remove(key);
            }

            foreach (Entry<TId, TState> entry in edit.Adds)
            {
                next[keySelector(entry.Identity)] = entry.Identity;
            }

            identities = next;
        }

        CollectionSnapshot<TKey, TId, TState> after = new(
            identities,
            before.States.With(newStates, removed));

        return MaybeInternal.Some(
            new CollectionChange<TKey, TId, TState>(after, newStates, added, removed));
    }

    private Cell<TProjected> CreateStateCell<TProjected>(
        ProjectedKey cacheKey,
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

        this.PruneCache();
        this.stateCellCache[cacheKey] = new WeakReference<object>(stateCell);

        return stateCell;
    }

    private void PruneCache()
    {
        if (this.stateCellCache.Count < 64)
        {
            return;
        }

        List<ProjectedKey> dead = new();

        foreach (KeyValuePair<ProjectedKey, WeakReference<object>> pair in this.stateCellCache)
        {
            if (!pair.Value.TryGetTarget(out object? _))
            {
                dead.Add(pair.Key);
            }
        }

        foreach (ProjectedKey key in dead)
        {
            this.stateCellCache.Remove(key);
        }
    }

    /// <summary>
    ///     A cache key: which key, and what the wrapper asked the cell to hold.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private readonly struct ProjectedKey : IEquatable<ProjectedKey>
    {
        private readonly Type projectedType;
        private readonly TKey key;

        internal ProjectedKey(Type projectedType, TKey key)
        {
            this.projectedType = projectedType;
            this.key = key;
        }

        public bool Equals(ProjectedKey other) =>
            this.projectedType == other.projectedType &&
            EqualityComparer<TKey>.Default.Equals(this.key, other.key);

        public override bool Equals(object? obj) => obj is ProjectedKey other && this.Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (this.projectedType.GetHashCode() * 397) ^
                    EqualityComparer<TKey>.Default.GetHashCode(this.key);
            }
        }
    }
}
