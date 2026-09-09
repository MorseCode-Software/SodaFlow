using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Collections;

/// <summary>
///     A keyed collection of items, ordered. Both the root collection and every view derived from
///     it are one of these, so <c>Filter</c> and <c>SortBy</c> take one and return one, the way
///     <c>Where</c> takes and returns an <c>IEnumerable</c>.
/// </summary>
/// <remarks>
///     <para>
///         A collection is two separable things: an item store and a sequence of keys into it. The
///         store lives once, in the root; every view down the chain shares it and differs only in
///         which keys it holds and in what order. That is why <see cref="StateCell" /> gives the
///         same cell whichever view you ask — sharing is not arranged, it is what falls out of a
///         view never copying anything.
///     </para>
///     <para>
///         Nothing here mutates. What can change a collection is fixed when it is created, from the
///         edit streams it is given; what can change a view is fixed by the stage that derives it.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TId">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
public interface IFrpCollection<TKey, TId, TState>
    where TKey : notnull
    where TId : notnull
{
    /// <summary>This collection's keys, in order.</summary>
    Cell<IOrderedKeys<TKey, TId, TState>> KeysCell { get; }

    /// <summary>Membership and ordering changes, as operations to apply in sequence.</summary>
    Stream<CollectionViewChange<TKey, TId, TState>> ChangesStream { get; }

    /// <summary>
    ///     The shared item store, spanning every view of the same root — not this collection's
    ///     contents.
    /// </summary>
    /// <remarks>
    ///     This is the one place the unification shows a seam. A view of ten items still exposes the
    ///     store of all hundred thousand, because the store is what makes sharing work and
    ///     restricting it per view would mean either copying or a wrapper per stage. Ask
    ///     <see cref="KeysCell" /> what is in the collection; ask this what an item is.
    /// </remarks>
    Cell<CollectionSnapshot<TKey, TId, TState>> SnapshotCell { get; }

    /// <summary>
    ///     The item's mutable portion, no value while the key is absent from the store.
    /// </summary>
    /// <param name="key">The key to observe.</param>
    /// <returns>A cell tracking that key's state.</returns>
    /// <remarks>
    ///     This answers for the store, not for membership: asking a filtered view about a key it
    ///     filtered out still gives that item's state. Membership questions belong to
    ///     <see cref="KeysCell" />.
    /// </remarks>
    Cell<Maybe<TState>> StateCell(TKey key);

    /// <summary>The item's immutable portion, no value while the key is absent.</summary>
    /// <param name="key">The key to observe.</param>
    /// <returns>A cell tracking that key's identity.</returns>
    Cell<Maybe<TId>> IdentityCell(TKey key);
}
