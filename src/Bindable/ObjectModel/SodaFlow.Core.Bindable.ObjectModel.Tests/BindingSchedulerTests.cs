using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     Covers the one rule <see cref="IBindingScheduler.Post" /> states: an action must never run
///     synchronously while a transaction is in flight. A dispatcher-backed scheduler satisfies it by
///     construction; the immediate one has to be careful, and it is the one tests run against, so
///     everything else in this assembly depends on it getting this right.
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

            // Transaction.RunVoid takes an Action, so what happened inside is recorded here and
            // asserted below rather than awaited in place.
            ranBeforeTheTransactionClosed = ranInside;
        });

        await Assert.That(ranBeforeTheTransactionClosed).IsFalse().Because("running here would be inside the transaction");

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

        await Assert.That(order).IsEquivalentTo([1, 2, 3], CollectionOrdering.Matching);
    }

    [Test]
    public async Task ImmediateRejectsANullAction() =>
        // ReSharper disable once NullableWarningSuppressionIsUsed - Testing for exception on null.
        await Assert.That(static () => BindingScheduler.Immediate.Post(null!)).ThrowsExactly<ArgumentNullException>();

    // The reason the rule exists. A notification raised from inside the transaction would leave a
    // handler unable to send into another sink - which is an ordinary thing for a view model to do,
    // and something a real dispatcher would never have prevented.
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
