using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     A collection at one version: the identity map, which changes only at a structural edit, and
///     the state map. A view reads them through its own scope.
/// </summary>
/// <remarks>
///     The snapshot of the root is the full store. The snapshot of a view is the same two maps
///     behind a set of keys that the view holds. Thus, a stage costs one small object for each
///     change and copies nothing, and a key that the view does not hold is missing from it.
///     <see cref="Count" /> counts the view, <see cref="ContainsKey" /> answers for the view, and a
///     lookup of a key out of the view finds nothing. For that cause there is no path from a view
///     to the store below it. The scope is the snapshot, and it is not a wrapper that a caller can
///     remove.
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
[PublicAPI]
public sealed class CollectionSnapshot<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>The keys that this snapshot accepts, or <see langword="null" /> for the full store.</summary>
    private readonly OrderedKeys<TKey, TIdentity, TState>? visible;

    /// <summary>
    ///     The scoped faces of the two maps. This code builds them at their first use, because the
    ///     important paths use <see cref="TryGetHalves" /> and read no face. Those paths
    ///     are a new build of a stage and a test of a predicate.
    /// </summary>
    /// <remarks>
    ///     There is no lock here. Two threads can each build one, and this code discards one of
    ///     the two. The two are immutable and give the same answers, thus the only cost is one
    ///     allocation.
    /// </remarks>
    private IReadOnlyDictionary<TKey, TIdentity>? scopedIdentities;

    private StateMap<TKey, TState>? scopedStates;

    internal CollectionSnapshot(
        ImmutableDictionary<TKey, TIdentity> identities,
        ImmutableStateMap<TKey, TState> states,
        ImmutableDictionary<TKey, long> arrivals,
        long nextArrival)
        : this(identities: identities, states: states, arrivals: arrivals, nextArrival: nextArrival, visible: null)
    {
    }

    private CollectionSnapshot(
        ImmutableDictionary<TKey, TIdentity> identities,
        ImmutableStateMap<TKey, TState> states,
        ImmutableDictionary<TKey, long> arrivals,
        long nextArrival,
        OrderedKeys<TKey, TIdentity, TState>? visible)
    {
        this.IdentitiesImpl = identities;
        this.StatesImpl = states;
        this.ArrivalsImpl = arrivals;
        this.NextArrival = nextArrival;
        this.visible = visible;
    }

    /// <summary>
    ///     The state map in the store, before a scope. It has the concrete type, because the
    ///     collection moves it forward and only this type can move forward.
    /// </summary>
    internal ImmutableStateMap<TKey, TState> StatesImpl { get; }

    /// <summary>
    ///     The immutable part of each item. This code replaces this object only at a structural
    ///     edit, thus a test of the two references is a correct test for a change of the shape.
    /// </summary>
    public IReadOnlyDictionary<TKey, TIdentity> Identities =>
        this.visible is null
            ? this.IdentitiesImpl
            : this.scopedIdentities ??=
                new ScopedIdentityMap<TKey, TIdentity, TState>(inner: this.IdentitiesImpl, visible: this.visible);

    /// <summary>The mutable part of each item.</summary>
    public StateMap<TKey, TState> States =>
        this.visible is null
            ? this.StatesImpl
            : this.scopedStates ??=
                new ScopedStateMap<TKey, TIdentity, TState>(inner: this.StatesImpl, visible: this.visible);

    /// <summary>
    ///     The identity map as its concrete type. Thus, this code builds the next version from this
    ///     version and does not copy it.
    /// </summary>
    /// <remarks>
    ///     This is a trie, and it was a usual dictionary. A usual dictionary reads faster and costs
    ///     <c>O(n)</c> to write, because a copy is the only path to its next version. Thus, a
    ///     structural edit had a cost in proportion to the collection, at each cost of the view
    ///     stages below it. The add-and-remove benchmark in <c>KeyedCollectionViewBenchmarks</c>
    ///     found that. The cost is now <c>O(log32 n)</c> for each key in the edit, which gives the
    ///     identity map the cost of the state map beside it. The state map was always a trie.
    /// </remarks>
    internal ImmutableDictionary<TKey, TIdentity> IdentitiesImpl { get; }

    /// <summary>
    ///     The time of the arrival of each key, as a number that only increases. This is the order
    ///     of the collection: the items come in the sequence of their arrival, and not in an order
    ///     of their values.
    /// </summary>
    /// <remarks>
    ///     This code writes this only at a structural edit, as it writes the identity map beside
    ///     it. Thus, a state edit, which is the usual edit, has no more cost. A key that an edit
    ///     removes and then adds is a new arrival.
    /// </remarks>
    internal ImmutableDictionary<TKey, long> ArrivalsImpl { get; }

    /// <summary>The number of the next key that comes to the collection.</summary>
    internal long NextArrival { get; }

    /// <summary>The number of items.</summary>
    public int Count => this.visible?.Count ?? this.IdentitiesImpl.Count;

    /// <summary>The same two maps, admitting only <paramref name="keys" />.</summary>
    /// <remarks>
    ///     A scope replaces the previous scope and does not add to it. That is correct,
    ///     because the keys of a stage are always a subset of the keys of the stage above it.
    /// </remarks>
    internal CollectionSnapshot<TKey, TIdentity, TState> ScopedTo(OrderedKeys<TKey, TIdentity, TState> keys) =>
        new(
            identities: this.IdentitiesImpl,
            states: this.StatesImpl,
            arrivals: this.ArrivalsImpl,
            nextArrival: this.NextArrival,
            visible: keys);

    /// <summary>Whether a key is one this snapshot admits.</summary>
    private bool IsVisible(TKey key) => this.visible is null || this.visible.Contains(key);

    /// <summary>True when this snapshot has a key.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns><see langword="true" /> when the key is available.</returns>
    public bool ContainsKey(TKey key) => this.IsVisible(key) && this.IdentitiesImpl.ContainsKey(key);

    /// <summary>Returns the two parts of the item for a key, when there is one.</summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="item">The item for that key, when this method returns true.</param>
    /// <returns><see langword="true" /> when the key is available.</returns>
    /// <remarks>
    ///     This is a <c>TryGet</c> and not an optional value, because this assembly does not
    ///     reference SodaFlow.Functional. Each language wrapper adds a <c>Lookup</c> above this
    ///     method, and that <c>Lookup</c> answers with the optional type of its language.
    /// </remarks>
    public bool TryGetItem(TKey key, [NotNullWhen(true)] out Item<TIdentity, TState>? item)
    {
        if (this.TryGetHalves(key: key, identity: out TIdentity? identity, state: out TState? state))
        {
            item = new Item<TIdentity, TState>(identity: identity, state: state);

            return true;
        }

        item = null;

        return false;
    }

    /// <summary>The immutable part of an item, with no read of the state map.</summary>
    /// <remarks>
    ///     This is for an order that makes its sort value from the identity only. That order then
    ///     costs one lookup for each key and not two, and a new build does this for each key that
    ///     it keeps.
    /// </remarks>
    internal bool TryGetIdentity(TKey key, [NotNullWhen(true)] out TIdentity? identity) =>
        this.IdentitiesImpl.TryGet(key: key, value: out identity) && this.IsVisible(key);

    /// <summary>When a key arrived, if this snapshot admits it.</summary>
    internal bool TryGetArrival(TKey key, out long arrival) =>
        this.ArrivalsImpl.TryGet(key: key, value: out arrival) && this.IsVisible(key);

    /// <summary>
    ///     The two parts of an item, with no <see cref="Item{TIdentity,TState}" /> around them.
    ///     <see cref="TryGetItem" /> adds that type.
    /// </summary>
    /// <remarks>
    ///     This is for the paths that read each key and not one key, such as a new build of a view
    ///     stage and a test of the predicate of a filter. On those paths that type is one
    ///     allocation for each key and for each build, and no code keeps it.
    ///     This code does the two lookups in each condition, and does not omit the second lookup
    ///     when the first lookup finds nothing. Thus, the compiler can see that this code assigns
    ///     the two outputs, and no suppression is necessary. A key that is missing from the
    ///     identity map is also missing from the state map, thus the second lookup is unnecessary
    ///     only for a key that the collection does not have.
    /// </remarks>
    internal bool TryGetHalves(
        TKey key,
        [NotNullWhen(true)] out TIdentity? identity,
        [NotNullWhen(true)] out TState? state)
    {
        // This calls the helper of this assembly and not the concrete TryGetValue. TryGetValue
        // declares a null output at false, and that makes a warning for a notnull TIdentity.
        bool hasIdentity = this.IdentitiesImpl.TryGet(key: key, value: out identity);
        bool hasState = this.StatesImpl.TryGetState(key: key, state: out state);

        // The test of the scope is last. Thus, the root, where that test is a test against null,
        // has no cost for it, and this code assigns the two outputs on each path with no
        // suppression.
        return hasIdentity && hasState && this.IsVisible(key);
    }

    /// <summary>
    ///     The next version of the identity map, with <paramref name="removed" /> out of it and
    ///     <paramref name="added" /> in it. This code builds it from this version and does not copy
    ///     this version.
    /// </summary>
    internal ImmutableDictionary<TKey, TIdentity> WithIdentities(
        IEnumerable<KeyValuePair<TKey, TIdentity>> added,
        IEnumerable<TKey> removed)
    {
        // ToBuilder and ToImmutable are each <c>O(1)</c>, because the builder uses the root of
        // this map and does not copy it. Thus, the cost is one <c>O(log32 n)</c> write for each key
        // in the edit.
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

    /// <summary>
    ///     The next version of the arrival numbers, with <paramref name="removed" /> out of it.
    ///     This code gives a number to each key in <paramref name="added" />, in the sequence of
    ///     that collection.
    /// </summary>
    /// <remarks>
    ///     An add of a key that is here now gets a number as each other arrival does, thus it goes
    ///     to the end. Such an add is a removal and an add in one edit, which is the method to
    ///     replace the identity of an item. This code builds the result from this version and does
    ///     not copy this version, as it does for the identity map.
    /// </remarks>
    internal (ImmutableDictionary<TKey, long> Arrivals, long NextArrival) WithArrivals(
        IEnumerable<TKey> added,
        IEnumerable<TKey> removed)
    {
        ImmutableDictionary<TKey, long>.Builder builder = this.ArrivalsImpl.ToBuilder();

        foreach (TKey key in removed)
        {
            builder.Remove(key);
        }

        long next = this.NextArrival;

        foreach (TKey key in added)
        {
            builder[key] = next++;
        }

        return (Arrivals: builder.ToImmutable(), NextArrival: next);
    }
}

/// <summary>
///     The identity map of a snapshot, with only the keys that one view holds.
/// </summary>
/// <remarks>
///     An enumeration reads the keys of the view and looks up each one, which is one lookup for
///     each key. The map with no scope permits one read of all keys. That difference is necessary,
///     because the keys of a view are not adjacent in the storage of the map, thus no code can read
///     them in the sequence of the storage.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class ScopedIdentityMap<TKey, TIdentity, TState> : IReadOnlyDictionary<TKey, TIdentity>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly ImmutableDictionary<TKey, TIdentity> inner;
    private readonly OrderedKeys<TKey, TIdentity, TState> visible;

    internal ScopedIdentityMap(
        ImmutableDictionary<TKey, TIdentity> inner,
        OrderedKeys<TKey, TIdentity, TState> visible)
    {
        this.inner = inner;
        this.visible = visible;
    }

    public int Count => this.visible.Count;

    public IEnumerable<TKey> Keys => this.visible;

    public IEnumerable<TIdentity> Values => this.visible.Select(this.IdentityOf);

    public TIdentity this[TKey key] =>
        this.TryGetValue(key: key, value: out TIdentity? identity)
            ? identity
            : throw new KeyNotFoundException($"The view does not hold the key {key}.");

    public bool ContainsKey(TKey key) => this.visible.Contains(key) && this.inner.ContainsKey(key);

#if NET
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TIdentity value) =>
        this.inner.TryGet(key: key, value: out value) && this.visible.Contains(key);
#else
    public bool TryGetValue(TKey key, out TIdentity value)
    {
        if (this.inner.TryGet(key: key, value: out TIdentity? valueLocal) && this.visible.Contains(key))
        {
            value = valueLocal;
            return true;
        }

        // ReSharper disable once NullableWarningSuppressionIsUsed
        value = default!;
        return false;
    }
#endif

    public IEnumerator<KeyValuePair<TKey, TIdentity>> GetEnumerator() =>
        this.visible
            .Select(key => new KeyValuePair<TKey, TIdentity>(key: key, value: this.IdentityOf(key)))
            .GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    /// <summary>The identity of a key this view holds.</summary>
    /// <remarks>
    ///     The keys of a view are a subset of the keys of the map, thus this lookup finds each key
    ///     while the two agree. It throws an exception and does not give a default value, because a
    ///     default value here is an incorrect answer with no message.
    /// </remarks>
    private TIdentity IdentityOf(TKey key) =>
        this.inner.TryGet(key: key, value: out TIdentity? identity)
            ? identity
            : throw new KeyNotFoundException($"The view holds the key {key} but the identity map behind it does not.");
}

/// <summary>The state map of a snapshot, admitting only the keys one view holds.</summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class ScopedStateMap<TKey, TIdentity, TState> : StateMap<TKey, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly StateMap<TKey, TState> inner;
    private readonly OrderedKeys<TKey, TIdentity, TState> visible;

    internal ScopedStateMap(
        StateMap<TKey, TState> inner,
        OrderedKeys<TKey, TIdentity, TState> visible)
    {
        this.inner = inner;
        this.visible = visible;
    }

    public override int Count => this.visible.Count;

    public override IEnumerable<TKey> Keys => this.visible;

    /// <inheritdoc />
    /// <remarks>
    ///     This is one lookup for each key, and the map with no scope permits one read of all
    ///     keys. See
    ///     <see cref="ScopedIdentityMap{TKey,TIdentity,TState}" /> for why that cannot be avoided.
    /// </remarks>
    public override IEnumerable<KeyValuePair<TKey, TState>> Pairs =>
        this.visible.Select(key => new KeyValuePair<TKey, TState>(key: key, value: this.StateOf(key)));

    /// <summary>The state of a key this view holds.</summary>
    /// <remarks>This throws an exception and does not give a default value, for the cause in the
    /// identity map.</remarks>
    private TState StateOf(TKey key) =>
        this.inner.TryGetState(key: key, state: out TState? state)
            ? state
            : throw new KeyNotFoundException($"The view holds the key {key} but the state map behind it does not.");

    public override bool TryGetState(TKey key, [NotNullWhen(true)] out TState? state) =>
        this.inner.TryGetState(key: key, state: out state) && this.visible.Contains(key);

    public override bool ContainsKey(TKey key) => this.visible.Contains(key) && this.inner.ContainsKey(key);
}
