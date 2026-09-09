using System.Collections.Generic;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Collections;

/// <summary>
///     The whole collection at one logical version: the identity map (which only changes on a
///     structural edit) and the state map.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
public sealed class CollectionSnapshot<TKey, TId, TState>
    where TKey : notnull
    where TId : notnull
{
    internal CollectionSnapshot(
        IReadOnlyDictionary<TKey, TId> identities,
        IStateMap<TKey, TState> states)
    {
        this.Identities = identities;
        this.States = states;
    }

    /// <summary>
    ///     The immutable portion of every item. This object is replaced only on a structural edit,
    ///     which is what makes reference equality a sound test for "did the shape change".
    /// </summary>
    public IReadOnlyDictionary<TKey, TId> Identities { get; }

    /// <summary>The mutable portion of every item.</summary>
    public IStateMap<TKey, TState> States { get; }

    /// <summary>The number of items.</summary>
    public int Count => this.Identities.Count;

    /// <summary>Whether a key is present.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns><see langword="true" /> if the key is present.</returns>
    public bool ContainsKey(TKey key) => this.Identities.ContainsKey(key);

    /// <summary>Returns both halves of the item stored under a key, if there is one.</summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The entry, or no value if the key is absent.</returns>
    public Maybe<Entry<TId, TState>> Lookup(TKey key) =>
        this.Identities.TryGetValue(key).Match(
            identity => this.States.Lookup(key).Match(
                state => Maybe.Some(new Entry<TId, TState>(identity, state)),
                static () => Maybe<Entry<TId, TState>>.None),
            static () => Maybe<Entry<TId, TState>>.None);
}
