using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Memory;

/// <summary>
///     Lifetime tests over the work that a child transaction defers.
/// </summary>
/// <remarks>
///     A posted action and a split item each run in a child transaction. A post or
///     a split from a child waits in a list that the process shares, until the
///     child commits or fails. That list lives as long as the process. Thus, an
///     entry that stays in it keeps its action alive permanently, and no test of
///     behavior sees that. These tests hold the list to its rule: it keeps
///     nothing after the child that used it.
/// </remarks>
public sealed class DeferredWorkGarbageCollectionTests
{
    [Test]
    public async Task APostFromAPostedActionThatCommitsIsCollected()
    {
        WeakReference held = PostFromAPostedAction(fail: false);

        Collect();

        await Assert.That(held.IsAlive)
            .IsFalse()
            .Because("the post ran, thus nothing should hold what its action captured");
    }

    [Test]
    public async Task APostFromAPostedActionThatThrowsIsCollected()
    {
        WeakReference held = PostFromAPostedAction(fail: true);

        Collect();

        await Assert.That(held.IsAlive)
            .IsFalse()
            .Because("the post went with the posted action that failed, thus nothing should hold its action");
    }

    [Test]
    public async Task AValueDeferredFromAPostedActionThatCommitsIsCollected()
    {
        StreamSink<object> sink = Stream.CreateSink<object>();
        int arrived = 0;
        using IStrongListener l = Operational.Defer(sink).ListenStrong(_ => arrived++);

        WeakReference value = SendFromAPostedAction(sink);

        Collect();

        await Assert.That(arrived).IsEqualTo(1);

        await Assert.That(value.IsAlive)
            .IsFalse()
            .Because("the deferred value arrived, thus nothing should hold it");
    }

    [Test]
    public async Task AValueDeferredFromAPostedActionWhoseTransactionFailsIsCollected()
    {
        StreamSink<object> sink = Stream.CreateSink<object>();
        int arrived = 0;
        using IStrongListener l = Operational.Defer(sink).ListenStrong(_ => arrived++);

        // The two maps put this listener after the defer. Thus, the defer queues the value before
        // the throw.
        using IStrongListener thrower =
            sink.Map(static value => value)
                .Map(static value => value)
                .ListenStrong(static _ => throw new InvalidOperationException("listener"));

        WeakReference value = SendFromAPostedAction(sink);

        Collect();

        await Assert.That(arrived).IsEqualTo(0);

        await Assert.That(value.IsAlive)
            .IsFalse()
            .Because("the deferred value went with the transaction that failed, thus nothing should hold it");
    }

    // Not inlined, thus no local of the test holds the object after this method returns.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PostFromAPostedAction(bool fail)
    {
        object captured = new();

        try
        {
            Transaction.RunVoid(() =>
                Transaction.Post(() =>
                {
                    Transaction.Post(() => GC.KeepAlive(captured));

                    if (fail)
                    {
                        throw new InvalidOperationException("post");
                    }
                }));
        }
        catch (InvalidOperationException)
        {
            // The test is about what stays after the failure.
        }

        return new WeakReference(captured);
    }

    // The send is in a posted action, thus the defer queues the value from a child transaction.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SendFromAPostedAction(StreamSink<object> sink)
    {
        object value = new();

        try
        {
            Transaction.RunVoid(() => Transaction.Post(() => sink.Send(value)));
        }
        catch (InvalidOperationException)
        {
            // The test is about what stays after the failure.
        }

        return new WeakReference(value);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
