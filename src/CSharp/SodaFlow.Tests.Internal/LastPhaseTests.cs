using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
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
    public async Task AnAfterLastActionRunsAfterEachLastAction()
    {
        List<string> order = [];

        TransactionInternal.Apply((trans, _) =>
        {
            trans.AfterLast(commit: () => order.Add("after"), discard: static () => { });
            trans.Last(() => order.Add("last 1"));
            // A last action that adds a last action. The added one runs in the same drain, and the
            // after-last action runs after it.
            trans.Last(() => trans.Last(() => order.Add("last 2")));
            return UnitInternal.Value;
        });

        await Assert.That(order)
            .IsEquivalentTo(expected: ["last 1", "last 2", "after"], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task AnAfterLastDiscardRunsAfterEachDiscardAndSeesTheFailure()
    {
        List<string> order = [];
        bool failedWhenTheDiscardRan = false;
        bool failedWhenTheAfterLastDiscardRan = false;

        await Assert.That(() =>
                TransactionInternal.Apply<UnitInternal>((trans, _) =>
                {
                    TransactionOutcome outcome = trans.Outcome;

                    trans.AfterLast(
                        commit: static () => { },
                        discard: () =>
                        {
                            order.Add("after");
                            failedWhenTheAfterLastDiscardRan = outcome.Failed;
                        });

                    // The throw comes first in the queue. A last action that ran before the throw
                    // has committed, and no discard runs for it.
                    trans.Last(static () => throw new InvalidOperationException("abort"));

                    trans.Last(
                        commit: static () => { },
                        discard: () =>
                        {
                            order.Add("last");
                            failedWhenTheDiscardRan = outcome.Failed;
                        });

                    return UnitInternal.Value;
                }))
            .Throws<InvalidOperationException>();

        await Assert.That(order).IsEquivalentTo(expected: ["last", "after"], ordering: CollectionOrdering.Matching);
        await Assert.That(failedWhenTheDiscardRan).IsTrue();
        await Assert.That(failedWhenTheAfterLastDiscardRan).IsTrue();
    }

    [Test]
    public async Task TheOutcomeIsUnknownUntilEachLastActionRan()
    {
        bool knownInTheLastAction = true;
        bool committedInTheAfterLastAction = false;

        TransactionOutcome outcome =
            TransactionInternal.Apply((trans, _) =>
            {
                TransactionOutcome o = trans.Outcome;
                trans.AfterLast(commit: () => committedInTheAfterLastAction = o.Committed, discard: static () => { });
                trans.Last(() => knownInTheLastAction = o.Known);
                return o;
            });

        await Assert.That(knownInTheLastAction).IsFalse();
        await Assert.That(committedInTheAfterLastAction).IsTrue();
        await Assert.That(outcome.Committed).IsTrue();
        await Assert.That(outcome.Failed).IsFalse();
    }

    [Test]
    public async Task AFailureAfterTheLastActionsRanReplacesCommittedWithFailed()
    {
        TransactionOutcome? outcome = null;

        // The last action queues work, which the check after the after-last actions refuses.
        await Assert.That(() =>
                TransactionInternal.Apply<UnitInternal>((trans, _) =>
                {
                    outcome = trans.Outcome;
                    trans.Last(() => trans.Prioritized(node: new Node<int>(), action: static _ => { }));
                    return UnitInternal.Value;
                }))
            .Throws<InvalidOperationException>();

        await Assert.That(outcome?.Failed).IsTrue();
        await Assert.That(outcome?.Committed).IsFalse();
    }

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
