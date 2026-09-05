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
        public bool CheckAccess() => true;

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
    public async Task TheImmediateSchedulerHasNotifiedByTheTimeTheSendReturns()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayBindableValue<int> b = c.ToOneWayImpl(scheduler: BindingScheduler.Immediate);

        List<int> observed = new();

        using IDisposable _ = b.ListenForValueChanges(observed.Add);

        c.Send(1);

        await Assert.That(observed).IsEquivalentTo(new[] { 1 }, CollectionOrdering.Matching).Because("the notification is delivered before Send returns, not left queued");
    }

    // Transactions are serialized process-wide, and that guarantee reaches the binding thread: a
    // two-way setter opens a transaction to push its write, so it waits for any transaction
    // already in flight. Worth pinning down - it is the reason a long transaction on a background
    // thread stalls the UI, and the reason a scheduler which blocks would deadlock against it.
    [Test]
    public async Task ASetterWaitsWhileAnotherThreadHoldsATransactionOpen()
    {
        CellSink<int> c = Cell.CreateSink(0);

        ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: BindingScheduler.Immediate);

        // TaskCompletionSource rather than an event: nothing here needs disposing, so the threads
        // below capture nothing whose lifetime is shorter than their own.
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

            await Assert.That(setter.Join(200)).IsFalse().Because("the setter cannot complete while another thread holds the transaction open");

            release.TrySetResult(true);

            await Assert.That(setter.Join(TimeSpan.FromSeconds(30))).IsTrue().Because("and completes once that transaction closes");

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

    // An update carries the value captured when it fired. Delivered late - after a second write
    // has already moved the cached value on - it puts the older value back, and the view shows a
    // value the user has already replaced until the reconciliation behind it corrects the
    // correction. Sampling the cell in the handler, as the reconciliation does, would make a late
    // notification harmless.
    [Test]
    public async Task AStaleUpdateDoesNotRevertANewerValue()
    {
        QueueingScheduler scheduler = new();
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: scheduler);

        List<int> observed = new();

        using IDisposable _ = b.ListenForValueChanges(observed.Add);

        b.Value = 1;
        b.Value = 2;

        scheduler.RunAll();

        await Assert.That(observed).DoesNotContain(1).Because("the view is never told to go back to a value the caller has already replaced");

        await Assert.That(b.Value).IsEqualTo(2);
        await Assert.That(c.Sample()).IsEqualTo(2);
    }

    // The same pair of writes down the other path. A setter normally sends synchronously - with no
    // transaction open, PostWrite runs the write there and then - so the first write has reached
    // the cell before the second setter is even called, and no refresh can sample between them.
    // Called from inside a transaction the write defers instead, and both sit in the post queue
    // until it closes. That is the arrangement where a refresh sampling too early would hand the
    // view the older value back, so it is the one worth pinning: both writes drain before any
    // refresh runs, and the refreshes sample rather than carrying a value, so neither can.
    [Test]
    public async Task TwoWritesInsideOneTransactionDoNotRevert()
    {
        QueueingScheduler scheduler = new();
        StreamSink<string> edits = Stream.CreateSink<string>();

        // Normalizing, so the cell's value differs from the one written and a reversion would
        // actually be visible rather than hidden behind an equality check.
        Cell<string> upperCased = edits.Map(static v => v.ToUpperInvariant()).Hold(string.Empty);

        using ITwoWayBindableValue<string> b =
            upperCased.ToTwoWayImpl(editsStreamSink: edits, scheduler: scheduler);

        List<string> observed = new();

        using IDisposable _ = b.ListenForValueChanges(observed.Add);

        // The lambda runs synchronously inside RunVoid, so it cannot outlive the using scope.
        // ReSharper disable AccessToDisposedClosure
        Transaction.RunVoid(() =>
        {
            b.Value = "a";
            b.Value = "b";
        });

        // ReSharper restore AccessToDisposedClosure

        scheduler.RunAll();

        await Assert.That(observed).DoesNotContain("A").Because("the deferred first write never reaches the view after the second has replaced it");

        await Assert.That(b.Value).IsEqualTo("B");
        await Assert.That(upperCased.Sample()).IsEqualTo("B");
    }

    // The cached value is a record of what the cell held when it was last sampled, not of what
    // the cell holds now. Between an update and the refresh it queues, the two disagree - and a
    // write whose value matches the stale cache is indistinguishable from a no-op, so the equality
    // check discards it. The caller asked for a value the graph does not hold, and nothing carries
    // the request anywhere.
    [Test]
    public async Task ASetterIsNotDiscardedWhileARefreshIsInFlight()
    {
        QueueingScheduler scheduler = new();
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: scheduler);

        // The graph moves on its own. The refresh this queues has not run, so the cached value
        // still reads 0 - true a moment ago, not true now.
        c.Send(1);

        // An assertion rather than an assumption: the scheduler queues rather than running, so this
        // is not a maybe, and a test which stopped meeting its own precondition should say so.
        await Assert.That(b.Value).IsEqualTo(0)
            .Because("precondition: the refresh has not been delivered yet");

        // Something asks for the value the property still reports. The graph does not hold it.
        b.Value = 0;

        scheduler.RunAll();

        await Assert.That(c.Sample()).IsEqualTo(0).Because("a write is not dropped for matching a cached value the graph had already left behind");
    }

    // The constructor samples the cell and attaches its listener inside one transaction, and the
    // sample is stored before attaching. Constructing from inside a transaction which then goes
    // on to update that same cell is the case where the listener can fire before the constructor
    // has returned - so it is the one worth pinning down. The update must win, because it is
    // newer than the sample; losing it would mean an update had slipped through the gap between
    // sampling and subscribing, and reporting it as the value before the sample landed would mean
    // the constructor had overwritten it.
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

        await Assert.That(b.Value).IsEqualTo(5).Because("the update fired after the listener was attached and is newer than the sample");
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
    ///     Runs <paramref name="body" /> on another thread and returns whatever it threw.
    /// </summary>
    private static Exception? CaughtOffTheBindingThread<TState>(TState state, Action<TState> body)
    {
        Exception? caught = null;

        Thread thread =
            new(s =>
            {
                try
                {
                    body((TState)s);
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

    // A context is enough to establish affinity; it does not have to be installed as Current,
    // because the scheduler captures the constructing thread alongside it.
    private static SynchronizationContextBindingScheduler AffineScheduler() => new(new SynchronizationContext());

    [Test]
    public async Task ReadingOneWayOffTheBindingThreadThrows()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayBindableValue<int> b = c.ToOneWayImpl(scheduler: AffineScheduler());

        await Assert.That(b.Value).IsEqualTo(0).Because("the constructing thread is the binding thread for this scheduler");

        Exception? caught = CaughtOffTheBindingThread(state: b, body: static target => _ = target.Value);

        await Assert.That(caught).IsTypeOf<InvalidOperationException>().Because("reading from another thread is caught rather than left to return a stale value");
    }

    [Test]
    public async Task ReadingTwoWayOffTheBindingThreadThrows()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: AffineScheduler());

        await Assert.That(b.Value).IsEqualTo(0).Because("the constructing thread is the binding thread for this scheduler");

        Exception? caught = CaughtOffTheBindingThread(state: b, body: static target => _ = target.Value);

        await Assert.That(caught).IsTypeOf<InvalidOperationException>().Because("reading from another thread is caught rather than left to return a stale value");
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

    // The one that had no scheduler before, and so no way to be checked at all.
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

    // Nothing changes for a scheduler with no affinity, which is what keeps every existing test
    // and every headless host working unchanged.
    [Test]
    public async Task TheImmediateSchedulerNeverRejectsAThread()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: BindingScheduler.Immediate);

        Exception? caught = CaughtOffTheBindingThread(state: b, body: static target => target.Value = 5);

        await Assert.That(caught).IsNull().Because("the immediate scheduler runs work wherever it is called");
        await Assert.That(c.Sample()).IsEqualTo(5);
    }

    // What a burst costs and what it produces, which is the question to answer before trying to
    // coalesce the refreshes: every queued refresh sees the same cell, so the first one to run
    // does the work and the rest find nothing to do.
    [Test]
    public async Task ABurstOfUpdatesQueuesARefreshEachButNotifiesOnce()
    {
        QueueingScheduler scheduler = new();
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = c.ToTwoWayImpl(scheduler: scheduler);

        List<int> observed = new();

        using IDisposable _ = b.ListenForValueChanges(observed.Add);

        c.Send(1);
        c.Send(2);
        c.Send(3);

        int ran = scheduler.RunAll();

        await Assert.That(ran).IsEqualTo(3).Because("one refresh queued per update");

        await Assert.That(observed).IsEquivalentTo(new[] { 3 }, CollectionOrdering.Matching).Because("but only one notification, because they all sample the same settled cell");

        await Assert.That(b.Value).IsEqualTo(3);
    }

    // The deliberate asymmetry with the two-way value above, and the reason a one-way value was
    // left carrying the update's value rather than sampling like its neighbour: with no setter
    // racing it, the cache is written only by posted work, in order, so the last update to run
    // leaves the cell's current value behind. Sampling would be sound too - and would collapse
    // this to a single notification carrying only the final value.
    [Test]
    public async Task ABurstOfUpdatesReachesAOneWayValueOneAtATime()
    {
        QueueingScheduler scheduler = new();
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayBindableValue<int> b = c.ToOneWayImpl(scheduler: scheduler);

        List<int> observed = new();

        using IDisposable _ = b.ListenForValueChanges(observed.Add);

        c.Send(1);
        c.Send(2);
        c.Send(3);

        int ran = scheduler.RunAll();

        await Assert.That(ran).IsEqualTo(3).Because("one delivery queued per update");

        await Assert.That(observed).IsEquivalentTo(new[] { 1, 2, 3 }, CollectionOrdering.Matching).Because("each value the cell held is reported, in the order it held them");

        await Assert.That(b.Value).IsEqualTo(3).Because("and the last one delivered agrees with the cell");
    }
}
