using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace SodaFlow;

// A view of the cached listener snapshot of a node.
//
// This is a readonly struct with its own GetEnumerator, and not an IReadOnlyList<Target>. A
// foreach statement binds to this GetEnumerator before it examines IEnumerable<T>. Thus, a read
// of a snapshot compiles to an indexed loop on the array and allocates nothing. Through the
// interface, each read allocated an enumerator, and Send reads the listener set on each firing.
// The same accounting removed the closure in SendEntry.
//
// This also prevents a change by a caller. The array below is cached and shared by each reader.
// Thus, a write through it can damage the listener set for all readers, and not a private
// copy.
//
// That safety has no cost. Do not remove this wrapper and give out the Target[] array for
// speed, because a measurement shows no gain. One sink sent to eight mapped streams, each with
// a listener, for 200,000 sends. That measured 2312 bytes for each send through IReadOnlyList,
// 2024 bytes through this struct, and 2024 bytes through a Target[] array. The last two are
// equal, because a foreach on each one compiles to the same indexed loop. The times were
// 1319ns, 1252ns to 1274ns, and 1292ns for each send. Repeated runs of one variant differed
// more than the variants differed from each other. The full saving of 288 bytes is the
// enumerator of the interface: nine enumerators for each send here, one for each stream that
// fires.
internal readonly struct TargetSnapshot<TTarget>
    where TTarget : Node.Target
{
    private readonly TTarget[] targets;

    internal TargetSnapshot(TTarget[] targets) => this.targets = targets;

    internal int Count => this.targets?.Length ?? 0;

    // Node<T> holds a Node<T>.Target[] array, but the accessor of Node uses Node.Target.
    // Arrays are covariant, and no code writes through the reference. Thus, this wraps the same
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

    // The one node for an entry at rank zero. Nothing links into it, thus its rank stays zero,
    // and an entry on it goes into no list. A value listener's initial value and a switch's
    // first selection queue on it. Before, each made a node of its own for that one entry. That
    // was a node, a list, and the list's array, for a rank that does not change.
    internal static readonly Node RankZero = new Node<UnitInternal>();

    // A small lock. It stops more than one thread from changing the listeners and the nodes.
    protected static readonly object ListenersLock = new();

    // The largest stack that a walk gives back to walkFrames. A chain of this depth or less then
    // allocates nothing for the stack of the walk. One deeper chain does not keep a large array for
    // the life of the process.
    private const int MaxWalkFramesToKeep = 1024;

    // The stack of the walk in EnsureDescendantsBiggerThan. Only code that holds ListenersLock
    // walks, thus one array serves each walk, and no two walks use it at the same time.
    private static Frame[] walkFrames = [];

    // SodaFlow allocates this on first use. Each stream owns a node, but a node needs this
    // only after SodaFlow queues work against it. The last node of a chain has no such
    // work.
    internal List<TransactionInternal.Entry>? Entries;

    // True while the node has a listener. Node<T> writes this while it holds ListenersLock. A
    // reader that holds a transaction can read it without the lock. See EnsureBiggerThan.
    protected bool HasListeners;

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

        // A node with no listeners has no descendants, and the walk visits nothing. The target
        // of a link in a chain is a new node with no listeners. Thus, this read saves a lock
        // for each link. The read is safe without the lock. Only Link adds a listener, each
        // Link runs in a transaction, and the transaction lock orders this read after that
        // write. An unlisten can clear the flag at the same time. A stale true then causes a
        // walk that finds nothing.
        if (!node.HasListeners)
        {
            return;
        }

        lock (ListenersLock)
        {
            EnsureDescendantsBiggerThan(trans: trans, originalNode: node);
        }
    }

    // A walk that goes deep first, with a stack of its own and not a recursion. The recursion used
    // one stack frame for each node in a chain. A chain of 10,000 nodes then stopped the process
    // with a stack overflow, and no code can catch that. This walk visits the nodes in the sequence
    // that the recursion used, and it reads the rank of the parent at the same point. Thus, each node
    // gets the same rank, and a cycle causes the same exception.
    private static void EnsureDescendantsBiggerThan(TransactionInternal trans, Node originalNode)
    {
        Frame root = new(node: originalNode, targets: originalNode.GetListenerTargetsUnsafe().GetEnumerator());
        Frame[] frames = walkFrames;
        int depth = 0;
        int usedFrames = 0;

        try
        {
            while (true)
            {
                ref Frame frame = ref depth == 0 ? ref root : ref frames[depth - 1];

                if (!frame.Targets.MoveNext())
                {
                    if (depth == 0)
                    {
                        return;
                    }

                    depth--;
                    continue;
                }

                Node node = frame.Targets.Current.Node;
                int limit = frame.Node.Rank;

                if (ReferenceEquals(objA: originalNode, objB: node))
                {
                    throw new DependencyCycleException();
                }

                if (node.Rank > limit)
                {
                    continue;
                }

                node.Rank = limit + 1;

                if (node.Entries != null)
                {
                    foreach (TransactionInternal.Entry e in node.Entries)
                    {
                        trans.AddRerankEntry(e);
                    }
                }

                if (depth == frames.Length)
                {
                    Array.Resize(array: ref frames, newSize: Math.Max(val1: 4, val2: depth * 2));
                }

                frames[depth] = new Frame(node: node, targets: node.GetListenerTargetsUnsafe().GetEnumerator());
                depth++;
                usedFrames = Math.Max(val1: usedFrames, val2: depth);
            }
        }
        finally
        {
            // A frame refers to a node. Thus, an array that keeps its frames keeps those nodes alive.
            Array.Clear(array: frames, index: 0, length: usedFrames);

            if (frames.Length <= MaxWalkFramesToKeep)
            {
                walkFrames = frames;
            }
        }
    }

    // This gives the targets and does not select their nodes. Thus, a read of them does not
    // allocate a LINQ iterator for each node in a rerank cascade.
    //
    // A rerank holds ListenersLock for its complete cascade. Thus, it could read the live HashSet
    // with the struct enumerator of that set and use no snapshot. A test of that measured no
    // difference, with fan-outs of 200 and 1000 and chains of 200 and 1000. Three of the four
    // tests allocated the same number of bytes. The snapshot has no cost here, because a node
    // with no listeners receives the shared NoListeners array, and a cascade during
    // construction reads only those new nodes. For no saving, that change adds a virtual
    // call for each node, make EnsureDescendantsBiggerThan protected, and make this condition
    // necessary: no code changes the listener set during a cascade. Today the snapshot lets a
    // reader see an older view, and the reader does not throw.
    protected abstract TargetSnapshot<Target> GetListenerTargetsUnsafe();

    // One level of the walk in EnsureDescendantsBiggerThan: a node, and the position in its targets.
    // The walk moves the enumerator through a ref, thus the field is not readonly.
    private struct Frame(in Node node, in TargetSnapshot<Target>.Enumerator targets)
    {
        public readonly Node Node = node;
        public TargetSnapshot<Target>.Enumerator Targets = targets;
    }

    public abstract class Target(Node node, bool isActivated)
    {
        public readonly Node Node = node;
        public bool IsActivated = isActivated;

        // True when the action of this link is reachable. The registry asks this of the first
        // attached link of a stream, and not a weak handle to the stream. See
        // StreamNode<T>.Attach.
        internal abstract bool IsAlive { get; }
    }
}

internal class Node<T> : Node
{
    public static readonly Node<T> Null = new(NullRank);

    private static readonly Target[] NoListeners = [];

    // A node holds its first listener in a field and allocates the set at the second link.
    // A HashSet is expensive, and a node in a chain has exactly one listener. After the
    // second link, the set holds all the listeners, and the field stays null.
    private Target? firstListener;
    private HashSet<Target>? listeners;
    private int listenersCapacity;

    // A snapshot of the listeners. SodaFlow builds it again when a caller asks for it. Send reads the
    // listener set on each firing, but the set changes only when SodaFlow connects the graph
    // or removes a dead weak reference. Without this snapshot, each firing allocated a new
    // array. A null array is stale, and an empty array is correct. Each write holds
    // ListenersLock and is a volatile write. GetListenersCopy reads it without the lock.
    private Target[]? listenersSnapshot;

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
    internal Target Link(TransactionInternal trans, object action, Node target)
    {
        Target t = new(action: action, source: this, node: target, isActivated: trans.ActivatedTargets);

        if (!trans.ActivatedTargets)
        {
            trans.AddTargetToActivate(t);
        }

        lock (ListenersLock)
        {
            if (this.listeners != null)
            {
                this.listeners.Add(t);
                this.listenersCapacity++;
            }
            else if (this.firstListener == null)
            {
                this.firstListener = t;
            }
            else
            {
                this.listeners = [this.firstListener, t];
                this.listenersCapacity = 2;
                this.firstListener = null;
            }

            this.HasListeners = true;
            Volatile.Write(location: ref this.listenersSnapshot, value: null);
        }

        // No lock on the ranks. Each caller holds a transaction, and a transaction holds the
        // transaction lock. Thus, no two walks run at the same time, and no code reads a rank on
        // a different thread. ListenersLock stays, because an unlisten can run with no
        // transaction open.
        EnsureBiggerThan(trans: trans, node: target, limit: this.Rank);

        return t;
    }

    // Send calls this for each stream that fires. Thus, a read of a current snapshot does not get
    // ListenersLock. Only a stale snapshot gets the lock, to build a new array. The volatile write
    // of a new array occurs after the writes of its elements. Thus, a reader that gets the array
    // also gets its elements. A Link occurs in a transaction, and the transaction lock orders it
    // before each subsequent Send. Thus, a Send sees each link. An unlisten can occur on a
    // different thread with no transaction. A Send can then read the previous array, as a Send
    // that got the lock immediately before the unlisten did. A queued SendEntry also delivers to a
    // target that an unlisten removes after the Send.
    internal TargetSnapshot<Target> GetListenersCopy()
    {
        if (Volatile.Read(location: ref this.listenersSnapshot) is { } snapshot)
        {
            return new TargetSnapshot<Target>(snapshot);
        }

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
                if (this.firstListener == target)
                {
                    this.firstListener = null;
                    this.HasListeners = false;
                    Volatile.Write(location: ref this.listenersSnapshot, value: null);
                }

                return;
            }

            this.listeners.Remove(target);
            this.HasListeners = this.listeners.Count > 0;
            Volatile.Write(location: ref this.listenersSnapshot, value: null);

            // A HashSet does not release space after a removal. Thus, make a new HashSet when
            // that releases a large quantity of space
            if (this.listenersCapacity > 100 && this.listeners.Count < this.listenersCapacity / 2)
            {
                this.listeners = [.. this.listeners];
                this.listenersCapacity = this.listeners.Count;
            }
        }
    }

    // A caller must hold ListenersLock. TargetSnapshot gives no method to change the array,
    // and each snapshot is immutable after SodaFlow builds it. Thus, a caller that
    // reads a previous snapshot after an invalidation sees the listener set from the start of
    // that read. The previous copy for each call gave the same behavior.
    private TargetSnapshot<Target> GetListenersSnapshotUnsafe()
    {
        if (this.listenersSnapshot is not { } snapshot)
        {
            if (this.listeners is { Count: > 0 })
            {
                snapshot = new Target[this.listeners.Count];
                this.listeners.CopyTo(snapshot);
            }
            else if (this.firstListener != null)
            {
                snapshot = [this.firstListener];
            }
            else
            {
                snapshot = NoListeners;
            }

            Volatile.Write(location: ref this.listenersSnapshot, value: snapshot);
        }

        return new TargetSnapshot<Target>(snapshot);
    }

    protected override TargetSnapshot<Node.Target> GetListenerTargetsUnsafe() =>
        this.GetListenersSnapshotUnsafe().AsBaseTargets();

    // A target is also the weak view of the listener that made its link. It holds the source
    // node, thus it can remove itself from that node. It holds the action through a weak
    // reference only, thus it does not keep the listener or the stream alive. Before, an
    // object of its own held the source node and the target for that, one for each link.
    //
    // The action is an IStreamAction<T> or an Action<TransactionInternal, T>, and the code that
    // calls it tests the type. See IStreamAction<T>.
    public new sealed class Target(
        in object action,
        in Node<T> source,
        in Node node,
        in bool isActivated)
        : Node.Target(node: node, isActivated: isActivated), IListenerWithWeakReference
    {
        public readonly WeakReference<object> Action = new(action);

        private readonly Node<T> source = source;

        internal override bool IsAlive => this.Action.TryGetTarget(out _);

        public void Unlisten() => this.source.RemoveListener(this);
    }
}

// The node of a stream. It holds the registry entry of that stream: a weak handle to the
// stream and the weak views of the listeners attached to the stream. The registry holds the
// node, and the sweep reads the handle from it. A node outlives its stream, because the
// targets of the nodes above it hold it. Thus, it can hold the entry. Before, an object of its
// own held the handle and the views, one for each stream. A node that no stream owns, such as
// the node of a gate, is a plain Node<T> and does not pay for the entry. The handle is weak,
// thus the registry does not keep a stream alive. Release frees it.
//
// The node registers on the first attach, and not when the stream is built. The registry
// exists to stop the attached listeners of a dead stream, which unlinks its node from the
// nodes above it. A stream with no attached listener has nothing to stop, and before, its entry
// cost a handle and a registry slot for nothing. The registry also holds the node, and with it
// each target below the node, from a collection to the sweep after it. For a stream with an
// attached listener, the entry held those targets before, through the views. For a sink or a
// stream that is only listened to, it held 40 bytes. A draft that registered each stream made
// each such stream survive a collection with its node. ListenStrong on one stream then cost
// 371 to 521 microseconds per thousand, and a sink 392 to 416. The lazy registration takes
// that back and more, because a sink then allocates no handle at all.
//
// The entry allocates no handle at all when the first attached listener is the target of a link,
// which is the case for each operator. The action of that link is an object that holds the
// stream, and the stream holds the action. Thus, the action is
// reachable when and only when the stream is, and the weak reference in the target answers for
// the stream. This is an invariant that no compiler checks: the first attached action of an
// operator's output must reference that output. Otherwise, the sweep releases the links of a
// live stream, and the stream stops with no message. Each action in StreamAction.cs and beside the operators
// holds its output, and the memory test AliveOperatorStreamsSurviveTheSweep holds each shape.
// A first listener that is not a target, such as the composite listener of a lift of a list, keeps
// the handle.
internal sealed class StreamNode<T> : Node<T>, StreamListenerManager.IEntry
{
    private GCHandle streamHandle;

    // The first attached listener sits in a field, because a stream in a chain has exactly one.
    // The list is for the second and later.
    private IListenerWithWeakReference? firstAttached;
    private List<IListenerWithWeakReference>? moreAttached;

    public bool IsStreamAlive =>
        this.firstAttached is Node.Target target
            ? target.IsAlive
            : this.streamHandle is { IsAllocated: true, Target: not null };

    // The stream is complete when this runs, because a caller attaches a listener to a stream
    // that exists. Thus, the registry cannot find an incomplete stream through the handle.
    internal void Attach(object stream, IListenerWithWeakReference listener)
    {
        if (this.firstAttached == null)
        {
            // The listener and the handle go in before the registration, and not after it. The
            // sweep reads them out of any lock after it finds the node in the registry. Before,
            // a sweep between the registration and the store found no listener and no handle,
            // and read the stream as dead. Its release then stopped the listener that the store
            // put there after that: the link of a live stream. The registration takes the
            // registry lock, which the sweep holds, thus the sweep sees the two stores.
            this.firstAttached = listener;

            if (listener is not Node.Target)
            {
                this.streamHandle = GCHandle.Alloc(value: stream, type: GCHandleType.Weak);
            }

            StreamListenerManager.Register(this);
        }
        else
        {
            this.moreAttached ??= [];
            this.moreAttached.Add(listener);
        }
    }

    // The sweep calls this after the garbage collector removed the stream. It stops the attached
    // listeners, which disconnects this node from the nodes above it.
    public void Release()
    {
        this.firstAttached?.Unlisten();
        this.firstAttached = null;

        if (this.moreAttached != null)
        {
            foreach (IListenerWithWeakReference l in this.moreAttached)
            {
                l.Unlisten();
            }

            this.moreAttached = null;
        }

        if (this.streamHandle.IsAllocated)
        {
            this.streamHandle.Free();
        }
    }
}
