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
///     the graph, thus a cancel in a transaction waits for the commit, and a
///     transaction that fails cancels nothing.
/// </summary>
public sealed class AsyncQueuedItemTests
{
    [Test]
    public async Task Cancel_WithNoTransactionOpen_CancelsTheTokenNow()
    {
        using CancellationTokenSource cancellation = new();
        AsyncMapBase.AsyncQueuedItem<int> item = NewItem(cancellation: cancellation, report: TestUtil.Unexpected);

        item.Cancel();

        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task Cancel_InATransaction_CancelsTheTokenAtTheCommit()
    {
        using CancellationTokenSource cancellation = new();
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

    [Test]
    public async Task Cancel_InATransactionThatFails_CancelsNothing()
    {
        using CancellationTokenSource cancellation = new();
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

    // The pipeline reads this record where it must know of a cancel before the commit: for a value
    // that a strategy refuses. The record belongs to one transaction. A transaction that fails must
    // not leave the item as canceled for the transactions after it.
    [Test]
    public async Task IsCancelAsked_IsTrueOnlyInTheTransactionThatAskedUntilTheCancelCommits()
    {
        using CancellationTokenSource cancellation = new();
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
        using CancellationTokenSource cancellation = new();
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
        using CancellationTokenSource cancellation = new();
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
        CancellationTokenSource cancellation = new();
        AsyncMapBase.AsyncQueuedItem<int> item = NewItem(cancellation: cancellation, report: TestUtil.Unexpected);

        cancellation.Dispose();

        item.Cancel();

        TransactionInternal.Apply((_, _) =>
        {
            item.Cancel();

            return UnitInternal.Value;
        });

        await Assert.That(cancellation.IsCancellationRequested).IsFalse();
    }

    private static AsyncMapBase.AsyncQueuedItem<int> NewItem(
        CancellationTokenSource cancellation,
        Action<Exception> report) =>
        new(id: Guid.NewGuid(), value: 1, cancellation: cancellation, reportCancelFailure: report);
}
