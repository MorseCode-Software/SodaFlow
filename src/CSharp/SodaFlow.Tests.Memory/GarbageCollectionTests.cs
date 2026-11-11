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
    // The one value that completes the wait of the last test.
    private const int LastValue = 99;

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
    public async Task ListenIsCollectedOnceDropped()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> @out = [];

        WeakReference listener = CreateListenerAndDropTheReference(s: s, @out: @out);

        Collect();

        // Listen roots nothing. The listener from the call is the only thing that holds the
        // handler, thus a caller that drops it gets no more values.
        await Assert.That(listener.IsAlive)
            .IsFalse()
            .Because("a listener should be collected once the caller drops it");

        s.Send(5);

        await Assert.That(@out).IsEmpty().Because("a collected listener should not fire");
    }

    [Test]
    public async Task ListenOnceAsyncWaitsOnAStreamThatOnlyTheWaitHolds()
    {
        StreamSink<int> s = Stream.CreateSink<int>();

        // The task holds no listener, and nothing holds the map. Only the root set of the sink
        // keeps the wait.
        Task<int> next = CreateListenOnceAsyncOnAMap(s);

        Collect();
        s.Send(5);

        await Assert.That(next.IsCompleted)
            .IsTrue()
            .Because("a source that can fire the stream should hold a wait that no caller holds");

        await Assert.That(await next).IsEqualTo(6);
    }

    [Test]
    public async Task ACanceledListenOnceAsyncLeavesTheRoot()
    {
        StreamSink<int> s = Stream.CreateSink<int>();

        WeakReference mapped = CreateListenOnceAsyncOnAMapAndCancel(s);

        await Assert.That(await IsCollectedSoon(mapped))
            .IsTrue()
            .Because("a cancellation should take the wait out of the root set of the sink, which lives");

        GC.KeepAlive(s);
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

        List<IListener> listeners =
            Transaction.Run(() =>
                new List<IListener>
                {
                    a.Map(static v => v + 1).Map(static v => v * 2).Listen(Mark<int>(seen: seen, tag: "map")),
                    a.Filter(static v => v > 0).Listen(Mark<int>(seen: seen, tag: "filter")),
                    a.Choose(Maybe.Some).Listen(Mark<int>(seen: seen, tag: "choose")),
                    a.Snapshot(c: c, f: static (v, w) => v + w).Listen(Mark<int>(seen: seen, tag: "snapshot")),
                    a.SnapshotLatest(c: c, f: static (v, w) => v + w)
                        .Listen(Mark<int>(seen: seen, tag: "snapshot latest")),
                    a.OrElse(b).Listen(Mark<int>(seen: seen, tag: "or else")),
                    a.Merge(b).Listen(Mark<(Maybe<int>, Maybe<int>)>(seen: seen, tag: "merge of two types")),
                    new[] { a, b, Stream.Never<int>() }.OrElse()
                        .Listen(Mark<int>(seen: seen, tag: "or else of three")),
                    a.Accum(initialState: 0, f: static (v, total) => total + v)
                        .Listen(Mark<int>(seen: seen, tag: "accum")),
                    a.Calm().Listen(Mark<int>(seen: seen, tag: "calm")),
                    a.Collect(initialState: 0, f: static (v, state) => (v, state + 1))
                        .Listen(Mark<int>(seen: seen, tag: "collect")),
                    c.Map(static v => v + 1).Lift(c2: c, f: static (x, y) => x + y)
                        .Listen(Mark<int>(seen: seen, tag: "lift")),
                    new[] { c, c.Map(static v => v) }.Lift()
                        .Listen(Mark<IReadOnlyList<int>>(seen: seen, tag: "lift of a list")),
                    selector.SwitchS().Listen(Mark<int>(seen: seen, tag: "switch s")),
                    cellSelector.SwitchC().Listen(Mark<int>(seen: seen, tag: "switch c")),
                    Stream.Loop<int>().WithoutCaptures(_ => a.Map(static v => v * 3))
                        .Listen(Mark<int>(seen: seen, tag: "loop")),
                    Operational.Split<int, int[]>(a.Map(static v => new[] { v }))
                        .Listen(Mark<int>(seen: seen, tag: "split")),
                    c.Map(static v => v + 1).Listen(Mark<int>(seen: seen, tag: "map of a cell")),
                    c.Lift(c2: Cell.Constant(1), f: static (x, y) => x + y)
                        .Listen(Mark<int>(seen: seen, tag: "lift of two sinks"))
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

        foreach (IListener listener in listeners)
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

        // Each collection also wakes the cleanup thread. That thread can take the collected
        // entries from the registry before this sweep does, and stop their links a short time
        // later. Thus, the count can stay high for a short time after this sweep returns.
        int count = source.GetListenersCopy().Count;

        for (int attempt = 0; attempt < 100 && count != before; attempt++)
        {
            await Task.Delay(millisecondsDelay: 10, cancellationToken: CancellationToken.None);
            StreamListenerManager.Sweep();
            count = source.GetListenersCopy().Count;
        }

        await Assert.That(count)
            .IsEqualTo(before)
            .Because("the sweep should stop the links of a collected map and lift");
    }

    // The stream of the updates of a derived cell holds the derivation, and the derivation lets the
    // cell go when the stream comes. A listener on that stream must keep the link from the
    // source after the garbage collector removes the cell, and must not keep the cell.
    [Test]
    public async Task AListenerOnTheUpdatesOfADerivedCellOutlivesTheCell()
    {
        CellSink<int> c = Cell.CreateSink(1);
        List<int> @out = [];

        (WeakReference cell, IListener listener) = CreateMapAndListenToItsUpdates(source: c, @out: @out);

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

            using (tail.Listen(seen.Add))
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

        // ListenOnce roots nothing, thus the listener from the call is the only thing that holds
        // the handler. A caller that drops it loses that firing.
        await Assert.That(listener.IsAlive)
            .IsFalse()
            .Because("a one-shot listener should be collected once the caller drops it");

        s.Send(5);

        await Assert.That(@out).IsEmpty().Because("a collected one-shot listener should not fire");
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

        await Assert.That(await IsCollectedSoon(stream))
            .IsTrue()
            .Because("a cancellation token that lives on should not root the stream after the task completes");
    }

    // The tests from here to the helpers are for the keep-alive sets. ListenOnceAsync is the one
    // caller that roots a listener. Its task holds no listener and no stream. Thus, a wait that the
    // garbage collector removed leaves a task that does not complete, and each test reads
    // IsCompleted before it awaits the task.

    [Test]
    public async Task AWaitOnALiftOutlivesTheFirstRoot()
    {
        CellSink<int> kept = Cell.CreateSink(2);

        Task<int> next = CreateLiftWithADroppedFirstRoot(kept);

        Collect();
        kept.Send(10);

        await Assert.That(next.IsCompleted)
            .IsTrue()
            .Because("the root that lives should hold the wait, whichever input of the lift it feeds");

        await Assert.That(await next).IsEqualTo(11);
    }

    [Test]
    public async Task AWaitOnAMergeOutlivesTheLeftRoot()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        Task<int> next = CreateMergeWithADroppedLeftRoot(kept);

        Collect();
        kept.Send(10);

        await Assert.That(next.IsCompleted)
            .IsTrue()
            .Because("the root that lives should hold the wait, whichever side of the merge it feeds");

        await Assert.That(await next).IsEqualTo(10);
    }

    [Test]
    public async Task AWaitOnASwitchOutlivesTheOuterRoot()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        Task<int> next = CreateSwitchOfAConstantCell(kept);

        Collect();
        kept.Send(10);

        await Assert.That(next.IsCompleted)
            .IsTrue()
            .Because("the root of the selected inner should hold the wait when nothing holds the outer cell");

        await Assert.That(await next).IsEqualTo(10);
    }

    [Test]
    public async Task AWaitOnASwitchLeavesTheInnerTheSwitchLeft()
    {
        StreamSink<int> first = Stream.CreateSink<int>();

        WeakReference switched = CreateSwitchThenSelectADroppedInner(first);

        Collect();

        await Assert.That(switched.IsAlive)
            .IsFalse()
            .Because("an inner stream the switch left should no longer hold the wait");

        GC.KeepAlive(first);
    }

    [Test]
    public async Task ACanceledWaitOnALiftLeavesEveryRoot()
    {
        CellSink<int> first = Cell.CreateSink(1);
        CellSink<int> second = Cell.CreateSink(2);

        WeakReference updates = CreateWaitOnALiftAndCancel(first: first, second: second);

        await Assert.That(await IsCollectedSoon(updates))
            .IsTrue()
            .Because("no root should hold a wait after a cancellation, however many roots the lift has");

        GC.KeepAlive(first);
        GC.KeepAlive(second);
    }

    [Test]
    public async Task AWaitInALoopThroughASwitchOutlivesTheLoop()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        Task<int> next = CreateWaitInALoopThroughASwitch(kept);

        Collect();
        kept.Send(1);

        await Assert.That(next.IsCompleted)
            .IsTrue()
            .Because("the sink should hold the wait when nothing holds the loop that the switches go through");

        await Assert.That(await next).IsEqualTo(1);
    }

    // A loop is a root with a set of its own, and a stream made from the loop stream uses that
    // set. Nothing holds that set but the stream that closes the loop, which gives it to its roots.
    // In each of the next tests, the wait is on a map of the loop stream. Thus, only the set of the
    // loop roots the wait.

    [Test]
    public async Task AWaitOnALoopStreamOutlivesTheLoop()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        Task<int> next = CreateWaitOnALoopStream(kept);

        Collect();
        kept.Send(1);

        await Assert.That(next.IsCompleted)
            .IsTrue()
            .Because("the root of the stream that closes a loop should hold the set of the loop, which holds the wait");

        await Assert.That(await next).IsEqualTo(10);
    }

    [Test]
    public async Task AWaitOnALoopedCellOutlivesTheLoop()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        Task<int> next = CreateWaitOnALoopedCell(kept);

        Collect();
        kept.Send(1);

        await Assert.That(next.IsCompleted)
            .IsTrue()
            .Because("the root of the cell that closes a loop should hold the set of the loop, which holds the wait");

        await Assert.That(await next).IsEqualTo(10);
    }

    [Test]
    public async Task AWaitOnALoopThatASwitchClosesFollowsTheSwitch()
    {
        StreamSink<int> second = Stream.CreateSink<int>();

        Task<int> next = CreateWaitOnALoopThatASwitchClosesThenSelect(second);

        Collect();
        second.Send(1);

        await Assert.That(next.IsCompleted)
            .IsTrue()
            .Because("the root of the inner that the switch selects now should hold the set of the loop");

        await Assert.That(await next).IsEqualTo(10);
    }

    [Test]
    public async Task AWaitOnALoopThatASwitchClosesLeavesTheInnerTheSwitchLeft()
    {
        StreamSink<int> first = Stream.CreateSink<int>();

        WeakReference fromLoop = CreateWaitOnALoopThatASwitchClosesThenSelectADroppedInner(first);

        Collect();

        await Assert.That(fromLoop.IsAlive)
            .IsFalse()
            .Because("an inner stream the switch left should no longer hold the set of the loop");

        GC.KeepAlive(first);
    }

    // The set of a loop goes to the roots of the stream that closes the loop. It goes when it gets
    // its first target, and not at the close. Each of the next tests gives a set its first target
    // in a different way. The ways are a wait before the close, a wait on a merge, a wait on a
    // switch, and the set of a second loop. More ways are the move of a switch with one
    // registration and with two, and a long chain of loops. Two more tests are for a set that is
    // in the roots.

    [Test]
    public async Task AWaitOnALoopStreamBeforeItsCloseOutlivesTheLoop()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        Task<int> next = CreateWaitOnALoopStreamBeforeItsClose(kept);

        Collect();
        kept.Send(1);

        await Assert.That(next.IsCompleted)
            .IsTrue()
            .Because("a set that has a wait when its loop closes should go to the roots at the close");

        await Assert.That(await next).IsEqualTo(10);
    }

    [Test]
    public async Task AWaitOnAMergeOfALoopStreamOutlivesTheLoop()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        Task<int> next = CreateWaitOnAMergeOfALoopStream(kept);

        Collect();
        kept.Send(1);

        await Assert.That(next.IsCompleted)
            .IsTrue()
            .Because("a set that gets its first target from a merge should go to its roots");

        await Assert.That(await next).IsEqualTo(10);
    }

    [Test]
    public async Task AWaitOnASwitchThatSelectsALoopStreamOutlivesTheLoop()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        Task<int> next = CreateWaitOnASwitchThatSelectsALoopStream(kept);

        Collect();
        kept.Send(1);

        await Assert.That(next.IsCompleted)
            .IsTrue()
            .Because("a set that gets its first target from a registration should go to its roots");

        await Assert.That(await next).IsEqualTo(10);
    }

    [Test]
    public async Task AWaitOnALoopThatASecondLoopFeedsOutlivesTheLoops()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        Task<int> next = CreateWaitOnALoopThatASecondLoopFeeds(kept);

        Collect();
        kept.Send(1);

        await Assert.That(next.IsCompleted)
            .IsTrue()
            .Because("the set of a loop is the first target of the set of the loop that feeds it, which should then go to its roots");

        await Assert.That(await next).IsEqualTo(100);
    }

    [Test]
    public async Task AWaitThatASwitchMovesToALoopOutlivesTheLoop()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        Task<int> next = CreateWaitThatASwitchMovesToALoop(kept);

        Collect();
        kept.Send(1);

        await Assert.That(next.IsCompleted)
            .IsTrue()
            .Because("a set that gets its first target at the move of a switch should go to its roots at that move");

        await Assert.That(await next).IsEqualTo(10);
    }

    [Test]
    public async Task TwoWaitsThatASwitchMovesToALoopOutliveTheLoop()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        (Task<int> first, Task<int> second) = CreateTwoWaitsThatASwitchMovesToALoop(kept);

        Collect();
        kept.Send(1);

        await Assert.That(first.IsCompleted && second.IsCompleted)
            .IsTrue()
            .Because("a set that gets its first target at a move of two registrations should go to its roots at that move");

        await Assert.That(await first + await second).IsEqualTo(20);
    }

    [Test]
    public async Task AWaitOnALongChainOfLoopsOutlivesTheLoops()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        Task<int> next = CreateWaitOnAChainOfLoops(source: kept, length: 20_000);

        Collect();
        kept.Send(1);

        await Assert.That(next.IsCompleted)
            .IsTrue()
            .Because("each set in the chain is the first target of the next set, and each should go to its roots");

        await Assert.That(await next).IsEqualTo(1);
    }

    [Test]
    public async Task ASecondWaitOnALoopStreamOutlivesTheLoop()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        (Task<int> first, Task<int> second) = CreateTwoWaitsOnALoopStreamInTurn(kept);

        Collect();
        kept.Send(2);

        await Assert.That(await first).IsEqualTo(10);

        await Assert.That(second.IsCompleted)
            .IsTrue()
            .Because("the set of a loop should stay in the roots after its first wait fired");

        await Assert.That(await second).IsEqualTo(30);
    }

    [Test]
    public async Task ASetOfALoopInTheRootsDoesNotHoldTheOtherRootsOfTheLoop()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        (Task<int> fired, WeakReference fromOther) = CreateWaitOnTheSecondRootOfALoopThatHadAWait(kept);

        await Assert.That(await fired).IsEqualTo(10);

        await Assert.That(await IsCollectedSoon(fromOther))
            .IsTrue()
            .Because("a set that is in the roots should not hold the way to them, which holds each wait of each root");

        GC.KeepAlive(kept);
    }

    // A set that gets its first target stays in a queue until the call that gave the target is done
    // with its walks. That call then gives the set to its roots. A set that stays in the queue is
    // held for good, with each wait in it. Thus, each of the next tests drops the source of a
    // wait, and expects that nothing holds the source after that. Each test is one way to a first
    // target.

    [Test]
    public async Task NothingHoldsAWaitOnALoopStreamAfterItsSourceIsDropped() =>
        await AssertThatNothingHoldsTheSource(static source => CreateWaitOnALoopStream(source));

    [Test]
    public async Task NothingHoldsAWaitFromBeforeTheCloseOfALoopAfterItsSourceIsDropped() =>
        await AssertThatNothingHoldsTheSource(static source => CreateWaitOnALoopStreamBeforeItsClose(source));

    [Test]
    public async Task NothingHoldsAWaitOnAMergeOfALoopStreamAfterItsSourceIsDropped() =>
        await AssertThatNothingHoldsTheSource(static source => CreateWaitOnAMergeOfALoopStream(source));

    [Test]
    public async Task NothingHoldsAWaitOnASwitchThatSelectsALoopStreamAfterItsSourceIsDropped() =>
        await AssertThatNothingHoldsTheSource(static source => CreateWaitOnASwitchThatSelectsALoopStream(source));

    [Test]
    public async Task NothingHoldsAWaitThatASwitchMovesToALoopAfterItsSourceIsDropped() =>
        await AssertThatNothingHoldsTheSource(static source => CreateWaitThatASwitchMovesToALoop(source));

    [Test]
    public async Task NothingHoldsTwoWaitsThatASwitchMovesToALoopAfterTheirSourceIsDropped() =>
        await AssertThatNothingHoldsTheSource(static source => CreateTwoWaitsThatASwitchMovesToALoop(source));

    [Test]
    public async Task NothingHoldsAWaitOnAChainOfLoopsAfterItsSourceIsDropped() =>
        await AssertThatNothingHoldsTheSource(static source => CreateWaitOnAChainOfLoops(source: source, length: 3));

    private static async Task AssertThatNothingHoldsTheSource(Func<Stream<int>, object> startWait)
    {
        WeakReference source = CreateSourceOfAWait(startWait);

        await Assert.That(await IsCollectedSoon(source))
            .IsTrue()
            .Because("each set that got a target should be in its roots and not in the queue, and nothing holds the roots");
    }

    // With no wait, the set of a loop has no target. Thus, no root holds it, and no switch holds it.

    [Test]
    public async Task ALoopWithNoWaitLeavesNothingInTheRootOfTheStreamThatClosesIt()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        WeakReference set = CreateSetOfALoopWithNoWait(kept);

        await Assert.That(await IsCollectedSoon(set))
            .IsTrue()
            .Because("a set with no target keeps nothing alive, thus the root should not hold it");

        GC.KeepAlive(kept);
    }

    [Test]
    public async Task ALoopWithNoWaitThatASwitchClosesLeavesNothingInTheSwitch()
    {
        StreamSink<int> first = Stream.CreateSink<int>();
        CellSink<Stream<int>> selected = Cell.CreateSink<Stream<int>>(first);
        Stream<int> switched = selected.SwitchS();

        WeakReference set = CreateSetOfALoopWithNoWait(switched);

        await Assert.That(await IsCollectedSoon(set))
            .IsTrue()
            .Because("a set with no target needs no registration, thus the switch should hold nothing of the loop");

        GC.KeepAlive(first);
        GC.KeepAlive(selected);
        GC.KeepAlive(switched);
    }

    [Test]
    public async Task ACanceledWaitInALoopThroughASwitchLeavesTheRoot()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();

        WeakReference updates = CreateWaitInALoopThroughASwitchAndCancel(kept);

        await Assert.That(await IsCollectedSoon(updates))
            .IsTrue()
            .Because("no root should hold a wait after a cancellation, when the keep-alive set of a switch reaches itself");

        GC.KeepAlive(kept);
    }

    [Test]
    public async Task ACanceledWaitLeavesEachRootAfterASwitchInTheLoopMovesOutOfIt()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();
        StreamSink<int> other = Stream.CreateSink<int>();
        CellSink<bool> inLoop = Cell.CreateSink(true);

        WeakReference first = CreateThreeSwitchLoopMoveAndCancel(kept: kept, other: other, inLoop: inLoop);

        await Assert.That(await IsCollectedSoon(first))
            .IsTrue()
            .Because("no root should hold a wait after a cancellation, when a switch in its loop moved out of the loop before");

        GC.KeepAlive(kept);
        GC.KeepAlive(other);
        GC.KeepAlive(inLoop);
    }

    [Test]
    public async Task AListenerIsCollectedOnceDroppedWhileASwitchInTheLoopMoves()
    {
        StreamSink<int> kept = Stream.CreateSink<int>();
        StreamSink<int> other = Stream.CreateSink<int>();
        CellSink<bool> inLoop = Cell.CreateSink(true);
        List<string> seen = [];

        // The wait stays until the last send of the test, because its filter passes only that
        // value. Thus, its registration passes the switches in the loop, and each move of the
        // second switch walks it again.
        (Task<int> wait, WeakReference[] dropped) =
            CreateThreeSwitchLoopWithAWaitAndListeners(kept: kept, other: other, inLoop: inLoop, seen: seen);

        inLoop.Send(false);
        inLoop.Send(true);
        Collect();
        seen.Clear();
        kept.Send(1);
        inLoop.Send(false);
        other.Send(2);
        bool anyAlive = dropped.Any(static w => w.IsAlive);

        bool completedEarly = wait.IsCompleted;
        inLoop.Send(true);
        kept.Send(LastValue);

        await Assert.That(anyAlive)
            .IsFalse()
            .Because("a keep-alive registration that passes a switch should not hold a listener that the caller dropped, its handler, or a stream that only that listener holds");

        await Assert.That(seen)
            .IsEmpty()
            .Because("the handler of a listener that was collected should not run");

        await Assert.That(completedEarly).IsFalse().Because("the wait should be there for the whole test");

        await Assert.That(wait.IsCompleted)
            .IsTrue()
            .Because("the roots should hold the wait through each move of the switch, or this test shows nothing");
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
                mapped.Listen(static _ =>
                {
                });

            listener.Unlisten();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateMappedStreamAndUnlisten(StreamSink<int> s, ICollection<string> @out)
    {
        Stream<string> mapped = s.Map(static x => (x + 2).ToString());
        IListener listener = mapped.Listen(@out.Add);
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
            current.Listen(static _ =>
            {
            });

        listener.Unlisten();
        return [.. chain];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateListenerAndDropTheReference(Stream<int> s, ICollection<int> @out)
    {
        IListener listener = s.Listen(@out.Add);
        return new WeakReference(listener);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateListenOnceAndDropTheReference(Stream<int> s, ICollection<int> @out)
    {
        IListener listener = s.ListenOnce(@out.Add);
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
        IListener listener = s.Listen(@out.Add);
        listener.Unlisten();
        return new WeakReference(listener);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> CreateListenOnceAsyncOnAMap(Stream<int> s) =>
        s.Map(static v => v + 1).ListenOnceAsync();

    // The sink lives, and its root set holds the wait until the cancellation.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateListenOnceAsyncOnAMapAndCancel(Stream<int> s)
    {
        using CancellationTokenSource cts = new();
        Stream<int> mapped = s.Map(static v => v + 1);
        _ = mapped.ListenOnceAsync(cts.Token);
        cts.Cancel();
        return new WeakReference(mapped);
    }

    // The wait is on the updates. The value stream of a cell fires in the transaction that listens,
    // and that firing completes a wait immediately.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> CreateLiftWithADroppedFirstRoot(Cell<int> kept)
    {
        CellSink<int> dropped = Cell.CreateSink(1);
        return dropped.Lift(c2: kept, f: static (x, y) => x + y).Updates().ListenOnceAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> CreateMergeWithADroppedLeftRoot(Stream<int> kept)
    {
        StreamSink<int> dropped = Stream.CreateSink<int>();
        return dropped.OrElse(kept).ListenOnceAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> CreateSwitchOfAConstantCell(Stream<int> kept) =>
        Cell.Constant(kept).SwitchS().ListenOnceAsync();

    // The first inner lives, and the switch leaves it for an inner that nothing holds. After that,
    // only a root set of the first inner can hold the wait and the switch.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateSwitchThenSelectADroppedInner(Stream<int> first)
    {
        StreamSink<int> second = Stream.CreateSink<int>();
        CellSink<Stream<int>> selected = Cell.CreateSink(first);
        Stream<int> switched = selected.SwitchS();
        _ = switched.ListenOnceAsync();
        selected.Send(second);
        return new WeakReference(switched);
    }

    // An object that switches onto a stream from its own output, and a second switch whose outer
    // depends on that loop. The keep-alive set of each switch reaches itself through the loop.
    private static Cell<int> LoopThroughASwitch(Stream<int> kept) =>
        Transaction.Run(() =>
            ForwardReference<Looped>.WithoutCaptures(reference =>
            {
                Stream<int> fromLoop = reference.Map(static l => l.Output).SwitchS();
                Cell<int> total = fromLoop.OrElse(kept).Accum(initialState: 0, f: static (x, sum) => sum + x);
                Stream<int> output = total.Map(_ => kept).SwitchS();
                return new Looped(output: output.Filter(static x => x < 0), total: total);
            })).Total;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> CreateWaitInALoopThroughASwitch(Stream<int> kept) =>
        LoopThroughASwitch(kept).Updates().ListenOnceAsync();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateWaitInALoopThroughASwitchAndCancel(Stream<int> kept)
    {
        using CancellationTokenSource cts = new();
        Stream<int> updates = LoopThroughASwitch(kept).Updates();
        _ = updates.ListenOnceAsync(cts.Token);
        cts.Cancel();
        return new WeakReference(updates);
    }

    // A running total in a loop that the given stream feeds. The result is a map of the loop stream.
    private static Stream<int> MapOfALoopStream(Stream<int> source) =>
        Stream.Loop<int>()
            .WithCaptures(l =>
                (Stream: source.Snapshot(c: l.Hold(0), f: static (n, o) => n + o),
                    Captures: l.Map(static v => v * 10)))
            .Captures;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> CreateWaitOnALoopStream(Stream<int> kept) => MapOfALoopStream(kept).ListenOnceAsync();

    // The same total in a cell loop. The wait is on a map of the updates of the looped cell.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> CreateWaitOnALoopedCell(Stream<int> kept) =>
        Cell.Loop<int>()
            .WithCaptures(l =>
                (Cell: kept.Snapshot(c: l, f: static (n, o) => n + o).Hold(0),
                    Captures: l.Updates().Map(static v => v * 10)))
            .Captures
            .ListenOnceAsync();

    // A switch feeds the loop, thus the set of the loop goes to the roots that the switch reaches
    // now. The switch moves to the inner that the caller keeps, and nothing holds the first inner
    // or the outer cell after that.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> CreateWaitOnALoopThatASwitchClosesThenSelect(Stream<int> second)
    {
        StreamSink<int> first = Stream.CreateSink<int>();
        CellSink<Stream<int>> selected = Cell.CreateSink<Stream<int>>(first);
        Task<int> next = MapOfALoopStream(selected.SwitchS()).ListenOnceAsync();
        selected.Send(second);
        return next;
    }

    // The switch leaves the inner that the caller keeps for an inner that nothing holds. After that,
    // only a root set of the first inner can hold the set of the loop, and with it the wait.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateWaitOnALoopThatASwitchClosesThenSelectADroppedInner(Stream<int> first)
    {
        StreamSink<int> second = Stream.CreateSink<int>();
        CellSink<Stream<int>> selected = Cell.CreateSink(first);
        Stream<int> fromLoop = MapOfALoopStream(selected.SwitchS());
        _ = fromLoop.ListenOnceAsync();
        selected.Send(second);
        return new WeakReference(fromLoop);
    }

    // The wait starts in the block of the loop, thus the set of the loop has a target before the
    // loop closes.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> CreateWaitOnALoopStreamBeforeItsClose(Stream<int> kept) =>
        Stream.Loop<int>()
            .WithCaptures(l =>
                (Stream: kept.Snapshot(c: l.Hold(0), f: static (n, o) => n + o),
                    Captures: l.Map(static v => v * 10).ListenOnceAsync()))
            .Captures;

    // The wait is on a merge of a map of the loop stream and a sink that nothing holds. The set of
    // the merge is a fan-out, thus a walk gives the wait to the set of the loop.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> CreateWaitOnAMergeOfALoopStream(Stream<int> kept) =>
        MapOfALoopStream(kept).OrElse(Stream.CreateSink<int>()).ListenOnceAsync();

    // The wait is on a switch that selects a map of the loop stream. Thus, a registration gives the
    // wait to the set of the loop. Nothing holds the outer cell after the call.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> CreateWaitOnASwitchThatSelectsALoopStream(Stream<int> kept) =>
        Cell.CreateSink(MapOfALoopStream(kept)).SwitchS().ListenOnceAsync();

    // Two running totals. A map of the stream of the inner loop feeds the outer loop. Thus, the
    // stream that closes the outer loop uses the set of the inner loop. The wait is on the outer
    // loop.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> CreateWaitOnALoopThatASecondLoopFeeds(Stream<int> kept) =>
        MapOfALoopStream(MapOfALoopStream(kept)).ListenOnceAsync();

    // The wait is on a switch, and its registration passes the switch. The switch then selects a
    // map of a loop stream. No wait is on that loop, thus the move gives the set of the loop its
    // first target. Nothing holds the outer cell or the first inner after that.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> CreateWaitThatASwitchMovesToALoop(Stream<int> kept)
    {
        CellSink<Stream<int>> selected = Cell.CreateSink<Stream<int>>(Stream.CreateSink<int>());
        Task<int> next = selected.SwitchS().ListenOnceAsync();
        selected.Send(MapOfALoopStream(kept));
        return next;
    }

    // Two waits on one switch, thus two registrations pass it. The switch then selects a map of a
    // loop stream with no wait on it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task<int> First, Task<int> Second) CreateTwoWaitsThatASwitchMovesToALoop(Stream<int> kept)
    {
        CellSink<Stream<int>> selected = Cell.CreateSink<Stream<int>>(Stream.CreateSink<int>());
        Stream<int> switched = selected.SwitchS();
        (Task<int> First, Task<int> Second) waits = (switched.ListenOnceAsync(), switched.ListenOnceAsync());
        selected.Send(MapOfALoopStream(kept));
        return waits;
    }

    // A chain of loops, each fed by a map of the stream of the loop before it. Each loop sends on
    // what it gets. The wait is on the last loop. Thus, each set in turn is the first target of
    // the set before it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> CreateWaitOnAChainOfLoops(Stream<int> source, int length)
    {
        Stream<int> last = source;

        for (int index = 0; index < length; index++)
        {
            Stream<int> feed = last;

            last =
                Stream.Loop<int>()
                    .WithCaptures(l =>
                        (Stream: feed.Snapshot(c: l.Hold(0), f: static (n, _) => n), Captures: l.Map(static v => v)))
                    .Captures;
        }

        return last.ListenOnceAsync();
    }

    // The stream that closes the loop is made from two sinks, thus the set of the loop goes to the
    // roots of the two. The wait on the loop fires. After that, a wait starts on a map of the
    // second sink, and nothing holds that sink or that map.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task<int> Fired, WeakReference FromOther) CreateWaitOnTheSecondRootOfALoopThatHadAWait(
        StreamSink<int> kept)
    {
        StreamSink<int> other = Stream.CreateSink<int>();
        Task<int> fired = MapOfALoopStream(kept.OrElse(other)).ListenOnceAsync();
        kept.Send(1);

        Stream<int> fromOther = other.Map(static v => v);
        _ = fromOther.ListenOnceAsync();
        return (fired, new WeakReference(fromOther));
    }

    // The first wait fires and leaves the set of the loop. The second wait starts after that.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task<int> First, Task<int> Second) CreateTwoWaitsOnALoopStreamInTurn(StreamSink<int> kept)
    {
        Stream<int> fromLoop = MapOfALoopStream(kept);
        Task<int> first = fromLoop.ListenOnceAsync();
        kept.Send(1);
        return (first, fromLoop.ListenOnceAsync());
    }

    // A sink, and a wait that the given function starts from it. The wait holds its stream, and
    // the stream holds the sink. Nothing holds the wait or the sink after the call.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateSourceOfAWait(Func<Stream<int>, object> startWait)
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        _ = startWait(source);
        return new WeakReference(source);
    }

    // The set of a loop that the given stream feeds. Nothing holds the loop after the call.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateSetOfALoopWithNoWait(Stream<int> source) =>
        new(MapOfALoopStream(source).KeepListenersAlive);

    // Three switches in one loop. The first selects the output of the object, which is the second.
    // The second selects the third while inLoop is true. The outer of the third is a cell made from
    // the first, thus the inner of the second reaches the first. When inLoop is false, the second
    // selects other, which is outside the loop.
    private static SwitchLoop ThreeSwitchLoop(Stream<int> kept, Stream<int> other, Cell<bool> inLoop) =>
        Transaction.Run(() =>
            ForwardReference<SwitchLoop>.WithoutCaptures(reference =>
            {
                Stream<int> first = reference.Map(static l => l.Output).SwitchS();
                Cell<int> total = first.OrElse(kept).Accum(initialState: 0, f: static (x, sum) => sum + x);
                Stream<int> third = total.Map(_ => kept).SwitchS();
                Stream<int> second = inLoop.Map(on => on ? third : other).SwitchS();
                return new SwitchLoop(output: second, first: first);
            }));

    // The second switch moves out of the loop, then the wait stops. Nothing fires the first switch
    // in between, thus the wait is there when the switch moves.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateThreeSwitchLoopMoveAndCancel(
        Stream<int> kept,
        Stream<int> other,
        CellSink<bool> inLoop)
    {
        using CancellationTokenSource cts = new();
        SwitchLoop loop = ThreeSwitchLoop(kept: kept, other: other, inLoop: inLoop);
        _ = loop.First.ListenOnceAsync(cts.Token);
        inLoop.Send(false);
        cts.Cancel();
        return new WeakReference(loop.First);
    }

    // A wait on a filter of the first switch that passes only LastValue. Also, listeners on the
    // first switch, on the second, and on a map of the second. The second switch moves out of the loop
    // and back while the listeners are in use, and then the caller drops them. The caller gets the
    // task of the wait and a weak reference to each listener, each handler, and the map.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task<int> Wait, WeakReference[] Dropped) CreateThreeSwitchLoopWithAWaitAndListeners(
        StreamSink<int> kept,
        StreamSink<int> other,
        CellSink<bool> inLoop,
        List<string> seen)
    {
        SwitchLoop loop = ThreeSwitchLoop(kept: kept, other: other, inLoop: inLoop);
        Task<int> wait = loop.First.Filter(static v => v == LastValue).ListenOnceAsync();

        Stream<int> map = loop.Output.Map(static v => v + 1);
        List<WeakReference> weak = [new(map)];
        List<IListener> listeners = [];

        foreach ((Stream<int> stream, string tag) in new[] { (loop.First, "first"), (loop.Output, "second"), (map, "map") })
        {
            Action<int> handler = Mark<int>(seen: seen, tag: tag);
            IListener listener = stream.Listen(handler);
            weak.Add(new WeakReference(handler));
            weak.Add(new WeakReference(listener));
            listeners.Add(listener);
        }

        kept.Send(1);
        inLoop.Send(false);
        other.Send(2);
        inLoop.Send(true);
        GC.KeepAlive(listeners);
        return (Wait: wait, Dropped: [.. weak]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateWaitOnALiftAndCancel(Cell<int> first, Cell<int> second)
    {
        using CancellationTokenSource cts = new();
        Stream<int> updates = first.Lift(c2: second, f: static (x, y) => x + y).Updates();
        _ = updates.ListenOnceAsync(cts.Token);
        cts.Cancel();
        return new WeakReference(updates);
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
    private static (WeakReference Cell, IListener Listener) CreateMapAndListenToItsUpdates(
        Cell<int> source,
        ICollection<int> @out)
    {
        Cell<int> map = source.Map(static v => v * 2);
        return (Cell: new WeakReference(map), Listener: map.Updates().Listen(@out.Add));
    }

    // ReSharper disable once SuggestBaseTypeForParameter - CA1859 asks for the list, which every caller passes, and a call on a List<T> costs less than a call through the interface.
    private static Action<T> Mark<T>(List<string> seen, string tag) => _ => seen.Add(tag);

    // The token registration of a wait holds the listener, and the listener holds the stream. A
    // continuation on the thread pool removes the registration after the task completes or
    // cancels. Thus, the stream can stay in memory for a short time after that. This collects
    // again until the stream is gone or the time is up.
    private static async Task<bool> IsCollectedSoon(WeakReference reference)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            Collect();

            if (!reference.IsAlive)
            {
                return true;
            }

            await Task.Delay(millisecondsDelay: 10, cancellationToken: CancellationToken.None);
        }

        return false;
    }

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

    private sealed class SwitchLoop(Stream<int> output, Stream<int> first)
    {
        public Stream<int> Output { get; } = output;

        public Stream<int> First { get; } = first;
    }

    private sealed class Looped(Stream<int> output, Cell<int> total)
    {
        public Stream<int> Output { get; } = output;

        public Cell<int> Total { get; } = total;
    }
}
