using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     A keyed collection of items, ordered. The collection you create is one of these and so is
///     every view derived from it, so <c>Filter</c> and <c>SortBy</c> take one and return one, the
///     way <c>Where</c> takes and returns an <c>IEnumerable</c>.
/// </summary>
/// <remarks>
///     <para>
///         A view answers for itself. Its keys are its own, its changes are its own, and its
///         snapshot and per-item cells hold what it holds and nothing else - a key its criteria
///         excluded is simply not in it. Nothing here leads back to the collection a view was
///         derived from, the way nothing on an <c>IEnumerable</c> leads back to the sequence it was
///         projected from.
///     </para>
///     <para>
///         A class rather than an interface, and one nobody outside this assembly can derive from,
///         which is the same bargain <c>Cell</c> and <c>Stream</c> make. There is one implementation
///         of a reactive collection and one of a view, they are not meant to be substituted or
///         mocked, and being concrete is what lets the language wrappers reach the internals they
///         need without a cast that could fail.
///     </para>
///     <para>
///         Nothing here mutates. What can change a collection is fixed when it is created, from the
///         edit streams it is given; what can change a view is fixed by the stage that derives it.
///     </para>
///     <para>
///         The per-item cells are deliberately not on this class. They answer with an optional
///         value, and which optional value differs by language - <c>Maybe</c> in C# and
///         <c>option</c> in F# - so each wrapper declares its own over the internals below.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
[PublicAPI]
public abstract class ReactiveCollection<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>
    ///     A plain object rather than <c>System.Threading.Lock</c>, which arrived in .NET 9 and is
    ///     not available on any of this package's target frameworks.
    /// </summary>
    private readonly object cacheGate = new();

    /// <summary>
    ///     The same for identities. Kept apart from the states rather than sharing one dictionary,
    ///     because a collection whose identity and state are the same type would otherwise file two
    ///     different questions under one key and answer the second with the first.
    /// </summary>
    private readonly Dictionary<Type, object> identityCaches = new();

    /// <summary>One per-key cell cache per projected type, for states.</summary>
    private readonly Dictionary<Type, object> stateCaches = new();

    /// <summary>
    ///     Internal, so that this assembly is the only thing that can produce one. See the class
    ///     remarks for why that is deliberate.
    /// </summary>
    internal ReactiveCollection()
    {
    }

    /// <summary>This collection's keys, in order.</summary>
    public abstract Cell<OrderedKeys<TKey, TIdentity, TState>> KeysCell { get; }

    /// <summary>
    ///     How those keys changed: which entered, which left, which moved, and to what position -
    ///     operations to apply in sequence.
    /// </summary>
    /// <remarks>
    ///     Positions and no states, where <see cref="ItemChangesStream" /> carries states and no
    ///     positions. This is what a list binds to, because a list has to know where a row went.
    /// </remarks>
    public abstract Stream<CollectionViewChange<TKey, TIdentity, TState>> KeyChangesStream { get; }

    /// <summary>What this collection holds, at one logical version.</summary>
    public abstract Cell<CollectionSnapshot<TKey, TIdentity, TState>> SnapshotCell { get; }

    /// <summary>
    ///     How this collection's items changed: the keys added, removed or altered, carrying the new
    ///     state of each.
    /// </summary>
    /// <remarks>
    ///     States and no positions, and keyed rather than ordered - reading it never asks this
    ///     collection to order itself. Fold it when the answer follows values rather than order: a
    ///     total, an average, a count of items matching something.
    /// </remarks>
    public abstract Stream<ItemChange<TKey, TIdentity, TState>> ItemChangesStream { get; }

    /// <summary>
    ///     The immutable half of everything this collection holds, which moves only when its
    ///     membership does.
    /// </summary>
    public abstract Cell<IReadOnlyDictionary<TKey, TIdentity>> ShapeCell { get; }

    /// <summary>
    ///     The collection that owns the store. A collection created directly is its own; a view's is
    ///     the one it was ultimately derived from.
    /// </summary>
    /// <remarks>
    ///     Internal because a view has nothing to want it for. It is here so that the per-item cell
    ///     caches can be reached where they live.
    /// </remarks>
    internal abstract ReactiveCollection<TKey, TIdentity, TState> Root { get; }

    /// <summary>
    ///     Defines a collection from its initial contents and every stream that will ever edit it.
    ///     There is no imperative entry point: what can change the collection is fixed here, at
    ///     construction, and is visible in one place.
    /// </summary>
    /// <param name="keySelector">Derives an item's key from its immutable portion.</param>
    /// <param name="initialItems">The collection's initial contents.</param>
    /// <param name="editStreams">Every stream that will ever edit the collection.</param>
    /// <returns>The collection.</returns>
    /// <remarks>
    ///     Use <see cref="CollectionEdit{TKey,TIdentity,TState}" />'s lifting factories to turn
    ///     domain streams into edits. Where the edits depend on something derived from the
    ///     collection itself, close the circle with a stream loop at the call site rather than
    ///     reaching for a sink.
    /// </remarks>
    public static ReactiveCollection<TKey, TIdentity, TState> Create(
        Func<TIdentity, TKey> keySelector,
        IEnumerable<Item<TIdentity, TState>> initialItems,
        params Stream<CollectionEdit<TKey, TIdentity, TState>>[] editStreams) =>
        RootCollection<TKey, TIdentity, TState>.CreateImpl(
            keySelector: keySelector,
            initialEntries: initialItems,
            editStreams: editStreams);

    /// <summary>
    ///     A cell tracking one item's mutable portion as this collection sees it, shaped by the
    ///     projection the language wrapper supplies.
    /// </summary>
    /// <remarks>
    ///     Cached weakly per key and per projected type, so observers of one key through one
    ///     collection share a node - and the same key through two views is two cells, because they
    ///     are two answers.
    /// </remarks>
    /// <typeparam name="TProjected">What the wrapper asked the cell to hold.</typeparam>
    /// <param name="key">The key to observe.</param>
    /// <param name="onPresent">Projects the value the cell holds while the key is there.</param>
    /// <param name="onAbsent">Projects the value it holds while the key is not.</param>
    /// <returns>The cell.</returns>
    internal Cell<TProjected> StateCellImpl<TProjected>(
        TKey key,
        Func<TState, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        lock (this.cacheGate)
        {
            ProjectedCellCache<TKey, TProjected> cache = CacheFor<TProjected>(this.stateCaches);
            Cell<TProjected>? cached = cache.Get(key);

            if (cached is not null)
            {
                return cached;
            }

            Cell<TProjected> created = this.CreateStateCell(key: key, onPresent: onPresent, onAbsent: onAbsent);
            cache.Set(key: key, cell: created);

            return created;
        }
    }

    /// <summary>The same for an item's immutable portion.</summary>
    /// <remarks>
    ///     Moves only when the key enters or leaves this collection, so a state edit never wakes
    ///     one and holding one for the life of a row costs nothing.
    /// </remarks>
    /// <typeparam name="TProjected">What the wrapper asked the cell to hold.</typeparam>
    /// <param name="key">The key to observe.</param>
    /// <param name="onPresent">Projects the value the cell holds while the key is there.</param>
    /// <param name="onAbsent">Projects the value it holds while the key is not.</param>
    /// <returns>The cell.</returns>
    internal Cell<TProjected> IdentityCellImpl<TProjected>(
        TKey key,
        Func<TIdentity, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        lock (this.cacheGate)
        {
            ProjectedCellCache<TKey, TProjected> cache = CacheFor<TProjected>(this.identityCaches);
            Cell<TProjected>? cached = cache.Get(key);

            if (cached is not null)
            {
                return cached;
            }

            Cell<TProjected> created = this.CreateIdentityCell(key: key, onPresent: onPresent, onAbsent: onAbsent);
            cache.Set(key: key, cell: created);

            return created;
        }
    }

    /// <summary>Builds the cell <see cref="StateCellImpl{TProjected}" /> will cache.</summary>
    internal abstract Cell<TProjected> CreateStateCell<TProjected>(
        TKey key,
        Func<TState, TProjected> onPresent,
        Func<TProjected> onAbsent);

    /// <summary>Builds the cell <see cref="IdentityCellImpl{TProjected}" /> will cache.</summary>
    internal abstract Cell<TProjected> CreateIdentityCell<TProjected>(
        TKey key,
        Func<TIdentity, TProjected> onPresent,
        Func<TProjected> onAbsent);

    /// <summary>The cache for one projected type, created the first time that type is asked for.</summary>
    /// <remarks>
    ///     This is where the one cast lives, and it is sound because the dictionary is keyed by the
    ///     very type being cast to: an entry under <c>typeof(TProjected)</c> can only have been put
    ///     there by a call whose <c>TProjected</c> was that type. A dictionary from a type to a
    ///     thing parameterized by that type is a higher-kinded thing, which C# cannot express - so
    ///     the claim is made here once rather than at every lookup.
    /// </remarks>
    private static ProjectedCellCache<TKey, TProjected> CacheFor<TProjected>(IDictionary<Type, object> caches)
    {
        if (caches.TryGetValue(key: typeof(TProjected), value: out object? existing))
        {
            return (ProjectedCellCache<TKey, TProjected>)existing;
        }

        ProjectedCellCache<TKey, TProjected> created = new();
        caches[typeof(TProjected)] = created;

        return created;
    }
}

/// <summary>
///     Creates collections whose identities carry their own key, so the selector every other
///     overload asks for can be left out.
/// </summary>
/// <remarks>
///     <para>
///         A companion to <see cref="ReactiveCollection{TKey,TIdentity,TState}" /> rather than more
///         overloads on it, because a static method cannot add a constraint to the type parameters
///         of the class declaring it - and <c>TIdentity : IIdentity&lt;TKey&gt;</c> is the whole of what
///         these are. The same shape as <c>Cell</c> beside <c>Cell&lt;T&gt;</c>.
///     </para>
///     <para>
///         <c>TKey</c> is inferred from the edit streams. Type inference does not read constraints,
///         so it cannot come from <see cref="IIdentity{TKey}" /> alone: a collection created with no
///         edit streams at all has to name the three type arguments, or use the selector overloads
///         instead.
///     </para>
/// </remarks>
[PublicAPI]
public static class ReactiveCollection
{
    /// <summary>
    ///     Defines a collection from its initial contents and every stream that will ever edit it,
    ///     taking each item's key from the identity itself.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
    /// <param name="initialEntries">The collection's initial contents.</param>
    /// <param name="editStreams">Every stream that will ever edit the collection.</param>
    /// <returns>The collection.</returns>
    public static ReactiveCollection<TKey, TIdentity, TState> Create<TKey, TIdentity, TState>(
        IEnumerable<Item<TIdentity, TState>> initialEntries,
        params Stream<CollectionEdit<TKey, TIdentity, TState>>[] editStreams)
        where TKey : notnull
        where TIdentity : IIdentity<TKey> =>
        ReactiveCollection<TKey, TIdentity, TState>.Create(
            keySelector: static identity => identity.Key,
            initialItems: initialEntries,
            editStreams: editStreams);
}
