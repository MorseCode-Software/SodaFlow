using System;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

public sealed class TransactionTests
{
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

    // The root commits before it runs its posted actions. A posted action that throws made the root
    // discard itself. Its outcome then showed a failure, although each value that it sent stayed in
    // the graph.
    [Test]
    public async Task ARootKeepsItsCommittedOutcomeWhenAPostedActionThrows()
    {
        TransactionOutcome? outcome = null;

        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    outcome = TransactionInternal.CurrentOutcome();
                    Transaction.Post(static () => throw new InvalidOperationException("posted"));
                }))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(outcome?.Committed).IsTrue();
    }
}
