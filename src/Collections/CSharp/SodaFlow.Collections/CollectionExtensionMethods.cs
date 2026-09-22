using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Collections;

/// <summary>
///     The part of the C# surface with optional values: the cells for one item, and the lookups
///     that answer with a <see cref="Maybe{T}" /> and not with a <c>TryGet</c>.
/// </summary>
/// <remarks>
///     These are here and not in SodaFlow.Collections.Core, because <see cref="Maybe{T}" /> is in
///     SodaFlow.Functional and F# does not use that assembly. F# has <c>option</c>. Thus, the core
///     answers with a <c>TryGet</c> and with an integer, and each language surface adds its own
///     optional type above the core. See
///     <see cref="ReactiveCollection{TKey,TIdentity,TState}" />.
/// </remarks>
[PublicAPI]
public static class CollectionExtensionMethods
{
    /// <summary>
    ///     A cell that follows the mutable part of one item. It has no value while the store does
    ///     not have the key.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="collection">The collection or view to monitor.</param>
    /// <param name="key">The key to monitor.</param>
    /// <returns>A cell tracking that key's state.</returns>
    /// <remarks>
    ///     <para>
    ///         The cost is low, thus code can make one for each bound view. It filters on one hash
    ///         lookup and does not read the other items. A weak cache holds one for each key, thus
    ///         N observers of one key share a node, and two views of the same root give the same
    ///         cell.
    ///     </para>
    ///     <para>
    ///         The key can be missing now. A removal sends no value, and a subsequent add with the
    ///         same key sends a value again. Thus, a view that binds to a key can continue after the
    ///         item.
    ///     </para>
    ///     <para>
    ///         This answers for the store and not for the members of a view. A read of a filtered
    ///         view for a key that its filter removed gives the state of that item.
    ///         <see cref="ReactiveCollection{TKey,TIdentity,TState}.KeysCell" /> answers for the
    ///         members.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<Maybe<TState>> StateCell<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> collection,
        TKey key)
        where TKey : notnull
        where TIdentity : notnull =>
        collection.StateCellImpl(
            key: key,
            onPresent: static state => Maybe.Some(state),
            onAbsent: static () => Maybe<TState>.None);

    /// <summary>
    ///     A cell that follows the immutable part of one item. It has no value while the key is
    ///     missing.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="collection">The collection or view to monitor.</param>
    /// <param name="key">The key to monitor.</param>
    /// <returns>A cell tracking that key's identity.</returns>
    /// <remarks>Fires only on structural change, so it is near-free to hold.</remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<Maybe<TIdentity>> IdentityCell<TKey, TIdentity, TState>(
        this ReactiveCollection<TKey, TIdentity, TState> collection,
        TKey key)
        where TKey : notnull
        where TIdentity : notnull =>
        collection.IdentityCellImpl(
            key: key,
            onPresent: static identity => Maybe.Some(identity),
            onAbsent: static () => Maybe<TIdentity>.None);

    /// <summary>Returns the two parts of the item for a key, when there is one.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="snapshot">The snapshot to look in.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>The item, or no value when the key is missing.</returns>
    [Pure]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Maybe<Item<TIdentity, TState>> Lookup<TKey, TIdentity, TState>(
        this CollectionSnapshot<TKey, TIdentity, TState> snapshot,
        TKey key)
        where TKey : notnull
        where TIdentity : notnull =>
        snapshot.TryGetItem(key: key, item: out Item<TIdentity, TState>? item)
            ? Maybe.Some(item)
            : Maybe<Item<TIdentity, TState>>.None;

    /// <summary>Returns the state for a key, when there is one.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="states">The state map to look in.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>The state, or no value when the key is missing.</returns>
    [Pure]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Maybe<TState> Lookup<TKey, TState>(
        this StateMap<TKey, TState> states,
        TKey key)
        where TKey : notnull =>
        states.TryGetState(key: key, state: out TState? state) ? Maybe.Some(state) : Maybe<TState>.None;

    /// <summary>
    ///     The result of a change on one key. The two levels are deliberate and they give
    ///     different data. The outer <see cref="Maybe{T}" /> gives if the change named the key, and
    ///     no value means no event for this observer. The inner <see cref="Maybe{T}" /> gives if
    ///     the collection has the key after the change, thus a removal comes as a value that holds
    ///     no value.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="change">The change to read.</param>
    /// <param name="key">The key to read.</param>
    /// <returns>The change to that key, when there is one.</returns>
    [Pure]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Maybe<Maybe<TState>> ChangeFor<TKey, TIdentity, TState>(
        this ItemChange<TKey, TIdentity, TState> change,
        TKey key)
        where TKey : notnull
        where TIdentity : notnull
    {
        if (change.TryGetNewState(key: key, state: out TState? state))
        {
            return Maybe.Some(Maybe.Some(state));
        }

        return change.WasChanged(key)
            ? Maybe.Some(Maybe<TState>.None)
            : Maybe<Maybe<TState>>.None;
    }

    /// <summary>The position of a key in an ordered set, when the set has that key.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="keys">The ordered set to look in.</param>
    /// <param name="key">The key to look for.</param>
    /// <returns>Its position, or no value when the key is missing.</returns>
    /// <remarks>
    ///     The core answers -1 for a missing key, which is the convention of each other
    ///     <c>IndexOf</c> in the framework, and it keeps that answer private. This is the only
    ///     <c>IndexOf</c> in the public C# API, and it answers as the other parts of the C# API
    ///     do.
    /// </remarks>
    [Pure]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Maybe<int> IndexOf<TKey, TIdentity, TState>(
        this OrderedKeys<TKey, TIdentity, TState> keys,
        TKey key)
        where TKey : notnull
        where TIdentity : notnull
    {
        int index = keys.IndexOfInternal(key);

        return index >= 0 ? Maybe.Some(index) : Maybe<int>.None;
    }
}
