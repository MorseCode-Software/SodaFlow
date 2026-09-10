using System.Collections.Generic;

namespace SodaFlow.Collections;

/// <summary>
///     One stage of a chain: its own keys and order, everything else delegated to the store it
///     shares with every other stage over the same root.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class CollectionViewStage<TKey, TIdentity, TState> : IReactiveCollection<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    private readonly IReactiveCollection<TKey, TIdentity, TState> source;

    internal CollectionViewStage(
        IReactiveCollection<TKey, TIdentity, TState> source,
        Cell<IOrderedKeys<TKey, TIdentity, TState>> keysCell,
        Stream<CollectionViewChange<TKey, TIdentity, TState>> changesStream)
    {
        this.source = source;
        this.KeysCell = keysCell;
        this.ChangesStream = changesStream;
    }

    public Cell<IOrderedKeys<TKey, TIdentity, TState>> KeysCell { get; }

    public Stream<CollectionViewChange<TKey, TIdentity, TState>> ChangesStream { get; }

    public Cell<CollectionSnapshot<TKey, TIdentity, TState>> SnapshotCell => this.source.SnapshotCell;

    public ReactiveCollection<TKey, TIdentity, TState> Root => this.source.Root;
}

/// <summary>
///     The pre-transaction inputs a stage reads when the event it is processing does not carry a
///     newer value for them.
/// </summary>
internal sealed class StageContext<TKey, TIdentity, TState, TCriteria>
    where TKey : notnull
    where TIdentity : notnull
{
    internal StageContext(
        TCriteria criteria,
        IOrderedKeys<TKey, TIdentity, TState> upstreamKeys,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
    {
        this.Criteria = criteria;
        this.UpstreamKeys = upstreamKeys;
        this.Snapshot = snapshot;
    }

    internal TCriteria Criteria { get; }

    internal IOrderedKeys<TKey, TIdentity, TState> UpstreamKeys { get; }

    internal CollectionSnapshot<TKey, TIdentity, TState> Snapshot { get; }
}

/// <summary>
///     What reached a stage in one transaction: an upstream change, a new criteria, or both.
/// </summary>
internal sealed class StageInput<TKey, TIdentity, TState, TCriteria>
    where TKey : notnull
    where TIdentity : notnull
{
    internal StageInput(
        MaybeInternal<CollectionViewChange<TKey, TIdentity, TState>> change,
        MaybeInternal<TCriteria> criteria)
    {
        this.Change = change;
        this.Criteria = criteria;
    }

    internal MaybeInternal<CollectionViewChange<TKey, TIdentity, TState>> Change { get; }

    internal MaybeInternal<TCriteria> Criteria { get; }
}

/// <summary>What a stage produced in one transaction, before it becomes a change event.</summary>
internal sealed class StageResult<TKey, TIdentity, TState>
    where TKey : notnull
    where TIdentity : notnull
{
    internal StageResult(
        IOrderedKeys<TKey, TIdentity, TState> keys,
        IReadOnlyList<ViewOperation<TKey>> operations,
        bool isReset,
        CollectionSnapshot<TKey, TIdentity, TState> snapshot)
    {
        this.Keys = keys;
        this.Operations = operations;
        this.IsReset = isReset;
        this.Snapshot = snapshot;
    }

    internal IOrderedKeys<TKey, TIdentity, TState> Keys { get; }

    internal IReadOnlyList<ViewOperation<TKey>> Operations { get; }

    internal bool IsReset { get; }

    internal CollectionSnapshot<TKey, TIdentity, TState> Snapshot { get; }
}
