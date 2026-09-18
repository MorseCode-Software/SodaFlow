using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     Tests the one rule that <see cref="IBindingScheduler.Post" /> states: an action must not
///     run immediately while a transaction is open. A scheduler on a dispatcher meets that rule by
///     its design. The immediate scheduler must be careful, and the tests use it. Thus each other
///     test in this assembly depends on it.
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

    [Test]
    public async Task ImmediateDefersUntilTheTransactionCloses()
    {
        bool ranInside = false;
        bool ranBeforeTheTransactionClosed = true;

        Transaction.RunVoid(() =>
        {
            BindingScheduler.Immediate.Post(() => ranInside = true);

            // Transaction.RunVoid has an Action parameter. Thus this code keeps the result here and
            // asserts it below, and does not await it at this point.
            ranBeforeTheTransactionClosed = ranInside;
        });

        await Assert.That(ranBeforeTheTransactionClosed)
            .IsFalse()
            .Because("running here would be inside the transaction");

        await Assert.That(ranInside).IsTrue().Because("and it still runs, once the transaction has closed");
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
