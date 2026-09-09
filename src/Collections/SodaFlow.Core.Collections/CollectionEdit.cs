using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     A requested edit. Updates carry transforms rather than values so they compose against
///     whatever the state is when the transaction runs.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
public sealed class CollectionEdit<TKey, TId, TState>
    where TKey : notnull
    where TId : notnull
{
    /// <summary>Creates an edit from its three parts.</summary>
    /// <param name="updates">Transforms to apply, by key.</param>
    /// <param name="adds">Items to add.</param>
    /// <param name="removes">Keys to remove.</param>
    public CollectionEdit(
        IReadOnlyDictionary<TKey, Func<TState, TState>> updates,
        IReadOnlyCollection<Entry<TId, TState>> adds,
        IReadOnlyCollection<TKey> removes)
    {
        this.Updates = updates;
        this.Adds = adds;
        this.Removes = removes;
    }

    /// <summary>The edit that changes nothing.</summary>
    public static CollectionEdit<TKey, TId, TState> Empty { get; } = new(
        new Dictionary<TKey, Func<TState, TState>>(),
        Array.Empty<Entry<TId, TState>>(),
        Array.Empty<TKey>());

    /// <summary>The transforms to apply, by key.</summary>
    public IReadOnlyDictionary<TKey, Func<TState, TState>> Updates { get; }

    /// <summary>The items to add.</summary>
    public IReadOnlyCollection<Entry<TId, TState>> Adds { get; }

    /// <summary>The keys to remove.</summary>
    public IReadOnlyCollection<TKey> Removes { get; }

    /// <summary>An edit applying one transform to one key.</summary>
    /// <param name="key">The key to transform.</param>
    /// <param name="transform">The transform to apply to that key's state.</param>
    /// <returns>The edit.</returns>
    public static CollectionEdit<TKey, TId, TState> Update(TKey key, Func<TState, TState> transform) =>
        new(
            new Dictionary<TKey, Func<TState, TState>> { [key] = transform },
            Array.Empty<Entry<TId, TState>>(),
            Array.Empty<TKey>());

    /// <summary>An edit adding one or more items.</summary>
    /// <param name="entries">The items to add.</param>
    /// <returns>The edit.</returns>
    public static CollectionEdit<TKey, TId, TState> Add(params Entry<TId, TState>[] entries) =>
        new(new Dictionary<TKey, Func<TState, TState>>(), entries, Array.Empty<TKey>());

    /// <summary>An edit removing one or more keys.</summary>
    /// <param name="keys">The keys to remove.</param>
    /// <returns>The edit.</returns>
    public static CollectionEdit<TKey, TId, TState> Remove(params TKey[] keys) =>
        new(new Dictionary<TKey, Func<TState, TState>>(), Array.Empty<Entry<TId, TState>>(), keys);

    /// <summary>
    ///     Lifts a stream of keyed transforms into edits, for wiring at collection construction.
    /// </summary>
    /// <param name="updatesStream">The stream of keyed transforms.</param>
    /// <returns>The stream of edits.</returns>
    public static Stream<CollectionEdit<TKey, TId, TState>> FromUpdates(
        Stream<(TKey Key, Func<TState, TState> Transform)> updatesStream) =>
        updatesStream.MapImpl(static update => Update(update.Key, update.Transform));

    /// <summary>
    ///     Lifts a stream of transforms for one fixed key into edits — the usual shape when a view
    ///     drives a single item.
    /// </summary>
    /// <param name="key">The key the transforms apply to.</param>
    /// <param name="transformsStream">The stream of transforms.</param>
    /// <returns>The stream of edits.</returns>
    public static Stream<CollectionEdit<TKey, TId, TState>> FromUpdates(
        TKey key,
        Stream<Func<TState, TState>> transformsStream) =>
        transformsStream.MapImpl(transform => Update(key, transform));

    /// <summary>Lifts a stream of new states for one fixed key into edits.</summary>
    /// <param name="key">The key the states apply to.</param>
    /// <param name="statesStream">The stream of new states.</param>
    /// <returns>The stream of edits.</returns>
    public static Stream<CollectionEdit<TKey, TId, TState>> FromStates(
        TKey key,
        Stream<TState> statesStream) =>
        statesStream.MapImpl(state => Update(key, _ => state));

    /// <summary>Lifts a stream of items into edits which add them.</summary>
    /// <param name="addsStream">The stream of items to add.</param>
    /// <returns>The stream of edits.</returns>
    public static Stream<CollectionEdit<TKey, TId, TState>> FromAdds(
        Stream<Entry<TId, TState>> addsStream) =>
        addsStream.MapImpl(static entry => Add(entry));

    /// <summary>Lifts a stream of keys into edits which remove them.</summary>
    /// <param name="removesStream">The stream of keys to remove.</param>
    /// <returns>The stream of edits.</returns>
    public static Stream<CollectionEdit<TKey, TId, TState>> FromRemoves(
        Stream<TKey> removesStream) =>
        removesStream.MapImpl(static key => Remove(key));

    /// <summary>
    ///     Coalesces two edits firing in the same transaction. SodaFlow gives no ordering guarantee
    ///     between them, so anything order-dependent is rejected rather than silently resolved: two
    ///     transforms for one key in one transaction would compose in an undefined order.
    /// </summary>
    /// <param name="other">The edit to combine with this one.</param>
    /// <returns>The combined edit.</returns>
    public CollectionEdit<TKey, TId, TState> CombineWith(CollectionEdit<TKey, TId, TState> other)
    {
        Dictionary<TKey, Func<TState, TState>> updates = new(this.Updates.Count + other.Updates.Count);

        foreach (KeyValuePair<TKey, Func<TState, TState>> pair in this.Updates)
        {
            updates.Add(pair.Key, pair.Value);
        }

        foreach (KeyValuePair<TKey, Func<TState, TState>> pair in other.Updates)
        {
            // ContainsKey rather than Dictionary.TryAdd, which netstandard2.0 and net472 do not
            // have.
            if (updates.ContainsKey(pair.Key))
            {
                throw new InvalidOperationException(
                    $"Two updates for key '{pair.Key}' in one transaction. Merge order is " +
                    "arbitrary, so composing them has no defined result. Combine them into a " +
                    "single transform before firing.");
            }

            updates.Add(pair.Key, pair.Value);
        }

        List<Entry<TId, TState>> adds = new(this.Adds.Count + other.Adds.Count);
        adds.AddRange(this.Adds);
        adds.AddRange(other.Adds);

        List<TKey> removes = new(this.Removes.Count + other.Removes.Count);
        removes.AddRange(this.Removes);
        removes.AddRange(other.Removes);

        return new CollectionEdit<TKey, TId, TState>(updates, adds, removes);
    }
}
