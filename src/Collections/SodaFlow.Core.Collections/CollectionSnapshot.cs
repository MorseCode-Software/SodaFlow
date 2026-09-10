using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     A collection at one logical version: the identity map (which only changes on a structural
///     edit) and the state map, seen through whichever view asked for it.
/// </summary>
/// <remarks>
///     The root's snapshot is the whole store. A view's is this same pair of maps behind a set of
///     visible keys, so a stage costs one small object per change rather than a copy of anything,
///     and a key the view does not hold is absent from it - <see cref="Count" /> counts the view,
///     <see cref="ContainsKey" /> answers for the view, and a lookup outside it finds nothing.
///     That is why there is no way to reach past a view to the store it draws on: the scoping is
///     the snapshot, not a wrapper the caller can unwrap.
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
public sealed class CollectionSnapshot<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>The keys this snapshot admits, or <see langword="null" /> for the whole store.</summary>
    private readonly IOrderedKeys<TKey, TIdentity, TState>? visible;

    /// <summary>
    ///     The state map as stored, before any scoping. Concrete, because the collection advances
    ///     it and only this type can be advanced.
    /// </summary>
    internal ImmutableStateMap<TKey, TState> StatesImpl { get; }

    /// <summary>
    ///     The scoped faces of the two maps, built on first use because the paths that matter -
    ///     rebuilding a stage, testing a predicate - go through <see cref="TryGetHalves" /> and
    ///     never ask for either.
    /// </summary>
    /// <remarks>
    ///     Raced rather than locked. Two threads can each build one, and the loser's is discarded;
    ///     both are immutable and answer identically, so the only cost of losing is the allocation.
    /// </remarks>
    private IReadOnlyDictionary<TKey, TIdentity>? scopedIdentities;

    private StateMap<TKey, TState>? scopedStates;

    internal CollectionSnapshot(
        ImmutableDictionary<TKey, TIdentity> identities,
        ImmutableStateMap<TKey, TState> states)
        : this(identities, states, visible: null)
    {
    }

    private CollectionSnapshot(
        ImmutableDictionary<TKey, TIdentity> identities,
        ImmutableStateMap<TKey, TState> states,
        IOrderedKeys<TKey, TIdentity, TState>? visible)
    {
        this.IdentitiesImpl = identities;
        this.StatesImpl = states;
        this.visible = visible;
    }

    /// <summary>The same two maps, admitting only <paramref name="keys" />.</summary>
    /// <remarks>
    ///     Scoping replaces rather than intersects, which is sound because a stage's keys are always
    ///     a subset of the keys of the stage above it.
    /// </remarks>
    internal CollectionSnapshot<TKey, TIdentity, TState> ScopedTo(
        IOrderedKeys<TKey, TIdentity, TState> keys) =>
        new(this.IdentitiesImpl, this.StatesImpl, keys);

    /// <summary>Whether a key is one this snapshot admits.</summary>
    private bool IsVisible(TKey key) => this.visible is null || this.visible.Contains(key);

    /// <summary>
    ///     The immutable portion of every item. This object is replaced only on a structural edit,
    ///     which is what makes reference equality a sound test for "did the shape change".
    /// </summary>
    public IReadOnlyDictionary<TKey, TIdentity> Identities =>
        this.visible is null
            ? this.IdentitiesImpl
            : this.scopedIdentities ??=
                new ScopedIdentityMap<TKey, TIdentity, TState>(this.IdentitiesImpl, this.visible);

    /// <summary>The mutable portion of every item.</summary>
    public StateMap<TKey, TState> States =>
        this.visible is null
            ? this.StatesImpl
            : this.scopedStates ??=
                new ScopedStateMap<TKey, TIdentity, TState>(this.StatesImpl, this.visible);

    /// <summary>
    ///     The identity map as its concrete type, which is what lets the next version of it be
    ///     built from this one rather than copied out of it.
    /// </summary>
    /// <remarks>
    ///     A trie rather than the plain dictionary this was. A plain one is faster to read and costs
    ///     O(n) to write, because the only way to produce its next version is to copy it — so a
    ///     structural edit scaled with the collection however cheaply the view stages below it
    ///     absorbed the change. <c>KeyedCollectionViewBenchmarks</c>'s add-and-remove is what found
    ///     that; it costs O(log32 n) per key touched now, which puts the identity map on the same
    ///     footing as the state map beside it, and that was always a trie.
    /// </remarks>
    internal ImmutableDictionary<TKey, TIdentity> IdentitiesImpl { get; }

    /// <summary>The number of items.</summary>
    public int Count => this.visible?.Count ?? this.IdentitiesImpl.Count;

    /// <summary>Whether a key is present.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns><see langword="true" /> if the key is present.</returns>
    public bool ContainsKey(TKey key) =>
        this.IsVisible(key) && this.IdentitiesImpl.ContainsKey(key);

    /// <summary>Returns both halves of the item stored under a key, if there is one.</summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="item">The item stored under it, when this returns true.</param>
    /// <returns><see langword="true" /> if the key is present.</returns>
    /// <remarks>
    ///     A <c>TryGet</c> rather than an optional value because this assembly does not reference
    ///     SodaFlow.Functional; the language wrappers add <c>Lookup</c> over this, answering with
    ///     each language's own optional type.
    /// </remarks>
    public bool TryGetItem(TKey key, out Item<TIdentity, TState>? item)
    {
        if (this.TryGetHalves(key, out TIdentity identity, out TState state))
        {
            item = new Item<TIdentity, TState>(identity, state);

            return true;
        }

        item = null;

        return false;
    }

    /// <summary>The immutable half of an item, without reading the state map for it.</summary>
    /// <remarks>
    ///     For an order that projects its sort value from the identity alone, which then costs one
    ///     lookup per key rather than two - and a rebuild does this for every key it keeps.
    /// </remarks>
    internal bool TryGetIdentity(TKey key, out TIdentity identity) =>
        this.IdentitiesImpl.TryGet(key, out identity) && this.IsVisible(key);

    /// <summary>
    ///     Both halves of an item, without the <see cref="Item{TIdentity,TState}" /> that
    ///     <see cref="TryGetItem" /> wraps them in.
    /// </summary>
    /// <remarks>
    ///     For the paths that read every key rather than one - rebuilding a view stage, testing a
    ///     filter's predicate - where that wrapper is an allocation per key per rebuild and nothing
    ///     keeps it afterwards.
    ///     Both lookups happen either way, rather than the second being skipped when the first
    ///     misses, so that both outputs are definitely assigned without a suppression. A key absent
    ///     from the identity map is absent from the state map too, so the wasted lookup only
    ///     happens for a key that is not there at all.
    /// </remarks>
    internal bool TryGetHalves(TKey key, out TIdentity identity, out TState state)
    {
        // Through the assembly's own helper rather than the concrete TryGetValue, which is
        // annotated to leave its output null on false and so warns against a notnull TIdentity.
        bool hasIdentity = this.IdentitiesImpl.TryGet(key, out identity);
        bool hasState = this.StatesImpl.TryGetState(key, out state);

        // The visibility test comes last so that the root, where it is a null check, pays nothing
        // for it, and so that both outputs are assigned on every path without a suppression.
        return hasIdentity && hasState && this.IsVisible(key);
    }

    /// <summary>
    ///     The next version of the identity map, with <paramref name="removed" /> dropped and
    ///     <paramref name="added" /> put in. Built from this one rather than copied out of it.
    /// </summary>
    internal ImmutableDictionary<TKey, TIdentity> WithIdentities(
        IEnumerable<KeyValuePair<TKey, TIdentity>> added,
        IEnumerable<TKey> removed)
    {
        // ToBuilder and ToImmutable are both O(1) - the builder wraps this map's root rather than
        // copying it - so what this costs is one O(log32 n) write per key touched.
        ImmutableDictionary<TKey, TIdentity>.Builder builder = this.IdentitiesImpl.ToBuilder();

        foreach (TKey key in removed)
        {
            builder.Remove(key);
        }

        foreach (KeyValuePair<TKey, TIdentity> pair in added)
        {
            builder[pair.Key] = pair.Value;
        }

        return builder.ToImmutable();
    }
}

/// <summary>
///     The identity map of a snapshot, admitting only the keys one view holds.
/// </summary>
/// <remarks>
///     Enumeration walks the view's keys and looks each one up, which is a lookup per key rather
///     than the single walk the unscoped map allows. That is inherent: the keys a view holds are
///     not contiguous in the map's storage, so nothing can be read in storage order.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class ScopedIdentityMap<TKey, TIdentity, TState> : IReadOnlyDictionary<TKey, TIdentity>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly ImmutableDictionary<TKey, TIdentity> inner;
    private readonly IOrderedKeys<TKey, TIdentity, TState> visible;

    internal ScopedIdentityMap(
        ImmutableDictionary<TKey, TIdentity> inner,
        IOrderedKeys<TKey, TIdentity, TState> visible)
    {
        this.inner = inner;
        this.visible = visible;
    }

    public int Count => this.visible.Count;

    public IEnumerable<TKey> Keys => this.visible;

    public IEnumerable<TIdentity> Values => this.visible.Select(this.IdentityOf);

    public TIdentity this[TKey key] =>
        this.TryGetValue(key, out TIdentity identity)
            ? identity
            : throw new KeyNotFoundException($"The view does not hold the key {key}.");

    public bool ContainsKey(TKey key) =>
        this.visible.Contains(key) && this.inner.ContainsKey(key);

    public bool TryGetValue(TKey key, out TIdentity value) =>
        this.inner.TryGet(key, out value) && this.visible.Contains(key);

    public IEnumerator<KeyValuePair<TKey, TIdentity>> GetEnumerator() =>
        this.visible
            .Select(key => new KeyValuePair<TKey, TIdentity>(key, this.IdentityOf(key)))
            .GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    /// <summary>The identity of a key this view holds.</summary>
    /// <remarks>
    ///     A view's keys are a subset of the map's, so this cannot miss unless the two have been
    ///     allowed to disagree. It throws rather than yielding a default, because a default here
    ///     would be a wrong answer travelling quietly.
    /// </remarks>
    private TIdentity IdentityOf(TKey key) =>
        this.inner.TryGet(key, out TIdentity identity)
            ? identity
            : throw new KeyNotFoundException(
                $"The view holds the key {key} but the identity map behind it does not.");
}

/// <summary>The state map of a snapshot, admitting only the keys one view holds.</summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class ScopedStateMap<TKey, TIdentity, TState> : StateMap<TKey, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly StateMap<TKey, TState> inner;
    private readonly IOrderedKeys<TKey, TIdentity, TState> visible;

    internal ScopedStateMap(
        StateMap<TKey, TState> inner,
        IOrderedKeys<TKey, TIdentity, TState> visible)
    {
        this.inner = inner;
        this.visible = visible;
    }

    public override int Count => this.visible.Count;

    public override IEnumerable<TKey> Keys => this.visible;

    /// <inheritdoc />
    /// <remarks>
    ///     A lookup per key, unlike the unscoped map's single walk. See
    ///     <see cref="ScopedIdentityMap{TKey,TIdentity,TState}" /> for why that cannot be avoided.
    /// </remarks>
    public override IEnumerable<KeyValuePair<TKey, TState>> Pairs =>
        this.visible.Select(key => new KeyValuePair<TKey, TState>(key, this.StateOf(key)));

    /// <summary>The state of a key this view holds.</summary>
    /// <remarks>Throws rather than yielding a default, for the reason the identity map's does.</remarks>
    private TState StateOf(TKey key) =>
        this.inner.TryGetState(key, out TState state)
            ? state
            : throw new KeyNotFoundException(
                $"The view holds the key {key} but the state map behind it does not.");

    public override bool TryGetState(TKey key, out TState state) =>
        this.inner.TryGetState(key, out state) && this.visible.Contains(key);

    public override bool ContainsKey(TKey key) =>
        this.visible.Contains(key) && this.inner.ContainsKey(key);
}
