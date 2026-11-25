using System;
using System.Collections.Generic;

namespace SodaFlow.Collections;

/// <summary>
///     Sends each change of a collection or a view stage only to the per-key
///     streams of the keys that the change names.
/// </summary>
/// <remarks>
///     <para>
///         Before, each per-key cell was a filter on the full change stream. Thus,
///         each change went to each per-key cell, and each cell tested the change
///         for its key. The cost of a change was in proportion to the number of
///         cells and not to the number of keys that it changed. A cell that nothing
///         reads also got each change until a collection and a sweep released it.
///     </para>
///     <para>
///         The router has one link on the change stream, and a dictionary from each
///         key to its per-key streams. The dictionary holds each per-key action
///         through the weak reference of its link on the anchor, thus it keeps no
///         cell alive. Each per-key action holds the
///         router, thus a cell keeps the router alive, as the filter kept the
///         change stream alive.
///     </para>
///     <para>
///         Each per-key stream links from an anchor stream on the node of the
///         router, and not from the change stream. The anchor never fires. The link
///         gives the per-key stream a rank above the router, and no work.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TChange">The type of the changes.</typeparam>
// ReSharper disable once InheritdocConsiderUsage - The summary says what the router does, which the base action does not.
internal abstract class KeyedRouter<TKey, TChange> : StreamAction<TChange>
    where TKey : notnull
{
    /// <summary>The smallest number of keys at which a registration prunes dead routes.</summary>
    private const int MinPruneThreshold = 64;

    private readonly Stream<TChange> anchor;

    /// <summary>
    ///     The routes of each key: the weak reference of its one route, or a
    ///     <see cref="Bucket" /> when the key has two or more. Most keys have one
    ///     cell. Thus, most keys cost no bucket and no list.
    /// </summary>
    private readonly Dictionary<TKey, object> buckets;

    /// <summary>The keys whose buckets a change emptied, for removal after the change.</summary>
    private readonly List<TKey> emptied = [];

    private readonly Stream<TChange> source;

    /// <summary>
    ///     A new value for each change. A bucket, or the one route of a key, records
    ///     the value of the last change that visited it. Thus, a key that a change
    ///     names two times gets one value.
    /// </summary>
    private long generation;

    private int pruneThreshold = MinPruneThreshold;

    /// <summary>
    ///     The number of changes that this router sent in the transaction of
    ///     <see cref="routedTransactionId" />.
    /// </summary>
    private int routedFirings;

    /// <summary>The transaction of the last change, or zero. No transaction has the id zero.</summary>
    private long routedTransactionId;

    private protected KeyedRouter(TransactionInternal trans, Stream<TChange> source, IEqualityComparer<TKey> comparer)
    {
        this.source = source;
        this.buckets = new Dictionary<TKey, object>(comparer);
        this.anchor = new Stream<TChange>(source.KeepListenersAlive);

        this.anchor.UnsafeAttachListener(
            source.Listen(target: this.anchor.Node, trans: trans, action: this, suppressEarlierFirings: false));
    }

    /// <summary>The number of keys that have a bucket. Tests read it.</summary>
    internal int KeyCount => this.buckets.Count;

    public override void Fire(TransactionInternal trans, TChange value)
    {
        if (trans.Id != this.routedTransactionId)
        {
            this.routedTransactionId = trans.Id;
            this.routedFirings = 0;
        }

        this.routedFirings++;

        if (this.buckets.Count == 0)
        {
            return;
        }

        this.generation++;

        if (this.NamedKeyCount(value) <= this.buckets.Count)
        {
            this.RouteNamedKeys(trans: trans, change: value);
        }
        else
        {
            // A change that names more keys than have buckets, such as a reset, visits each bucket. The
            // projection of each stream then tests its own key, as the filter did.
            foreach (KeyValuePair<TKey, object> pair in this.buckets)
            {
                this.RouteEntry(trans: trans, change: value, key: pair.Key, entry: pair.Value);
            }
        }

        // The removal is after the routes, because a removal during the enumeration above is not
        // permitted.
        // ReSharper disable once ForCanBeConvertedToForeach - The list is a field, and an index does not box an enumerator.
        for (int index = 0; index < this.emptied.Count; index++)
        {
            this.buckets.Remove(this.emptied[index]);
        }

        this.emptied.Clear();
    }

    /// <summary>
    ///     Makes the stream of one key. The caller holds the stream, through the hold
    ///     of a cell.
    /// </summary>
    /// <remarks>
    ///     A stream that a transaction makes after a change in the same transaction
    ///     gets that change again, as a filter on the change stream did.
    /// </remarks>
    internal Stream<TProjected> StreamFor<TProjected>(
        TransactionInternal trans,
        TKey key,
        Func<TChange, MaybeInternal<TProjected>> project)
    {
        Stream<TProjected> @out = new(this.anchor.KeepListenersAlive);
        PerKey<TProjected> perKey = new(router: this, @out: @out, project: project);

        @out.UnsafeAttachListener(
            this.anchor.Listen(target: @out.Node, trans: trans, action: perKey, suppressEarlierFirings: true));

        // The link on the anchor holds the action weakly. The router uses that reference, and does not
        // make a second one, because each weak reference is a handle that each collection examines.
        this.Register(key: key, route: ((Node<TChange>.Target)perKey.GetListenerWithWeakReference()).Action);

        if (this.routedTransactionId == trans.Id)
        {
            this.source.ReplayEarlierFirings(trans: trans, target: @out.Node, action: perKey, count: this.routedFirings);
        }

        return @out;
    }

    /// <summary>The number of keys that a change names, or <see cref="int.MaxValue" /> when it names no list of keys.</summary>
    private protected abstract int NamedKeyCount(TChange change);

    /// <summary>Calls <see cref="RouteKey" /> for each key that the change names. A key can occur two or more times.</summary>
    private protected abstract void RouteNamedKeys(TransactionInternal trans, TChange change);

    private protected void RouteKey(TransactionInternal trans, TChange change, TKey key)
    {
        if (this.buckets.TryGetValue(key: key, value: out object? entry))
        {
            this.RouteEntry(trans: trans, change: change, key: key, entry: entry);
        }
    }

    private void Register(TKey key, WeakReference<object> route)
    {
        this.Prune();

        if (!this.buckets.TryGetValue(key: key, value: out object? entry))
        {
            this.buckets.Add(key: key, value: route);

            return;
        }

        if (entry is Bucket bucket)
        {
            bucket.Routes.Add(route);

            return;
        }

        // The second route of a key makes its bucket. A dead first route goes, and the key keeps one.
        WeakReference<object> first = (WeakReference<object>)entry;
        this.buckets[key] = first.TryGetTarget(out _) ? new Bucket(first: first, second: route) : route;
    }

    private void RouteEntry(TransactionInternal trans, TChange change, TKey key, object entry)
    {
        if (entry is Bucket bucket)
        {
            this.RouteBucket(trans: trans, change: change, key: key, bucket: bucket);

            return;
        }

        if (!((WeakReference<object>)entry).TryGetTarget(out object? target))
        {
            this.emptied.Add(key);

            return;
        }

        Route route = (Route)target;

        if (route.Generation == this.generation)
        {
            return;
        }

        route.Generation = this.generation;
        route.RouteChange(trans: trans, change: change);
    }

    private void RouteBucket(TransactionInternal trans, TChange change, TKey key, Bucket bucket)
    {
        if (bucket.Generation == this.generation)
        {
            return;
        }

        bucket.Generation = this.generation;
        List<WeakReference<object>> routes = bucket.Routes;

        // The loop goes from the end, thus a removal does not move a route that the loop did not visit.
        for (int index = routes.Count - 1; index >= 0; index--)
        {
            if (routes[index].TryGetTarget(out object? route))
            {
                ((Route)route).RouteChange(trans: trans, change: change);
            }
            else
            {
                routes.RemoveAt(index);
            }
        }

        if (routes.Count == 0)
        {
            this.emptied.Add(key);
        }
    }

    /// <summary>
    ///     Removes the dead routes of each key when the number of keys reaches the
    ///     threshold. Then, it sets the threshold to two times the number of keys
    ///     that stay. <see cref="ProjectedCellCache{TKey, TProjected}" /> prunes with
    ///     the same procedure.
    /// </summary>
    /// <remarks>
    ///     A change removes the dead routes of each key that it visits. This removes
    ///     those of keys that no change names.
    /// </remarks>
    private void Prune()
    {
        if (this.buckets.Count < this.pruneThreshold)
        {
            return;
        }

        List<TKey> dead = [];

        foreach (KeyValuePair<TKey, object> pair in this.buckets)
        {
            if (pair.Value is Bucket bucket)
            {
                bucket.Routes.RemoveAll(static reference => !reference.TryGetTarget(out _));

                if (bucket.Routes.Count == 0)
                {
                    dead.Add(pair.Key);
                }
            }
            else if (!((WeakReference<object>)pair.Value).TryGetTarget(out _))
            {
                dead.Add(pair.Key);
            }
        }

        foreach (TKey key in dead)
        {
            this.buckets.Remove(key);
        }

        this.pruneThreshold = Math.Max(val1: MinPruneThreshold, val2: 2 * this.buckets.Count);
    }

    /// <summary>The routes of a key that has two or more.</summary>
    private sealed class Bucket(WeakReference<object> first, WeakReference<object> second)
    {
        internal readonly List<WeakReference<object>> Routes = [first, second];

        /// <summary>The generation of the last change that visited this bucket.</summary>
        internal long Generation = -1;
    }

    /// <summary>The per-key action, as the router sees it.</summary>
    // ReSharper disable once InheritdocConsiderUsage - The summary says what a route is, which the base action does not.
    private abstract class Route : StreamAction<TChange>
    {
        /// <summary>
        ///     The generation of the last change that visited this route, when it is
        ///     the one route of its key.
        /// </summary>
        internal long Generation = -1;

        internal abstract void RouteChange(TransactionInternal trans, TChange change);
    }

    /// <summary>The action of one per-key stream.</summary>
    // ReSharper disable once InheritdocConsiderUsage - The summary says what this action does, which the base action does not.
    private sealed class PerKey<TProjected>(
        in KeyedRouter<TKey, TChange> router,
        in Stream<TProjected> @out,
        in Func<TChange, MaybeInternal<TProjected>> project) : Route
    {
        private readonly Stream<TProjected> @out = @out;
        private readonly Func<TChange, MaybeInternal<TProjected>> project = project;

        /// <summary>
        ///     This holds the router, thus a cell keeps the router and its link alive.
        ///     The dictionary of the router holds this action weakly.
        /// </summary>
        // ReSharper disable once UnusedMember.Local - The field is a strong reference and nothing reads it.
        private readonly KeyedRouter<TKey, TChange> router = router;

        internal override void RouteChange(TransactionInternal trans, TChange change)
        {
            if (this.project(change).TryGetValue(out TProjected value))
            {
                this.@out.Send(trans: trans, a: value);
            }
        }

        // The anchor never fires. A replay of the changes of the current transaction comes here.
        public override void Fire(TransactionInternal trans, TChange value) =>
            this.RouteChange(trans: trans, change: value);
    }
}

/// <summary>The router of the per-key streams of a root collection.</summary>
// ReSharper disable once InheritdocConsiderUsage - The summary says what the router does, which the base action does not.
internal sealed class ItemChangeRouter<TKey, TIdentity, TState>(
    in TransactionInternal trans,
    in Stream<ItemChange<TKey, TIdentity, TState>> source,
    in IEqualityComparer<TKey> comparer)
    : KeyedRouter<TKey, ItemChange<TKey, TIdentity, TState>>(trans: trans, source: source, comparer: comparer)
    where TKey : notnull
    where TIdentity : notnull
{
    // The projections of ItemChange give a value only for a key in one of these three sets.
    private protected override int NamedKeyCount(ItemChange<TKey, TIdentity, TState> change) =>
        change.NewStates.Count + change.Removed.Count + change.Added.Count;

    private protected override void RouteNamedKeys(TransactionInternal trans, ItemChange<TKey, TIdentity, TState> change)
    {
        foreach (KeyValuePair<TKey, TState> pair in change.NewStates)
        {
            this.RouteKey(trans: trans, change: change, key: pair.Key);
        }

        foreach (TKey key in change.Removed)
        {
            this.RouteKey(trans: trans, change: change, key: key);
        }

        foreach (TKey key in change.Added)
        {
            this.RouteKey(trans: trans, change: change, key: key);
        }
    }
}

/// <summary>The router of the per-key streams of a view stage.</summary>
// ReSharper disable once InheritdocConsiderUsage - The summary says what the router does, which the base action does not.
internal sealed class ViewChangeRouter<TKey, TIdentity, TState>(
    in TransactionInternal trans,
    in Stream<CollectionViewChange<TKey, TIdentity, TState>> source,
    in IEqualityComparer<TKey> comparer)
    : KeyedRouter<TKey, CollectionViewChange<TKey, TIdentity, TState>>(trans: trans, source: source, comparer: comparer)
    where TKey : notnull
    where TIdentity : notnull
{
    // The projections of CollectionViewChange give a value for a key that an operation names. After a
    // reset, they give a value for each key before or after it. Thus, a reset names each key.
    private protected override int NamedKeyCount(CollectionViewChange<TKey, TIdentity, TState> change) =>
        change.IsReset ? int.MaxValue : change.Operations.Count;

    private protected override void RouteNamedKeys(
        TransactionInternal trans,
        CollectionViewChange<TKey, TIdentity, TState> change)
    {
        IReadOnlyList<ViewOperation<TKey>> operations = change.Operations;

        // ReSharper disable once ForCanBeConvertedToForeach - An index does not box an enumerator of the interface.
        for (int index = 0; index < operations.Count; index++)
        {
            this.RouteKey(trans: trans, change: change, key: operations[index].Key);
        }
    }
}
