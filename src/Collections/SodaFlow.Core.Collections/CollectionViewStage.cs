using System.Collections.Generic;
using SodaFlow.Functional;

namespace SodaFlow.Collections;

/// <summary>
///     One stage of a chain: its own keys and order, everything else delegated to the store it
///     shares with every other stage over the same root.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class CollectionViewStage<TKey, TId, TState> : IFrpCollection<TKey, TId, TState>
    where TKey : notnull
    where TId : notnull
{
    private readonly IFrpCollection<TKey, TId, TState> source;

    internal CollectionViewStage(
        IFrpCollection<TKey, TId, TState> source,
        Cell<IOrderedKeys<TKey, TId, TState>> keysCell,
        Stream<CollectionViewChange<TKey, TId, TState>> changesStream)
    {
        this.source = source;
        this.KeysCell = keysCell;
        this.ChangesStream = changesStream;
    }

    public Cell<IOrderedKeys<TKey, TId, TState>> KeysCell { get; }

    public Stream<CollectionViewChange<TKey, TId, TState>> ChangesStream { get; }

    public Cell<CollectionSnapshot<TKey, TId, TState>> SnapshotCell => this.source.SnapshotCell;

    public Cell<Maybe<TState>> StateCell(TKey key) => this.source.StateCell(key);

    public Cell<Maybe<TId>> IdentityCell(TKey key) => this.source.IdentityCell(key);
}

/// <summary>
///     The pre-transaction inputs a stage reads when the event it is processing does not carry a
///     newer value for them.
/// </summary>
internal sealed class StageContext<TKey, TId, TState, TCriteria>
    where TKey : notnull
    where TId : notnull
{
    internal StageContext(
        TCriteria criteria,
        IOrderedKeys<TKey, TId, TState> upstreamKeys,
        CollectionSnapshot<TKey, TId, TState> snapshot)
    {
        this.Criteria = criteria;
        this.UpstreamKeys = upstreamKeys;
        this.Snapshot = snapshot;
    }

    internal TCriteria Criteria { get; }

    internal IOrderedKeys<TKey, TId, TState> UpstreamKeys { get; }

    internal CollectionSnapshot<TKey, TId, TState> Snapshot { get; }
}

/// <summary>
///     What reached a stage in one transaction: an upstream change, a new criteria, or both.
/// </summary>
internal sealed class StageInput<TKey, TId, TState, TCriteria>
    where TKey : notnull
    where TId : notnull
{
    internal StageInput(
        Maybe<CollectionViewChange<TKey, TId, TState>> change,
        Maybe<TCriteria> criteria)
    {
        this.Change = change;
        this.Criteria = criteria;
    }

    internal Maybe<CollectionViewChange<TKey, TId, TState>> Change { get; }

    internal Maybe<TCriteria> Criteria { get; }
}

/// <summary>What a stage produced in one transaction, before it becomes a change event.</summary>
internal sealed class StageResult<TKey, TId, TState>
    where TKey : notnull
    where TId : notnull
{
    internal StageResult(
        IOrderedKeys<TKey, TId, TState> keys,
        IReadOnlyList<ViewOperation<TKey>> operations,
        bool isReset,
        CollectionSnapshot<TKey, TId, TState> snapshot)
    {
        this.Keys = keys;
        this.Operations = operations;
        this.IsReset = isReset;
        this.Snapshot = snapshot;
    }

    internal IOrderedKeys<TKey, TId, TState> Keys { get; }

    internal IReadOnlyList<ViewOperation<TKey>> Operations { get; }

    internal bool IsReset { get; }

    internal CollectionSnapshot<TKey, TId, TState> Snapshot { get; }
}
