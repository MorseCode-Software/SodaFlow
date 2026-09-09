using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     An edit after resolution: the new snapshot, plus the resolved new state of every key that
///     moved. Per-key observers read this off the event itself rather than snapshotting a cell,
///     because a cell sampled mid-transaction still holds its pre-transaction value.
/// </summary>
/// <remarks>
///     What this change did to one key is two questions, not one, and they are separate members
///     here for the same reason the C# wrapper folds them back into a nested optional:
///     <see cref="WasChanged" /> is whether the key moved at all, and
///     <see cref="TryGetNewState" /> is whether it is present afterwards. A removal is the pair
///     (<see langword="true" />, <see langword="false" />).
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
public sealed class CollectionChange<TKey, TId, TState>
    where TKey : notnull
    where TId : notnull
{
    /// <summary>
    ///     Held as sets rather than reached for through <see cref="Added" />, so that the membership
    ///     tests below stay O(1). <see cref="Added" /> and <see cref="Removed" /> are declared as
    ///     <see cref="IReadOnlyCollection{T}" /> rather than <c>IReadOnlySet</c> because that
    ///     interface arrived in .NET 5 and this package also targets net472 and netstandard2.0.
    /// </summary>
    private readonly HashSet<TKey> added;

    private readonly HashSet<TKey> removed;

    internal CollectionChange(
        CollectionSnapshot<TKey, TId, TState> after,
        IReadOnlyDictionary<TKey, TState> newStates,
        HashSet<TKey> added,
        HashSet<TKey> removed)
    {
        this.After = after;
        this.NewStates = newStates;
        this.added = added;
        this.removed = removed;
    }

    /// <summary>The collection as of this change.</summary>
    public CollectionSnapshot<TKey, TId, TState> After { get; }

    /// <summary>The resolved state of every added or updated key.</summary>
    public IReadOnlyDictionary<TKey, TState> NewStates { get; }

    /// <summary>The keys this change added.</summary>
    public IReadOnlyCollection<TKey> Added => this.added;

    /// <summary>The keys this change removed.</summary>
    public IReadOnlyCollection<TKey> Removed => this.removed;

    /// <summary>Whether the item count changed or a key changed.</summary>
    public bool IsStructural => this.added.Count > 0 || this.removed.Count > 0;

    /// <summary>Every key this change touched, added, updated or removed.</summary>
    public IEnumerable<TKey> ChangedKeys => this.NewStates.Keys.Concat(this.removed);

    /// <summary>
    ///     Whether this change touched the key at all. An observer of a key this returns
    ///     <see langword="false" /> for has no event to react to.
    /// </summary>
    /// <param name="key">The key to ask about.</param>
    /// <returns><see langword="true" /> if the key was added, updated or removed.</returns>
    public bool WasChanged(TKey key) =>
        this.NewStates.ContainsKey(key) || this.removed.Contains(key);

    /// <summary>The key's state after this change, if it is still present.</summary>
    /// <param name="key">The key to ask about.</param>
    /// <param name="state">Its new state, when this returns true.</param>
    /// <returns>
    ///     <see langword="true" /> if this change gave the key a new state. A key this returns
    ///     <see langword="false" /> for was either removed or left alone;
    ///     <see cref="WasChanged" /> is what separates those two.
    /// </returns>
    public bool TryGetNewState(TKey key, out TState state) =>
        this.NewStates.TryGet(key, out state);

    internal bool WasAdded(TKey key) => this.added.Contains(key);

    /// <summary>
    ///     What this change did to one key, in whatever shape the caller wants it. This is what a
    ///     per-item cell filters on: no value means no event for that observer, and the projection
    ///     is applied inside the same map rather than needing a second one.
    /// </summary>
    internal MaybeInternal<TProjected> ProjectChangeFor<TProjected>(
        TKey key,
        Func<TState, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        if (this.TryGetNewState(key, out TState state))
        {
            return MaybeInternal.Some(onPresent(state));
        }

        return this.removed.Contains(key)
            ? MaybeInternal.Some(onAbsent())
            : MaybeInternal<TProjected>.None;
    }
}
