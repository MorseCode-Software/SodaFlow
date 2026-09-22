using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     A view of the mutable part of each item that other code cannot change. The key comes from
///     the immutable part.
/// </summary>
/// <remarks>
///     <para>
///         This type selects the storage strategy. Each implementation must obey this
///         contract:
///     </para>
///     <para>
///         <b>
///             A value returned by an instance never changes for the lifetime of that
///             instance.
///         </b>
///     </para>
///     <para>
///         In a transaction, SodaFlow reads the value of a cell from <i>before</i> that
///         transaction. When a snapshot from before points to storage that other code changed,
///         those reads give a future value and no message. Thus an implementation that changes its
///         storage in position must give a version to that storage and must answer an older
///         instance from a record. It must not answer with the live map.
///     </para>
///     <para>
///         A lookup is a <c>TryGet</c> and not an optional value, because this assembly does not
///         reference SodaFlow.Functional. Each language wrapper adds a <c>Lookup</c> above this
///         type, and that <c>Lookup</c> answers with the optional type of its language.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
[PublicAPI]
public abstract class StateMap<TKey, TState>
    where TKey : notnull
{
    /// <summary>
    ///     This is internal, thus only this assembly can make one. There is one storage strategy
    ///     and one filtered face of it, and no code replaces them. A closed hierarchy lets the map
    ///     that moves the store forward keep that operation private.
    /// </summary>
    internal StateMap()
    {
    }

    /// <summary>The number of items in this version of the map.</summary>
    public abstract int Count { get; }

    /// <summary>The keys in this version of the map, in no particular order.</summary>
    public abstract IEnumerable<TKey> Keys { get; }

    /// <summary>
    ///     Each key with its state, in no sequence. This is one read of the map and not one lookup
    ///     for each key.
    /// </summary>
    /// <remarks>
    ///     This is for code that reads the full collection, such as a total, an average, or a
    ///     count of the items that agree with a predicate. An enumeration of <see cref="Keys" />
    ///     with a call of <see cref="TryGetState" /> for each key gives the same answer and costs
    ///     one lookup for each item. On the default trie each lookup is <c>O(log32 n)</c> of
    ///     pointer reads, and it reads the full structure in the sequence of the keys and not in
    ///     the sequence of the storage. A measurement shows this: on one hundred thousand items, a
    ///     total of one field with one lookup for each key cost more than a sort of the full
    ///     collection.
    /// </remarks>
    public abstract IEnumerable<KeyValuePair<TKey, TState>> Pairs { get; }

    /// <summary>Returns the state for a key, when there is one.</summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="state">The state for that key, when this method returns true.</param>
    /// <returns><see langword="true" /> when the key is available.</returns>
    public abstract bool TryGetState(TKey key, [NotNullWhen(true)] out TState? state);

    /// <summary>True when this version of the map has a key.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns><see langword="true" /> when the key is available.</returns>
    public abstract bool ContainsKey(TKey key);
}

internal static class ImmutableStateMap<TState>
{
    internal static ImmutableStateMap<TKey, TState> Create<TKey>(IEqualityComparer<TKey> keyEqualityComparer)
        where TKey : notnull =>
        new(keyEqualityComparer);
}

/// <summary>
///     The default strategy, which is a hash array mapped trie. An update costs approximately
///     <c>O(log32 n)</c> and allocates only the path from the root. Thus a collection with 100k
///     items writes approximately four nodes again for each edit. Start with this strategy. Use a
///     strategy that changes its storage in position only after a measurement shows that this
///     strategy is the limit.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class ImmutableStateMap<TKey, TState> : StateMap<TKey, TState>
    where TKey : notnull
{
    private readonly ImmutableDictionary<TKey, TState> states;

    internal ImmutableStateMap(IEqualityComparer<TKey> keyEqualityComparer)
        : this(ImmutableDictionary<TKey, TState>.Empty.WithComparers(keyEqualityComparer))
    {
    }

    private ImmutableStateMap(ImmutableDictionary<TKey, TState> states) => this.states = states;

    /// <inheritdoc />
    public override int Count => this.states.Count;

    /// <inheritdoc />
    public override IEnumerable<TKey> Keys => this.states.Keys;

    /// <inheritdoc />
    /// <remarks>The trie walks itself, which is where its enumeration is cheapest.</remarks>
    public override IEnumerable<KeyValuePair<TKey, TState>> Pairs => this.states;

    /// <inheritdoc />
    public override bool TryGetState(TKey key, [NotNullWhen(true)] out TState? state) =>
        this.states.TryGet(key: key, value: out state);

    /// <inheritdoc />
    public override bool ContainsKey(TKey key) => this.states.ContainsKey(key);

    /// <summary>
    ///     The map after this code applies <paramref name="updated" /> and removes
    ///     <paramref name="removed" />.
    /// </summary>
    /// <remarks>
    ///     This method is not on <see cref="StateMap{TKey,TState}" />, and it is internal, because
    ///     only the collection moves the store forward. A part of the store in a view has no next
    ///     version. A caller that moves a map forward holds a version that the collection does not
    ///     know.
    /// </remarks>
    /// <param name="updated">The states to add or to replace.</param>
    /// <param name="removed">The keys to remove.</param>
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
