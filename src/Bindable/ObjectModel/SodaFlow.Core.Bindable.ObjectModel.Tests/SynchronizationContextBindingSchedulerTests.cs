using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     Tests that <see cref="SynchronizationContextBindingScheduler" /> gives the
///     posts of one transaction to its context as one item. A control that gets
///     two related values, such as a list and the selected item, then gets the two
///     in one turn of the dispatcher.
/// </summary>
public sealed class SynchronizationContextBindingSchedulerTests
{
    [Test]
    public async Task ThePostsOfOneTransactionRunAsOneItemOfTheContext()
    {
        QueueingContext context = new();
        SynchronizationContextBindingScheduler scheduler = new(context);
        List<int> order = [];

        Transaction.RunVoid(() =>
        {
            scheduler.Post(() => order.Add(1));
            scheduler.Post(() => order.Add(2));
            scheduler.Post(() => order.Add(3));
        });

        int items = context.Count;
        int ranBeforeTheItem = order.Count;
        context.RunNext();

        await Assert.That(items).IsEqualTo(1);
        await Assert.That(ranBeforeTheItem).IsEqualTo(0).Because("the context runs the actions, not the transaction");
        await Assert.That(order).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task EachTransactionGetsItsOwnItem()
    {
        QueueingContext context = new();
        SynchronizationContextBindingScheduler scheduler = new(context);
        List<int> order = [];

        Transaction.RunVoid(() =>
        {
            scheduler.Post(() => order.Add(1));
            scheduler.Post(() => order.Add(2));
        });

        Transaction.RunVoid(() => scheduler.Post(() => order.Add(3)));

        int items = context.Count;
        context.RunNext();
        List<int> afterTheFirstItem = [.. order];
        context.RunNext();

        await Assert.That(items).IsEqualTo(2);

        await Assert.That(afterTheFirstItem)
            .IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching);

        await Assert.That(order).IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task APostWithNoTransactionOpenIsAnItemOfItsOwn()
    {
        QueueingContext context = new();
        SynchronizationContextBindingScheduler scheduler = new(context);
        List<int> order = [];

        scheduler.Post(() => order.Add(1));
        scheduler.Post(() => order.Add(2));

        int items = context.Count;
        context.RunAll();

        await Assert.That(items).IsEqualTo(2);
        await Assert.That(order).IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task ThePostsOfATransactionComeBeforeTheWorkThatItPosts()
    {
        QueueingContext context = new();
        SynchronizationContextBindingScheduler scheduler = new(context);
        List<int> order = [];

        Transaction.RunVoid(() =>
        {
            Transaction.Post(() => scheduler.Post(() => order.Add(2)));
            scheduler.Post(() => order.Add(1));
        });

        int items = context.Count;
        context.RunAll();

        await Assert.That(items).IsEqualTo(2).Because("the posted work runs in a transaction of its own");
        await Assert.That(order).IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task ATransactionThatThrowsStillGivesItsPostsToTheContext()
    {
        QueueingContext context = new();
        SynchronizationContextBindingScheduler scheduler = new(context);
        CellSink<int> source = Cell.CreateSink(0);
        bool ran = false;

        IListener posts = source.Updates().Listen(_ => scheduler.Post(() => ran = true));

        // Each map ranks the node one step lower. Thus, the post above runs first, and then this
        // listener stops the transaction.
        IListener throws =
            source.Updates()
                .Map(static value => value)
                .Map(static value => value)
                .Listen(static _ => throw new InvalidOperationException("from the graph"));

        Exception? thrown = null;

        try
        {
            source.Send(1);
        }
        catch (Exception e)
        {
            thrown = e;
        }
        finally
        {
            posts.Unlisten();
            throws.Unlisten();
        }

        context.RunAll();

        await Assert.That(thrown).IsNotNull();
        await Assert.That(ran).IsTrue().Because("a post that a failed transaction made still reaches the context");
    }

    [Test]
    public async Task AThrowFromOneActionDoesNotStopTheOthersInTheItem()
    {
        QueueingContext context = new();
        SynchronizationContextBindingScheduler scheduler = new(context);
        List<int> order = [];

        Transaction.RunVoid(() =>
        {
            scheduler.Post(() => order.Add(1));
            scheduler.Post(static () => throw new InvalidOperationException("posted"));
            scheduler.Post(() => order.Add(3));
        });

        await Assert.That(context.RunNext)
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage("posted");

        await Assert.That(order).IsEquivalentTo(expected: [1, 3], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task TwoThrowsInOneItemLeaveItAsOneAggregateException()
    {
        QueueingContext context = new();
        SynchronizationContextBindingScheduler scheduler = new(context);

        Transaction.RunVoid(() =>
        {
            scheduler.Post(static () => throw new InvalidOperationException("first"));
            scheduler.Post(static () => throw new ArgumentException("second"));
        });

        AggregateException? thrown = null;

        try
        {
            context.RunNext();
        }
        catch (AggregateException e)
        {
            thrown = e;
        }

        await Assert.That(thrown).IsNotNull();

        // ReSharper disable once NullableWarningSuppressionIsUsed - The assertion above checks it.
        await Assert.That(thrown!.InnerExceptions.Select(static e => e.Message))
            .IsEquivalentTo(expected: ["first", "second"], ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task BindablesThatOneTransactionUpdatesNotifyInOneItem()
    {
        QueueingContext context = new();
        SynchronizationContextBindingScheduler scheduler = new(context);
        CellSink<int> items = Cell.CreateSink(0);
        CellSink<string> selected = Cell.CreateSink("none");

        using IOneWayBindableValue<int> itemsValue = items.ToOneWayImpl(scheduler: scheduler);
        using ITwoWayBindableValue<string> selectedValue = selected.ToTwoWayImpl(scheduler: scheduler);

        List<string> notified = [];

        using IDisposable itemsListener = itemsValue.ListenForValueChanges(value => notified.Add($"items {value}"));

        using IDisposable selectedListener =
            selectedValue.ListenForValueChanges(value => notified.Add($"selected {value}"));

        Transaction.RunVoid(() =>
        {
            items.Send(3);
            selected.Send("b");
        });

        int contextItems = context.Count;
        context.RunNext();

        await Assert.That(contextItems).IsEqualTo(1);
        await Assert.That(context.Count).IsEqualTo(0).Because("the one item gave both changes");

        await Assert.That(notified)
            .IsEquivalentTo(expected: ["items 3", "selected b"], ordering: CollectionOrdering.Any);
    }

    /// <summary>
    ///     A synchronization context that keeps each item until the test runs it, as
    ///     a dispatcher keeps it until its thread is free.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class QueueingContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> items = new();

        /// <summary>Gets the number of items that did not run.</summary>
        public int Count => this.items.Count;

        /// <inheritdoc />
        public override void Post(SendOrPostCallback d, object? state) => this.items.Enqueue((d, state));

        /// <inheritdoc />
        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException("The scheduler must post and never send.");

        /// <summary>Runs the first item.</summary>
        public void RunNext()
        {
            (SendOrPostCallback callback, object? state) = this.items.Dequeue();
            callback(state);
        }

        /// <summary>Runs each item, and also an item that an item adds.</summary>
        public void RunAll()
        {
            while (this.items.Count > 0)
            {
                this.RunNext();
            }
        }
    }
}
