using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     The result of one stage of a view chain in a transaction. It is the public change
///     notification and it is also the protocol between two stages.
/// </summary>
/// <remarks>
///     <para>
///         The event holds its result <see cref="Keys" /> and the store on the two sides of the
///         change, which are <see cref="Before" /> and <see cref="After" />. The next stage does
///         not sample them. A stage below runs in the same transaction, and a sample of a cell
///         there gives the value from before the transaction. Thus, the event must hold the
///         results, or the chain does not agree with itself. That also lets a consumer make a
///         delta with no copy of the previous values.
///     </para>
///     <para>
///         The index of an operation is correct for a consumer that applies the operations in
///         sequence to the previous key list.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable part of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable part of an item.</typeparam>
[PublicAPI]
public sealed class CollectionViewChange<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    internal CollectionViewChange(
        CollectionSnapshot<TKey, TIdentity, TState> before,
        CollectionSnapshot<TKey, TIdentity, TState> after,
        OrderedKeys<TKey, TIdentity, TState> keys,
        IReadOnlyList<ViewOperation<TKey>> operations,
        bool isReset,
        bool movesKeys,
        bool changesMembership,
        bool reordersOnly,
        IEqualityComparer<TKey> keyEqualityComparer)
    {
        this.Before = before;
        this.After = after;
        this.Keys = keys;
        this.Operations = operations;
        this.IsReset = isReset;
        this.MovesKeys = movesKeys;
        this.ChangesMembership = changesMembership;
        this.ReordersOnly = reordersOnly;
        this.KeyEqualityComparer = keyEqualityComparer;
    }

    /// <summary>The store as this transaction left it.</summary>
    /// <remarks>
    ///     This is the store and not the content of this view, which is <see cref="Keys" />. It
    ///     comes with <see cref="Before" />, which is the same store from before the transaction.
    ///     Thus, a delta across a value of an item needs no other data.
    /// </remarks>
    public CollectionSnapshot<TKey, TIdentity, TState> After { get; }

    /// <summary>The store as this transaction found it.</summary>
    /// <remarks>
    ///     This is the same instance as the <see cref="After" /> of the previous change. Thus, a
    ///     listener on a sequence of these changes keeps no more memory than a listener on their
    ///     <see cref="After" /> alone. A transaction that changes only a criteria does not change
    ///     the store, and this field and <see cref="After" /> are then the same object.
    /// </remarks>
    public CollectionSnapshot<TKey, TIdentity, TState> Before { get; }

    /// <summary>This stage's keys after the change.</summary>
    public OrderedKeys<TKey, TIdentity, TState> Keys { get; }

    /// <summary>The operations to apply, in order, to the previous key list.</summary>
    public IReadOnlyList<ViewOperation<TKey>> Operations { get; }

    /// <summary>
    ///     The stage built its keys again and did not change them incrementally.
    ///     <see cref="Operations" /> is empty. Read all of <see cref="Keys" />.
    /// </summary>
    /// <remarks>
    ///     A stage resets at a change of its order, and it always resets then and never reports
    ///     moves. It also resets at a change of the limits of its window, at a change that is too
    ///     large for a list of operations, and at a reset in the stage above it. A change of the
    ///     predicate alone comes as the adds and the removals from it, and it is a reset only when
    ///     it is that large.
    /// </remarks>
    public bool IsReset { get; }

    /// <summary>Whether this change alters what the view holds, or the order it holds it in.</summary>
    internal bool MovesKeys { get; }

    /// <summary>Whether this change alters what the view holds, rather than only where.</summary>
    /// <remarks>
    ///     A change of order is not a change of the members, thus a cell on the shape sends no
    ///     value at one.
    /// </remarks>
    internal bool ChangesMembership { get; }

    /// <summary>
    ///     True when this is a reset that changed only the order. The stage holds the keys that it
    ///     held, and no value of those keys changed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A sort reports this at a new order, when no other change came to it in the
    ///         transaction. A stage below, with no change of its own criteria, then keeps its
    ///         content and does not build again from the stage above. A filter moves its members to
    ///         the new order and does not test its predicate again, because no value that changes
    ///         the answer changed. A sort keeps its list, because its members and its own order did
    ///         not change. The two stages report the same change, thus the next stage below can do
    ///         the same.
    ///     </para>
    ///     <para>
    ///         A window cannot do that. A change of the order above it changes the keys in the
    ///         window, thus a slice builds again and reports a usual reset, and the stages below it
    ///         also build again.
    ///     </para>
    /// </remarks>
    internal bool ReordersOnly { get; }

    internal IEqualityComparer<TKey> KeyEqualityComparer { get; }

    /// <summary>
    ///     The result of this change for one key, or nothing when the change has no result for
    ///     it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the equivalent, for a view, of the projection for one item on the root. A
    ///         cell for one item on a view can thus use this stream, and not a lift against the
    ///         keys of the view. An observer from this stream is a stream node that
    ///         removes itself when the change does not name its key. An observer from a lift is a
    ///         cell node that the graph reads at each change of the view, and measurements
    ///         show approximately four times the cost.
    ///     </para>
    ///     <para>
    ///         A reset has no operations, because each position can be different. Thus, this code
    ///         calculates the answer again from the store, and only for a key that one of the two
    ///         sides holds.
    ///     </para>
    /// </remarks>
    internal MaybeInternal<TProjected> ProjectChangeFor<TProjected>(
        TKey key,
        Func<TState, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        if (this.IsReset)
        {
            return this.Before.ContainsKey(key) || this.After.ContainsKey(key)
                ? this.Project(key: key, onPresent: onPresent, onAbsent: onAbsent)
                : MaybeInternal<TProjected>.None;
        }

        // This code uses an index and not an enumeration. Operations has an interface type, thus
        // a foreach boxes an enumerator one time for each observer and for each change. This method
        // keeps that cost low. A LINQ query also boxes one.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (int index = 0; index < this.Operations.Count; index++)
        {
            if (this.KeyEqualityComparer.Equals(x: this.Operations[index].Key, y: key))
            {
                return this.Project(key: key, onPresent: onPresent, onAbsent: onAbsent);
            }
        }

        return MaybeInternal<TProjected>.None;
    }

    /// <summary>
    ///     The result of this change for the identity of one key, as this view reads it, or
    ///     nothing.
    /// </summary>
    /// <remarks>
    ///     An update is a new state and a move is a new position. The two are not a new identity,
    ///     thus an observer of the identity sends no value for them. The identity changes when the
    ///     key enters this view or leaves it. On a view, and not on the collection, that also
    ///     occurs when a criteria gives a different answer for an item that no edit changed.
    /// </remarks>
    internal MaybeInternal<TProjected> ProjectIdentityChangeFor<TProjected>(
        TKey key,
        Func<TIdentity, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        if (this.IsReset)
        {
            return this.Before.ContainsKey(key) || this.After.ContainsKey(key)
                ? this.ProjectIdentity(key: key, onPresent: onPresent, onAbsent: onAbsent)
                : MaybeInternal<TProjected>.None;
        }

        // This code uses an index and not an enumeration, for the cause in the projection
        // above.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (int index = 0; index < this.Operations.Count; index++)
        {
            ViewOperation<TKey> operation = this.Operations[index];

            if (operation is ViewUpdate<TKey> or ViewMove<TKey>
                || !this.KeyEqualityComparer.Equals(x: operation.Key, y: key))
            {
                continue;
            }

            return this.ProjectIdentity(key: key, onPresent: onPresent, onAbsent: onAbsent);
        }

        return MaybeInternal<TProjected>.None;
    }

    /// <summary>
    ///     The result of this change for the two parts of one key together, as this view reads
    ///     them, or nothing.
    /// </summary>
    /// <remarks>
    ///     This takes each operation that names the key, as the state projection does, because an
    ///     item holds the state. An update and a move thus send a value from one of these, and an
    ///     observer of the identity alone sleeps through the two.
    /// </remarks>
    internal MaybeInternal<TProjected> ProjectItemChangeFor<TProjected>(
        TKey key,
        Func<Item<TIdentity, TState>, TProjected> onPresent,
        Func<TProjected> onAbsent)
    {
        if (this.IsReset)
        {
            return this.Before.ContainsKey(key) || this.After.ContainsKey(key)
                ? this.ProjectItem(key: key, onPresent: onPresent, onAbsent: onAbsent)
                : MaybeInternal<TProjected>.None;
        }

        // This code uses an index and not an enumeration, for the cause in the projection above.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (int index = 0; index < this.Operations.Count; index++)
        {
            if (this.KeyEqualityComparer.Equals(x: this.Operations[index].Key, y: key))
            {
                return this.ProjectItem(key: key, onPresent: onPresent, onAbsent: onAbsent);
            }
        }

        return MaybeInternal<TProjected>.None;
    }

    /// <summary>The key's two parts as this change left them, or their absence.</summary>
    private MaybeInternal<TProjected> ProjectItem<TProjected>(
        TKey key,
        Func<Item<TIdentity, TState>, TProjected> onPresent,
        Func<TProjected> onAbsent) =>
        MaybeInternal.Some(
            this.After.TryGetItem(key: key, item: out Item<TIdentity, TState>? item)
                ? onPresent(item)
                : onAbsent());

    /// <summary>The key's identity as this change left it, or its absence.</summary>
    private MaybeInternal<TProjected> ProjectIdentity<TProjected>(
        TKey key,
        Func<TIdentity, TProjected> onPresent,
        Func<TProjected> onAbsent) =>
        MaybeInternal.Some(
            this.After.TryGetIdentity(key: key, identity: out TIdentity? identity)
                ? onPresent(identity)
                : onAbsent());

    /// <summary>This change as keyed deltas, which is what an item change is.</summary>
    /// <remarks>
    ///     This changes the shape of the data and does not calculate it again. A view change names
    ///     the keys that entered, the keys that left, and the keys that changed, and it holds the
    ///     store on the two sides for their values. A reset names no key, thus this code reads the
    ///     current content and the previous content of the view. That cost is the size of the view
    ///     and not the size of the collection, and it occurs only at a change of criteria.
    /// </remarks>
    internal ItemChange<TKey, TIdentity, TState> ToItemChange(IEqualityComparer<TKey> keyEqualityComparer)
    {
        HashSet<TKey> added = new(keyEqualityComparer);
        HashSet<TKey> removed = new(keyEqualityComparer);
        Dictionary<TKey, TState> newStates = new(keyEqualityComparer);

        if (this.IsReset)
        {
            foreach (TKey key in this.Keys)
            {
                if (!this.Before.ContainsKey(key))
                {
                    added.Add(key);
                }

                if (this.After.States.TryGetState(key: key, state: out TState? state))
                {
                    newStates[key] = state;
                }
            }

            foreach (TKey key in this.Before.Identities.Keys.Where(key => !this.After.ContainsKey(key)))
            {
                removed.Add(key);
            }

            return new ItemChange<TKey, TIdentity, TState>(
                before: this.Before,
                after: this.After,
                newStates: newStates,
                added: added,
                removed: removed);
        }

        // ReSharper disable once ForCanBeConvertedToForeach
        for (int index = 0; index < this.Operations.Count; index++)
        {
            ViewOperation<TKey> operation = this.Operations[index];

            switch (operation)
            {
                case ViewInsert<TKey>:
                    added.Add(operation.Key);

                    break;

                case ViewRemove<TKey>:
                    removed.Add(operation.Key);

                    continue;

                case ViewUpdate<TKey>:
                case ViewMove<TKey>:
                    break;

                default:
                    continue;
            }

            if (this.After.States.TryGetState(key: operation.Key, state: out TState? state))
            {
                newStates[operation.Key] = state;
            }
        }

        return new ItemChange<TKey, TIdentity, TState>(
            before: this.Before,
            after: this.After,
            newStates: newStates,
            added: added,
            removed: removed);
    }

    /// <summary>The key's value as this change left it, or its absence.</summary>
    private MaybeInternal<TProjected> Project<TProjected>(
        TKey key,
        Func<TState, TProjected> onPresent,
        Func<TProjected> onAbsent) =>
        MaybeInternal.Some(
            this.After.TryGetHalves(key: key, identity: out _, state: out TState? state)
                ? onPresent(state)
                : onAbsent());
}

/// <summary>One change to a view's key list.</summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
[PublicAPI]
public abstract class ViewOperation<TKey>
    where TKey : notnull
{
    private protected ViewOperation(TKey key) => this.Key = key;

    /// <summary>The key this operation concerns.</summary>
    public TKey Key { get; }
}

/// <summary>The key entered the view at <see cref="Index" />.</summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class ViewInsert<TKey> : ViewOperation<TKey>
    where TKey : notnull
{
    internal ViewInsert(TKey key, int index)
        : base(key) =>
        this.Index = index;

    /// <summary>The position the key was inserted at.</summary>
    public int Index { get; }
}

/// <summary>The key left the view from <see cref="Index" />.</summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class ViewRemove<TKey> : ViewOperation<TKey>
    where TKey : notnull
{
    internal ViewRemove(TKey key, int index)
        : base(key) =>
        this.Index = index;

    /// <summary>The position the key was removed from.</summary>
    public int Index { get; }
}

/// <summary>
///     The key stayed and its position changed. It is equivalent to a removal at
///     <see cref="FromIndex" /> and then an add at <see cref="ToIndex" />. This code reports one
///     operation, thus a bound list can move the row and keep its selection.
/// </summary>
/// <remarks>
///     <para>
///         A move comes only from a change to the value of the key, in an order that reads that
///         value. The move is the sort, and no <see cref="ViewUpdate{TKey}" /> comes with it. A
///         consumer that makes a delta must read this as an update that also moved the key, and a
///         stage below must sort on it.
///     </para>
///     <para>
///         A change of order is never a set of moves, at each count of the keys that move. It is
///         always a reset, with
///         <see cref="CollectionViewChange{TKey,TIdentity,TState}.IsReset" /> set. The stages use
///         that rule. A filter keeps the order of its upstream collection and sorts a key that
///         moved in the order that it holds. Thus, a move from a new order leaves the filter in the
///         previous order. Each stage below also reads a move as a change of value: a stage sorts
///         the key again, and a cell for one item sends a value. For a change of order alone, that
///         is work with no result.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class ViewMove<TKey> : ViewOperation<TKey>
    where TKey : notnull
{
    internal ViewMove(TKey key, int fromIndex, int toIndex)
        : base(key)
    {
        this.FromIndex = fromIndex;
        this.ToIndex = toIndex;
    }

    /// <summary>The position the key moved from.</summary>
    public int FromIndex { get; }

    /// <summary>The position the key moved to.</summary>
    public int ToIndex { get; }
}

/// <summary>
///     The item at <see cref="Index" /> changed, and it did not enter the view, go out of the
///     view, or move.
/// </summary>
/// <remarks>
///     This operation makes a chain possible. A filter below, and a sort below, must test an item
///     again when its state changed, also when the stage above saw no change of position. This
///     operation gives that message. A UI consumer can ignore it, because the <c>StateCell</c> of
///     the row reports the value.
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class ViewUpdate<TKey> : ViewOperation<TKey>
    where TKey : notnull
{
    internal ViewUpdate(TKey key, int index)
        : base(key) =>
        this.Index = index;

    /// <summary>The position of the item that changed.</summary>
    public int Index { get; }
}
