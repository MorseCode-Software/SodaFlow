using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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
///         The tests in <see cref="StreamTests" /> cover the same ground but count live objects with
///         dotMemory, so they are all <c>[Ignore]</c>d and never run in CI. These assert the same
///         invariants using only weak references and the node's own listener set, so they actually
///         guard the cleanup machinery on each build.
///     </para>
///     <para>
///         Two things are deliberately not asserted. First, that a node stays connected immediately
///         after a collection: <see cref="StreamListenerManager" /> unhooks nodes from a background
///         thread, thus that is a race. Second, which of the two cleanup paths did the work. That is
///         the background thread, or the lazy prune in <c>Stream.Send</c> when a weak reference of the
///         target died. What matters is that the node becomes disconnected, thus
///         these tests send a value to force a deterministic outcome and check that.
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
        // StreamListenerManager tracks each stream ever created, thus when the sweep did not
        // reap collected ones the registry grows with no limit. Nothing else here
        // notice: the node-level tests above succeed in each condition, because Stream.Send prunes dead
        // targets on its own.
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
    private static WeakReference CreateListenerAndUnlisten(Stream<int> s, ICollection<int> @out)
    {
        IListener listener = s.ListenStrong(@out.Add);
        listener.Unlisten();
        return new WeakReference(listener);
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
}
