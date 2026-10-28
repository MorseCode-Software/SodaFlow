using System;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

/// <summary>
///     Covers a last action that queues work on its transaction. A last action
///     runs after the drain, thus that work has no drain to run in. The
///     transaction fails, and the work does not run in a subsequent transaction.
/// </summary>
public sealed class LastPhaseTests
{
    [Test]
    public async Task ALastActionThatQueuesAPrioritizedEntryFailsTheTransaction()
    {
        bool ran = false;

        await Assert.That(() =>
                TransactionInternal.Apply<UnitInternal>((trans, _) =>
                {
                    trans.Last(() => trans.Prioritized(node: new Node<int>(), action: _ => ran = true));
                    return UnitInternal.Value;
                }))
            .Throws<InvalidOperationException>();

        await Assert.That(ran).IsFalse();

        // The entry went into the static queue. A failure discards it, thus the next transaction
        // does not run it.
        TransactionInternal.Apply(static (_, _) => UnitInternal.Value);

        await Assert.That(ran).IsFalse();
    }

    [Test]
    public async Task ALastActionThatSendsFailsTheTransaction()
    {
        bool ran = false;

        await Assert.That(() =>
                TransactionInternal.Apply<UnitInternal>((trans, _) =>
                {
                    trans.Last(() => trans.Send(_ => ran = true));
                    return UnitInternal.Value;
                }))
            .Throws<InvalidOperationException>();

        await Assert.That(ran).IsFalse();
    }

    [Test]
    public async Task ALastActionThatSamplesFailsTheTransaction()
    {
        bool ran = false;

        await Assert.That(() =>
                TransactionInternal.Apply<UnitInternal>((trans, _) =>
                {
                    trans.Last(() => trans.Sample(() => ran = true));
                    return UnitInternal.Value;
                }))
            .Throws<InvalidOperationException>();

        await Assert.That(ran).IsFalse();
    }

    // A last action can queue a last action and a post. The last queue drains until it is empty,
    // and the posts run after it.
    [Test]
    public async Task ALastActionCanQueueALastActionAndAPost()
    {
        int lastRuns = 0;
        bool posted = false;

        TransactionInternal.Apply((trans, _) =>
        {
            trans.Last(() =>
            {
                lastRuns++;
                trans.Last(() => lastRuns++);
                TransactionInternal.PostImpl(() => posted = true);
            });

            return UnitInternal.Value;
        });

        await Assert.That(lastRuns).IsEqualTo(2);
        await Assert.That(posted).IsTrue();
    }
}
