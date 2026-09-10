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
        bool isReset)
    {
        this.Before = before;
        this.After = after;
        this.Keys = keys;
        this.Operations = operations;
        this.IsReset = isReset;
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
    ///     The stage rebuilt rather than adjusted — its predicate, ordering, or limit changed, or
    ///     its upstream reset. <see cref="Operations" /> is empty; read <see cref="Keys" />
    ///     wholesale.
    /// </summary>
    public bool IsReset { get; }

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
    ///         A move carries a position and no value, and a re-file pairs one with an update, so
    ///         the update is what answers and the move is skipped. A reset carries no operations at
    ///         all - every position may differ - so the answer is recomputed from the store, but
    ///         only for a key one side or the other holds.
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
                ? this.Project(key, onPresent, onAbsent)
                : MaybeInternal<TProjected>.None;
        }

        // Indexed rather than enumerated. Operations is an interface-typed list, so a foreach
        // boxes an enumerator - once per observer per change, which is exactly the traffic this
        // method exists to keep cheap. A LINQ query would box one too.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (int index = 0; index < this.Operations.Count; index++)
        {
            ViewOperation<TKey> operation = this.Operations[index];

            if (operation is ViewMove<TKey> ||
                !EqualityComparer<TKey>.Default.Equals(operation.Key, key))
            {
                continue;
            }

            return this.Project(key, onPresent, onAbsent);
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
                ? this.ProjectIdentity(key, onPresent, onAbsent)
                : MaybeInternal<TProjected>.None;
        }

        // Indexed rather than enumerated, for the reason the projection above is.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (int index = 0; index < this.Operations.Count; index++)
        {
            ViewOperation<TKey> operation = this.Operations[index];

            if (operation is ViewUpdate<TKey> or ViewMove<TKey> ||
                !EqualityComparer<TKey>.Default.Equals(operation.Key, key))
            {
                continue;
            }

            return this.ProjectIdentity(key, onPresent, onAbsent);
        }

        return MaybeInternal<TProjected>.None;
    }

    /// <summary>The key's identity as this change left it, or its absence.</summary>
    private MaybeInternal<TProjected> ProjectIdentity<TProjected>(
        TKey key,
        Func<TIdentity, TProjected> onPresent,
        Func<TProjected> onAbsent) =>
        MaybeInternal.Some(
            this.After.TryGetIdentity(key, out TIdentity identity)
                ? onPresent(identity)
                : onAbsent());

    /// <summary>Whether this change alters what the view holds, rather than only where.</summary>
    /// <remarks>
    ///     A reorder is not a membership change, which is what lets a shape cell sleep through one.
    /// </remarks>
    internal bool ChangesMembership
    {
        get
        {
            if (this.IsReset)
            {
                return true;
            }

            // Indexed rather than enumerated, for the reason the projections above are.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (int index = 0; index < this.Operations.Count; index++)
            {
                if (this.Operations[index] is ViewInsert<TKey> or ViewRemove<TKey>)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>This change as keyed deltas, which is what an item change is.</summary>
    /// <remarks>
    ///     A translation rather than a derivation: a view change already names the keys that
    ///     entered, left and changed, and carries the store on both sides to read their values
    ///     from. A reset names none of them, so it is answered by walking what the view holds and
    ///     held - which costs the view rather than the collection, and only when a criteria moves.
    /// </remarks>
    internal ItemChange<TKey, TIdentity, TState> ToItemChange()
    {
        HashSet<TKey> added = new();
        HashSet<TKey> removed = new();
        Dictionary<TKey, TState> newStates = new();

        if (this.IsReset)
        {
            foreach (TKey key in this.Keys)
            {
                if (!this.Before.ContainsKey(key))
                {
                    added.Add(key);
                }

                if (this.After.States.TryGetState(key, out TState state))
                {
                    newStates[key] = state;
                }
            }

            foreach (TKey key in this.Before.Identities.Keys.Where(key => !this.After.ContainsKey(key)))
            {
                removed.Add(key);
            }

            return new ItemChange<TKey, TIdentity, TState>(
                this.Before,
                this.After,
                newStates,
                added,
                removed);
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
                    break;

                // A move carries a position and no value, so it is nothing to a keyed delta.
                default:
                    continue;
            }

            if (this.After.States.TryGetState(operation.Key, out TState state))
            {
                newStates[operation.Key] = state;
            }
        }

        return new ItemChange<TKey, TIdentity, TState>(
            this.Before,
            this.After,
            newStates,
            added,
            removed);
    }

    /// <summary>The key's value as this change left it, or its absence.</summary>
    private MaybeInternal<TProjected> Project<TProjected>(
        TKey key,
        Func<TState, TProjected> onPresent,
        Func<TProjected> onAbsent) =>
        MaybeInternal.Some(
            this.After.TryGetHalves(key, out TIdentity _, out TState state)
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
