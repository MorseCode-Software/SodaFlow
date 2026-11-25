using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

/// <summary>
///     Covers ApplyInNewTransaction: the code runs in a new transaction, and an
///     open transaction makes it throw and not run the code.
/// </summary>
public sealed class ApplyInNewTransactionTests
{
    [Test]
    public async Task WithNoTransactionOpenTheCodeRunsInANewTransaction()
    {
        bool openBefore = TransactionInternal.HasCurrentTransaction();
        bool givenIsCurrent = false;

        int result = TransactionInternal.ApplyInNewTransaction(
            code: (trans, state) =>
            {
                givenIsCurrent = ReferenceEquals(objA: trans, objB: TransactionInternal.GetCurrentTransaction());

                return state + 1;
            },
            state: 6);

        await Assert.That(openBefore).IsFalse();
        await Assert.That(result).IsEqualTo(7);
        await Assert.That(givenIsCurrent).IsTrue();
        await Assert.That(TransactionInternal.HasCurrentTransaction()).IsFalse();
    }

    // The throw comes before the code runs and before the open transaction changes. Thus, that
    // transaction commits its own send when its body catches the throw.
    [Test]
    public async Task AnOpenTransactionThrowsAndKeepsWhatItDid()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> received = [];
        IListener listener = sink.Listen(received.Add);
        bool ran = false;
        Exception? thrown = null;

        TransactionInternal.Apply((_, _) =>
        {
            sink.Send(1);

            try
            {
                TransactionInternal.ApplyInNewTransaction(
                    code: (_, _) =>
                    {
                        ran = true;

                        return UnitInternal.Value;
                    },
                    state: UnitInternal.Value);
            }
            catch (InvalidOperationException e)
            {
                thrown = e;
            }

            return UnitInternal.Value;
        });

        listener.Dispose();

        await Assert.That(thrown).IsNotNull();
        await Assert.That(ran).IsFalse();
        await Assert.That(received).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);
    }

    // AfterReleaseImpl runs its actions after it sets the current transaction to null. Thus, an
    // action there gets a new transaction.
    [Test]
    public async Task AnActionAfterAReleaseRunsInANewTransaction()
    {
        List<string> order = [];

        TransactionInternal.Apply((_, _) =>
        {
            TransactionInternal.AfterReleaseImpl(() =>
                TransactionInternal.ApplyInNewTransaction(
                    code: static (_, o) =>
                    {
                        o.Add("after release");

                        return UnitInternal.Value;
                    },
                    state: order));

            order.Add("body");

            return UnitInternal.Value;
        });

        await Assert.That(order)
            .IsEquivalentTo(expected: ["body", "after release"], ordering: CollectionOrdering.Matching);
    }
}
