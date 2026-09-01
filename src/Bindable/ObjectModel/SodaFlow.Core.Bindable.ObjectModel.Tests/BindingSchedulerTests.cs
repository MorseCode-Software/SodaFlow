using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     Covers the one rule <see cref="IBindingScheduler.Post" /> states: an action must never run
///     synchronously while a transaction is in flight. A dispatcher-backed scheduler satisfies it by
///     construction; the immediate one has to be careful, and it is the one tests run against, so
///     everything else in this assembly depends on it getting this right.
/// </summary>
[TestFixture]
public class BindingSchedulerTests
{
    [Test]
    public void ImmediateRunsInlineWhenNoTransactionIsOpen()
    {
        bool ran = false;

        BindingScheduler.Immediate.Post(() => ran = true);

        Assert.IsTrue(condition: ran, message: "with nothing in flight there is nothing to wait for");
    }

    [Test]
    public void ImmediateDefersUntilTheTransactionCloses()
    {
        bool ranInside = false;

        Transaction.RunVoid(() =>
        {
            BindingScheduler.Immediate.Post(() => ranInside = true);

            Assert.IsFalse(condition: ranInside, message: "running here would be inside the transaction");
        });

        Assert.IsTrue(condition: ranInside, message: "and it still runs, once the transaction has closed");
    }

    [Test]
    public void ImmediatePreservesOrdering()
    {
        List<int> order = new();

        Transaction.RunVoid(() =>
        {
            BindingScheduler.Immediate.Post(() => order.Add(1));
            BindingScheduler.Immediate.Post(() => order.Add(2));
            BindingScheduler.Immediate.Post(() => order.Add(3));
        });

        CollectionAssert.AreEqual(expected: new[] { 1, 2, 3 }, actual: order);
    }

    [Test]
    public void ImmediateRejectsANullAction() =>
        Assert.Throws<ArgumentNullException>(() => BindingScheduler.Immediate.Post(null!));

    // The reason the rule exists. A notification raised from inside the transaction would leave a
    // handler unable to send into another sink - which is an ordinary thing for a view model to do,
    // and something a real dispatcher would never have prevented.
    [Test]
    public void AHandlerCanSendIntoAnotherSink()
    {
        CellSink<int> source = Cell.CreateSink(0);
        CellSink<int> other = Cell.CreateSink(0);

        using (IOneWayBindableValue<int> b = source.ToOneWayImpl(scheduler: BindingScheduler.Immediate))
        {
            b.PropertyChanged += (_, __) => other.Send(b.Value * 2);

            Assert.DoesNotThrow(() => source.Send(21));
        }

        Assert.AreEqual(expected: 42, actual: other.Sample());
    }
}