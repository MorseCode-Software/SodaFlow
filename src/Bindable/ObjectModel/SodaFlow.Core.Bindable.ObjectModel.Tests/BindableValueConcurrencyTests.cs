using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     The behavior of the bindable values with the scheduler and the transaction lock.
/// </summary>
/// <remarks>
///     <para>
///         These tests are not in <see cref="BindableValueTests" />, because they need a
///         scheduler that queues and not one that runs at the close of the current transaction.
///         The two give the same sequence for one write, which is why the other test class uses
///         the immediate scheduler. The two are different when a second write arrives before SodaFlow
///         delivers the notifications of the first write. These tests cover that difference.
///     </para>
///     <para>
///         The last two tests failed when someone wrote them. They are the cause of two
///         changes in the two-way value: it now samples the cell in its update handler, and it
///         counts the refresh operations in its queue. The two tests show the same error from
///         different directions. That error is to read the cached value as a fact about the
///         graph, when it is only a fact about the last test of the two.
///     </para>
/// </remarks>
public sealed class BindableValueConcurrencyTests
{
    // This is the contract of ImmediateBindingScheduler. It defers, but only to the end of the
    // open transaction. Thus, a test does not have to run a message loop to see the
    // notification.
    [Test]
    public async Task TheImmediateSchedulerHasNotifiedByTheTimeTheSendReturns()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayBindableValue<int> b = c.ToOneWayImpl(scheduler: BindingScheduler.Immediate);

        List<int> observed = [];

        using IDisposable _ = b.ListenForValueChanges(observed.Add);

        c.Send(1);

        await Assert.That(observed)
            .IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching)
            .Because("the notification is delivered before Send returns, not left queued");
    }

    // SodaFlow runs one transaction at a time across the process, and that guarantee reaches
    // the binding thread. A two-way setter opens a transaction to send its write, thus it waits
    // for each open transaction. This test holds that behavior. It is the cause of two results: a
    // transaction that runs for a long time on a background thread stops the UI, and a scheduler
    // that waits causes a deadlock.
    [Test]
    public async Task ASetterWaitsWhileAnotherThreadHoldsATransactionOpen()
    {
        CellSink<int> c = Cell.CreateSink(0);

        ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: BindingScheduler.Immediate);

        // This uses a TaskCompletionSource and not an event, because no object here needs a call
        // to Dispose. Thus, the threads below capture no object with a life shorter than their
        // own.
        TaskCompletionSource<bool> holding = new();
        TaskCompletionSource<bool> release = new();

        Thread holder =
            new(() =>
                Transaction.RunVoid(() =>
                {
                    holding.TrySetResult(true);
                    release.Task.Wait();
                })) { IsBackground = true, Name = "transaction holder" };

        Thread setter =
            new(static state =>
            {
                if (state is ITwoWayBindableValue<int> target)
                {
                    target.Value = 5;
                }
            }) { IsBackground = true, Name = "setter" };

        try
        {
            holder.Start();
            holding.Task.Wait();

            setter.Start(b);

            await Assert.That(setter.Join(200))
                .IsFalse()
                .Because("the setter cannot complete while another thread holds the transaction open");

            release.TrySetResult(true);

            await Assert.That(setter.Join(TimeSpan.FromSeconds(30)))
                .IsTrue()
                .Because("and completes once that transaction closes");

            await Assert.That(c.Sample()).IsEqualTo(5).Because("the write reached the graph");
        }
        finally
        {
            release.TrySetResult(true);
            holder.Join();
            setter.Join();
            b.Dispose();
        }
    }

    // An update carries the value that SodaFlow captured at the firing. When it arrives late,
    // after a second write moved the cached value, it puts the previous value back. The view then
    // shows a value that the user replaced, until the second step corrects it. A sample of the
    // cell in the handler, which is what the second step does, makes a late notification safe.
    [Test]
    public async Task AStaleUpdateDoesNotRevertANewerValue()
    {
        QueueingScheduler scheduler = new();
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: scheduler);

        List<int> observed = [];

        using IDisposable _ = b.ListenForValueChanges(observed.Add);

        b.Value = 1;
        b.Value = 2;

        scheduler.RunAll();

        await Assert.That(observed)
            .DoesNotContain(1)
            .Because("the view is never told to go back to a value the caller has already replaced");

        await Assert.That(b.Value).IsEqualTo(2);
        await Assert.That(c.Sample()).IsEqualTo(2);
    }

    // The same two writes on the other path. A setter usually sends immediately, because with no
    // open transaction PostWrite runs the write there. Thus, the first write reaches the
    // cell before the code calls the second setter, and no refresh can sample between the two.
    // From inside a transaction the write defers, and the two stay in the post queue until that
    // transaction closes. In that arrangement a refresh that samples before that point gives the view the
    // previous value. This test holds the correct behavior: the two writes drain before a refresh
    // runs, and each refresh samples and carries no value, thus neither one can do that.
    [Test]
    public async Task TwoWritesInsideOneTransactionDoNotRevert()
    {
        QueueingScheduler scheduler = new();
        StreamSink<string> edits = Stream.CreateSink<string>();

        // This changes the value, thus the value of the cell is different from the value that the
        // test wrote. A change back to the previous value then shows, and an equality test
        // does not hide it.
        Cell<string> upperCased = edits.Map(static v => v.ToUpperInvariant()).Hold(string.Empty);

        using ITwoWayBindableValue<string> b =
            upperCased.ToTwoWayImpl(editsStreamSink: edits, scheduler: scheduler);

        List<string> observed = [];

        using IDisposable _ = b.ListenForValueChanges(observed.Add);

        // The lambda runs immediately in RunVoid, thus it cannot have a longer life than the
        // using block.
        // ReSharper disable AccessToDisposedClosure
        Transaction.RunVoid(() =>
        {
            b.Value = "a";
            b.Value = "b";
        });

        // ReSharper restore AccessToDisposedClosure

        scheduler.RunAll();

        await Assert.That(observed)
            .DoesNotContain("A")
            .Because("the deferred first write never reaches the view after the second has replaced it");

        await Assert.That(b.Value).IsEqualTo("B");
        await Assert.That(upperCased.Sample()).IsEqualTo("B");
    }

    // The cached value is a record of the cell at the last sample, and not of the cell now.
    // Between an update and the refresh that it queues, the two disagree. A write with a value
    // equal to the stale cache then looks the same as a write that changes nothing, thus the
    // equality test discards it. The caller asked for a value that the graph does not hold, and
    // no code carries that request.
    [Test]
    public async Task ASetterIsNotDiscardedWhileARefreshIsInFlight()
    {
        QueueingScheduler scheduler = new();
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: scheduler);

        // The graph changes without a write. The refresh that this queues did not run, thus the
        // cached value is 0. That was correct before this point and is not correct now.
        c.Send(1);

        // This is an assertion and not an assumption. The scheduler queues and does not run,
        // thus this condition is sure. A test that no longer meets its own precondition must
        // report that.
        await Assert.That(b.Value)
            .IsEqualTo(0)
            .Because("precondition: the refresh has not been delivered yet");

        // Some code asks for the value that the property reports. The graph does not hold it.
        b.Value = 0;

        scheduler.RunAll();

        await Assert.That(c.Sample())
            .IsEqualTo(0)
            .Because("a write is not dropped for matching a cached value the graph had already left behind");
    }

    // The constructor samples the cell and attaches its listener in one transaction, and it
    // keeps the sample before it attaches the listener. A build in a transaction that then
    // updates the same cell is the condition where the listener can fire before the constructor
    // returns. Thus, this test holds that behavior. The update must win, because it is newer than
    // the sample. A lost update means that an update went through the interval between the sample
    // and the subscription. A report of the sample after the update means that the constructor
    // wrote over the update.
    [Test]
    public async Task OneWayConstructedInsideATransactionWhichThenFires()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayBindableValue<int> b =
            Transaction.Run(() =>
            {
                IOneWayBindableValue<int> created = c.ToOneWayImpl(scheduler: BindingScheduler.Immediate);

                c.Send(5);

                return created;
            });

        await Assert.That(b.Value)
            .IsEqualTo(5)
            .Because("the update fired after the listener was attached and is newer than the sample");
    }

    [Test]
    public async Task TwoWayConstructedInsideATransactionWhichThenFires()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b =
            Transaction.Run(() =>
            {
                ITwoWayBindableValue<int> created = c.ToTwoWayImpl(scheduler: BindingScheduler.Immediate);

                c.Send(5);

                return created;
            });

        await Assert.That(b.Value).IsEqualTo(5);
    }

    /// <summary>
    ///     Runs <paramref name="body" /> on a different thread and gives the exception that it
    ///     threw.
    /// </summary>
    private static Exception? CaughtOffTheBindingThread<TState>(TState state, Action<TState> body)
    {
        Exception? caught = null;

        Thread thread =
            new(s =>
            {
                try
                {
                    // ReSharper disable once NullableWarningSuppressionIsUsed - s is the state
                    // passed to Start below, of type TState.
                    body((TState)s!);
                }
                catch (Exception e)
                {
                    caught = e;
                }
            }) { IsBackground = true, Name = "off the binding thread" };

        thread.Start(state);
        thread.Join();

        return caught;
    }

    // A context is sufficient to identify the thread. It does not have to be the Current
    // context, because the scheduler captures the building thread with it.
    private static SynchronizationContextBindingScheduler AffineScheduler() => new(new SynchronizationContext());

    [Test]
    public async Task ReadingOneWayOffTheBindingThreadThrows()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayBindableValue<int> b = c.ToOneWayImpl(scheduler: AffineScheduler());

        await Assert.That(b.Value)
            .IsEqualTo(0)
            .Because("the constructing thread is the binding thread for this scheduler");

        Exception? caught = CaughtOffTheBindingThread(state: b, body: static target => _ = target.Value);

        await Assert.That(caught)
            .IsTypeOf<InvalidOperationException>()
            .Because("reading from another thread is caught rather than left to return a stale value");
    }

    [Test]
    public async Task ReadingTwoWayOffTheBindingThreadThrows()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: AffineScheduler());

        await Assert.That(b.Value)
            .IsEqualTo(0)
            .Because("the constructing thread is the binding thread for this scheduler");

        Exception? caught = CaughtOffTheBindingThread(state: b, body: static target => _ = target.Value);

        await Assert.That(caught)
            .IsTypeOf<InvalidOperationException>()
            .Because("reading from another thread is caught rather than left to return a stale value");
    }

    [Test]
    public async Task WritingTwoWayOffTheBindingThreadThrows()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: AffineScheduler());

        Exception? caught = CaughtOffTheBindingThread(state: b, body: static target => target.Value = 5);

        await Assert.That(caught).IsTypeOf<InvalidOperationException>();
        await Assert.That(c.Sample()).IsEqualTo(0).Because("and the write never reached the graph");
    }

    // This one had no scheduler before, thus no code tested it.
    [Test]
    public async Task WritingOneWayToSourceOffTheBindingThreadThrows()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayToSourceBindableValue<int> b =
            c.ToOneWayToSourceImpl(scheduler: AffineScheduler());

        Exception? caught = CaughtOffTheBindingThread(state: b, body: static target => target.Value = 5);

        await Assert.That(caught).IsTypeOf<InvalidOperationException>();
        await Assert.That(c.Sample()).IsEqualTo(0);
    }

    // Nothing changes for a scheduler with no thread of its own. Thus, each test that exists,
    // and each host with no UI, continues to operate.
    [Test]
    public async Task TheImmediateSchedulerNeverRejectsAThread()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: BindingScheduler.Immediate);

        Exception? caught = CaughtOffTheBindingThread(state: b, body: static target => target.Value = 5);

        await Assert.That(caught).IsNull().Because("the immediate scheduler runs work wherever it is called");
        await Assert.That(c.Sample()).IsEqualTo(5);
    }

    // The cost and the result of a group of updates. Answer this before you try to make the
    // refresh operations into one. Each refresh in the queue reads the same cell, thus the first
    // one does the work and the others find no change.
    [Test]
    public async Task ABurstOfUpdatesQueuesARefreshEachButNotifiesOnce()
    {
        QueueingScheduler scheduler = new();
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: scheduler);

        List<int> observed = [];

        using IDisposable _ = b.ListenForValueChanges(observed.Add);

        c.Send(1);
        c.Send(2);
        c.Send(3);

        int ran = scheduler.RunAll();

        await Assert.That(ran).IsEqualTo(3).Because("one refresh queued per update");

        await Assert.That(observed)
            .IsEquivalentTo(expected: [3], ordering: CollectionOrdering.Matching)
            .Because("but only one notification, because they all sample the same settled cell");

        await Assert.That(b.Value).IsEqualTo(3);
    }

    // This differs from the two-way value above, and the difference is deliberate. A one-way
    // value carries the value of the update and does not sample. No setter competes with it, thus
    // only posted work writes the cache, in sequence. The last update to run leaves the current
    // value of the cell. A sample is also correct, and it makes this one notification that
    // carries the last value only.
    [Test]
    public async Task ABurstOfUpdatesReachesAOneWayValueOneAtATime()
    {
        QueueingScheduler scheduler = new();
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayBindableValue<int> b = c.ToOneWayImpl(scheduler: scheduler);

        List<int> observed = [];

        using IDisposable _ = b.ListenForValueChanges(observed.Add);

        c.Send(1);
        c.Send(2);
        c.Send(3);

        int ran = scheduler.RunAll();

        await Assert.That(ran).IsEqualTo(3).Because("one delivery queued per update");

        await Assert.That(observed)
            .IsEquivalentTo(expected: [1, 2, 3], ordering: CollectionOrdering.Matching)
            .Because("each value the cell held is reported, in the order it held them");

        await Assert.That(b.Value).IsEqualTo(3).Because("and the last one delivered agrees with the cell");
    }

    /// <summary>
    ///     This takes the place of a dispatcher. It queues work, which is the important part. A
    ///     real scheduler gives work to the message loop of a different thread and returns. Thus,
    ///     work that a write posts is in the queue when the setter returns.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class QueueingScheduler : IBindingScheduler
    {
        private readonly Queue<Action> queue = new();

        /// <inheritdoc />
        public bool CheckAccess() => true;

        /// <inheritdoc />
        public void Post(Action action) => this.queue.Enqueue(action);

        /// <summary>Runs each action in the queue, and also an action that another action
        /// adds while this method runs.</summary>
        /// <returns>The number of actions that ran.</returns>
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
}
