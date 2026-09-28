using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     The C# surface of the fold over a collection. A fold gives one cell from the items that a
///     collection holds. A total of a balance and a count of the items of a view are two of them.
/// </summary>
[PublicAPI]
public static class CollectionFoldExtensionMethods
{
    /// <summary>
    ///     Folds each item into one cell, and keeps that cell current as the collection changes.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
    /// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
    /// <typeparam name="TAccumulate">The type of the value that the fold gives.</typeparam>
    /// <param name="collection">The collection or view to fold.</param>
    /// <param name="select">
    ///     The part of an item that this fold adds, from its identity and its state, such as a
    ///     balance.
    /// </param>
    /// <param name="zero">
    ///     The value for no items, which must be the identity of <paramref name="add" />.
    /// </param>
    /// <param name="add">Adds the value of one item to the accumulated value.</param>
    /// <param name="subtract">
    ///     Removes the value of one item from the accumulated value. It must remove each value that
    ///     <paramref name="add" /> includes.
    /// </param>
    /// <returns>A cell with the folded value of the items that the collection holds.</returns>
    /// <remarks>
    ///     <para>
    ///         The cost of one edit is the count of the keys in that edit, and not the count of the
    ///         items. Each change carries the states before it and the states after it. Thus, this
    ///         code removes the previous value of a key and adds the new one. A fold written by
    ///         hand usually reads the store again, which is <c>O(n)</c> for each edit.
    ///     </para>
    ///     <para>
    ///         <paramref name="add" /> and <paramref name="subtract" /> must make a group with
    ///         <paramref name="zero" />: <paramref name="subtract" /> must remove each value that
    ///         <paramref name="add" /> includes, and <paramref name="zero" /> changes no value. A sum
    ///         and a count are groups. A maximum is not one, because no function removes a value
    ///         from a maximum, and this operation cannot give one. Read the first key of a view
    ///         with a sort for that. The group must also be commutative: this code adds and
    ///         subtracts in the sequence of the keys of a change.
    ///     </para>
    ///     <para>
    ///         <typeparamref name="TAccumulate" /> is the type of the caller, thus a group gives
    ///         more than a sum. A pair of a sum and a count, with a Map after it, gives an average.
    ///         A map of one counter for each group gives a count for each group, where
    ///         <paramref name="add" /> adds one to a counter and <paramref name="subtract" />
    ///         removes one. A select of 1 or 0 counts the items that a condition accepts, with no
    ///         Filter stage. Where <paramref name="add" /> and <paramref name="subtract" /> are one
    ///         function, such as exclusive or, the value is a fingerprint of the states.
    ///     </para>
    ///     <para>
    ///         A view folds its own items. The fold of a Filter stage adds an item as the filter
    ///         accepts it, and removes the item as the filter refuses it. Thus, the value follows
    ///         the view and not the store.
    ///     </para>
    ///     <para>
    ///         A floating-point type is a group that loses precision. Each edit adds one operation
    ///         to the value. Thus, a long sequence of edits can move the value away from the sum of
    ///         the items. Fold a decimal, or an integer of the smallest unit, where that matters:
    ///         the Accounts sample holds a balance in cents for this cause.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<TAccumulate> Fold<TKey, TIdentity, TState, TAccumulate>(
        this ReactiveCollection<TKey, TIdentity, TState> collection,
        Func<TIdentity, TState, TAccumulate> select,
        TAccumulate zero,
        Func<TAccumulate, TAccumulate, TAccumulate> add,
        Func<TAccumulate, TAccumulate, TAccumulate> subtract)
        where TKey : notnull
        where TIdentity : notnull =>
        CollectionFoldUtility.FoldImpl(
            collection: collection,
            select: select,
            zero: zero,
            add: add,
            subtract: subtract);
}
