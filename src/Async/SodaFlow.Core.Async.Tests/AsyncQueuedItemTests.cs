using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Async.Tests;

/// <summary>
///     Covers the cancel of one tracked item. A cancellation is an effect out of
///     the graph. Thus, a cancel in a transaction waits for the commit, and a
///     transaction that fails cancels nothing.
/// </summary>
public sealed class AsyncQueuedItemTests
{
    [Test]
    public async Task Cancel_WithNoTransactionOpen_CancelsTheTokenNow()
    {
        using AsyncItemCancellationSource cancellation = new();
        AsyncMapBase.AsyncQueuedItem<int> item = NewItem(cancellation: cancellation, report: TestUtil.Unexpected);

        item.Cancel();

        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task Cancel_InATransaction_CancelsTheTokenAtTheCommit()
    {
        using AsyncItemCancellationSource cancellation = new();
        AsyncMapBase.AsyncQueuedItem<int> item = NewItem(cancellation: cancellation, report: TestUtil.Unexpected);

        bool canceledInTheTransaction =
            TransactionInternal.Apply((_, _) =>
            {
                item.Cancel();

                // ReSharper disable once AccessToDisposedClosure - The transaction ends before the disposal.
                return cancellation.IsCancellationRequested;
            });

        await Assert.That(canceledInTheTransaction).IsFalse().Because("the cancel waits for the commit");
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
    }

    // The cancel has two parts. A posted action after the cancel runs after the commit, with the
    // lock. There, the record shows the cancel and the token does not. After the release of the
    // lock, the token shows it also. Thus, the callbacks of the token run with no transaction open.
    [Test]
    public async Task Cancel_InATransaction_RecordsTheCancelWithTheLockAndCancelsTheTokenAfterTheRelease()
    {
        using AsyncItemCancellationSource cancellation = new();
        AsyncMapBase.AsyncQueuedItem<int> item = NewItem(cancellation: cancellation, report: TestUtil.Unexpected);
        List<bool> inACallback = [];

        // The registration lives as long as the source, which this test disposes.
        _ = cancellation.Token.Register(() => inACallback.Add(Transaction.IsActive()));

        List<(bool Recorded, bool Canceled)> atThePost = [];

        TransactionInternal.Apply((_, _) =>
        {
            item.Cancel();

            // The post of the cancel comes first, thus this one runs after it.
            // ReSharper disable AccessToDisposedClosure - The transaction ends before the disposal.
            TransactionInternal.PostImpl(() =>
                atThePost.Add((cancellation.IsCancelCommitted, cancellation.IsCancellationRequested)));

            // ReSharper restore AccessToDisposedClosure

            return UnitInternal.Value;
        });

        await Assert.That(atThePost.SequenceEqual([(true, false)]))
            .IsTrue()
            .Because("the record comes at the commit, and the token changes after the release of the lock");

        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(inACallback.SequenceEqual([false])).IsTrue();
    }

    // The end of an item can come on a different thread while the cancel runs out of the lock. A
    // disposal during Cancel() is not permitted, thus the end leaves the disposal to the cancel. Here,
    // a callback of the token gives the end during the cancel.
    [Test]
    public async Task DisposeAtTheEnd_DuringTheCancel_DisposesTheSourceAfterTheCancel()
    {
        AsyncItemCancellationSource cancellation = new();
        AsyncMapBase.AsyncQueuedItem<int> item = NewItem(cancellation: cancellation, report: TestUtil.Unexpected);
        List<bool> tokenReadsInTheCallback = [];

        _ = cancellation.Token.Register(() =>
        {
            cancellation.DisposeAtTheEnd();
            tokenReadsInTheCallback.Add(ReadsTheToken(cancellation));
        });

        item.Cancel();

        await Assert.That(tokenReadsInTheCallback.SequenceEqual([true])).IsTrue().Because("the cancel runs");
        await Assert.That(ReadsTheToken(cancellation)).IsFalse().Because("the cancel disposed the source at its end");
        await Assert.That(item.Cancel).ThrowsNothing();
    }

    [Test]
    public async Task Cancel_InATransactionThatFails_CancelsNothing()
    {
        using AsyncItemCancellationSource cancellation = new();
        AsyncMapBase.AsyncQueuedItem<int> item = NewItem(cancellation: cancellation, report: TestUtil.Unexpected);

        await Assert.That(() =>
                TransactionInternal.Apply<UnitInternal>((_, _) =>
                {
                    item.Cancel();

                    throw new InvalidOperationException("The test fails this transaction.");
                }))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(cancellation.IsCancellationRequested).IsFalse();
    }

    // The test for a transaction reads the transaction of this thread. A transaction that a
    // different thread holds open at the same time is not the transaction of this cancel. Thus,
    // the cancel runs immediately, and does not wait for that transaction to end.
    [Test]
    public async Task Cancel_WhileADifferentThreadHoldsATransactionOpen_CancelsTheTokenNow()
    {
        using AsyncItemCancellationSource cancellation = new();
        AsyncMapBase.AsyncQueuedItem<int> item = NewItem(cancellation: cancellation, report: TestUtil.Unexpected);
        using ManualResetEventSlim otherOpened = new(false);
        using ManualResetEventSlim otherCanEnd = new(false);
        using ManualResetEventSlim otherEnded = new(false);

        // The wait has a limit. Thus, a cancel that waits for this transaction ends the test with a
        // failure, and does not stop the test run.
        // ReSharper disable AccessToDisposedClosure - The test awaits this task before the disposals.
        // ReSharper disable once MethodSupportsCancellation - The token of this test belongs to the item, and its
        // cancel must not end this task.
        Task other =
            Task.Run(() =>
                TransactionInternal.Apply((_, _) =>
                {
                    otherOpened.Set();
                    otherCanEnd.Wait(TimeSpan.FromSeconds(5));
                    otherEnded.Set();

                    return UnitInternal.Value;
                }));

        // ReSharper restore AccessToDisposedClosure

        bool opened = otherOpened.Wait(TimeSpan.FromSeconds(5));

        item.Cancel();

        bool canceled = cancellation.IsCancellationRequested;
        bool ended = otherEnded.IsSet;

        otherCanEnd.Set();
        await other;

        await Assert.That(opened).IsTrue();
        await Assert.That(canceled).IsTrue();

        await Assert.That(ended)
            .IsFalse()
            .Because("the cancel must not wait for the transaction of a different thread");
    }

    // The pipeline reads this record where it must know of a cancel before the commit: for a value
    // that a strategy refuses. The record belongs to one transaction. A transaction that fails must
    // not leave the item as canceled for the transactions after it.
    [Test]
    public async Task IsCancelAsked_IsTrueOnlyInTheTransactionThatAskedUntilTheCancelCommits()
    {
        using AsyncItemCancellationSource cancellation = new();
        AsyncMapBase.AsyncQueuedItem<int> item = NewItem(cancellation: cancellation, report: TestUtil.Unexpected);
        List<bool> asked = [TransactionInternal.Apply((_, _) => item.IsCancelAsked())];

        try
        {
            TransactionInternal.Apply<UnitInternal>((_, _) =>
            {
                item.Cancel();
                asked.Add(item.IsCancelAsked());

                throw new InvalidOperationException("The test fails this transaction.");
            });
        }
        catch (InvalidOperationException)
        {
        }

        asked.Add(TransactionInternal.Apply((_, _) => item.IsCancelAsked()));

        TransactionInternal.Apply((_, _) =>
        {
            item.Cancel();

            return UnitInternal.Value;
        });

        asked.Add(TransactionInternal.Apply((_, _) => item.IsCancelAsked()));

        await Assert.That(asked.SequenceEqual([false, true, false, true])).IsTrue();
    }

    // A callback on the token can throw. After the commit that throw has no caller, thus it goes to
    // the report, which is the handleException of the pipeline.
    [Test]
    public async Task Cancel_InATransaction_GivesAThrowFromACallbackOfTheTokenToTheReport()
    {
        using AsyncItemCancellationSource cancellation = new();
        List<Exception> reported = [];
        InvalidOperationException fromCallback = new("The callback of the token throws.");
        AsyncMapBase.AsyncQueuedItem<int> item = NewItem(cancellation: cancellation, report: reported.Add);

        // The registration lives as long as the source, which this test disposes.
        _ = cancellation.Token.Register(() => throw fromCallback);

        TransactionInternal.Apply((_, _) =>
        {
            item.Cancel();

            return UnitInternal.Value;
        });

        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(reported.Count).IsEqualTo(1);

        await Assert.That(((AggregateException)reported[0]).InnerExceptions.Single())
            .IsSameReferenceAs(fromCallback);
    }

    // With no transaction open there is a caller, and it gets the throw as it did before.
    [Test]
    public async Task Cancel_WithNoTransactionOpen_GivesAThrowFromACallbackOfTheTokenToTheCaller()
    {
        using AsyncItemCancellationSource cancellation = new();
        InvalidOperationException fromCallback = new("The callback of the token throws.");
        AsyncMapBase.AsyncQueuedItem<int> item = NewItem(cancellation: cancellation, report: TestUtil.Unexpected);

        // The registration lives as long as the source, which this test disposes.
        _ = cancellation.Token.Register(() => throw fromCallback);

        await Assert.That(item.Cancel).ThrowsExactly<AggregateException>();
    }

    // The pipeline disposes the source of an item at its end, and a strategy can hold the item
    // after that.
    [Test]
    public async Task Cancel_AfterTheDisposalOfTheSource_DoesNothing()
    {
        AsyncItemCancellationSource cancellation = new();
        AsyncMapBase.AsyncQueuedItem<int> item = NewItem(cancellation: cancellation, report: TestUtil.Unexpected);

        cancellation.DisposeAtTheEnd();

        item.Cancel();

        TransactionInternal.Apply((_, _) =>
        {
            item.Cancel();

            return UnitInternal.Value;
        });

        await Assert.That(cancellation.IsCancellationRequested).IsFalse();
    }

    private static bool ReadsTheToken(CancellationTokenSource source)
    {
        try
        {
            _ = source.Token;

            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static AsyncMapBase.AsyncQueuedItem<int> NewItem(
        AsyncItemCancellationSource cancellation,
        Action<Exception> report) =>
        new(id: Guid.NewGuid(), value: 1, cancellation: cancellation, reportCancelFailure: report);
}
