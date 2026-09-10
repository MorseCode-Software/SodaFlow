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
///         <c>option</c> in F# — so each wrapper declares its own over the collection that owns the
///         store and the cache behind them, reached inside the assembly rather than from here.
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

    /// <summary>
    ///     How those keys changed: which entered, which left, which moved, and to what position —
    ///     operations to apply in sequence.
    /// </summary>
    /// <remarks>
    ///     This carries positions and no states. It is what a list binds to, because a list has to
    ///     know where a row went. It is not
    ///     <see cref="ReactiveCollection{TKey,TIdentity,TState}.ItemChangesStream" /> in a different shape: an item whose state changed
    ///     without moving arrives here as an update carrying an index, and what the new state
    ///     actually is has to be looked up in <see cref="SnapshotCell" /> or read from the other
    ///     stream.
    /// </remarks>
    Stream<CollectionViewChange<TKey, TIdentity, TState>> KeyChangesStream { get; }

    /// <summary>
    ///     The shared item store, spanning every view of the same root — not this collection's
    ///     contents.
    /// </summary>
    /// <remarks>
    ///     This is the one place on this interface where the unification shows a seam. A view of
    ///     ten items still exposes the store of all hundred thousand,
    ///     because the store is what makes sharing work and restricting it per view would mean
    ///     either copying or a wrapper per stage. Ask <see cref="KeysCell" /> what is in the
    ///     collection; ask this what an item is.
    /// </remarks>
    Cell<CollectionSnapshot<TKey, TIdentity, TState>> SnapshotCell { get; }
}

/// <summary>
///     What the language wrappers need and consumers do not: the collection a view was derived
///     from, which owns the item store and the per-item cell cache.
/// </summary>
/// <remarks>
///     Off <see cref="IReactiveCollection{TKey,TIdentity,TState}" /> deliberately. A view answers
///     for itself - its keys, its changes, its snapshot - and nothing on it leads back to the
///     collection it came from, the way nothing on an <c>IEnumerable</c> leads back to the sequence
///     it was projected from. The wrappers still need the root to reach the shared per-item cells,
///     so they reach it here, inside the assembly boundary rather than through the public surface.
/// </remarks>
/// <inheritdoc />
internal interface IReactiveCollectionInternal<TKey, TIdentity, TState>
    : IReactiveCollection<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>The collection that owns the store. A root's own is itself.</summary>
    ReactiveCollection<TKey, TIdentity, TState> Root { get; }
}
