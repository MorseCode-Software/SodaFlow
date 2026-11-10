using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     Tests the one rule that <see cref="IBindingScheduler.Post" /> states: an
///     action must not run immediately while a transaction is open. A scheduler on
///     a dispatcher meets that rule by its design. The immediate scheduler must be
///     careful, and the tests use it. Thus, each other test in this assembly
///     depends on it.
/// </summary>
public sealed class BindingSchedulerTests
{
    [Test]
    public async Task ImmediateRunsInlineWhenNoTransactionIsOpen()
    {
        bool ran = false;

        BindingScheduler.Immediate.Post(() => ran = true);

        await Assert.That(ran).IsTrue().Because("with nothing in flight there is nothing to wait for");
    }

    // The scheduler does not open a transaction for the action. Thus, a send from the action commits
    // in a transaction of its own, and a throw after it does not discard it. A dispatcher does the
    // same.
    [Test]
    public async Task ImmediateOpensNoTransactionForAnAction()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> seen = [];
        using IListener l = sink.Listen(seen.Add);

        await Assert.That(() =>
                BindingScheduler.Immediate.Post(() =>
                {
                    sink.Send(1);

                    throw new InvalidOperationException("after the send");
                }))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(seen).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);
    }

    // The action runs while no thread holds the transaction lock. Thus, the action can wait for a
    // transaction on a different thread.
    [Test]
    public async Task ImmediateHoldsNoLockWhileAnActionRuns()
    {
        bool completed = false;

        BindingScheduler.Immediate.Post(() =>
            completed = Task.Run(static () => Transaction.RunVoid(static () => { }))
                .Wait(TimeSpan.FromSeconds(10)));

        await Assert.That(completed).IsTrue();
    }

    [Test]
    public async Task ImmediateDefersUntilTheTransactionCloses()
    {
        bool ranInside = false;
        bool ranBeforeTheTransactionClosed = true;

        Transaction.RunVoid(() =>
        {
            BindingScheduler.Immediate.Post(() => ranInside = true);

            // Transaction.RunVoid has an Action parameter. Thus, this code keeps the result here and
            // asserts it below, and does not await it at this point.
            ranBeforeTheTransactionClosed = ranInside;
        });

        await Assert.That(ranBeforeTheTransactionClosed)
            .IsFalse()
            .Because("running here would be inside the transaction");

        await Assert.That(ranInside).IsTrue().Because("and it still runs, once the transaction has closed");
    }

    // A bindable value keeps a flag and a count for each post, and only the post clears them. Thus,
    // a post must run if its transaction commits and also if it fails, as a post to a dispatcher
    // does.
    [Test]
    public async Task ImmediateRunsAPostFromATransactionThatFails()
    {
        bool ran = false;

        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    BindingScheduler.Immediate.Post(() => ran = true);

                    throw new InvalidOperationException("the transaction fails");
                }))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(ran).IsTrue();
    }

    // One action that throws does not stop the actions after it. The throw comes out of the
    // transaction, after each action ran.
    [Test]
    public async Task ImmediateRunsEachPostAfterOneThrows()
    {
        List<int> ran = [];

        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    BindingScheduler.Immediate.Post(() => ran.Add(1));
                    BindingScheduler.Immediate.Post(static () => throw new InvalidOperationException("second"));
                    BindingScheduler.Immediate.Post(() => ran.Add(3));
                }))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(ran).IsEquivalentTo(expected: [1, 3], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task ImmediatePreservesOrdering()
    {
        List<int> order = [];

        Transaction.RunVoid(() =>
        {
            BindingScheduler.Immediate.Post(() => order.Add(1));
            BindingScheduler.Immediate.Post(() => order.Add(2));
            BindingScheduler.Immediate.Post(() => order.Add(3));
        });

        await Assert.That(order).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task ImmediateRejectsANullAction() =>
        // ReSharper disable once NullableWarningSuppressionIsUsed - Testing for exception on null.
        await Assert.That(static () => BindingScheduler.Immediate.Post(null!)).ThrowsExactly<ArgumentNullException>();

    // This is the cause of the rule. A notification from inside the transaction stops a handler
    // from sending into a different sink. A view model does that frequently, and a real
    // dispatcher does not prevent it.
    [Test]
    public async Task AHandlerCanSendIntoAnotherSink()
    {
        CellSink<int> source = Cell.CreateSink(0);
        CellSink<int> other = Cell.CreateSink(0);

        using IOneWayBindableValue<int> b = source.ToOneWayImpl(scheduler: BindingScheduler.Immediate);

        using IDisposable _ = b.ListenForValueChanges(value => other.Send(value * 2));

        await Assert.That(() => source.Send(21)).ThrowsNothing();
        await Assert.That(other.Sample()).IsEqualTo(42);
    }
}
