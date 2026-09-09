using System.Collections.Generic;
using System.Collections.Immutable;
using JetBrains.Annotations;

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
        ImmutableDictionary<TKey, TId> identities,
        IStateMap<TKey, TState> states)
    {
        this.IdentitiesImpl = identities;
        this.States = states;
    }

    /// <summary>
    ///     The immutable portion of every item. This object is replaced only on a structural edit,
    ///     which is what makes reference equality a sound test for "did the shape change".
    /// </summary>
    public IReadOnlyDictionary<TKey, TId> Identities => this.IdentitiesImpl;

    /// <summary>The mutable portion of every item.</summary>
    public IStateMap<TKey, TState> States { get; }

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
    internal ImmutableDictionary<TKey, TId> IdentitiesImpl { get; }

    /// <summary>The number of items.</summary>
    public int Count => this.IdentitiesImpl.Count;

    /// <summary>Whether a key is present.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns><see langword="true" /> if the key is present.</returns>
    public bool ContainsKey(TKey key) => this.IdentitiesImpl.ContainsKey(key);

    /// <summary>Returns both halves of the item stored under a key, if there is one.</summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="entry">The item stored under it, when this returns true.</param>
    /// <returns><see langword="true" /> if the key is present.</returns>
    /// <remarks>
    ///     A <c>TryGet</c> rather than an optional value because this assembly does not reference
    ///     SodaFlow.Functional; the language wrappers add <c>Lookup</c> over this, answering with
    ///     each language's own optional type.
    /// </remarks>
    public bool TryGetEntry(TKey key, out Entry<TId, TState>? entry)
    {
        // Through the assembly's own helper rather than the concrete TryGetValue, which is
        // annotated to leave its output null on false and so warns against a notnull TId.
        if (this.IdentitiesImpl.TryGet(key, out TId identity) &&
            this.States.TryGetState(key, out TState state))
        {
            entry = new Entry<TId, TState>(identity, state);

            return true;
        }

        entry = null;

        return false;
    }

    /// <summary>
    ///     The next version of the identity map, with <paramref name="removed" /> dropped and
    ///     <paramref name="added" /> put in. Built from this one rather than copied out of it.
    /// </summary>
    internal ImmutableDictionary<TKey, TId> WithIdentities(
        IEnumerable<KeyValuePair<TKey, TId>> added,
        IEnumerable<TKey> removed)
    {
        // ToBuilder and ToImmutable are both O(1) - the builder wraps this map's root rather than
        // copying it - so what this costs is one O(log32 n) write per key touched.
        ImmutableDictionary<TKey, TId>.Builder builder = this.IdentitiesImpl.ToBuilder();

        foreach (TKey key in removed)
        {
            builder.Remove(key);
        }

        foreach (KeyValuePair<TKey, TId> pair in added)
        {
            builder[pair.Key] = pair.Value;
        }

        return builder.ToImmutable();
    }

    internal MaybeInternal<Entry<TId, TState>> LookupInternal(TKey key) =>
        this.TryGetEntry(key, out Entry<TId, TState>? entry) && entry is not null
            ? MaybeInternal.Some(entry)
            : MaybeInternal<Entry<TId, TState>>.None;
}
