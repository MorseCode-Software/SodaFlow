using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Memory;

/// <summary>
///     The two paths of a <see cref="Cleanup" />: the sweep runs the action when
///     the object is unreachable, and runs nothing after <c>CleanupNow</c> ran it.
/// </summary>
public sealed class CleanupGarbageCollectionTests
{
    // A counter on the heap, thus the action references no test field and the Cleanup can
    // become unreachable.
    private sealed class Counter
    {
        internal int Runs;
    }

    [Test]
    public async Task TheSweepRunsTheActionWhenTheCleanupIsUnreachable()
    {
        Counter counter = new();

        CreateAndDrop(counter);

        Collect();
        StreamListenerManager.Sweep();

        // The collection also signals the cleanup thread, and its sweep can take the entry
        // before the sweep above. That sweep releases the entry out of the registry lock, thus
        // the action can run a moment after the call above returns. The wait has a limit.
        SpinWait.SpinUntil(condition: () => Volatile.Read(ref counter.Runs) == 1, timeout: TimeSpan.FromSeconds(5));

        await Assert.That(counter.Runs).IsEqualTo(1);
    }

    [Test]
    public async Task TheSweepRunsNothingAfterCleanupNow()
    {
        Counter counter = new();

        CreateCleanUpNowAndDrop(counter);

        await Assert.That(counter.Runs).IsEqualTo(1).Because("CleanupNow runs the action at the call");

        Collect();
        StreamListenerManager.Sweep();

        await Assert.That(counter.Runs).IsEqualTo(1).Because("the sweep must not run the action a second time");
    }

    // Each test makes its Cleanup in a method of its own, with no inlining. Thus, no local in the
    // test method keeps it alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateAndDrop(Counter counter)
    {
        Cleanup cleanup = new(() => counter.Runs++);
        GC.KeepAlive(cleanup);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateCleanUpNowAndDrop(Counter counter)
    {
        Cleanup cleanup = new(() => counter.Runs++);
        cleanup.CleanupNow();
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
