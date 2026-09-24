using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     An edit after its resolution. It holds the new snapshot and the new state of each key that
///     changed. An observer of one key reads this event and does not sample a cell, because a
///     sample of a cell in a transaction gives the value from before the transaction.
/// </summary>
/// <remarks>
///     The result of this change on one key is two questions and not one. They are two members
///     here, for the cause that makes the C# wrapper put them in one nested optional value.
///     <see cref="WasChanged" /> gives if the change named the key, and
///     <see cref="TryGetNewState" /> gives if the collection has the key after the change. A
///     removal is the pair (<see langword="true" />, <see langword="false" />).
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
[PublicAPI]
public sealed class ItemChange<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>
    ///     This code holds these as sets and does not read them through <see cref="Added" />, thus
    ///     the membership tests below stay <c>O(1)</c>. <see cref="Added" /> and
    ///     <see cref="Removed" /> have the type <see cref="IReadOnlyCollection{T}" /> and not
    ///     <c>IReadOnlySet</c>, because that interface came with .NET 5 and this package also
    ///     targets net472 and netstandard2.0.
    /// </summary>
    private readonly HashSet<TKey> added;

    private readonly HashSet<TKey> removed;

    internal ItemChange(
        CollectionSnapshot<TKey, TIdentity, TState> before,
        CollectionSnapshot<TKey, TIdentity, TState> after,
        IReadOnlyDictionary<TKey, TState> newStates,
        HashSet<TKey> added,
        HashSet<TKey> removed)
    {
        this.Before = before;
        this.After = after;
        this.NewStates = newStates;
        this.added = added;
        this.removed = removed;
    }

    /// <summary>The store as this transaction left it.</summary>
    /// <remarks>
    ///     This comes with <see cref="Before" />, thus a delta across a value of an item needs no
    ///     copy of the previous values. Examples of such a delta are a total, an average, and a
    ///     count. <see cref="NewStates" /> gives the keys to read, and these two give their
    ///     values.
    /// </remarks>
    public CollectionSnapshot<TKey, TIdentity, TState> After { get; }

    /// <summary>The store as this transaction found it.</summary>
    /// <remarks>
    ///     This is the same instance as the <see cref="After" /> of the previous change. Thus, a
    ///     listener on a sequence of these changes keeps no more memory than a listener on their
    ///     <see cref="After" /> alone.
    /// </remarks>
    public CollectionSnapshot<TKey, TIdentity, TState> Before { get; }

    /// <summary>The resolved state of each key that an edit added or updated.</summary>
    public IReadOnlyDictionary<TKey, TState> NewStates { get; }

    /// <summary>The keys this change added.</summary>
    public IReadOnlyCollection<TKey> Added => this.added;

    /// <summary>The keys this change removed.</summary>
    public IReadOnlyCollection<TKey> Removed => this.removed;

    /// <summary>Whether the item count changed or a key changed.</summary>
    public bool IsStructural => this.added.Count > 0 || this.removed.Count > 0;

    /// <summary>Each key that this change added, updated, or removed.</summary>
    public IEnumerable<TKey> ChangedKeys => this.NewStates.Keys.Concat(this.removed);

    /// <summary>
    ///     True when this change names the key. An observer of a key with a
    ///     <see langword="false" /> answer has no event to react to.
    /// </summary>
    /// <param name="key">The key to read.</param>
    /// <returns><see langword="true" /> when the change added, updated, or removed the key.</returns>
    public bool WasChanged(TKey key) => this.NewStates.ContainsKey(key) || this.removed.Contains(key);

    /// <summary>The state of the key after this change, when the collection has the key.</summary>
    /// <param name="key">The key to read.</param>
    /// <param name="state">Its new state, when this method returns true.</param>
    /// <returns>
    ///     <see langword="true" /> when this change gave the key a new state. For a key with a
    ///     <see langword="false" /> answer, the change removed the key or did not name it.
    ///     <see cref="WasChanged" /> gives which one of the two occurred.
    /// </returns>
    public bool TryGetNewState(TKey key, [NotNullWhen(true)] out TState? state) =>
        this.NewStates.TryGet(key: key, value: out state);

    internal bool WasAdded(TKey key) => this.added.Contains(key);

    /// <summary>
    ///     The result of this change for the identity of one key, or nothing when the change has
    ///     no result for it.
    /// </summary>
    /// <remarks>
    ///     An identity is constant while the collection has its key, thus only an add or a removal
    ///     can change one. A state edit has no result for an observer of the identity and gives no
    ///     value here. Thus, code can hold such an observer for the life of a row at no cost.
    /// </remarks>
    internal MaybeInternal<TProjected> ProjectIdentityChangeFor<TProjected>(
        TKey key,
        Func<TIdentity, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        if (this.WasAdded(key))
        {
            return MaybeInternal.Some(
                this.After.TryGetIdentity(key: key, identity: out TIdentity? identity)
                    ? onPresent(identity)
                    : onAbsent());
        }

        return this.removed.Contains(key)
            ? MaybeInternal.Some(onAbsent())
            : MaybeInternal<TProjected>.None;
    }

    internal MaybeInternal<TProjected> ProjectChangeFor<TProjected>(
        TKey key,
        Func<TState, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        if (this.TryGetNewState(key: key, state: out TState? state))
        {
            return MaybeInternal.Some(onPresent(state));
        }

        return this.removed.Contains(key)
            ? MaybeInternal.Some(onAbsent())
            : MaybeInternal<TProjected>.None;
    }

    /// <summary>
    ///     The result of this change for the two parts of one key together, or nothing when the
    ///     change has no result for it.
    /// </summary>
    /// <remarks>
    ///     This sends a value for each change that the state projection sends one for, because an
    ///     item holds the state. Thus, an observer of this reads an identity again at each edit to
    ///     the state of its key, and an observer of the identity alone does not.
    /// </remarks>
    internal MaybeInternal<TProjected> ProjectItemChangeFor<TProjected>(
        TKey key,
        Func<Item<TIdentity, TState>, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        if (this.removed.Contains(key))
        {
            return MaybeInternal.Some(onAbsent());
        }

        // NewStates holds each key that an edit added or updated, thus one test covers the entry
        // of a key and an edit to the state of a key that is here.
        if (!this.NewStates.ContainsKey(key) && !this.WasAdded(key))
        {
            return MaybeInternal<TProjected>.None;
        }

        return MaybeInternal.Some(
            this.After.TryGetItem(key: key, item: out Item<TIdentity, TState>? item)
                ? onPresent(item)
                : onAbsent());
    }
}
