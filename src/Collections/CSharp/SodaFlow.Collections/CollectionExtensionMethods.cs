using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Collections;

/// <summary>
///     The optional-valued half of the C# surface: the per-item cells, and the lookups which
///     answer with a <see cref="Maybe{T}" /> rather than a <c>TryGet</c>.
/// </summary>
/// <remarks>
///     These are here rather than in SodaFlow.Collections.Core because <see cref="Maybe{T}" />
///     lives in SodaFlow.Functional, which F# has no use for — it has <c>option</c>. The core
///     therefore answers in <c>TryGet</c>s and integers, and each language surface puts its own
///     optional type back on top. See <see cref="ReactiveCollection{TKey,TIdentity,TState}" />.
/// </remarks>
[PublicAPI]
public static class CollectionExtensionMethods
{
    /// <summary>
    ///     A cell tracking one item's mutable portion, with no value while the key is absent from
    ///     the store.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="collection">The collection or view to observe through.</param>
    /// <param name="key">The key to observe.</param>
    /// <returns>A cell tracking that key's state.</returns>
    /// <remarks>
    ///     <para>
    ///         Cheap enough to create per bound view: it filters on a single hash lookup and never
    ///         touches the rest of the collection. Cached weakly per key, so N observers of one key
    ///         share a node, and asking two views of the same root gives the same cell.
    ///     </para>
    ///     <para>
    ///         The key need not exist yet. A removal fires no value and a later add under the same
    ///         key fires one again, so a view bound to a key can outlive the item.
    ///     </para>
    ///     <para>
    ///         This answers for the store rather than for membership: asking a filtered view about a
    ///         key it filtered out still gives that item's state. Membership questions belong to
    ///         <see cref="IReactiveCollection{TKey,TIdentity,TState}.KeysCell" />.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<Maybe<TState>> StateCell<TKey, TIdentity, TState>(
        this IReactiveCollection<TKey, TIdentity, TState> collection,
        TKey key)
        where TKey : notnull
        where TIdentity : notnull =>
        collection.RootOf().StateCellImpl(
            key,
            static state => Maybe.Some(state),
            static () => Maybe<TState>.None);

    /// <summary>
    ///     A cell tracking one item's immutable portion, with no value while the key is absent.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="collection">The collection or view to observe through.</param>
    /// <param name="key">The key to observe.</param>
    /// <returns>A cell tracking that key's identity.</returns>
    /// <remarks>Fires only on structural change, so it is near-free to hold.</remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<Maybe<TIdentity>> IdentityCell<TKey, TIdentity, TState>(
        this IReactiveCollection<TKey, TIdentity, TState> collection,
        TKey key)
        where TKey : notnull
        where TIdentity : notnull =>
        collection.RootOf().ShapeCell.Map(identities => identities.TryGetValue(key));

    /// <summary>Returns both halves of the item stored under a key, if there is one.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="snapshot">The snapshot to look in.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>The item, or no value if the key is absent.</returns>
    [Pure]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Maybe<Item<TIdentity, TState>> Lookup<TKey, TIdentity, TState>(
        this CollectionSnapshot<TKey, TIdentity, TState> snapshot,
        TKey key)
        where TKey : notnull
        where TIdentity : notnull =>
        snapshot.TryGetItem(key, out Item<TIdentity, TState>? item) && item is not null
            ? Maybe.Some(item)
            : Maybe<Item<TIdentity, TState>>.None;

    /// <summary>Returns the state stored under a key, if there is one.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="states">The state map to look in.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>The state, or no value if the key is absent.</returns>
    [Pure]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Maybe<TState> Lookup<TKey, TState>(
        this IStateMap<TKey, TState> states,
        TKey key)
        where TKey : notnull =>
        states.TryGetState(key, out TState state) ? Maybe.Some(state) : Maybe<TState>.None;

    /// <summary>
    ///     What a change did to one key. The nesting is deliberate and the two levels mean
    ///     different things: the outer <see cref="Maybe{T}" /> is whether the key moved at all — no
    ///     value meaning no event for this observer — and the inner one is whether the key is
    ///     present afterwards, so a removal arrives as a value containing no value.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="change">The change to ask about.</param>
    /// <param name="key">The key to ask about.</param>
    /// <returns>What happened to that key, if anything.</returns>
    [Pure]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Maybe<Maybe<TState>> ChangeFor<TKey, TIdentity, TState>(
        this ItemChange<TKey, TIdentity, TState> change,
        TKey key)
        where TKey : notnull
        where TIdentity : notnull
    {
        if (change.TryGetNewState(key, out TState state))
        {
            return Maybe.Some(Maybe.Some(state));
        }

        return change.WasChanged(key)
            ? Maybe.Some(Maybe<TState>.None)
            : Maybe<Maybe<TState>>.None;
    }

    /// <summary>The position of a key in an ordered set, if it is present.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="keys">The ordered set to look in.</param>
    /// <param name="key">The key to look for.</param>
    /// <returns>Its position, or no value if the key is absent.</returns>
    /// <remarks>
    ///     <see cref="IOrderedKeys{TKey,TIdentity,TState}.IndexOf" /> itself answers -1, following the
    ///     convention every other <c>IndexOf</c> in the framework does. This is the same question
    ///     asked the way the rest of the C# API answers.
    /// </remarks>
    [Pure]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Maybe<int> IndexOfMaybe<TKey, TIdentity, TState>(
        this IOrderedKeys<TKey, TIdentity, TState> keys,
        TKey key)
        where TKey : notnull
        where TIdentity : notnull
    {
        int index = keys.IndexOf(key);

        return index >= 0 ? Maybe.Some(index) : Maybe<int>.None;
    }
}
