using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     An ordered collection of items with keys. The collection that you make is one of these,
///     and so is each view from it. Thus, <c>Filter</c> and <c>SortBy</c> accept one and return
///     one, as <c>Where</c> accepts an <c>IEnumerable</c> and returns one.
/// </summary>
/// <remarks>
///     <para>
///         A view answers for itself. Its keys are its own, its changes are its own, and its
///         snapshot and its cells for one item hold its content and nothing more. A key that its
///         criteria removed is not in it. No member here goes back to the collection of the view,
///         as no member of an <c>IEnumerable</c> goes back to its source sequence.
///     </para>
///     <para>
///         This is a class and not an interface, and code out of this assembly cannot subclass
///         it. <c>Cell</c> and <c>Stream</c> have the same agreement. There is one implementation
///         of a reactive collection and one implementation of a view. No code replaces them with a
///         replacement or a mock. A concrete class lets the language wrappers use the internal
///         members that are necessary for them, with no cast that can fail.
///     </para>
///     <para>
///         No member here changes a value. The construction of a collection sets the code that
///         can change it, from the edit streams that it receives. The stage that makes a view sets
///         the code that can change that view.
///     </para>
///     <para>
///         The cells for one item are not on this class. Each one answers with an optional value,
///         and each language has a different optional type: <c>Maybe</c> in C# and <c>option</c>
///         in F#. Thus, each wrapper declares its own cells above the internal members below.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
[PublicAPI]
public abstract class ReactiveCollection<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    /// <summary>
    ///     This is a usual object and not a <c>System.Threading.Lock</c>. That type came with
    ///     .NET 9, and no target framework of this package has it.
    /// </summary>
    private readonly object cacheGate = new();

    /// <summary>
    ///     This is the same for the identities. It is a second dictionary and not one shared
    ///     dictionary, because a collection whose identity type and state type are the same type
    ///     then puts two different values at one key and answers the second with the first.
    /// </summary>
    private readonly Dictionary<Type, object> identityCaches = new();

    private readonly Dictionary<Type, object> itemCaches = new();

    /// <summary>One per-key cell cache per projected type, for states.</summary>
    private readonly Dictionary<Type, object> stateCaches = new();

    /// <summary>
    ///     This is internal, thus only this assembly can make one. The class remarks give the
    ///     cause.
    /// </summary>
    internal ReactiveCollection()
    {
    }

    /// <summary>This collection's keys, in order.</summary>
    /// <remarks>
    ///     This changes only at a change of the members or the order of this collection. A state
    ///     edit that moves no key comes to <see cref="KeyChangesStream" /> as an update and does
    ///     not change this cell. Thus, an edit that moved no row does not build a list from this
    ///     cell again.
    /// </remarks>
    public abstract Cell<OrderedKeys<TKey, TIdentity, TState>> KeysCell { get; }

    /// <summary>
    ///     The change to those keys: the keys that entered, the keys that left, the keys that
    ///     moved, and their new positions. They are operations to apply in sequence.
    /// </summary>
    /// <remarks>
    ///     This stream has positions and no states, and <see cref="ItemChangesStream" /> has
    ///     states and no positions. A list binds to this stream, because a list must know the new
    ///     position of a row.
    /// </remarks>
    public abstract Stream<CollectionViewChange<TKey, TIdentity, TState>> KeyChangesStream { get; }

    /// <summary>What this collection holds, at one logical version.</summary>
    public abstract Cell<CollectionSnapshot<TKey, TIdentity, TState>> SnapshotCell { get; }

    /// <summary>
    ///     The change to the items of this collection: the keys that an edit added, removed, or
    ///     changed, with the new state of each one.
    /// </summary>
    /// <remarks>
    ///     This stream has states and no positions, and it uses keys and not an order. A read of
    ///     it never makes this collection sort itself. Fold it when the answer comes from the
    ///     values and not from the order, such as a total, an average, or a count of the items
    ///     that agree with a predicate.
    /// </remarks>
    public abstract Stream<ItemChange<TKey, TIdentity, TState>> ItemChangesStream { get; }

    /// <summary>
    ///     The immutable part of each item in this collection. It changes only at a change of the
    ///     members.
    /// </summary>
    public abstract Cell<IReadOnlyDictionary<TKey, TIdentity>> ShapeCell { get; }

    /// <summary>
    ///     The collection that holds the store. A collection from its own construction is its own
    ///     store holder. For a view, it is the collection at the start of the chain.
    /// </summary>
    /// <remarks>
    ///     This is internal because a view has no use for it. It is here to give access to the
    ///     caches of the cells for one item.
    /// </remarks>
    internal abstract RootCollection<TKey, TIdentity, TState> Root { get; }

    /// <summary>
    ///     Defines a collection from its initial contents and each stream that edits it. There is
    ///     no imperative entry point. This construction sets the code that can change the
    ///     collection, and a reader finds all of it in one position.
    /// </summary>
    /// <param name="keySelector">Makes the key of an item from its immutable part.</param>
    /// <param name="initialItems">The collection's initial contents.</param>
    /// <param name="editStreams">Each stream that edits the collection.</param>
    /// <returns>The collection.</returns>
    /// <remarks>
    ///     Use the lifting factories of
    ///     <see cref="CollectionEdit{TKey,TIdentity,TState}" /> to change a domain stream into
    ///     edits. Where an edit uses a value from the collection, close the cycle with a stream
    ///     loop at the call, and do not use a sink.
    /// </remarks>
    public static ReactiveCollection<TKey, TIdentity, TState> Create(
        Func<TIdentity, TKey> keySelector,
        IEnumerable<Item<TIdentity, TState>> initialItems,
        params Stream<CollectionEdit<TKey, TIdentity, TState>>[] editStreams) =>
        Create(
            keySelector: keySelector,
            keyEqualityComparer: EqualityComparer<TKey>.Default,
            initialItems: initialItems,
            editStreams: editStreams);

    /// <summary>
    ///     Defines a collection from its initial contents and each stream that edits it. There is
    ///     no imperative entry point. This construction sets the code that can change the
    ///     collection, and a reader finds all of it in one position.
    /// </summary>
    /// <param name="keySelector">Makes the key of an item from its immutable part.</param>
    /// <param name="keyEqualityComparer">The equality comparer for keys.</param>
    /// <param name="initialItems">The collection's initial contents.</param>
    /// <param name="editStreams">Each stream that edits the collection.</param>
    /// <returns>The collection.</returns>
    /// <remarks>
    ///     Use the lifting factories of
    ///     <see cref="CollectionEdit{TKey,TIdentity,TState}" /> to change a domain stream into
    ///     edits. Where an edit uses a value from the collection, close the cycle with a stream
    ///     loop at the call, and do not use a sink.
    /// </remarks>
    public static ReactiveCollection<TKey, TIdentity, TState> Create(
        Func<TIdentity, TKey> keySelector,
        IEqualityComparer<TKey> keyEqualityComparer,
        IEnumerable<Item<TIdentity, TState>> initialItems,
        params Stream<CollectionEdit<TKey, TIdentity, TState>>[] editStreams) =>
        Create(
            keySelector: keySelector,
            keyEqualityComparer: keyEqualityComparer,
            initialItems: CellInternal.ConstantImpl(initialItems),
            editStreams: editStreams);

    /// <summary>
    ///     Defines a collection from its initial contents and each stream that edits it. There is
    ///     no imperative entry point. This construction sets the code that can change the
    ///     collection, and a reader finds all of it in one position.
    /// </summary>
    /// <param name="keySelector">Makes the key of an item from its immutable part.</param>
    /// <param name="initialItems">The collection's initial contents, sampled lazily.</param>
    /// <param name="editStreams">Each stream that edits the collection.</param>
    /// <returns>The collection.</returns>
    /// <remarks>
    ///     Use the lifting factories of
    ///     <see cref="CollectionEdit{TKey,TIdentity,TState}" /> to change a domain stream into
    ///     edits. Where an edit uses a value from the collection, close the cycle with a stream
    ///     loop at the call, and do not use a sink.
    /// </remarks>
    public static ReactiveCollection<TKey, TIdentity, TState> Create(
        Func<TIdentity, TKey> keySelector,
        Cell<IEnumerable<Item<TIdentity, TState>>> initialItems,
        params Stream<CollectionEdit<TKey, TIdentity, TState>>[] editStreams) =>
        Create(
            keySelector: keySelector,
            keyEqualityComparer: EqualityComparer<TKey>.Default,
            initialItems: initialItems,
            editStreams: editStreams);

    /// <summary>
    ///     Defines a collection from its initial contents and each stream that edits it. There is
    ///     no imperative entry point. This construction sets the code that can change the
    ///     collection, and a reader finds all of it in one position.
    /// </summary>
    /// <param name="keySelector">Makes the key of an item from its immutable part.</param>
    /// <param name="keyEqualityComparer">The equality comparer for keys.</param>
    /// <param name="initialItems">The collection's initial contents, sampled lazily.</param>
    /// <param name="editStreams">Each stream that edits the collection.</param>
    /// <returns>The collection.</returns>
    /// <remarks>
    ///     Use the lifting factories of
    ///     <see cref="CollectionEdit{TKey,TIdentity,TState}" /> to change a domain stream into
    ///     edits. Where an edit uses a value from the collection, close the cycle with a stream
    ///     loop at the call, and do not use a sink.
    /// </remarks>
    public static ReactiveCollection<TKey, TIdentity, TState> Create(
        Func<TIdentity, TKey> keySelector,
        IEqualityComparer<TKey> keyEqualityComparer,
        Cell<IEnumerable<Item<TIdentity, TState>>> initialItems,
        params Stream<CollectionEdit<TKey, TIdentity, TState>>[] editStreams) =>
        RootCollection<TKey, TIdentity, TState>.CreateImpl(
            keySelector: keySelector,
            keyEqualityComparer: keyEqualityComparer,
            initialEntries: initialItems,
            editStreams: editStreams);

    /// <summary>
    ///     A cell that follows the mutable part of one item, as this collection reads it, in the
    ///     shape from the language wrapper.
    /// </summary>
    /// <remarks>
    ///     A weak cache holds one for each key and for each projected type. Thus, the observers of
    ///     one key through one collection share a node. The same key through two views is two
    ///     cells, because the two views give two answers.
    /// </remarks>
    /// <typeparam name="TProjected">The type that the wrapper gives to the cell.</typeparam>
    /// <param name="key">The key to monitor.</param>
    /// <param name="onPresent">Makes the value of the cell while the collection has the key.</param>
    /// <param name="onAbsent">Makes the value of the cell while the collection does not have the key.</param>
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

    /// <summary>The same for the immutable part of an item.</summary>
    /// <remarks>
    ///     This changes only when the key enters this collection or leaves it. Thus, a state edit
    ///     never sends a value from one, and a hold on one for the life of a row costs nothing.
    /// </remarks>
    /// <typeparam name="TProjected">The type that the wrapper gives to the cell.</typeparam>
    /// <param name="key">The key to monitor.</param>
    /// <param name="onPresent">Makes the value of the cell while the collection has the key.</param>
    /// <param name="onAbsent">Makes the value of the cell while the collection does not have the key.</param>
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

    /// <summary>
    ///     The same for the two parts of an item together.
    /// </summary>
    /// <remarks>
    ///     This sends a value at each change that the state cell sends one for, because an item
    ///     holds the state. Use it for a value that reads the two parts. Where one binding reads
    ///     the identity and a different binding reads the state, the two cells cost less: the
    ///     identity cell sleeps through an edit to the state.
    /// </remarks>
    /// <typeparam name="TProjected">The type that the wrapper gives to the cell.</typeparam>
    /// <param name="key">The key to monitor.</param>
    /// <param name="onPresent">Makes the value of the cell while the collection has the key.</param>
    /// <param name="onAbsent">Makes the value of the cell while the collection does not have the key.</param>
    /// <returns>The cell.</returns>
    internal Cell<TProjected> ItemCellImpl<TProjected>(
        TKey key,
        Func<Item<TIdentity, TState>, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        lock (this.cacheGate)
        {
            ProjectedCellCache<TKey, TProjected> cache = CacheFor<TProjected>(this.itemCaches);
            Cell<TProjected>? cached = cache.Get(key);

            if (cached is not null)
            {
                return cached;
            }

            Cell<TProjected> created = this.CreateItemCell(key: key, onPresent: onPresent, onAbsent: onAbsent);
            cache.Set(key: key, cell: created);

            return created;
        }
    }

    /// <summary>Builds the cell <see cref="ItemCellImpl{TProjected}" /> will cache.</summary>
    internal abstract Cell<TProjected> CreateItemCell<TProjected>(
        TKey key,
        Func<Item<TIdentity, TState>, TProjected> onPresent,
        Func<TProjected> onAbsent);

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
    ///     This method holds the one cast, and the cast is correct because the key of the
    ///     dictionary is the type of the cast. Only a call whose <c>TProjected</c> is that type can
    ///     put an entry at <c>typeof(TProjected)</c>. A dictionary from a type to a value with that
    ///     type as its parameter is higher-kinded, and C# cannot give that. Thus, this code makes
    ///     the statement here one time and not at each lookup.
    /// </remarks>
    private static ProjectedCellCache<TKey, TProjected> CacheFor<TProjected>(Dictionary<Type, object> caches)
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
///     Makes collections whose identities hold their own key. Thus, a caller can omit the selector
///     of each other overload.
/// </summary>
/// <remarks>
///     <para>
///         This is a second class beside
///         <see cref="ReactiveCollection{TKey,TIdentity,TState}" /> and not more overloads on that
///         class, because a static method cannot add a constraint to the type parameters of its
///         own class. <c>TIdentity : IIdentity&lt;TKey&gt;</c> is the one difference here. The
///         shape is the shape of <c>Cell</c> beside <c>Cell&lt;T&gt;</c>.
///     </para>
///     <para>
///         The compiler infers <c>TKey</c> from the edit streams. Type inference does not read a
///         constraint, thus it cannot infer <c>TKey</c> from <see cref="IIdentity{TKey}" /> alone.
///         A construction with no edit stream must name the three type arguments, or must use the
///         overloads with a selector.
///     </para>
/// </remarks>
[PublicAPI]
public static class ReactiveCollection
{
    /// <summary>
    ///     Defines a collection from its initial contents and each stream that edits it,
    ///     and this code takes the key of each item from the identity.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <param name="initialEntries">The collection's initial contents.</param>
    /// <param name="editStreams">Each stream that edits the collection.</param>
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
