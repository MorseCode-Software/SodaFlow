using System;
using System.Collections.Generic;
using System.Linq;

namespace SodaFlow.Collections;

/// <summary>
///     The incremental fold over a collection. The C# wrapper and the F# wrapper
///     use it through their own surfaces.
/// </summary>
/// <remarks>
///     The fold reads each change and never the store. An
///     <see cref="ItemChange{TKey,TIdentity,TState}" /> carries the states before
///     it and the states after it. Thus, the cost of one change is the count of
///     the keys in that change, and not the count of the items. A fold that reads
///     the store again at each change is <c>O(n)</c> for each edit, and this is
///     <c>O(1)</c> for an edit of one key.
///     <para>
///         The fold takes <c>add</c> and <c>subtract</c>, and not one function,
///         because an edit removes the previous value of a key. A group is thus
///         the minimum that this operation needs. <c>subtract</c> must remove each
///         value that <c>add</c> includes, and <c>zero</c> must be the identity of
///         <c>add</c>. A sum, a count, and a total of a product are groups. A
///         maximum is not one, because no function removes a value from a maximum.
///     </para>
/// </remarks>
internal static class CollectionFoldUtility
{
    /// <summary>
    ///     Folds each item of a collection into one cell, and keeps that cell current
    ///     with the changes of the collection.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <typeparam name="TAccumulate">The type of the value that the fold gives.</typeparam>
    /// <param name="collection">The collection to fold.</param>
    /// <param name="select">
    ///     The part of an item that the fold adds, from its identity
    ///     and its state.
    /// </param>
    /// <param name="zero">
    ///     The identity of <paramref name="add" />, which is the value
    ///     for no items.
    /// </param>
    /// <param name="add">Adds the value of one item to the accumulated value.</param>
    /// <param name="subtract">
    ///     Removes the value of one item from the accumulated
    ///     value.
    /// </param>
    /// <returns>A cell with the folded value of each item that the collection holds.</returns>
    internal static Cell<TAccumulate> FoldImpl<TKey, TIdentity, TState, TAccumulate>(
        ReactiveCollection<TKey, TIdentity, TState> collection,
        Func<TIdentity, TState, TAccumulate> select,
        TAccumulate zero,
        Func<TAccumulate, TAccumulate, TAccumulate> add,
        Func<TAccumulate, TAccumulate, TAccumulate> subtract)
        where TKey : notnull
        where TIdentity : notnull =>
        TransactionInternal.RunImpl(() =>
        {
            // The first value comes from the store, because a fold of no changes is a fold of the
            // items that the collection holds now. A view gives its own items here: the States of a
            // scoped snapshot hold the keys of that view alone.
            CollectionSnapshot<TKey, TIdentity, TState> snapshot = collection.SnapshotCell.SampleImpl();

            TAccumulate initial =
                snapshot.States.Pairs.Aggregate(
                    seed: zero,
                    func: (running, pair) =>
                        add(
                            arg1: running,
                            arg2: select(arg1: IdentityOf(snapshot: snapshot, key: pair.Key), arg2: pair.Value)));

            return collection.ItemChangesStream.AccumImpl(
                initialState: initial,
                f: (change, running) =>
                    Apply(
                        change: change,
                        running: running,
                        select: select,
                        add: add,
                        subtract: subtract));
        });

    /// <summary>
    ///     Folds the identity of each item of a collection into one cell. The cell
    ///     stays current with each change that adds or removes an item.
    /// </summary>
    /// <remarks>
    ///     An identity is constant while the collection has its key, thus only an add
    ///     or a removal can change this value. The fold reads only the changes that
    ///     add or remove a key, and not an edit of a state. Thus, the cell sends no
    ///     value at such an edit. The cost of the edit here is one test, and the count
    ///     of the keys in the edit does not change it.
    ///     <para>
    ///         A slice can name a key that stays in its window as a removal and an
    ///         add. An edit of a state that moves the keys of the window does this.
    ///         The fold reads that change, and the cell sends a value that is equal to
    ///         its previous value. The fold also reads a reset of a view, as
    ///         <see cref="FoldImpl" /> does, and it reads each key of the view for it.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <typeparam name="TAccumulate">The type of the value that the fold gives.</typeparam>
    /// <param name="collection">The collection to fold.</param>
    /// <param name="select">The part of an identity that the fold adds.</param>
    /// <param name="zero">
    ///     The identity of <paramref name="add" />, which is the value
    ///     for no items.
    /// </param>
    /// <param name="add">Adds the value of one item to the accumulated value.</param>
    /// <param name="subtract">
    ///     Removes the value of one item from the accumulated
    ///     value.
    /// </param>
    /// <returns>A cell with the folded value of each item that the collection holds.</returns>
    internal static Cell<TAccumulate> FoldByIdentityImpl<TKey, TIdentity, TState, TAccumulate>(
        ReactiveCollection<TKey, TIdentity, TState> collection,
        Func<TIdentity, TAccumulate> select,
        TAccumulate zero,
        Func<TAccumulate, TAccumulate, TAccumulate> add,
        Func<TAccumulate, TAccumulate, TAccumulate> subtract)
        where TKey : notnull
        where TIdentity : notnull =>
        TransactionInternal.RunImpl(() =>
        {
            // The Identities of a scoped snapshot hold the keys of the view alone, as its States
            // do in FoldImpl.
            TAccumulate initial =
                collection.SnapshotCell.SampleImpl()
                    .Identities.Aggregate(
                        seed: zero,
                        func: (running, pair) => add(arg1: running, arg2: select(pair.Value)));

            return collection.ItemChangesStream
                .FilterImpl(static change => change.IsStructural || change.IsReset)
                .AccumImpl(
                    initialState: initial,
                    f: (change, running) =>
                        ApplyIdentities(
                            change: change,
                            running: running,
                            select: select,
                            add: add,
                            subtract: subtract));
        });

    /// <summary>
    ///     The accumulated value after one change. This adds the new value of each key
    ///     that the change names and removes the value that the key had before the
    ///     change.
    /// </summary>
    /// <remarks>
    ///     The test for a previous value is a read of the items before the change, and
    ///     not a test against the added keys. A key that the change added has no
    ///     previous item, thus one lookup answers the two conditions. That lookup is
    ///     <c>O(1)</c>, and a test against the added keys is a search of a collection.
    ///     <para>
    ///         The second loop removes the value of each removed key that the first
    ///         loop did not read. A view can name one key as a removal and as an add.
    ///         An edit that replaces an item does this, and so does a slice that moves
    ///         a key in its window. The first loop removes the previous value of that
    ///         key, thus the second loop must not remove it again.
    ///     </para>
    ///     <para>
    ///         The value to remove reads the identity from before the change, and the
    ///         value to add reads the identity from after it. A replacement of an item
    ///         can change the identity of its key.
    ///     </para>
    /// </remarks>
    private static TAccumulate Apply<TKey, TIdentity, TState, TAccumulate>(
        ItemChange<TKey, TIdentity, TState> change,
        TAccumulate running,
        Func<TIdentity, TState, TAccumulate> select,
        Func<TAccumulate, TAccumulate, TAccumulate> add,
        Func<TAccumulate, TAccumulate, TAccumulate> subtract)
        where TKey : notnull
        where TIdentity : notnull
    {
        TAccumulate result = running;

        foreach (KeyValuePair<TKey, TState> pair in change.NewStates)
        {
            if (change.Before.TryGetHalves(key: pair.Key, identity: out TIdentity? wasIdentity, state: out TState? was))
            {
                result = subtract(arg1: result, arg2: select(arg1: wasIdentity, arg2: was));
            }

            result =
                add(
                    arg1: result,
                    arg2: select(arg1: IdentityOf(snapshot: change.After, key: pair.Key), arg2: pair.Value));
        }

        foreach (TKey key in change.Removed)
        {
            if (!change.NewStates.ContainsKey(key)
                && change.Before.TryGetHalves(key: key, identity: out TIdentity? wasIdentity, state: out TState? was))
            {
                result = subtract(arg1: result, arg2: select(arg1: wasIdentity, arg2: was));
            }
        }

        return result;
    }

    /// <summary>
    ///     The accumulated value after one change that adds or removes a key. This
    ///     adds the identity of each added key and removes the identity that the key
    ///     had before the change.
    /// </summary>
    /// <remarks>
    ///     An added key can have an identity before the change. The root gives that
    ///     for an edit that removes an item and adds one with the same key, and the
    ///     new identity can be different. A view names such a key as a removal and as
    ///     an add. Thus, the second loop skips a removed key that the first loop read,
    ///     as <see cref="Apply" /> does.
    ///     <para>
    ///         A reset does not put a replaced key in <c>Added</c> when the key stays
    ///         in the view.
    ///         Thus, the first loop reads each key of a reset, and not only the added
    ///         keys. A reset names each key of the view in <c>NewStates</c>, and the
    ///         cost is the size of the view.
    ///     </para>
    /// </remarks>
    private static TAccumulate ApplyIdentities<TKey, TIdentity, TState, TAccumulate>(
        ItemChange<TKey, TIdentity, TState> change,
        TAccumulate running,
        Func<TIdentity, TAccumulate> select,
        Func<TAccumulate, TAccumulate, TAccumulate> add,
        Func<TAccumulate, TAccumulate, TAccumulate> subtract)
        where TKey : notnull
        where TIdentity : notnull
    {
        TAccumulate result = running;

        IEnumerable<TKey> named = change.IsReset ? change.NewStates.Keys : change.Added;

        foreach (TKey key in named)
        {
            if (change.Before.TryGetIdentity(key: key, identity: out TIdentity? was))
            {
                result = subtract(arg1: result, arg2: select(was));
            }

            result = add(arg1: result, arg2: select(IdentityOf(snapshot: change.After, key: key)));
        }

        foreach (TKey key in change.Removed)
        {
            if (!change.NewStates.ContainsKey(key)
                && change.Before.TryGetIdentity(key: key, identity: out TIdentity? was))
            {
                result = subtract(arg1: result, arg2: select(was));
            }
        }

        return result;
    }

    /// <summary>The identity of a key that the snapshot must have.</summary>
    /// <remarks>
    ///     The identity map and the state map of a snapshot hold the same keys, and a
    ///     change adds a key to the two. Thus, a key with no identity here is a defect
    ///     of this assembly, and this code does not skip it.
    /// </remarks>
    private static TIdentity IdentityOf<TKey, TIdentity, TState>(
        CollectionSnapshot<TKey, TIdentity, TState> snapshot,
        TKey key)
        where TKey : notnull
        where TIdentity : notnull =>
        snapshot.TryGetIdentity(key: key, identity: out TIdentity? identity)
            ? identity
            : throw new InvalidOperationException($"Key '{key}' has no identity in the snapshot.");
}
