using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     What one stage of a view chain did in a transaction. This is both the public change
///     notification and the protocol between stages.
/// </summary>
/// <remarks>
///     <para>
///         The event carries its resulting <see cref="Keys" /> and the store on both sides of it,
///         <see cref="Before" /> and <see cref="After" />, rather than leaving the next stage to
///         sample them. A downstream stage runs inside the same transaction, where sampling a cell
///         still yields the pre-transaction value — so passing the results along the event is the
///         only way the chain stays consistent, and it is also what lets a consumer take a delta
///         without keeping its own copy of the previous values.
///     </para>
///     <para>
///         Operation indices are valid for a consumer applying them in order to the previous key
///         list.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TIdentity">The type of the immutable portion of an item.</typeparam>
/// <typeparam name="TState">The type of the mutable portion of an item.</typeparam>
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
    ///     The store, not this view's contents - those are <see cref="Keys" />. Paired with
    ///     <see cref="Before" />, which is the same store as the transaction found it, so a delta
    ///     over any value an item carries needs nothing kept alongside.
    /// </remarks>
    public CollectionSnapshot<TKey, TIdentity, TState> After { get; }

    /// <summary>The store as this transaction found it.</summary>
    /// <remarks>
    ///     The same instance as the previous change's <see cref="After" />, so following a sequence
    ///     of these retains no more than following their <see cref="After" /> alone would. A
    ///     transaction that changed only a criteria leaves the store alone, and then this and
    ///     <see cref="After" /> are the same object.
    /// </remarks>
    public CollectionSnapshot<TKey, TIdentity, TState> Before { get; }

    /// <summary>This stage's keys after the change.</summary>
    public OrderedKeys<TKey, TIdentity, TState> Keys { get; }

    /// <summary>The operations to apply, in order, to the previous key list.</summary>
    public IReadOnlyList<ViewOperation<TKey>> Operations { get; }

    /// <summary>
    ///     The stage rebuilt rather than adjusted. <see cref="Operations" /> is empty; read
    ///     <see cref="Keys" /> wholesale.
    /// </summary>
    /// <remarks>
    ///     A stage resets when its order changes - always, and never by reporting moves instead -
    ///     when its window's bounds change, when a change is too large for listing its operations
    ///     to be worth it, or when the stage above it reset. A predicate change on its own is
    ///     reported as the inserts and removes it causes, unless it is that large.
    /// </remarks>
    public bool IsReset { get; }

    /// <summary>Whether this change alters what the view holds, or the order it holds it in.</summary>
    internal bool MovesKeys { get; }

    /// <summary>Whether this change alters what the view holds, rather than only where.</summary>
    /// <remarks>
    ///     A reorder is not a membership change, which is what lets a shape cell sleep through one.
    /// </remarks>
    internal bool ChangesMembership { get; }

    /// <summary>
    ///     Whether this is a reset that changed nothing but the order: the stage holds the keys it held
    ///     before, and none of their values changed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         What a sort reports when it is handed a new order and nothing else in the transaction
    ///         reached it. A stage below with no criteria change of its own can then keep what it holds
    ///         rather than rebuilding from the stage above: a filter takes its members over to the new
    ///         order without testing its predicate again, since nothing that could change the answer
    ///         changed, and a sort keeps its list outright, since neither its members nor its own order
    ///         moved. Both report the same, so the next stage down can do likewise.
    ///     </para>
    ///     <para>
    ///         A window cannot. Reordering what is above it changes which keys fall inside it, so a
    ///         slice rebuilds and reports an ordinary reset, and the stages below it rebuild too.
    ///     </para>
    /// </remarks>
    internal bool ReordersOnly { get; }

    internal IEqualityComparer<TKey> KeyEqualityComparer { get; }

    /// <summary>
    ///     What this change means for one key, or nothing if it means nothing for it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The view equivalent of the root's per-item projection, and the reason a view's
    ///         per-item cell can hang off this stream rather than being lifted against the view's
    ///         keys. An observer built this way is a stream node that filters itself out when the
    ///         change did not touch its key; one built by lifting is a cell node the propagation
    ///         walks whenever the view moves at all, which measured about four times the cost.
    ///     </para>
    ///     <para>
    ///         A reset carries no operations at all - every position may differ - so the answer
    ///         is recomputed from the store, but only for a key one side or the other holds.
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

        // Indexed rather than enumerated. Operations is an interface-typed list, so a foreach
        // boxes an enumerator - once per observer per change, which is exactly the traffic this
        // method exists to keep cheap. A LINQ query would box one too.
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
    ///     What this change means for one key's identity as this view sees it, or nothing.
    /// </summary>
    /// <remarks>
    ///     An update is a new state and a move is a new position; neither is a new identity, so an
    ///     observer of the identity wakes for neither. What moves it is the key entering or leaving
    ///     this view - which, unlike on the collection, includes a criteria deciding differently
    ///     about an item the store never touched.
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

        // Indexed rather than enumerated, for the reason the projection above is.
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
    ///     A translation rather than a derivation: a view change already names the keys that
    ///     entered, left and changed, and carries the store on both sides to read their values
    ///     from. A reset names none of them, so it is answered by walking what the view holds and
    ///     held - which costs the view rather than the collection, and only when criteria moves.
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
///     The key stayed but its position changed. Equivalent to a remove at
///     <see cref="FromIndex" /> followed immediately by an insert at <see cref="ToIndex" />,
///     reported as one operation so a bound list can move the row and keep its selection.
/// </summary>
/// <remarks>
///     <para>
///         A move is only ever reported because the key's value changed, and the order reads that
///         value: the move is the re-file, and no <see cref="ViewUpdate{TKey}" /> accompanies it. A
///         consumer taking a delta has to treat this as an update that also moved, and a stage below
///         has to re-file on it.
///     </para>
///     <para>
///         A change of order is never reported as moves, however few keys it would move - it is
///         always a reset, with <see cref="CollectionViewChange{TKey,TIdentity,TState}.IsReset" />
///         set. Stages rely on that. A filter keeps its upstream collection's order and re-files a moved key
///         under the order it already holds, so a move caused by a new order would leave the filter
///         filed under the old one. And everything below treats a move as a changed value - a stage
///         re-files the key, a per-item cell fires - which for a pure reorder is work for nothing.
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
///     The item at <see cref="Index" /> changed, without entering, leaving, or moving.
/// </summary>
/// <remarks>
///     This is what makes chaining work. A downstream filter or sort has to re-test an item whose
///     state changed even when the stage above it saw no positional consequence, and this is how it
///     hears about it. A UI consumer can ignore it — the row's own <c>StateCell</c> already
///     reports the value.
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
