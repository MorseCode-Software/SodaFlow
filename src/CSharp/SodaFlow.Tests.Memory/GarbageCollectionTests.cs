using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Memory;

/// <summary>
///     Lifetime tests that run with plain <c>dotnet test</c>.
/// </summary>
/// <remarks>
///     <para>
///         The tests in <see cref="StreamTests" /> cover the same ground but count
///         live objects with dotMemory, so they are all <c>[Ignore]</c>d and never
///         run in CI. These assert the same invariants using only weak references
///         and the node's own listener set, so they actually guard the cleanup
///         machinery on each build.
///     </para>
///     <para>
///         Two things are deliberately not asserted. First, that a node stays
///         connected immediately after a collection:
///         <see cref="StreamListenerManager" /> unhooks nodes from a background
///         thread, thus that is a race. Second, which of the two cleanup paths did
///         the work. That is the background thread, or the lazy prune in
///         <c>Stream.Send</c> when a weak reference of the target died. What
///         matters is that the node becomes disconnected, thus these tests send a
///         value to force a deterministic outcome and check that.
///     </para>
/// </remarks>
public sealed class GarbageCollectionTests
{
    [Test]
    public async Task MappedStreamIsCollectedOnceDroppedAndUnlistened()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<string> @out = [];

        WeakReference mapped = CreateMappedStreamAndUnlisten(s: s, @out: @out);

        await Assert.That(@out)
            .IsEquivalentTo(expected: ["3"], ordering: CollectionOrdering.Matching)
            .Because("the mapped stream should have fired while it was listening");

        Collect();

        await Assert.That(mapped.IsAlive)
            .IsFalse()
            .Because("nothing should still root a mapped stream after the caller drops it and unlistens");
    }

    [Test]
    public async Task SourceNodeIsDisconnectedAfterDownstreamIsCollected()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<string> @out = [];

        await Assert.That(s.Node.GetListenersCopy().Count).IsEqualTo(0).Because("a fresh sink has no listeners");

        WeakReference mapped = CreateMappedStreamAndUnlisten(s: s, @out: @out);

        Collect();

        await Assert.That(mapped.IsAlive).IsFalse().Because("the mapped stream should have been collected");

        // Sending is what makes this deterministic. The cleanup thread unhooked
        // the node, or this send removes the target whose weak reference died.
        s.Send(2);

        await Assert.That(s.Node.GetListenersCopy().Count)
            .IsEqualTo(0)
            .Because("the source node should no longer be linked to a collected downstream stream");
    }

    [Test]
    public async Task EveryStreamInAChainIsCollected()
    {
        const int depth = 10;

        StreamSink<int> s = Stream.CreateSink<int>();
        WeakReference[] chain = CreateChainAndUnlisten(s: s, depth: depth);

        Collect();

        for (int i = 0; i < chain.Length; i++)
        {
            await Assert.That(chain[i].IsAlive).IsFalse().Because($"stream at depth {i} should have been collected");
        }

        s.Send(1);

        await Assert.That(s.Node.GetListenersCopy().Count)
            .IsEqualTo(0)
            .Because("the source node should be disconnected once the whole chain is gone");
    }

    [Test]
    public async Task ListenerIsKeptAliveWhileStillListening()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];

        WeakReference listener = CreateListenerAndDropTheReference(s: s, @out: @out);

        Collect();

        // This is deliberate, and not an oversight. ListenStrong roots the listener in the keep-alive
        // set of the stream for this cause: a caller that ignores the return value receives values.
        // Without this, a listener stops to fire with no message.
        await Assert.That(listener.IsAlive)
            .IsTrue()
            .Because("an active listener should stay alive even once the caller drops it");

        s.Send(5);

        await Assert.That(@out)
            .IsEquivalentTo(expected: [5], ordering: CollectionOrdering.Matching)
            .Because("a rooted listener should still be firing");
    }

    [Test]
    public async Task UnlistenReleasesTheListener()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];

        WeakReference listener = CreateListenerAndUnlisten(s: s, @out: @out);

        Collect();

        await Assert.That(listener.IsAlive)
            .IsFalse()
            .Because("Unlisten should stop the listener being rooted by the stream");

        s.Send(5);

        await Assert.That(@out).IsEmpty().Because("an unlistened listener should not fire");
    }

    [Test]
    public async Task CollectedStreamsAreReapedFromTheRegistry()
    {
        // StreamListenerManager tracks each stream ever created. Thus, a sweep that does not reap the
        // collected ones lets the registry become larger with no limit. Nothing else here reports that. The
        // node-level tests above succeed in each condition, because Stream.Send prunes dead targets on
        // its own.
        Collect();
        StreamListenerManager.Sweep();
        int before = StreamListenerManager.RegistryCount;

        CreateGarbageStreams(30);

        Collect();
        StreamListenerManager.Sweep();
        int after = StreamListenerManager.RegistryCount;

        await Assert.That(after)
            .IsLessThanOrEqualTo(before)
            .Because("the registry should be back to its previous size once the streams it tracked are collected");
    }

    // The registry asks the first attached link of an operator's output if the output is alive,
    // and not a weak handle to it. That rests on an invariant that no compiler checks: the
    // action of that link references the output. This builds each operator shape, holds the
    // output, collects, sweeps, and sends. For a shape whose action does not reference its output,
    // the sweep releases the output, and the shape stays silent here. See StreamNode<T>.
    [Test]
    public async Task AliveOperatorStreamsSurviveTheSweep()
    {
        StreamSink<int> a = Stream.CreateSink<int>();
        StreamSink<int> b = Stream.CreateSink<int>();
        CellSink<int> c = Cell.CreateSink(10);
        CellSink<Stream<int>> selector = Cell.CreateSink<Stream<int>>(a);
        CellSink<Cell<int>> cellSelector = Cell.CreateSink<Cell<int>>(c);
        List<string> seen = [];

        string[] tags =
        [
            "map", "filter", "choose", "snapshot", "snapshot latest", "or else", "merge of two types",
            "or else of three", "accum", "calm", "collect", "lift", "lift of a list", "switch s", "switch c", "loop",
            "split", "map of a cell", "lift of two sinks"
        ];

        List<IStrongListener> listeners =
            Transaction.Run(() =>
                new List<IStrongListener>
                {
                    a.Map(static v => v + 1).Map(static v => v * 2).ListenStrong(Mark<int>(seen: seen, tag: "map")),
                    a.Filter(static v => v > 0).ListenStrong(Mark<int>(seen: seen, tag: "filter")),
                    a.Choose(Maybe.Some).ListenStrong(Mark<int>(seen: seen, tag: "choose")),
                    a.Snapshot(c: c, f: static (v, w) => v + w).ListenStrong(Mark<int>(seen: seen, tag: "snapshot")),
                    a.SnapshotLatest(c: c, f: static (v, w) => v + w)
                        .ListenStrong(Mark<int>(seen: seen, tag: "snapshot latest")),
                    a.OrElse(b).ListenStrong(Mark<int>(seen: seen, tag: "or else")),
                    a.Merge(b).ListenStrong(Mark<(Maybe<int>, Maybe<int>)>(seen: seen, tag: "merge of two types")),
                    new[] { a, b, Stream.Never<int>() }.OrElse()
                        .ListenStrong(Mark<int>(seen: seen, tag: "or else of three")),
                    a.Accum(initialState: 0, f: static (v, total) => total + v)
                        .ListenStrong(Mark<int>(seen: seen, tag: "accum")),
                    a.Calm().ListenStrong(Mark<int>(seen: seen, tag: "calm")),
                    a.Collect(initialState: 0, f: static (v, state) => (v, state + 1))
                        .ListenStrong(Mark<int>(seen: seen, tag: "collect")),
                    c.Map(static v => v + 1).Lift(c2: c, f: static (x, y) => x + y)
                        .ListenStrong(Mark<int>(seen: seen, tag: "lift")),
                    new[] { c, c.Map(static v => v) }.Lift()
                        .ListenStrong(Mark<IReadOnlyList<int>>(seen: seen, tag: "lift of a list")),
                    selector.SwitchS().ListenStrong(Mark<int>(seen: seen, tag: "switch s")),
                    cellSelector.SwitchC().ListenStrong(Mark<int>(seen: seen, tag: "switch c")),
                    Stream.Loop<int>().WithoutCaptures(_ => a.Map(static v => v * 3))
                        .ListenStrong(Mark<int>(seen: seen, tag: "loop")),
                    Operational.Split<int, int[]>(a.Map(static v => new[] { v }))
                        .ListenStrong(Mark<int>(seen: seen, tag: "split")),
                    c.Map(static v => v + 1).ListenStrong(Mark<int>(seen: seen, tag: "map of a cell")),
                    c.Lift(c2: Cell.Constant(1), f: static (x, y) => x + y)
                        .ListenStrong(Mark<int>(seen: seen, tag: "lift of two sinks"))
                });

        // The cell listens fired at the construction. Only what fires after the sweep counts.
        seen.Clear();

        Collect();
        StreamListenerManager.Sweep();

        Transaction.RunVoid(() =>
        {
            a.Send(1);
            b.Send(2);
        });

        c.Send(20);

        foreach (IStrongListener listener in listeners)
        {
            listener.Unlisten();
        }

        await Assert.That(seen.Distinct())
            .IsEquivalentTo(expected: tags, ordering: CollectionOrdering.Any)
            .Because("every operator output a caller holds should still fire after a collection and a sweep");
    }

    // A map or a lift is its own node, and links its inputs into it. The registry stops those links
    // when nothing holds the derived cell, whether or not a caller asked for its updates. This reads
    // the source node after the sweep and before any send, because a send prunes a dead target by
    // itself.
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ADerivedCellReleasesItsLinksOnceDropped(bool readTheUpdates)
    {
        CellSink<int> c = Cell.CreateSink(1);
        Node<int> source = c.Updates().Node;
        int before = source.GetListenersCopy().Count;

        (WeakReference map, WeakReference lift) = CreateDerivedCells(source: c, readTheUpdates: readTheUpdates);

        await Assert.That(source.GetListenersCopy().Count).IsEqualTo(before + 2).Because("the map and the lift link in");

        Collect();
        StreamListenerManager.Sweep();

        await Assert.That(map.IsAlive).IsFalse().Because("the map should have been collected");
        await Assert.That(lift.IsAlive).IsFalse().Because("the lift should have been collected");

        await Assert.That(source.GetListenersCopy().Count)
            .IsEqualTo(before)
            .Because("the sweep should stop the links of a collected map and lift");
    }

    // The stream of the updates of a derived cell holds the derivation, and the derivation lets the
    // cell go when the stream comes. A strong listener on that stream must keep the link from the
    // source after the garbage collector removes the cell, and must not keep the cell.
    [Test]
    public async Task AStrongListenerOnTheUpdatesOfADerivedCellOutlivesTheCell()
    {
        CellSink<int> c = Cell.CreateSink(1);
        List<int> @out = [];

        (WeakReference cell, IStrongListener listener) = CreateMapAndListenToItsUpdates(source: c, @out: @out);

        Collect();
        StreamListenerManager.Sweep();
        c.Send(5);
        listener.Unlisten();

        await Assert.That(cell.IsAlive).IsFalse().Because("only the stream of the updates is held");
        await Assert.That(@out).IsEquivalentTo(expected: [10], ordering: CollectionOrdering.Matching);
    }

    // The sweep reads a registered node's first attached listener out of any lock. Before, the node
    // registered before it stored that listener. A sweep in that window found no listener and no
    // handle, and read the stream as dead. Its release then stopped the listener that the attach
    // stored after that: the link of a live stream. A sweeper thread here runs the sweep without
    // pause while the test builds chains, and each chain must fire.
    [Test]
    public async Task AStreamBuiltWhileTheSweepRunsKeepsItsLinks()
    {
        StrongBox<bool> stop = new(false);

        Thread sweeper =
            new(() =>
            {
                while (!Volatile.Read(ref stop.Value))
                {
                    StreamListenerManager.Sweep();
                }
            }) { IsBackground = true, Name = "sweeper" };

        sweeper.Start();
        List<int> seen = [];

        for (int round = 0; round < 300; round++)
        {
            StreamSink<int> s = Stream.CreateSink<int>();

            Stream<int> tail =
                Transaction.Run(() =>
                {
                    Stream<int> chain = s;

                    for (int i = 0; i < 100; i++)
                    {
                        chain = chain.Map(static v => v + 1);
                    }

                    return chain;
                });

            using (tail.ListenStrong(seen.Add))
            {
                s.Send(round);
            }
        }

        Volatile.Write(location: ref stop.Value, value: true);
        sweeper.Join();

        await Assert.That(seen.Count)
            .IsEqualTo(300)
            .Because("a chain built while the sweep runs must keep every link");
    }

    [Test]
    public async Task ListenOnceIsCollectedOnceDropped()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];

        WeakReference listener = CreateListenOnceAndDropTheReference(s: s, @out: @out);

        Collect();

        // The mirror image of ListenerIsKeptAliveWhileStillListening. It shows why ListenOnce and
        // ListenOnceStrong are two methods. ListenOnce roots nothing, thus the listener from the call
        // is the only thing that holds the handler. A caller that drops it loses that firing.
        await Assert.That(listener.IsAlive)
            .IsFalse()
            .Because("a weak one-shot listener should be collected once the caller drops it");

        s.Send(5);

        await Assert.That(@out).IsEmpty().Because("a collected one-shot listener should not fire");
    }

    [Test]
    public async Task ListenOnceStrongKeepsFiringOnceDropped()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];

        WeakReference listener = CreateListenOnceStrongAndDropTheReference(s: s, @out: @out);

        Collect();

        await Assert.That(listener.IsAlive)
            .IsTrue()
            .Because("a strong one-shot listener should stay rooted until its first firing");

        s.Send(5);
        s.Send(6);

        await Assert.That(@out)
            .IsEquivalentTo(expected: [5], ordering: CollectionOrdering.Matching)
            .Because("a rooted one-shot listener should fire exactly one time");
    }

    [Test]
    public async Task ListenOnceStrongReleasesItsRootAfterTheFirstFiring()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];

        // The one-shot listener stops itself, and that must release the keep-alive root and the
        // node. A release of only the node lets each ListenOnceStrong call add an object to the
        // stream that nothing removes.
        WeakReference listener = CreateListenOnceStrongAndFire(s: s, @out: @out);

        Collect();

        await Assert.That(listener.IsAlive)
            .IsFalse()
            .Because("a one-shot listener should stop rooting itself when it stops listening");

        await Assert.That(@out)
            .IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching)
            .Because("the one firing should have arrived before the listener stopped");
    }

    [Test]
    public async Task RootableListenerIsCollectedOnceDroppedBeforeRoot()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];

        WeakReference listener = CreateRootableListener(s: s, @out: @out, root: false);

        Collect();

        await Assert.That(listener.IsAlive)
            .IsFalse()
            .Because("a rootable listener should root nothing until Root");

        s.Send(5);

        await Assert.That(@out).IsEmpty().Because("a collected rootable listener should not fire");
    }

    [Test]
    public async Task RootableListenerKeepsFiringOnceDroppedAfterRoot()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];

        WeakReference listener = CreateRootableListener(s: s, @out: @out, root: true);

        Collect();

        await Assert.That(listener.IsAlive)
            .IsTrue()
            .Because("a rooted listener should stay rooted until its first firing");

        s.Send(5);
        s.Send(6);

        await Assert.That(@out)
            .IsEquivalentTo(expected: [5], ordering: CollectionOrdering.Matching)
            .Because("a rooted one-shot listener should fire exactly one time");
    }

    [Test]
    public async Task RootableListenerReleasesItsRootAfterTheFirstFiring()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];

        WeakReference listener = CreateRootableListenerAndFire(s: s, @out: @out);

        Collect();

        await Assert.That(listener.IsAlive)
            .IsFalse()
            .Because("a rooted listener should stop rooting itself when it stops listening");

        await Assert.That(@out)
            .IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching)
            .Because("the one firing should have arrived before the listener stopped");
    }

    [Test]
    public async Task RootableListenerIgnoresRootAfterUnlisten()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];

        // A cancellation can stop the listener before the async roots it. A Root after that
        // must not put the listener that stopped in the set, because nothing removes it again.
        WeakReference listener = CreateRootableListenerUnlistenAndRoot(s: s, @out: @out);

        Collect();

        await Assert.That(listener.IsAlive)
            .IsFalse()
            .Because("a Root after Unlisten should not root the listener");
    }

    [Test]
    public async Task ListenOnceAsyncReleasesTheStreamWhenItsTaskCompletes()
    {
        // The token lives longer than the stream. Its registration holds the listener, which holds
        // the stream, until the task completes.
        using CancellationTokenSource cts = new();
        (WeakReference stream, Task<int> task) = CreateListenOnceAsyncAndFire(cts.Token);

        await Assert.That(await task).IsEqualTo(1);

        // A continuation on the thread pool removes the registration. Thus, the stream can stay
        // alive for a short time after the task completes.
        bool collected = false;

        for (int attempt = 0; attempt < 100 && !collected; attempt++)
        {
            Collect();
            collected = !stream.IsAlive;

            if (!collected)
            {
                await Task.Delay(millisecondsDelay: 10, cancellationToken: CancellationToken.None);
            }
        }

        await Assert.That(collected)
            .IsTrue()
            .Because("a cancellation token that lives on should not root the stream after the task completes");
    }

    [Test]
    public async Task AStrongListenerOnALiftOutlivesTheFirstRoot()
    {
        CellSink<int> kept = Cell.CreateSink(2);
        List<int> @out = [];

        CreateLiftWithADroppedFirstRoot(kept: kept, @out: @out);

        Collect();
        @out.Clear();
        kept.Send(10);

        await Assert.That(@out)
            .IsEquivalentTo(expected: [11], ordering: CollectionOrdering.Matching)
            .Because("the root that lives should hold the listener, whichever input of the lift it feeds");
    }

    [Test]
    public async Task AStrongListenerOnAMergeOutlivesTheLeftRoot()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();
        List<int> @out = [];

        CreateMergeWithADroppedLeftRoot(kept: kept, @out: @out);

        Collect();
        kept.Send(10);

        await Assert.That(@out)
            .IsEquivalentTo(expected: [10], ordering: CollectionOrdering.Matching)
            .Because("the root that lives should hold the listener, whichever side of the merge it feeds");
    }

    [Test]
    public async Task AStrongListenerOnASwitchOutlivesTheOuterRoot()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();
        List<int> @out = [];

        CreateSwitchOfAConstantCell(kept: kept, @out: @out);

        Collect();
        kept.Send(10);

        await Assert.That(@out)
            .IsEquivalentTo(expected: [10], ordering: CollectionOrdering.Matching)
            .Because("the root of the selected inner should hold the listener when nothing holds the outer cell");
    }

    [Test]
    public async Task AStrongListenerOnASwitchLeavesTheInnerTheSwitchLeft()
    {
        StreamSink<int> first = Stream.CreateSink<int>();
        List<int> @out = [];

        WeakReference listener = CreateSwitchThenSelectADroppedInner(first: first, @out: @out);

        Collect();

        await Assert.That(listener.IsAlive)
            .IsFalse()
            .Because("an inner stream the switch left should no longer hold the listener");
    }

    [Test]
    public async Task UnlistenOnALiftReleasesTheListenerFromEveryRoot()
    {
        CellSink<int> first = Cell.CreateSink(1);
        CellSink<int> second = Cell.CreateSink(2);

        WeakReference listener = CreateLiftListenerAndUnlisten(first: first, second: second);

        Collect();

        await Assert.That(listener.IsAlive)
            .IsFalse()
            .Because("no root should hold a listener after Unlisten, however many roots the lift has");
    }

    // Each of these runs in its own non-inlined method. Thus, the locals are out of scope before
    // the caller collects, at each decision the JIT makes about what to keep in memory.

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateGarbageStreams(int count)
    {
        for (int i = 0; i < count; i++)
        {
            StreamSink<int> s = Stream.CreateSink<int>();
            Stream<int> mapped = s.Map(static v => v + 1);

            IListener listener =
                mapped.ListenStrong(static _ =>
                {
                });

            listener.Unlisten();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateMappedStreamAndUnlisten(StreamSink<int> s, ICollection<string> @out)
    {
        Stream<string> mapped = s.Map(static x => (x + 2).ToString());
        IListener listener = mapped.ListenStrong(@out.Add);
        s.Send(1);
        listener.Unlisten();
        return new WeakReference(mapped);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] CreateChainAndUnlisten(Stream<int> s, int depth)
    {
        List<WeakReference> chain = [];
        Stream<int> current = s;

        for (int i = 0; i < depth; i++)
        {
            current = current.Map(static v => v + 1);
            chain.Add(new WeakReference(current));
        }

        IListener listener =
            current.ListenStrong(static _ =>
            {
            });

        listener.Unlisten();
        return [.. chain];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateListenerAndDropTheReference(Stream<int> s, ICollection<int> @out)
    {
        IListener listener = s.ListenStrong(@out.Add);
        return new WeakReference(listener);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateListenOnceAndDropTheReference(Stream<int> s, ICollection<int> @out)
    {
        IWeakListener listener = s.ListenOnce(@out.Add);
        return new WeakReference(listener);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateListenOnceStrongAndDropTheReference(Stream<int> s, ICollection<int> @out)
    {
        IStrongListener listener = s.ListenOnceStrong(@out.Add);
        return new WeakReference(listener);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateListenOnceStrongAndFire(StreamSink<int> s, ICollection<int> @out)
    {
        IStrongListener listener = s.ListenOnceStrong(@out.Add);
        s.Send(1);
        return new WeakReference(listener);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateRootableListener(Stream<int> s, ICollection<int> @out, bool root)
    {
        IRootableListener listener = s.ListenOnceRootableImpl(@out.Add);

        if (root)
        {
            listener.Root();
        }

        return new WeakReference(listener);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateRootableListenerAndFire(StreamSink<int> s, ICollection<int> @out)
    {
        IRootableListener listener = s.ListenOnceRootableImpl(@out.Add);
        listener.Root();
        s.Send(1);
        return new WeakReference(listener);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateRootableListenerUnlistenAndRoot(Stream<int> s, ICollection<int> @out)
    {
        IRootableListener listener = s.ListenOnceRootableImpl(@out.Add);
        listener.Unlisten();
        listener.Root();
        return new WeakReference(listener);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Stream, Task<int> Task) CreateListenOnceAsyncAndFire(CancellationToken token)
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        Task<int> task = s.ListenOnceAsync(token);
        s.Send(1);
        return (new WeakReference(s), task);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateListenerAndUnlisten(Stream<int> s, ICollection<int> @out)
    {
        IListener listener = s.ListenStrong(@out.Add);
        listener.Unlisten();
        return new WeakReference(listener);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateLiftWithADroppedFirstRoot(Cell<int> kept, ICollection<int> @out)
    {
        CellSink<int> dropped = Cell.CreateSink(1);
        dropped.Lift(c2: kept, f: static (x, y) => x + y).ListenStrong(@out.Add);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateMergeWithADroppedLeftRoot(Stream<int> kept, ICollection<int> @out)
    {
        StreamSink<int> dropped = Stream.CreateSink<int>();
        dropped.OrElse(kept).ListenStrong(@out.Add);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateSwitchOfAConstantCell(Stream<int> kept, ICollection<int> @out) =>
        Cell.Constant(kept).SwitchS().ListenStrong(@out.Add);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateSwitchThenSelectADroppedInner(Stream<int> first, ICollection<int> @out)
    {
        StreamSink<int> second = Stream.CreateSink<int>();
        CellSink<Stream<int>> selected = Cell.CreateSink(first);
        IStrongListener listener = selected.SwitchS().ListenStrong(@out.Add);
        selected.Send(second);
        return new WeakReference(listener);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateLiftListenerAndUnlisten(Cell<int> first, Cell<int> second)
    {
        IStrongListener listener =
            first.Lift(c2: second, f: static (x, y) => x + y)
                .ListenStrong(static _ =>
                {
                });

        listener.Unlisten();
        return new WeakReference(listener);
    }

    // ReSharper disable once SuggestBaseTypeForParameter - CA1859 asks for the list, which the one caller gives.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Map, WeakReference Lift) CreateDerivedCells(Cell<int> source, bool readTheUpdates)
    {
        Cell<int> map = source.Map(static v => v + 1);
        Cell<int> lift = source.Lift(c2: Cell.Constant(1), f: static (x, y) => x + y);

        if (readTheUpdates)
        {
            _ = Operational.Updates(map);
            _ = Operational.Updates(lift);
        }

        return (Map: new WeakReference(map), Lift: new WeakReference(lift));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Cell, IStrongListener Listener) CreateMapAndListenToItsUpdates(
        Cell<int> source,
        ICollection<int> @out)
    {
        Cell<int> map = source.Map(static v => v * 2);
        return (Cell: new WeakReference(map), Listener: map.Updates().ListenStrong(@out.Add));
    }

    // ReSharper disable once SuggestBaseTypeForParameter - CA1859 asks for the list, which every caller passes, and a call on a List<T> costs less than a call through the interface.
    private static Action<T> Mark<T>(List<string> seen, string tag) => _ => seen.Add(tag);

    private static void Collect()
    {
        // Each generation, and a finalizer step in between. Stream has no finalizer. This said it
        // did, which was true at the time of writing and is not now. The finalizer that matters is
        // the sweep mechanism of StreamListenerManager, which asks for a sweep when a GC finalizes
        // it. These tests do read RegistryCount after a sweep. The generation is the other half. A
        // young collection does not see an object in a higher generation. Thus, a conclusive
        // collection has to get to all of them.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
