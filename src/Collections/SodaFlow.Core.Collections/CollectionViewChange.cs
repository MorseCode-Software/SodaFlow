using System.Collections.Generic;
using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     What one stage of a view chain did in a transaction. This is both the public change
///     notification and the protocol between stages.
/// </summary>
/// <remarks>
///     <para>
///         The event carries its resulting <see cref="Keys" /> and the <see cref="Snapshot" /> it
///         was computed against, rather than leaving the next stage to sample them. A downstream
///         stage runs inside the same transaction, where sampling a cell still yields the
///         pre-transaction value — so passing the results along the event is the only way the chain
///         stays consistent.
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
        CollectionSnapshot<TKey, TIdentity, TState> snapshot,
        IOrderedKeys<TKey, TIdentity, TState> keys,
        IReadOnlyList<ViewOperation<TKey>> operations,
        bool isReset)
    {
        this.Snapshot = snapshot;
        this.Keys = keys;
        this.Operations = operations;
        this.IsReset = isReset;
    }

    /// <summary>The collection as of this transaction.</summary>
    public CollectionSnapshot<TKey, TIdentity, TState> Snapshot { get; }

    /// <summary>This stage's keys after the change.</summary>
    public IOrderedKeys<TKey, TIdentity, TState> Keys { get; }

    /// <summary>The operations to apply, in order, to the previous key list.</summary>
    public IReadOnlyList<ViewOperation<TKey>> Operations { get; }

    /// <summary>
    ///     The stage rebuilt rather than adjusted — its predicate, ordering, or limit changed, or
    ///     its upstream reset. <see cref="Operations" /> is empty; read <see cref="Keys" />
    ///     wholesale.
    /// </summary>
    public bool IsReset { get; }
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
