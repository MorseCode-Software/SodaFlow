using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     How the bindable values behave around the scheduler and the transaction lock.
/// </summary>
/// <remarks>
///     <para>
///         Separate from <see cref="BindableValueTests" /> because these need a scheduler that
///         queues rather than one that runs at the close of the current transaction. The two
///         produce the same ordering for a single write, which is why the other fixture can use
///         the immediate one throughout; they diverge as soon as a second write arrives before
///         the first one's notifications have been delivered, and that divergence is what is
///         under test here.
///     </para>
///     <para>
///         The last two failed when they were written, and are the reason the two-way value now
///         samples the cell in its update handler and counts the refreshes it has queued. Both
///         describe the same underlying mistake from different ends: treating the cached value as
///         a statement about the graph, when it is only a statement about the last time the two
///         were compared.
///     </para>
/// </remarks>
[TestFixture]
public class BindableValueConcurrencyTests
{
    /// <summary>
    ///     Stands in for a dispatcher. The point is that it queues: a real scheduler hands work to
    ///     another thread's message loop and returns, so anything posted during a write is still
    ///     pending when the setter returns.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class QueueingScheduler : IBindingScheduler
    {
        private readonly Queue<Action> queue = new();

        /// <inheritdoc />
        public bool IsOnBindingThread => true;

        /// <inheritdoc />
        public void Post(Action action) => this.queue.Enqueue(action);

        /// <summary>Runs everything queued, including anything queued while draining.</summary>
        /// <returns>How many actions ran.</returns>
        internal int RunAll()
        {
            int ran = 0;

            while (this.queue.Count > 0)
            {
                this.queue.Dequeue()();
                ran++;
            }

            return ran;
        }
    }

    // The documented contract on ImmediateBindingScheduler: it defers, but only to the end of the
    // transaction in flight, so a test never has to pump anything to see the notification.
    [Test]
    public void TheImmediateSchedulerHasNotifiedByTheTimeTheSendReturns()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayBindableValue<int> b = c.ToOneWayImpl(scheduler: BindingScheduler.Immediate);

        List<int> observed = new();
        b.PropertyChanged += (sender, _) =>
        {
            if (sender is IOneWayBindableValue<int> notified)
            {
                observed.Add(notified.Value);
            }
        };

        c.Send(1);

        CollectionAssert.AreEqual(
            expected: new[] { 1 },
            actual: observed,
            message: "the notification is delivered before Send returns, not left queued");
    }

    // Transactions are serialized process-wide, and that guarantee reaches the binding thread: a
    // two-way setter opens a transaction to push its write, so it waits for any transaction
    // already in flight. Worth pinning down - it is the reason a long transaction on a background
    // thread stalls the UI, and the reason a scheduler which blocks would deadlock against it.
    [Test]
    public void ASetterWaitsWhileAnotherThreadHoldsATransactionOpen()
    {
        CellSink<int> c = Cell.CreateSink(0);

        ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: BindingScheduler.Immediate);

        // TaskCompletionSource rather than an event: nothing here needs disposing, so the threads
        // below capture nothing whose lifetime is shorter than their own.
        TaskCompletionSource<bool> holding = new();
        TaskCompletionSource<bool> release = new();

        Thread holder =
            new(() => Transaction.RunVoid(() =>
            {
                holding.TrySetResult(true);
                release.Task.Wait();
            }))
            {
                IsBackground = true,
                Name = "transaction holder",
            };

        Thread setter =
            new(static state =>
            {
                if (state is ITwoWayBindableValue<int> target)
                {
                    target.Value = 5;
                }
            })
            {
                IsBackground = true,
                Name = "setter",
            };

        try
        {
            holder.Start();
            holding.Task.Wait();

            setter.Start(b);

            Assert.IsFalse(
                setter.Join(200),
                "the setter cannot complete while another thread holds the transaction open");

            release.TrySetResult(true);

            Assert.IsTrue(
                setter.Join(TimeSpan.FromSeconds(30)),
                "and completes once that transaction closes");

            Assert.AreEqual(expected: 5, actual: c.Sample(), message: "the write reached the graph");
        }
        finally
        {
            release.TrySetResult(true);
            holder.Join();
            setter.Join();
            b.Dispose();
        }
    }

    // An update carries the value captured when it fired. Delivered late - after a second write
    // has already moved the cached value on - it puts the older value back, and the view shows a
    // value the user has already replaced until the reconciliation behind it corrects the
    // correction. Sampling the cell in the handler, as the reconciliation does, would make a late
    // notification harmless.
    [Test]
    public void AStaleUpdateDoesNotRevertANewerValue()
    {
        QueueingScheduler scheduler = new();
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: scheduler);

        List<int> observed = new();
        b.PropertyChanged += (sender, _) =>
        {
            if (sender is ITwoWayBindableValue<int> notified)
            {
                observed.Add(notified.Value);
            }
        };

        b.Value = 1;
        b.Value = 2;

        scheduler.RunAll();

        CollectionAssert.DoesNotContain(
            observed,
            1,
            "the view is never told to go back to a value the caller has already replaced");

        Assert.AreEqual(expected: 2, actual: b.Value);
        Assert.AreEqual(expected: 2, actual: c.Sample());
    }

    // The cached value is a record of what the cell held when it was last sampled, not of what
    // the cell holds now. Between an update and the refresh it queues, the two disagree - and a
    // write whose value matches the stale cache is indistinguishable from a no-op, so the equality
    // check discards it. The caller asked for a value the graph does not hold, and nothing carries
    // the request anywhere.
    [Test]
    public void ASetterIsNotDiscardedWhileARefreshIsInFlight()
    {
        QueueingScheduler scheduler = new();
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: scheduler);

        // The graph moves on its own. The refresh this queues has not run, so the cached value
        // still reads 0 - true a moment ago, not true now.
        c.Send(1);

        Assume.That(b.Value, Is.EqualTo(0), "precondition: the refresh has not been delivered yet");

        // Something asks for the value the property still reports. The graph does not hold it.
        b.Value = 0;

        scheduler.RunAll();

        Assert.AreEqual(
            expected: 0,
            actual: c.Sample(),
            "a write is not dropped for matching a cached value the graph had already left behind");
    }

    // The constructor samples the cell and attaches its listener inside one transaction, and the
    // sample is stored before the attach. Constructing from inside a transaction which then goes
    // on to update that same cell is the case where the listener can fire before the constructor
    // has returned - so it is the one worth pinning down. The update must win, because it is
    // newer than the sample; losing it would mean an update had slipped through the gap between
    // sampling and subscribing, and reporting it as the value before the sample landed would mean
    // the constructor had overwritten it.
    [Test]
    public void OneWayConstructedInsideATransactionWhichThenFires()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayBindableValue<int> b = Transaction.Run(() =>
        {
            IOneWayBindableValue<int> created = c.ToOneWayImpl(scheduler: BindingScheduler.Immediate);

            c.Send(5);

            return created;
        });

        Assert.AreEqual(
            expected: 5,
            actual: b.Value,
            message: "the update fired after the listener was attached and is newer than the sample");
    }

    [Test]
    public void TwoWayConstructedInsideATransactionWhichThenFires()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = Transaction.Run(() =>
        {
            ITwoWayBindableValue<int> created = c.ToTwoWayImpl(scheduler: BindingScheduler.Immediate);

            c.Send(5);

            return created;
        });

        Assert.AreEqual(expected: 5, actual: b.Value);
    }

    /// <summary>
    ///     Runs <paramref name="body" /> on another thread and returns whatever it threw.
    /// </summary>
    private static Exception? CaughtOffTheBindingThread(object state, Action<object?> body)
    {
        Exception? caught = null;

        Thread thread =
            new(s =>
            {
                try
                {
                    body(s);
                }
                catch (Exception e)
                {
                    caught = e;
                }
            })
            {
                IsBackground = true,
                Name = "off the binding thread",
            };

        thread.Start(state);
        thread.Join();

        return caught;
    }

    // A context is enough to establish affinity; it does not have to be installed as Current,
    // because the scheduler captures the constructing thread alongside it.
    private static SynchronizationContextBindingScheduler AffineScheduler() => new(new SynchronizationContext());

    [Test]
    public void ReadingOneWayOffTheBindingThreadThrows()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayBindableValue<int> b = c.ToOneWayImpl(scheduler: AffineScheduler());

        Assert.AreEqual(
            expected: 0,
            actual: b.Value,
            message: "the constructing thread is the binding thread for this scheduler");

        Exception? caught = CaughtOffTheBindingThread(
            b,
            static state =>
            {
                if (state is IOneWayBindableValue<int> target)
                {
                    _ = target.Value;
                }
            });

        Assert.IsInstanceOf<InvalidOperationException>(
            caught,
            "reading from another thread is caught rather than left to return a stale value");
    }

    [Test]
    public void WritingTwoWayOffTheBindingThreadThrows()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: AffineScheduler());

        Exception? caught = CaughtOffTheBindingThread(
            b,
            static state =>
            {
                if (state is ITwoWayBindableValue<int> target)
                {
                    target.Value = 5;
                }
            });

        Assert.IsInstanceOf<InvalidOperationException>(caught);
        Assert.AreEqual(expected: 0, actual: c.Sample(), message: "and the write never reached the graph");
    }

    // The one that had no scheduler before, and so no way to be checked at all.
    [Test]
    public void WritingOneWayToSourceOffTheBindingThreadThrows()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayToSourceBindableValue<int> b =
            c.ToOneWayToSourceImpl(scheduler: AffineScheduler());

        Exception? caught = CaughtOffTheBindingThread(
            b,
            static state =>
            {
                if (state is IOneWayToSourceBindableValue<int> target)
                {
                    target.Value = 5;
                }
            });

        Assert.IsInstanceOf<InvalidOperationException>(caught);
        Assert.AreEqual(expected: 0, actual: c.Sample());
    }

    // Nothing changes for a scheduler with no affinity, which is what keeps every existing test
    // and every headless host working unchanged.
    [Test]
    public void TheImmediateSchedulerNeverRejectsAThread()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: BindingScheduler.Immediate);

        Exception? caught = CaughtOffTheBindingThread(
            b,
            static state =>
            {
                if (state is ITwoWayBindableValue<int> target)
                {
                    target.Value = 5;
                }
            });

        Assert.IsNull(caught, "the immediate scheduler runs work wherever it is called");
        Assert.AreEqual(expected: 5, actual: c.Sample());
    }

    // What a burst costs and what it produces, which is the question to answer before trying to
    // coalesce the refreshes: every queued refresh sees the same cell, so the first one to run
    // does the work and the rest find nothing to do.
    [Test]
    public void ABurstOfUpdatesQueuesARefreshEachButNotifiesOnce()
    {
        QueueingScheduler scheduler = new();
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: scheduler);

        List<int> observed = new();
        b.PropertyChanged += (sender, _) =>
        {
            if (sender is ITwoWayBindableValue<int> notified)
            {
                observed.Add(notified.Value);
            }
        };

        c.Send(1);
        c.Send(2);
        c.Send(3);

        int ran = scheduler.RunAll();

        Assert.AreEqual(expected: 3, actual: ran, message: "one refresh queued per update");

        CollectionAssert.AreEqual(
            expected: new[] { 3 },
            actual: observed,
            message: "but only one notification, because they all sample the same settled cell");

        Assert.AreEqual(expected: 3, actual: b.Value);
    }
}
