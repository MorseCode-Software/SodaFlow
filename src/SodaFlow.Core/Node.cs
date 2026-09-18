using System;
using System.Collections.Generic;

namespace SodaFlow;

// A view of the cached listener snapshot of a node.
//
// This is a readonly struct with its own GetEnumerator, and not an IReadOnlyList<Target>. A
// foreach statement binds to this GetEnumerator before it examines IEnumerable<T>. Thus a read
// of a snapshot compiles to an indexed loop on the array and allocates nothing. Through the
// interface, each read allocated an enumerator, and Send reads the listener set on each firing.
// The same accounting removed the closure in SendEntry.
//
// This also prevents a change by a caller. The array below is cached and shared by each reader.
// Thus a write through it can damage the listener set for all readers, and not a private
// copy.
//
// That safety has no cost. Do not remove this wrapper and give out the Target[] array for
// speed, because a measurement shows no gain. One sink sent to eight mapped streams, each with
// a listener, for 200,000 sends. That measured 2312 bytes for each send through IReadOnlyList,
// 2024 bytes through this struct, and 2024 bytes through a Target[] array. The last two are
// equal, because a foreach on either one compiles to the same indexed loop. The times were
// 1319ns, 1252ns to 1274ns, and 1292ns for each send. Repeated runs of one variant differed
// more than the variants differed from each other. The full saving of 288 bytes is the
// enumerator of the interface: nine enumerators for each send here, one for each stream that
// fires.
internal readonly struct TargetSnapshot<TTarget>
    where TTarget : Node.Target
{
    private readonly TTarget[] targets;

    internal TargetSnapshot(TTarget[] targets) => this.targets = targets;

    // A default(TargetSnapshot<>) marks a stale snapshot. The null array reference did this
    // before. A snapshot that holds an empty array is correct and is not stale.
    internal bool IsStale => this.targets == null;

    internal int Count => this.targets?.Length ?? 0;

    // Node<T> holds a Node<T>.Target[] array, but the accessor of Node uses Node.Target.
    // Arrays are covariant, and no code writes through the reference. Thus this wraps the same
    // array again. A struct is invariant and cannot make that conversion without a cast, but
    // IReadOnlyList<out T> can.
    // ReSharper disable once CoVariantArrayConversion - This is fine since the elements of this.targets are only read.
    internal TargetSnapshot<Node.Target> AsBaseTargets() => new(this.targets);

    public Enumerator GetEnumerator() => new(this.targets);

    // These members are public, because the foreach pattern needs them. The type is internal,
    // thus they do not make the surface of the assembly larger.
    internal struct Enumerator
    {
        private readonly TTarget[] targets;
        private int index;

        internal Enumerator(TTarget[] targets)
        {
            this.targets = targets;
            this.index = -1;
        }

        public readonly TTarget Current => this.targets[this.index];

        public bool MoveNext() => this.targets != null && ++this.index < this.targets.Length;
    }
}

internal abstract class Node
{
    public const int NullRank = int.MaxValue;

    // A small lock. It protects the listeners and the nodes.
    protected static readonly object ListenersLock = new();

    internal static readonly object NodeRanksLock = new();

    // SodaFlow allocates this on first use. Each stream owns a node, but a node needs this
    // only after SodaFlow queues work against it. The last node of a chain has no such
    // work.
    internal List<TransactionInternal.Entry>? Entries;

    internal int Rank;

    internal Node()
    {
    }

    protected Node(int rank) => this.Rank = rank;

    internal int AddEntry(TransactionInternal.Entry entry)
    {
        this.Entries ??= [];
        int index = this.Entries.Count;
        this.Entries.Add(entry);
        return index;
    }

    protected static void EnsureBiggerThan(TransactionInternal trans, Node node, int limit)
    {
        if (node.Rank > limit)
        {
            return;
        }

        node.Rank = limit + 1;

        if (node.Entries != null)
        {
            foreach (TransactionInternal.Entry e in node.Entries)
            {
                trans.AddRerankEntry(e);
            }
        }

        lock (ListenersLock)
        {
            foreach (Target t in node.GetListenerTargetsUnsafe())
            {
                EnsureBiggerThanRecursive(trans: trans, originalNode: node, node: t.Node, limit: node.Rank);
            }
        }
    }

    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
    private static void EnsureBiggerThanRecursive(
        TransactionInternal trans,
        Node originalNode,
        Node node,
        int limit)
    {
        if (ReferenceEquals(objA: originalNode, objB: node))
        {
            throw new Exception("A dependency cycle was detected.");
        }

        if (node.Rank > limit)
        {
            return;
        }

        node.Rank = limit + 1;

        if (node.Entries != null)
        {
            foreach (TransactionInternal.Entry e in node.Entries)
            {
                trans.AddRerankEntry(e);
            }
        }

        foreach (Target t in node.GetListenerTargetsUnsafe())
        {
            EnsureBiggerThanRecursive(trans: trans, originalNode: originalNode, node: t.Node, limit: node.Rank);
        }
    }

    // This gives the targets and does not select their nodes. Thus a read of them does not
    // allocate a LINQ iterator for each node in a rerank cascade.
    //
    // A rerank holds ListenersLock for its full cascade. Thus it could read the live HashSet
    // with the struct enumerator of that set and use no snapshot. A test of that measured no
    // difference, with fan-outs of 200 and 1000 and chains of 200 and 1000. Three of the four
    // tests allocated the same number of bytes. The snapshot has no cost here, because a node
    // with no listeners receives the shared NoListeners array, and a cascade during
    // construction reads only those new nodes. For no saving, that change would add a virtual
    // call for each node, make EnsureBiggerThanRecursive protected, and make this condition
    // necessary: no code changes the listener set during a cascade. Today the snapshot lets a
    // reader see an older view, and the reader does not throw.
    protected abstract TargetSnapshot<Target> GetListenerTargetsUnsafe();

    public abstract class Target(Node node, bool isActivated)
    {
        public readonly Node Node = node;
        public bool IsActivated = isActivated;
    }
}

internal sealed class Node<T> : Node
{
    public static readonly Node<T> Null = new(NullRank);

    private static readonly Target[] NoListeners = [];

    // SodaFlow allocates this at the first link. A HashSet is expensive, and no code links
    // to the last node in a chain.
    private HashSet<Target>? listeners;
    private int listenersCapacity;

    // A snapshot of the listeners. SodaFlow builds it again on demand. Send reads the
    // listener set on each firing, but the set changes only when SodaFlow connects the graph
    // or removes a dead weak reference. Without this snapshot, each firing allocated a new
    // array. A default snapshot is stale. Each change below resets it with ListenersLock held.
    private TargetSnapshot<Target> listenersSnapshot;

    internal Node()
    {
    }

    private Node(int rank)
        : base(rank)
    {
    }

    /// <summary>
    ///     Links an action and a target node to this node.
    /// </summary>
    /// <param name="trans">The current transaction.</param>
    /// <param name="action">The action to link to this node.</param>
    /// <param name="target">The target node to link to this node.</param>
    /// <returns>
    ///     A tuple. It tells you if the node rank changed, and it contains the
    ///     <see cref="Target" /> object for this link.
    /// </returns>
    internal Target Link(TransactionInternal trans, Action<TransactionInternal, T> action, Node target)
    {
        Target t = new(action: action, node: target, isActivated: trans.ActivatedTargets);

        if (!trans.ActivatedTargets)
        {
            trans.AddTargetToActivate(t);
        }

        lock (ListenersLock)
        {
            this.listeners ??= [];
            this.listeners.Add(t);
            this.listenersCapacity++;
            this.listenersSnapshot = default;
        }

        lock (NodeRanksLock)
        {
            EnsureBiggerThan(trans: trans, node: target, limit: this.Rank);
        }

        return t;
    }

    internal void Unlink(Target target) => this.RemoveListener(target);

    internal TargetSnapshot<Target> GetListenersCopy()
    {
        lock (ListenersLock)
        {
            return this.GetListenersSnapshotUnsafe();
        }
    }

    internal void RemoveListener(Target target)
    {
        lock (ListenersLock)
        {
            if (this.listeners == null)
            {
                return;
            }

            this.listeners.Remove(target);
            this.listenersSnapshot = default;

            // A HashSet does not release space after a removal. Thus make a new HashSet if that releases a substantial amount of space
            if (this.listenersCapacity > 100 && this.listeners.Count < this.listenersCapacity / 2)
            {
                this.listeners = [.. this.listeners];
                this.listenersCapacity = this.listeners.Count;
            }
        }
    }

    // A caller must hold ListenersLock. TargetSnapshot gives no method to change the array,
    // and each snapshot is immutable after SodaFlow builds it. Thus a caller that
    // reads an older snapshot after an invalidation sees the listener set from the start of
    // that read. The previous copy for each call gave the same behavior.
    private TargetSnapshot<Target> GetListenersSnapshotUnsafe()
    {
        if (this.listenersSnapshot.IsStale)
        {
            if (this.listeners == null || this.listeners.Count == 0)
            {
                this.listenersSnapshot = new TargetSnapshot<Target>(NoListeners);
            }
            else
            {
                Target[] snapshot = new Target[this.listeners.Count];
                this.listeners.CopyTo(snapshot);
                this.listenersSnapshot = new TargetSnapshot<Target>(snapshot);
            }
        }

        return this.listenersSnapshot;
    }

    protected override TargetSnapshot<Node.Target> GetListenerTargetsUnsafe() =>
        this.GetListenersSnapshotUnsafe().AsBaseTargets();

    public new sealed class Target(Action<TransactionInternal, T> action, Node node, bool isActivated)
        : Node.Target(node: node, isActivated: isActivated)
    {
        public readonly WeakReference<Action<TransactionInternal, T>> Action = new(action);
    }
}
