using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace SodaFlow;

/// <summary>
///     Manages transactions.
/// </summary>
internal sealed class TransactionInternal
{
    // These fields use [ThreadStatic] and not ThreadLocal<T>. Almost all public entry points
    // read them. A thread-static field is a TLS access with no table, but ThreadLocal<T>.Value uses a
    // generic slot table. This code does not use the other members of ThreadLocal, which are
    // Values, IsValueCreated, value factories, and disposal. Thus, the two are equivalent here.
    [ThreadStatic] private static TransactionInternal? localTransaction;

    [ThreadStatic] private static bool runningOnStartHooks;

    // A coarse lock. SodaFlow holds it for the complete transaction.
    //
    // The library gives one transaction at a time across the process. This is a guarantee and not
    // an accident of the implementation. It makes a transaction atomic across threads and keeps
    // the sequence of updates the same on each run. Thus, a caller needs no synchronization. A
    // smaller lock, or many locks, changes the threading behavior of the library, also if
    // that looks like a good correction for contention. The remarks on the public
    // SodaFlow.Transaction class give the guarantee and its results.
    private static readonly object TransactionLock = new();
    private static readonly List<Action> OnStartHooks = [];

    private static readonly EntryPriorityQueue PrioritizedQueue = new();

    private readonly bool hasParentTransaction;
    internal bool ActivatedTargets;
    internal int InCallback;

    private bool isElevated;
    private Queue<Action>? lastQueue;
    private bool obtainedLock;
    private Queue<Action<TransactionInternal>>? postQueue;

    // The actions to run if this transaction fails. See OnFailureInternal.
    private Queue<Action>? failureQueue;
    private HashSet<Entry>? rerankEntriesSet;

    private List<Action>? sampleQueue;

    // SodaFlow allocates all of these on first use and not in the constructor. It creates a
    // transaction for each send that is not already in one, and most transactions use only two
    // of these fields. Thus, eager allocation of all seven fields, and of the dictionary and the
    // set in particular, was most of the cost of an empty transaction.
    private List<Action<TransactionInternal>>? sendQueue;
    private Dictionary<int, Action<TransactionInternal>>? splitQueue;
    private List<Node.Target>? targetsToActivate;

    private TransactionInternal()
    {
    }

    private TransactionInternal(TransactionInternal deferredOwner)
    {
        this.DeferredOwner = deferredOwner;
        this.hasParentTransaction = true;
    }

    // The root transaction owns the post queue and the split queue. It shares them with the
    // child transactions that it creates as it closes. Thus, work that a deferred action defers
    // goes into the queue that the root still drains. A child reaches these queues through
    // deferredOwner and does not hold its own.
    private TransactionInternal DeferredOwner => field ?? this;

    /// <summary>
    ///     Tells you if there is a current transaction.
    /// </summary>
    /// <returns><code>true</code> if there is a current transaction, or <code>false</code>.</returns>
    internal static bool HasCurrentTransaction() => localTransaction != null;

    /// <summary>
    ///     Gives the current transaction, or <code>null</code>.
    /// </summary>
    /// <returns>The current transaction, or <code>null</code>.</returns>
    internal static TransactionInternal? GetCurrentTransaction() => localTransaction;

    internal static T RunImpl<T>(Func<T> f) => Apply((_, _) => f());

    internal static T Apply<T>(Func<TransactionInternal, bool, T> code)
    {
        TransactionInternal? transaction = localTransaction;

        T? returnValue = default;
        Exception? exception = null;
        TransactionInternal? newTransaction = transaction;

        try
        {
            bool createdNewTransaction = newTransaction == null;

            if (newTransaction == null)
            {
                newTransaction = new TransactionInternal();

                localTransaction = newTransaction;
            }

            EnsureElevated(newTransaction);

            returnValue = code(arg1: newTransaction, arg2: createdNewTransaction);
        }
        catch (Exception e)
        {
            exception = e;
        }

        try
        {
            try
            {
                if (transaction == null)
                {
                    newTransaction?.Close();
                }
            }
            catch (Exception e)
            {
                if (exception == null)
                {
                    throw;
                }

                throw new AggregateException(exception, e);
            }

            if (exception != null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }

            // ReSharper disable once NullableWarningSuppressionIsUsed - returnValue must be non-null if exception
            // is null.
            return returnValue!;
        }
        finally
        {
            if (transaction == null)
            {
                if (newTransaction is { obtainedLock: true })
                {
                    Monitor.Exit(TransactionLock);
                }

                localTransaction = null;
            }
        }
    }

    private static void EnsureElevated(TransactionInternal? transaction)
    {
        if (transaction is { isElevated: false })
        {
            transaction.isElevated = true;

            if (!runningOnStartHooks)
            {
                if (!transaction.hasParentTransaction)
                {
                    Monitor.Enter(TransactionLock);
                    transaction.obtainedLock = true;
                }

                RunStartHooks(transaction);
            }
        }
    }

    internal static void OnStartImpl(Action action)
    {
        lock (TransactionLock)
        {
            OnStartHooks.Add(action);
        }
    }

    private static void RunStartHooks(TransactionInternal transaction)
    {
        if (OnStartHooks.Count > 0)
        {
            try
            {
                localTransaction = null;
                runningOnStartHooks = true;

                foreach (Action action in OnStartHooks)
                {
                    action();
                }
            }
            finally
            {
                localTransaction = transaction;
                runningOnStartHooks = false;
            }
        }
    }

    internal void Send(Action<TransactionInternal> action) => (this.sendQueue ??= []).Add(action);

    internal void AddTargetToActivate(Node.Target target) => (this.targetsToActivate ??= []).Add(target);

    internal void AddRerankEntry(Entry entry) => (this.rerankEntriesSet ??= []).Add(entry);

    internal void Prioritized(Node node, Action<TransactionInternal> action) =>
        this.Prioritized(new ActionEntry(node: node, action: action));

    // ReSharper disable once MemberCanBeMadeStatic.Global - This is static to discourage this method from being called
    // outside of a transaction.
    internal void Prioritized(Entry e)
    {
        lock (Node.NodeRanksLock)
        {
            PrioritizedQueue.Enqueue(e);
        }
    }

    internal void Sample(Action action) => (this.sampleQueue ??= []).Add(action);

    /// <summary>
    ///     Adds an action that runs after all prioritized actions.
    /// </summary>
    /// <param name="action">The action that runs after all prioritized actions.</param>
    internal void Last(Action action) => (this.lastQueue ??= new Queue<Action>()).Enqueue(action);

    /// <summary>
    ///     Add an action to run after all last actions.
    /// </summary>
    /// <param name="action">The action to run after all last actions.</param>
    private void Post(Action<TransactionInternal> action)
    {
        TransactionInternal owner = this.DeferredOwner;
        (owner.postQueue ?? (owner.postQueue = new Queue<Action<TransactionInternal>>())).Enqueue(action);
    }

    /// <summary>
    ///     Add an action to run after all last actions.
    /// </summary>
    /// <param name="index">The order index in which to run the action.</param>
    /// <param name="action">The action to run after all last actions.</param>
    internal void Split(int index, Action<TransactionInternal> action)
    {
        TransactionInternal owner = this.DeferredOwner;

        Dictionary<int, Action<TransactionInternal>> queue =
            owner.splitQueue ?? (owner.splitQueue = new Dictionary<int, Action<TransactionInternal>>());

        // If an entry is present, put the old entry and the new entry together.
        Action<TransactionInternal> @new;

        if (queue.TryGetValue(key: index, value: out Action<TransactionInternal>? existing))
        {
            @new = existing + action;
        }
        else
        {
            @new = action;
        }

        queue[index] = @new;
    }

    private static void PostInternal(Action<TransactionInternal> action) =>
        Apply((trans, createdNewTransaction) =>
        {
            if (createdNewTransaction)
            {
                action(trans);
            }
            else
            {
                trans.Post(action);
            }

            return UnitInternal.Value;
        });

    internal static void PostImpl(Action action) => PostInternal(_ => action());

    /// <summary>
    ///     Add an action to run if this transaction fails.
    /// </summary>
    /// <remarks>
    ///     The counterpart of <see cref="Post" />. A transaction that throws discards each action
    ///     that Post holds. Code can give a promise to something that is not in the graph, and keep
    ///     that promise in a posted action. Such code needs this method to release the promise. An
    ///     action here runs one time, in the failure of the transaction that owns the deferred work.
    ///     It runs before the queues of that transaction go. A throw from one of these actions does
    ///     not stop the actions after it, and it does not replace the exception of the transaction.
    ///     Where one action or more throws, the caller gets an AggregateException that holds the
    ///     exception of the transaction first, and then each of those.
    /// </remarks>
    /// <param name="action">The action to run if this transaction fails.</param>
    internal void OnFailureInternal(Action action)
    {
        TransactionInternal owner = this.DeferredOwner;
        (owner.failureQueue ?? (owner.failureQueue = new Queue<Action>())).Enqueue(action);
    }

    // If the priority queue holds entries when SodaFlow changes the rank of a node, SodaFlow must build the queue again to keep it correct.
    private void CheckRegen()
    {
        if (this.rerankEntriesSet == null)
        {
            return;
        }

        foreach (Entry entry in this.rerankEntriesSet)
        {
            PrioritizedQueue.ChangeRank(e: entry, newRank: entry.Node.Rank);
        }

        this.rerankEntriesSet.Clear();
    }

    private void Close()
    {
        try
        {
            EnsureElevated(this);

            if (this.targetsToActivate != null)
            {
                foreach (Node.Target target in this.targetsToActivate)
                {
                    target.IsActivated = true;
                }
            }

            this.ActivatedTargets = true;

            if (this.sendQueue != null)
            {
                // ReSharper disable once ForCanBeConvertedToForeach
                for (int i = 0; i < this.sendQueue.Count; i++)
                {
                    this.sendQueue[i](this);
                }

                this.sendQueue.Clear();
            }

            while (!PrioritizedQueue.IsEmpty() || this.sampleQueue?.Count > 0)
            {
                while (!PrioritizedQueue.IsEmpty())
                {
                    this.CheckRegen();

                    // ReSharper disable once NullableWarningSuppressionIsUsed - PrioritizedQueue.Dequeue() will not
                    // return null if PrioritizedQueue.IsEmpty() is false.
                    Entry e = PrioritizedQueue.Dequeue()!;
                    e.Execute(this);
                    e.Dispose();
                }

                List<Action>? sq = this.sampleQueue;
                this.sampleQueue = null;

                if (sq != null)
                {
                    foreach (Action s in sq)
                    {
                        s();
                    }
                }
            }

            while (this.lastQueue?.Count > 0)
            {
                this.lastQueue.Dequeue()();
            }

            if (!this.hasParentTransaction)
            {
                void ExecuteInNewTransaction(Action<TransactionInternal> action, bool runStartHooks)
                {
                    try
                    {
                        // The child defers into the queues of this transaction. Thus, a Post
                        // or a Split from inside a deferred action joins the drain that runs
                        // here, and does not stay on the child.
                        TransactionInternal transaction = new(this);

                        if (!runStartHooks)
                        {
                            // This prevents the start hooks from running.
                            transaction.isElevated = true;
                        }

                        localTransaction = transaction;

                        try
                        {
                            action(transaction);
                        }
                        finally
                        {
                            transaction.Close();
                        }
                    }
                    finally
                    {
                        localTransaction = this;
                    }
                }

                while (this.postQueue?.Count > 0 || this.splitQueue?.Count > 0)
                {
                    while (this.postQueue?.Count > 0)
                    {
                        ExecuteInNewTransaction(action: this.postQueue.Dequeue(), runStartHooks: true);
                    }

                    Dictionary<int, Action<TransactionInternal>>? sq = this.splitQueue;
                    this.splitQueue = null;

                    if (sq != null)
                    {
                        // This uses an array and not a List. SodaFlow copies the keys at
                        // their final size in the two conditions, thus a List adds only its own
                        // object to the backing array that it must allocate. Array.Sort is the
                        // same intro-sort that List.Sort calls, and a foreach on either one
                        // allocates nothing. At 4, 16, and 64 entries the List cost 32 more
                        // bytes each time, which is the size of the List.
                        //
                        // Do not use OrderBy(o => o.Key) to prevent the lookups below. It
                        // cannot sort without a copy. It makes a KeyValuePair buffer at sixteen
                        // bytes for each entry against four bytes here, an array of the
                        // extracted keys, an index map to keep the sort stable, and an
                        // enumerator class. That measured 384, 672, and 1824 bytes against 40,
                        // 88, and 280 bytes for the array, and was slower at each size: 106ns
                        // against 83ns at four entries, and 1639ns against 473ns at
                        // sixty-four. These lookups on int keys cost less than their
                        // prevention.
                        int[] splitIndexes = new int[sq.Count];
                        sq.Keys.CopyTo(array: splitIndexes, index: 0);
                        Array.Sort(splitIndexes);

                        foreach (int n in splitIndexes)
                        {
                            ExecuteInNewTransaction(action: sq[n], runStartHooks: false);
                        }
                    }
                }

                // The deferred work of this transaction has run by here. Thus, a failure after
                // this point has nothing left to release, and the actions go.
                this.failureQueue = null;
            }
        }
        catch (Exception transactionException)
        {
            // The failure actions come first. Each one releases something that the deferred work
            // of this transaction promised to release, and the queues below hold that work.
            Queue<Action>? failures = this.failureQueue;
            this.failureQueue = null;

            // A throw from one of these actions must not stop the actions after it, and it must not
            // replace the exception of the transaction. Thus, this keeps each one and the code at
            // the end of this block gives them all to the caller.
            List<Exception>? failureExceptions = null;

            while (failures?.Count > 0)
            {
                try
                {
                    failures.Dequeue()();
                }
                catch (Exception failureException)
                {
                    (failureExceptions ??= []).Add(failureException);
                }
            }

            // All of these become null and SodaFlow does not call Clear. The transaction
            // stops here, thus this releases the queues and not to empty them for a
            // second use. Clear keeps the list or the queue, and its backing array, at the
            // capacity that the failed transaction made. The scope of the parent holds a nested
            // transaction until the parent completes. Null is already the correct empty state,
            // because SodaFlow allocates each of these on first use and each reader tests
            // for null. Thus, no later code can fail on it.
            this.sendQueue = null;

            while (!PrioritizedQueue.IsEmpty())
            {
                // ReSharper disable once NullableWarningSuppressionIsUsed - PrioritizedQueue.Dequeue() will not
                // return null if PrioritizedQueue.IsEmpty() is false.
                Entry e = PrioritizedQueue.Dequeue()!;
                e.Dispose();
            }

            this.sampleQueue = null;

            this.lastQueue = null;

            this.postQueue = null;

            this.splitQueue = null;

            if (failureExceptions == null)
            {
                throw;
            }

            // The exception of the transaction comes first, because it is the failure that the
            // caller asked about. A throw from a failure action comes after it, and no code loses
            // it.
            failureExceptions.Insert(index: 0, item: transactionException);

            throw new AggregateException(failureExceptions);
        }
    }

    internal abstract class Entry : IDisposable
    {
        public readonly Node Node;
        public bool InPq;
        public Entry? PqNext;
        public Entry? PqPrev;
        public int PqRank;

        // The position of this entry in Node.Entries. Thus, removal needs no examination and no
        // move. A value of -1 means "not in the list". This also makes a second call to
        // Dispose do nothing, and prevents the removal of the entry that is now at that
        // position.
        private int nodeEntryIndex;

        protected Entry(Node node)
        {
            this.Node = node;
            this.PqRank = node.Rank;
            this.nodeEntryIndex = node.AddEntry(this);
        }

        public void Dispose()
        {
            int index = this.nodeEntryIndex;

            if (index < 0)
            {
                return;
            }

            this.nodeEntryIndex = -1;

            // Move the last entry into this position and do not move the entries after it.
            // SodaFlow reads Node.Entries only to put entries into rerankEntriesSet, which
            // is a HashSet. Thus, no code depends on the sequence. A wide fan-in also makes the
            // repeated RemoveAt(0) that this code replaces quadratic in the number of entries.
            // Cell.Lift on N cells is such a fan-in, because it links all N cells to one
            // node.
            // ReSharper disable once NullableWarningSuppressionIsUsed - Node.AddEntry() was called in the constructor
            // so this.Node.Entries cannot be null here.
            List<Entry> entries = this.Node.Entries!;
            int last = entries.Count - 1;

            if (index != last)
            {
                Entry moved = entries[last];
                entries[index] = moved;
                moved.nodeEntryIndex = index;
            }

            entries.RemoveAt(last);
        }

        // A subclass holds the state that the queued work needs, as fields. Thus, a caller on a
        // frequent path does not allocate a closure and a delegate and also the entry.
        public abstract void Execute(TransactionInternal trans);
    }

    // The general entry. Use it at a call site that runs one time for each construction, or
    // one time for each transaction, because such a site gets no benefit from the removal of the
    // delegate.
    private sealed class ActionEntry(Node node, Action<TransactionInternal> action)
        : Entry(node)
    {
        // ReSharper disable once ReplaceWithPrimaryConstructorParameter - This field is needed so action is not
        // captured into a mutable variable.
        private readonly Action<TransactionInternal> action = action;

        public override void Execute(TransactionInternal trans) => this.action(trans);
    }
}
