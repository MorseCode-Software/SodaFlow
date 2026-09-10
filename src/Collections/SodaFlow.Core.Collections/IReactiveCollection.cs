using JetBrains.Annotations;

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
///         which keys it holds and in what order. That is why the per-item cells give the same cell
///         whichever view you ask — sharing is not arranged, it is what falls out of a view never
///         copying anything.
///     </para>
///     <para>
///         Nothing here mutates. What can change a collection is fixed when it is created, from the
///         edit streams it is given; what can change a view is fixed by the stage that derives it.
///     </para>
///     <para>
///         The per-item cells are deliberately not on this interface. They answer with an optional
///         value, and which optional value differs by language — <c>Maybe</c> in C# and
///         <c>option</c> in F# — so each wrapper declares its own over <see cref="Root" />, which
///         is what owns the store and the cache behind them.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
public interface IReactiveCollection<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>This collection's keys, in order.</summary>
    Cell<IOrderedKeys<TKey, TIdentity, TState>> KeysCell { get; }

    /// <summary>Membership and ordering changes, as operations to apply in sequence.</summary>
    Stream<CollectionViewChange<TKey, TIdentity, TState>> ChangesStream { get; }

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
    Cell<CollectionSnapshot<TKey, TIdentity, TState>> SnapshotCell { get; }

    /// <summary>
    ///     The collection this view was ultimately derived from, which owns the item store. A
    ///     root's own <see cref="Root" /> is itself.
    /// </summary>
    ReactiveCollection<TKey, TIdentity, TState> Root { get; }
}
