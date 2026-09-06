using System;
using System.Collections.Generic;

namespace SodaFlow;

// A view over a node's cached listener snapshot.
//
// A readonly struct carrying its own GetEnumerator rather than an IReadOnlyList<Target>:
// foreach binds to this GetEnumerator before it ever considers IEnumerable<T>, so walking a
// snapshot compiles to an indexed loop over the array and allocates nothing. Through the
// interface each walk allocated an enumerator, and Send walks the listener set on every
// single firing - the same accounting that already collapsed SendEntry's closure.
//
// It also leaves callers no way to mutate what they are handed. The array underneath is
// cached and shared by every walker, so a write operation through it would corrupt the
// listener set for all of them rather than spoiling a private copy.
//
// That safety is free, so do not be tempted to strip this wrapper and hand out the bare
// Target[] for speed - it was measured, and there is nothing to win. One sink feeding eight
// mapped streams, each listened to, over 200,000 sends: 2312 bytes/send through
// IReadOnlyList, 2024 through this struct, and 2024 through a raw Target[] - identical to
// the byte, because foreach over either compiles to the same indexed loop. Times were 1319,
// 1252-1274 and 1292 ns/send, where repeat runs of one variant differed by more than the
// variants differed from each other. The whole 288 byte saving is the interface's enumerator,
// nine of them per send here, one per stream that fires.
internal readonly struct TargetSnapshot<TTarget>
    where TTarget : Node.Target
{
    private readonly TTarget[] targets;

    internal TargetSnapshot(TTarget[] targets) => this.targets = targets;

    // default(TargetSnapshot<>) is the stale marker, taking over the role the null array
    // reference used to play. A snapshot wrapping an empty array is valid and not stale.
    internal bool IsStale => this.targets == null;

    internal int Count => this.targets?.Length ?? 0;

    // Node<T> holds Node<T>.Target[] while Node's accessor is declared in terms of
    // Node.Target. Arrays are covariant and nothing ever writes through the reference, so the
    // same array is simply rewrapped. A struct, unlike IReadOnlyList<out T>, is invariant and
    // cannot make that conversion implicitly.
    // ReSharper disable once CoVariantArrayConversion - This is fine since the elements of this.targets are only read.
    internal TargetSnapshot<Node.Target> AsBaseTargets() => new(this.targets);

    public Enumerator GetEnumerator() => new(this.targets);

    // Public members because the foreach pattern requires them, on an internal type so
    // nothing widens the assembly's surface.
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

    // Fine-grained lock that protects listeners and nodes.
    protected static readonly object ListenersLock = new();

    internal static readonly object NodeRanksLock = new();

    // Allocated on first use: every stream owns a node, but a node only needs this once work is
    // actually queued against it, and the terminal node of a chain never has any.
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

    // Returns the targets themselves rather than projecting out their nodes, so that walking
    // them does not allocate a LINQ iterator per node visited during a rerank cascade.
    //
    // A rerank does hold ListenersLock for its whole cascade, so it could iterate the live
    // HashSet with its struct enumerator and skip the snapshot altogether. That was tried and
    // measured flat - fan-outs of 200 and 1000, chains of 200 and 1000, allocating the same
    // to the byte in three of the four. The snapshot is already free here: a node with no
    // listeners gets the shared NoListeners array, and a cascade during construction walks
    // exactly those freshly created nodes. Against no saving it would add a virtual call per
    // node visited, force EnsureBiggerThanRecursive to become protected, and make "nothing
    // mutates the listener set mid-cascade" load-bearing - today the snapshot means a walker
    // would simply see an older view rather than throw.
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

    // Allocated on first link. A HashSet is not cheap, and the last node in a chain never has
    // anything linked to it.
    private HashSet<Target>? listeners;
    private int listenersCapacity;

    // Snapshot of listeners, rebuilt lazily. Send walks the listener set on every single
    // firing while the set itself only changes when the graph is wired up or a dead weak
    // reference is reaped, so without this every firing allocated a fresh array.
    // A default snapshot means stale; all mutations below reset it under ListenersLock.
    private TargetSnapshot<Target> listenersSnapshot;

    internal Node()
    {
    }

    private Node(int rank)
        : base(rank)
    {
    }

    /// <summary>
    ///     Link an action and a target node to this node.
    /// </summary>
    /// <param name="trans">The current transaction.</param>
    /// <param name="action">The action to link to this node.</param>
    /// <param name="target">The target node to link to this node.</param>
    /// <returns>
    ///     A tuple containing whether changes were made to the node rank
    ///     and the <see cref="Target" /> object created for this link.
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

            // HashSet does not reclaim space after items are removed, so we will create a new one if we can reclaim a substantial amount of space
            if (this.listenersCapacity > 100 && this.listeners.Count < this.listenersCapacity / 2)
            {
                this.listeners = [.. this.listeners];
                this.listenersCapacity = this.listeners.Count;
            }
        }
    }

    // Callers must hold ListenersLock. TargetSnapshot hands out no way to mutate the array,
    // and each snapshot is immutable once built, so a caller that is still walking an older
    // one after an invalidation simply sees the listener set as of when it started - exactly
    // the semantics the previous copy-per-call gave.
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
