using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

public class TransactionTests
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
