using System;
using System.Collections.Generic;
using System.Linq;

namespace SodaFlow.Collections;

/// <summary>
///     The incremental fold over a collection. The C# wrapper and the F# wrapper use it through
///     their own surfaces.
/// </summary>
/// <remarks>
///     The fold reads each change and never the store. An
///     <see cref="ItemChange{TKey,TIdentity,TState}" /> carries the states before it and the states
///     after it. Thus, the cost of one change is the count of the keys in that change, and not the
///     count of the items. A fold that reads the store again at each change is <c>O(n)</c> for each
///     edit, and this is <c>O(1)</c> for an edit of one key.
///     <para>
///         The fold takes <c>add</c> and <c>subtract</c>, and not one function, because an edit
///         removes the previous value of a key. A group is thus the minimum that this operation
///         needs. <c>subtract</c> must remove each value that <c>add</c> includes, and <c>zero</c>
///         must be the identity of <c>add</c>. A sum, a count, and a total of a product are
///         groups. A maximum is not one, because no function removes a value from a maximum.
///     </para>
/// </remarks>
internal static class CollectionFoldUtility
{
    /// <summary>
    ///     Folds the state of each item of a collection into one cell, and keeps that cell current
    ///     with the changes of the collection.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <typeparam name="TAccumulate">The type of the value that the fold gives.</typeparam>
    /// <param name="collection">The collection to fold.</param>
    /// <param name="select">The part of a state that the fold adds.</param>
    /// <param name="zero">The identity of <paramref name="add" />, which is the value for no items.</param>
    /// <param name="add">Adds the value of one item to the accumulated value.</param>
    /// <param name="subtract">Removes the value of one item from the accumulated value.</param>
    /// <returns>A cell with the folded value of each item that the collection holds.</returns>
    internal static Cell<TAccumulate> FoldImpl<TKey, TIdentity, TState, TAccumulate>(
        ReactiveCollection<TKey, TIdentity, TState> collection,
        Func<TState, TAccumulate> select,
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
            TAccumulate initial =
                collection.SnapshotCell.SampleImpl()
                    .States.Pairs.Aggregate(
                        seed: zero,
                        func: (running, pair) => add(arg1: running, arg2: select(pair.Value)));

            return collection.ItemChangesStream.AccumImpl(
                initialState: initial,
                f: (change, running) => Apply(
                    change: change,
                    running: running,
                    select: select,
                    add: add,
                    subtract: subtract));
        });

    /// <summary>
    ///     The accumulated value after one change. This adds the new value of each key that the
    ///     change names and removes the value that the key had before the change.
    /// </summary>
    /// <remarks>
    ///     The test for a previous value is a read of the states before the change, and not a test
    ///     against the added keys. A key that the change added has no previous state, thus one
    ///     lookup answers the two conditions. That lookup is <c>O(1)</c>, and a test against the
    ///     added keys is a search of a collection.
    ///     <para>
    ///         A removed key is not in the states after the change. Thus, the first loop cannot
    ///         find it, and the second loop removes the value of each one.
    ///     </para>
    /// </remarks>
    private static TAccumulate Apply<TKey, TIdentity, TState, TAccumulate>(
        ItemChange<TKey, TIdentity, TState> change,
        TAccumulate running,
        Func<TState, TAccumulate> select,
        Func<TAccumulate, TAccumulate, TAccumulate> add,
        Func<TAccumulate, TAccumulate, TAccumulate> subtract)
        where TKey : notnull
        where TIdentity : notnull
    {
        TAccumulate result = running;

        foreach (KeyValuePair<TKey, TState> pair in change.NewStates)
        {
            if (change.Before.States.TryGetState(key: pair.Key, state: out TState? was))
            {
                result = subtract(arg1: result, arg2: select(was));
            }

            result = add(arg1: result, arg2: select(pair.Value));
        }

        foreach (TKey key in change.Removed)
        {
            if (change.Before.States.TryGetState(key: key, state: out TState? was))
            {
                result = subtract(arg1: result, arg2: select(was));
            }
        }

        return result;
    }
}
