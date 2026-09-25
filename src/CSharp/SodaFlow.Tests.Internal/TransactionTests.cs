using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

public sealed class TransactionTests
{
    /// <summary>The transaction that is open now.</summary>
    /// <returns>That transaction.</returns>
    /// <exception cref="InvalidOperationException">No transaction is open.</exception>
    private static TransactionInternal CurrentTransaction() =>
        TransactionInternal.GetCurrentTransaction()
        ?? throw new InvalidOperationException("No transaction is open.");

    /// <summary>
    ///     Sends on a sink whose listener throws, in one transaction, thus the transaction fails
    ///     while it propagates. That is the failure that reaches the catch in Close, and the one
    ///     that discards the queue of posted actions.
    /// </summary>
    /// <param name="inTransaction">Runs with the failing transaction open.</param>
    /// <returns>The exception that the transaction gave.</returns>
    private static Exception RunFailingTransaction(Action inTransaction)
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        IListener listener = sink.ListenStrong(static _ => throw new InvalidOperationException("listener"));

        try
        {
            Transaction.RunVoid(() =>
            {
                inTransaction();

                sink.Send(1);
            });
        }
        catch (Exception e)
        {
            return e;
        }
        finally
        {
            listener.Unlisten();
        }

        throw new InvalidOperationException("The transaction did not fail.");
    }

    [Test]
    public async Task OnFailureRunsWhenTheTransactionFails()
    {
        bool ran = false;

        Exception thrown =
            RunFailingTransaction(() =>
                CurrentTransaction().OnFailureInternal(() => ran = true));

        await Assert.That(ran).IsTrue().Because("a transaction that fails runs each failure action");

        await Assert.That(thrown)
            .IsTypeOf<InvalidOperationException>()
            .Because("the caller sees the exception of the transaction where no action throws");
    }

    [Test]
    public async Task OnFailureDoesNotRunWhenTheTransactionSucceeds()
    {
        bool ran = false;

        Transaction.RunVoid(() =>
            CurrentTransaction().OnFailureInternal(() => ran = true));

        await Assert.That(ran).IsFalse().Because("a transaction that closes releases nothing");
    }

    [Test]
    public async Task OnFailureKeepsEachThrowBesideTheExceptionOfTheTransaction()
    {
        List<string> ran = [];

        Exception thrown =
            RunFailingTransaction(() =>
            {
                TransactionInternal transaction = CurrentTransaction();

                transaction.OnFailureInternal(() =>
                {
                    ran.Add("first");

                    throw new NotSupportedException("first");
                });

                transaction.OnFailureInternal(() => ran.Add("second"));

                transaction.OnFailureInternal(() =>
                {
                    ran.Add("third");

                    throw new FormatException("third");
                });
            });

        await Assert.That(ran)
            .IsEquivalentTo(expected: ["first", "second", "third"], ordering: CollectionOrdering.Matching)
            .Because("a throw from one action does not stop the actions after it");

        await Assert.That(thrown)
            .IsTypeOf<AggregateException>()
            .Because("the caller gets each failure where an action throws");

        AggregateException aggregate = (AggregateException)thrown;

        await Assert.That(aggregate.InnerExceptions.Count).IsEqualTo(3);

        await Assert.That(aggregate.InnerExceptions[0])
            .IsTypeOf<InvalidOperationException>()
            .Because("the exception of the transaction comes first");

        await Assert.That(aggregate.InnerExceptions[1]).IsTypeOf<NotSupportedException>();
        await Assert.That(aggregate.InnerExceptions[2]).IsTypeOf<FormatException>();
    }

    [Test]
    public async Task PostSeeOutside()
    {
        OperationCanceledException? actual = null;
        AutoResetEvent re = new(false);

        using (CancellationTokenSource cts = new())
        {
            Task task =
                // ReSharper disable once MethodSupportsCancellation - We want to observe cancellation within the
                // running task.
                Task.Run(() =>
                {
                    Transaction.Post(() =>
                    {
                        re.Set();

                        Thread.Sleep(5000);

                        // ReSharper disable once AccessToDisposedClosure - Disposable will happen after this is
                        // reached.
                        cts.Token.ThrowIfCancellationRequested();
                    });
                });

            re.WaitOne();

            // ReSharper disable once MethodHasAsyncOverload - CancelAsync arrived in .NET 8,
            // and this compiles for net472 as well.
            cts.Cancel();

            try
            {
                await task;
            }
            catch (OperationCanceledException e)
            {
                actual = e;
            }
        }

        await Assert.That(actual).IsNotNull();
    }
}
