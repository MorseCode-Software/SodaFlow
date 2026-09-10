using System.Collections.Generic;
using System.Collections.Immutable;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     An immutable-appearing view of the mutable portion of every item, keyed by the key derived
///     from the immutable portion.
/// </summary>
/// <remarks>
///     <para>
///         This is the seam where the storage strategy is chosen. The contract that every
///         implementation must honor:
///     </para>
///     <para>
///         <b>A value returned by an instance never changes for the lifetime of that
///         instance.</b>
///     </para>
///     <para>
///         SodaFlow reads a cell's <i>pre-transaction</i> value during a transaction. If a
///         snapshot handed out earlier aliases storage that has since been mutated, those reads
///         silently observe the future. An implementation that mutates in place must therefore
///         version its storage and serve older instances from a log — it must not simply hand back
///         the live map.
///     </para>
///     <para>
///         Lookup is a <c>TryGet</c> rather than an optional value because this assembly does not
///         reference SodaFlow.Functional; the language wrappers add
///         <c>Lookup</c> over this, answering with each language's own optional type.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
public abstract class StateMap<TKey, TState>
    where TKey : notnull
{
    /// <summary>
    ///     Internal, so that this assembly is the only thing that can produce one. There is one
    ///     storage strategy and one filtered face of it, they are not meant to be substituted, and
    ///     being a closed hierarchy is what lets the map that advances the store keep that to
    ///     itself.
    /// </summary>
    internal StateMap()
    {
    }

    /// <summary>The number of items in this version of the map.</summary>
    public abstract int Count { get; }

    /// <summary>The keys in this version of the map, in no particular order.</summary>
    public abstract IEnumerable<TKey> Keys { get; }

    /// <summary>
    ///     Every key with its state, in no particular order - one walk of the map rather than a
    ///     lookup per key.
    /// </summary>
    /// <remarks>
    ///     For anything that reads the whole collection: a total, an average, a count of items
    ///     matching something. Iterating <see cref="Keys" /> and calling
    ///     <see cref="TryGetState" /> for each answers the same question and costs a lookup per
    ///     item, which on the default trie is O(log32 n) of pointer chasing apiece and touches the
    ///     whole structure in key order rather than in storage order. This was measured: on a
    ///     hundred thousand items, summing one field by lookup-per-key cost more than sorting the
    ///     entire collection.
    /// </remarks>
    public abstract IEnumerable<KeyValuePair<TKey, TState>> Pairs { get; }

    /// <summary>Returns the state stored under a key, if there is one.</summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="state">The state stored under it, when this returns true.</param>
    /// <returns><see langword="true" /> if the key is present.</returns>
    public abstract bool TryGetState(TKey key, out TState state);

    /// <summary>Whether a key is present in this version of the map.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns><see langword="true" /> if the key is present.</returns>
    public abstract bool ContainsKey(TKey key);
}

/// <summary>
///     The default strategy: a hash array mapped trie. Updates cost roughly O(log32 n) and
///     allocate only the path from the root, so a 100k-item collection rewrites about four nodes
///     per edit. Start here; only reach for an in-place strategy once measurement says this is the
///     bottleneck.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class ImmutableStateMap<TKey, TState> : StateMap<TKey, TState>
    where TKey : notnull
{
    private readonly ImmutableDictionary<TKey, TState> states;

    private ImmutableStateMap(ImmutableDictionary<TKey, TState> states) => this.states = states;

    /// <summary>The empty map, which every collection starts from unless told otherwise.</summary>
    public static ImmutableStateMap<TKey, TState> Empty { get; } =
        new(ImmutableDictionary<TKey, TState>.Empty);

    /// <inheritdoc />
    public override int Count => this.states.Count;

    /// <inheritdoc />
    public override IEnumerable<TKey> Keys => this.states.Keys;

    /// <inheritdoc />
    /// <remarks>The trie walks itself, which is where its enumeration is cheapest.</remarks>
    public override IEnumerable<KeyValuePair<TKey, TState>> Pairs => this.states;

    /// <inheritdoc />
    public override bool TryGetState(TKey key, out TState state) =>
        this.states.TryGet(key, out state);

    /// <inheritdoc />
    public override bool ContainsKey(TKey key) => this.states.ContainsKey(key);

    /// <summary>
    ///     The map resulting from applying <paramref name="updated" /> and dropping
    ///     <paramref name="removed" />.
    /// </summary>
    /// <remarks>
    ///     Not on <see cref="StateMap{TKey,TState}" />, and internal, because advancing the store is
    ///     the collection's business. A view's slice of it has no next version to produce, and a
    ///     caller who advanced a map themselves would hold a version the collection had never heard
    ///     of.
    /// </remarks>
    /// <param name="updated">The states to add or replace.</param>
    /// <param name="removed">The keys to drop.</param>
    /// <returns>The new logical version of the map.</returns>
    internal ImmutableStateMap<TKey, TState> With(
        IReadOnlyDictionary<TKey, TState> updated,
        IReadOnlyCollection<TKey> removed)
    {
        if (updated.Count == 0 && removed.Count == 0)
        {
            return this;
        }

        ImmutableDictionary<TKey, TState>.Builder builder = this.states.ToBuilder();

        foreach (TKey key in removed)
        {
            builder.Remove(key);
        }

        foreach (KeyValuePair<TKey, TState> pair in updated)
        {
            builder[pair.Key] = pair.Value;
        }

        return new ImmutableStateMap<TKey, TState>(builder.ToImmutable());
    }
}

