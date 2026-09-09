using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Collections;

/// <summary>
///     An edit after resolution: the new snapshot, plus the resolved new state of every key that
///     moved. Per-key observers read <see cref="ChangeFor" /> off the event itself rather than
///     snapshotting a cell, because a cell sampled mid-transaction still holds its pre-transaction
///     value.
/// </summary>
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
    ///     What this change did to one key. The nesting is deliberate and the two levels mean
    ///     different things: the outer <see cref="Maybe{T}" /> is whether the key moved at all —
    ///     no value means no event for this observer — and the inner one is whether the key is
    ///     present afterwards, so a removal arrives as a value containing no value.
    /// </summary>
    /// <param name="key">The key to ask about.</param>
    /// <returns>What happened to that key, if anything.</returns>
    public Maybe<Maybe<TState>> ChangeFor(TKey key) =>
        this.NewStates.TryGetValue(key).Match(
            static state => Maybe.Some(Maybe.Some(state)),
            () => this.removed.Contains(key)
                ? Maybe.Some(Maybe<TState>.None)
                : Maybe<Maybe<TState>>.None);

    internal bool WasAdded(TKey key) => this.added.Contains(key);
}
