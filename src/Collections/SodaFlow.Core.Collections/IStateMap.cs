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
public interface IStateMap<TKey, TState>
    where TKey : notnull
{
    /// <summary>The number of items in this version of the map.</summary>
    int Count { get; }

    /// <summary>The keys in this version of the map, in no particular order.</summary>
    IEnumerable<TKey> Keys { get; }

    /// <summary>Returns the state stored under a key, if there is one.</summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="state">The state stored under it, when this returns true.</param>
    /// <returns><see langword="true" /> if the key is present.</returns>
    bool TryGetState(TKey key, out TState state);

    /// <summary>Whether a key is present in this version of the map.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns><see langword="true" /> if the key is present.</returns>
    bool ContainsKey(TKey key);

    /// <summary>
    ///     Produces the map resulting from applying <paramref name="updated" /> (added or replaced
    ///     states) and <paramref name="removed" /> (dropped keys).
    /// </summary>
    /// <param name="updated">The states to add or replace.</param>
    /// <param name="removed">The keys to drop.</param>
    /// <returns>The new logical version of the map.</returns>
    /// <remarks>
    ///     The returned instance is the new logical version. <c>this</c> must continue to serve its
    ///     own version unchanged.
    /// </remarks>
    IStateMap<TKey, TState> With(
        IReadOnlyDictionary<TKey, TState> updated,
        IReadOnlyCollection<TKey> removed);
}

/// <summary>
///     The default strategy: a hash array mapped trie. Updates cost roughly O(log32 n) and
///     allocate only the path from the root, so a 100k-item collection rewrites about four nodes
///     per edit. Start here; only reach for an in-place strategy once measurement says this is the
///     bottleneck.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class ImmutableStateMap<TKey, TState> : IStateMap<TKey, TState>
    where TKey : notnull
{
    private readonly ImmutableDictionary<TKey, TState> states;

    private ImmutableStateMap(ImmutableDictionary<TKey, TState> states) => this.states = states;

    /// <summary>The empty map, which every collection starts from unless told otherwise.</summary>
    public static ImmutableStateMap<TKey, TState> Empty { get; } =
        new(ImmutableDictionary<TKey, TState>.Empty);

    /// <inheritdoc />
    public int Count => this.states.Count;

    /// <inheritdoc />
    public IEnumerable<TKey> Keys => this.states.Keys;

    /// <summary>Creates a map holding the given states.</summary>
    /// <param name="states">The states to store.</param>
    /// <returns>A map holding them.</returns>
    public static ImmutableStateMap<TKey, TState> Create(
        IEnumerable<KeyValuePair<TKey, TState>> states) =>
        new(ImmutableDictionary.CreateRange(states));

    /// <inheritdoc />
    public bool TryGetState(TKey key, out TState state) => this.states.TryGet(key, out state);

    /// <inheritdoc />
    public bool ContainsKey(TKey key) => this.states.ContainsKey(key);

    /// <inheritdoc />
    public IStateMap<TKey, TState> With(
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

