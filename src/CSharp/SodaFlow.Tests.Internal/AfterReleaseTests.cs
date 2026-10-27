using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

/// <summary>
///     Covers AfterReleaseImpl: an action runs on the same thread after the
///     transaction that holds the lock releases it.
/// </summary>
public sealed class AfterReleaseTests
{
    [Test]
    public async Task AnActionRunsAfterThePostsWithTheLockFree()
    {
        List<string> order = [];
        bool lockWasFree = false;

        TransactionInternal.Apply((_, _) =>
        {
            TransactionInternal.AfterReleaseImpl(() =>
            {
                order.Add("after release");
                lockWasFree = LockIsFree();
            });

            TransactionInternal.PostImpl(() => order.Add("post"));
            order.Add("body");
            return UnitInternal.Value;
        });

        await Assert.That(order)
            .IsEquivalentTo(expected: ["body", "post", "after release"], ordering: CollectionOrdering.Matching);

        await Assert.That(lockWasFree).IsTrue();
    }

    [Test]
    public async Task AnActionWithNoTransactionOpenRunsAtOnce()
    {
        bool ran = false;
        TransactionInternal.AfterReleaseImpl(() => ran = true);

        await Assert.That(ran).IsTrue();
    }

    // The code that asked for the action ran, thus the action runs also when the transaction fails.
    [Test]
    public async Task AnActionRunsAfterATransactionThatFails()
    {
        bool ran = false;

        await Assert.That(() =>
                TransactionInternal.Apply<UnitInternal>((_, _) =>
                {
                    TransactionInternal.AfterReleaseImpl(() => ran = true);
                    throw new InvalidOperationException("abort");
                }))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage("abort");

        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task AThrowFromAnActionComesAfterTheExceptionOfTheTransaction()
    {
        bool secondRan = false;
        AggregateException? thrown = null;

        try
        {
            TransactionInternal.Apply<UnitInternal>((_, _) =>
            {
                TransactionInternal.AfterReleaseImpl(static () => throw new ArgumentException("action"));
                TransactionInternal.AfterReleaseImpl(() => secondRan = true);
                throw new InvalidOperationException("transaction");
            });
        }
        catch (AggregateException e)
        {
            thrown = e;
        }

        await Assert.That(thrown).IsNotNull();

        // ReSharper disable once NullableWarningSuppressionIsUsed - The assertion above checks it.
        await Assert.That(thrown!.InnerExceptions[0]).IsTypeOf<InvalidOperationException>();
        await Assert.That(thrown.InnerExceptions[1]).IsTypeOf<ArgumentException>();
        await Assert.That(secondRan).IsTrue();
    }

    // A transaction that a start hook opens sees no open transaction, but the transaction of the hook
    // holds the lock. Thus, the action must wait for that transaction.
    [Test]
    public async Task AnActionFromAStartHookWaitsForTheTransactionThatHoldsTheLock()
    {
        int thread = Environment.CurrentManagedThreadId;
        bool armed = true;
        bool lockWasFree = false;
        bool ran = false;

        // A start hook stays for the life of the process, and other tests open transactions on
        // other threads. Thus, the hook acts one time and only on this thread.
        Transaction.OnStart(() =>
        {
            if (!armed || Environment.CurrentManagedThreadId != thread)
            {
                return;
            }

            armed = false;

            Transaction.RunVoid(() =>
                TransactionInternal.AfterReleaseImpl(() =>
                {
                    ran = true;
                    lockWasFree = LockIsFree();
                }));
        });

        Transaction.RunVoid(static () =>
        {
        });

        await Assert.That(ran).IsTrue();
        await Assert.That(lockWasFree).IsTrue();
    }

    // Opens a transaction on a different thread. Where this thread holds the lock, that transaction
    // waits for it, and the timeout ends the wait.
    private static bool LockIsFree() =>
        Task.Run(static () => Transaction.RunVoid(static () =>
            {
            }))
            .Wait(TimeSpan.FromSeconds(5));
}
