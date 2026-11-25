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
///     Tests the command. It covers availability, firing, the type of the
///     parameter, and disposal.
/// </summary>
public sealed class BindableActionTests
{
    private static IBindableAction<T> Action<T>(StreamSink<T> sink, Cell<bool>? isEnabled = null)
        where T : notnull =>
        sink.ToBindableActionImpl(isEnabledCell: isEnabled, scheduler: BindingScheduler.Immediate);

    // The same rule as the one-way value: with this scheduler, a transaction that fails discards
    // its posts, and CanExecute answers from the cell's committed value.
    [Test]
    public async Task ReportsTheCommittedEnablementAfterATransactionThatFails()
    {
        CellSink<bool> isEnabled = Cell.CreateSink(false);
        StreamSink<int> sink = Stream.CreateSink<int>();

        IBindableAction<int> action =
            sink.ToBindableActionImpl(isEnabledCell: isEnabled, scheduler: BindingScheduler.Immediate);

        bool initial = action.CanExecute(null);
        int notifications = 0;
        action.CanExecuteChanged += (_, _) => notifications++;

        IListener throws =
            isEnabled.Updates().Listen(static _ => throw new InvalidOperationException("from the graph"));

        try
        {
            isEnabled.Send(true);
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            throws.Dispose();
        }

        await Assert.That(initial).IsFalse();
        await Assert.That(action.CanExecute(null)).IsFalse();
        await Assert.That(notifications).IsEqualTo(0);

        isEnabled.Send(true);

        await Assert.That(action.CanExecute(null)).IsTrue();
        await Assert.That(notifications).IsEqualTo(1);
        action.Dispose();
    }

    // The same rule as the one-way value, with a scheduler that runs the action while the
    // transaction propagates. The action finds the outcome unknown and reads the cell, and that
    // read waits for the transaction. Thus, the wait in the listener must time out.
    [Test]
    public async Task ReportsTheCommittedEnablementWithASchedulerThatRunsDuringPropagation()
    {
        CellSink<bool> isEnabled = Cell.CreateSink(false);
        ThreadPerActionScheduler scheduler = new();

        IBindableAction<int> action =
            Stream.CreateSink<int>().ToBindableActionImpl(isEnabledCell: isEnabled, scheduler: scheduler);

        int notifications = 0;
        action.CanExecuteChanged += (_, _) => Interlocked.Increment(ref notifications);
        bool finishedWhileTheTransactionWasOpen = false;

        // This listener runs after the listener of the command, which posted before it.
        IListener throws = isEnabled.Updates().Listen(_ =>
        {
            scheduler.AnActionStarted.Wait();
            finishedWhileTheTransactionWasOpen = scheduler.AnActionFinished.Wait(TimeSpan.FromMilliseconds(500));
            throw new InvalidOperationException("from the graph");
        });

        try
        {
            isEnabled.Send(true);
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            throws.Dispose();
        }

        scheduler.Join();

        await Assert.That(finishedWhileTheTransactionWasOpen).IsFalse();
        await Assert.That(action.CanExecute(null)).IsFalse();
        await Assert.That(notifications).IsEqualTo(0);

        isEnabled.Send(true);
        scheduler.Join();

        await Assert.That(action.CanExecute(null)).IsTrue();
        await Assert.That(notifications).IsEqualTo(1);
        action.Dispose();
    }

    [Test]
    public async Task IsExecutableByDefault()
    {
        using IBindableAction<int> a = Action(Stream.CreateSink<int>());

        await Assert.That(a.CanExecute(null)).IsTrue().Because("no enablement cell means always enabled");
    }

    [Test]
    public async Task FollowsItsEnablementCell()
    {
        CellSink<bool> enabled = Cell.CreateSink(false);

        using IBindableAction<int> a = Action(sink: Stream.CreateSink<int>(), isEnabled: enabled);

        await Assert.That(a.CanExecute(null)).IsFalse().Because("the constructor samples the cell");

        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;

        enabled.Send(true);

        await Assert.That(a.CanExecute(null)).IsTrue();
        await Assert.That(notifications).IsEqualTo(1);
    }

    [Test]
    public async Task CarriesItsParameterIntoTheStream()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = [];

        using IBindableAction<int> a = Action(sink);

        using (a.FiringsStream.Listen(fired.Add))
        {
            a.Execute(42);

            await Assert.That(fired).IsEquivalentTo(expected: [42], ordering: CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task DoesNotFireWhileDisabled()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = [];

        using IBindableAction<int> a = Action(sink: sink, isEnabled: Cell.Constant(false));

        using (a.FiringsStream.Listen(fired.Add))
        {
            a.Execute(1);

            await Assert.That(fired).IsEmpty();
        }
    }

    // CanExecute reads a copy of the cell, and the scheduler moves each change of the cell to that
    // copy. Here the graph disables the command before the binding thread gets the change. Thus,
    // CanExecute gives true, and only the send can find that the cell is false.
    [Test]
    public async Task DoesNotFireWhenTheGraphDisabledItBeforeTheViewKnew()
    {
        QueueingScheduler scheduler = new();
        CellSink<bool> enabled = Cell.CreateSink(true);
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = [];

        using IBindableAction<int> a = sink.ToBindableActionImpl(isEnabledCell: enabled, scheduler: scheduler);

        using (a.FiringsStream.Listen(fired.Add))
        {
            enabled.Send(false);

            await Assert.That(a.CanExecute(null)).IsTrue().Because("the change of the copy is still in the queue");

            a.Execute(1);

            await Assert.That(fired).IsEmpty().Because("the cell was false in the transaction of the send");

            scheduler.RunAll();

            await Assert.That(a.CanExecute(null)).IsFalse().Because("the copy now agrees with the cell");
        }
    }

    // A call in an open transaction sends after that transaction closes. A change of the cell in
    // that transaction occurs first. Thus, the send must read the cell when it runs, and not when
    // the call occurs.
    [Test]
    public async Task DoesNotFireWhenTheTransactionOfTheCallDisablesIt()
    {
        CellSink<bool> enabled = Cell.CreateSink(true);
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = [];

        using IBindableAction<int> a = Action(sink: sink, isEnabled: enabled);

        using (a.FiringsStream.Listen(fired.Add))
        {
            Transaction.RunVoid(() =>
            {
                // ReSharper disable once AccessToDisposedClosure - Transaction.RunVoid runs the lambda before the
                // using disposes a.
                a.Execute(1);
                enabled.Send(false);
            });

            await Assert.That(fired).IsEmpty().Because("the cell was false when the send ran");
        }
    }

    // A nested message loop can deliver a click into a transaction that then fails for a cause
    // that has no connection to the click. The send must reach the graph also then. Before, it
    // waited in the post queue of that transaction, and the failure discarded it.
    [Test]
    public async Task FiresWhenTheTransactionOfTheCallFails()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        CellSink<int> other = Cell.CreateSink(0);
        List<int> fired = [];

        using IBindableAction<int> a = Action(sink);

        using (a.FiringsStream.Listen(fired.Add))
        {
            // The listener runs immediately in the Send below, thus it cannot have a longer life
            // than the using block.
            // ReSharper disable AccessToDisposedClosure
            IListener nested = other.Updates().Listen(_ =>
            {
                a.Execute(7);

                throw new InvalidOperationException("from the graph");
            });

            // ReSharper restore AccessToDisposedClosure

            try
            {
                await Assert.That(() => other.Send(1)).ThrowsExactly<InvalidOperationException>();
            }
            finally
            {
                nested.Dispose();
            }

            await Assert.That(fired)
                .IsEquivalentTo(expected: [7], ordering: CollectionOrdering.Matching)
                .Because("the send ran after the transaction failed");
        }
    }

    // The control for the two tests above: the send reads the cell, and a true cell lets the send occur.
    [Test]
    public async Task FiresWhenTheCellIsTrueAtTheSend()
    {
        CellSink<bool> enabled = Cell.CreateSink(true);
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = [];

        using IBindableAction<int> a = Action(sink: sink, isEnabled: enabled);

        using (a.FiringsStream.Listen(fired.Add))
        {
            Transaction.RunVoid(() =>
            {
                // ReSharper disable once AccessToDisposedClosure - Transaction.RunVoid runs the lambda before the
                // using disposes a.
                a.Execute(1);
                enabled.Send(false);
            });

            enabled.Send(true);

            // ReSharper disable once AccessToDisposedClosure - Transaction.RunVoid runs the lambda before the
            // using disposes a.
            Transaction.RunVoid(() => a.Execute(2));

            await Assert.That(fired)
                .IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching)
                .Because("only the second call found the cell true");
        }
    }

    // The test of the type is a diagnostic for the author of the XAML, thus it must occur at the
    // call site. A test in the posted send throws at a point that the author cannot see.
    [Test]
    public async Task RejectsAMistypedParameterAtTheCallSite()
    {
        using IBindableAction<int> a = Action(Stream.CreateSink<int>());

        // ReSharper disable once AccessToDisposedClosure - the assertion runs the lambda before
        // the using disposes a.
        await Assert.That(() => a.Execute("not an int")).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task RejectsNullForAReferenceType()
    {
        using IBindableAction<string> a = Action(Stream.CreateSink<string>());

        // ReSharper disable once AccessToDisposedClosure - the assertion runs the lambda before
        // the using disposes a.
        await Assert.That(() => a.Execute(null)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task RejectsNullForAValueType()
    {
        using IBindableAction<int> a = Action(Stream.CreateSink<int>());

        // ReSharper disable once AccessToDisposedClosure - the assertion runs the lambda before
        // the using disposes a.
        await Assert.That(() => a.Execute(null)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task StopsFiringOnceDisposed()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = [];

        IBindableAction<int> a = Action(sink);

        using (a.FiringsStream.Listen(fired.Add))
        {
            a.Dispose();
            a.Execute(1);

            await Assert.That(fired).IsEmpty();
            await Assert.That(a.CanExecute(null)).IsFalse();
        }
    }

    // In an open transaction the send waits until that transaction releases the lock. A Dispose
    // in that interval comes before the send, thus the command must not fire. The two-way value
    // holds the same rule for a write. Before, the command fired.
    [Test]
    public async Task DisposedInTheTransactionOfTheCallDoesNotFire()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = [];

        IBindableAction<int> a = Action(sink);

        using (a.FiringsStream.Listen(fired.Add))
        {
            Transaction.RunVoid(() =>
            {
                a.Execute(7);
                a.Dispose();
            });

            await Assert.That(fired).IsEmpty().Because("the command was disposed before the send ran");
        }
    }

    // A binding engine caches the last answer from CanExecute and asks again only when the
    // command tells it to. Thus, a call to Dispose with no notification leaves a button available,
    // and a click on that button does nothing.
    [Test]
    public async Task NotifiesTheViewWhenDisposalDisablesIt()
    {
        IBindableAction<int> a = Action(Stream.CreateSink<int>());
        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;

        a.Dispose();

        await Assert.That(notifications).IsEqualTo(1).Because("the view has to be told to re-query");
    }

    [Test]
    public async Task DoesNotNotifyWhenDisposingAnAlreadyDisabledCommand()
    {
        IBindableAction<int> a = Action(sink: Stream.CreateSink<int>(), isEnabled: Cell.Constant(false));
        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;

        a.Dispose();

        await Assert.That(notifications).IsEqualTo(0).Because("nothing changed, so there is nothing to report");
    }

    [Test]
    public async Task DisposesIdempotently()
    {
        IBindableAction<int> a = Action(Stream.CreateSink<int>());

        a.Dispose();
        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;
        a.Dispose();

        await Assert.That(notifications).IsEqualTo(0).Because("the second dispose does nothing at all");
    }

    // The cost and the result of a group of changes. One refresh is in the queue at a time, and
    // it reads the newest change. Before, each change posted a closure of its own, and the drain
    // announced each change in sequence.
    [Test]
    public async Task ABurstOfEnablementChangesPostsOneRefreshAndNotifiesOnce()
    {
        QueueingScheduler scheduler = new();
        CellSink<bool> enabled = Cell.CreateSink(false);

        IBindableAction<int> a =
            Stream.CreateSink<int>().ToBindableActionImpl(scheduler: scheduler, isEnabledCell: enabled);

        bool initial = a.CanExecute(null);
        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;

        enabled.Send(true);
        enabled.Send(false);
        enabled.Send(true);

        int ran = scheduler.RunAll();

        await Assert.That(initial).IsFalse();
        await Assert.That(ran).IsEqualTo(1).Because("the second and third changes find a refresh in the queue");
        await Assert.That(notifications).IsEqualTo(1).Because("and that refresh reads the newest change");
        await Assert.That(a.CanExecute(null)).IsTrue();
        a.Dispose();
    }

    // A group of changes that ends at the answer that the view has. Nothing changed for the view,
    // thus it gets no notification. Before, the drain announced each change.
    [Test]
    public async Task ABurstThatEndsAtTheAnswerOfTheViewNotifiesNothing()
    {
        QueueingScheduler scheduler = new();
        CellSink<bool> enabled = Cell.CreateSink(false);

        IBindableAction<int> a =
            Stream.CreateSink<int>().ToBindableActionImpl(scheduler: scheduler, isEnabledCell: enabled);

        bool initial = a.CanExecute(null);
        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;

        enabled.Send(true);
        enabled.Send(false);

        int ran = scheduler.RunAll();

        await Assert.That(initial).IsFalse();
        await Assert.That(notifications).IsEqualTo(0);
        await Assert.That(a.CanExecute(null)).IsFalse();
        await Assert.That(ran).IsEqualTo(1);
        a.Dispose();
    }

    // The slot holds the newest change with the outcome of its own transaction. The refresh finds
    // that this transaction did not commit, thus it reads the cell. That shows the value that the
    // change before it committed.
    [Test]
    public async Task ARefreshReadsTheCellWhenTheNewerChangeComesFromATransactionThatFailed()
    {
        QueueingScheduler scheduler = new();
        CellSink<bool> enabled = Cell.CreateSink(false);

        IBindableAction<int> a =
            Stream.CreateSink<int>().ToBindableActionImpl(scheduler: scheduler, isEnabledCell: enabled);

        bool initial = a.CanExecute(null);
        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;

        enabled.Send(true);

        IListener throws =
            enabled.Updates().Listen(static _ => throw new InvalidOperationException("from the graph"));

        try
        {
            enabled.Send(false);
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            throws.Dispose();
        }

        scheduler.RunAll();

        await Assert.That(initial).IsFalse();

        await Assert.That(a.CanExecute(null))
            .IsTrue()
            .Because("the cell holds true, and the refresh does not show the false of the failed transaction");

        await Assert.That(notifications).IsEqualTo(1);
        a.Dispose();
    }

    // A nested message loop can run the queue from a callback, while the transaction that changed
    // the cell is open on the binding thread. A read of the cell in that transaction gives the
    // value before the change. Thus, the refresh posts itself again and runs after the
    // transaction. Before, the posted action read the cell there and found no change, and the
    // command kept the answer from before the change.
    [Test]
    public async Task ARefreshThatRunsInTheChangingTransactionWaitsForIt()
    {
        QueueingScheduler scheduler = new();
        CellSink<bool> enabled = Cell.CreateSink(false);

        IBindableAction<int> a =
            Stream.CreateSink<int>().ToBindableActionImpl(scheduler: scheduler, isEnabledCell: enabled);

        bool initial = a.CanExecute(null);
        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;

        int ranInside = 0;
        int notificationsInside = -1;

        // This listener is newer than the listener of the command, thus it runs after it.
        IListener pump = enabled.Updates().Listen(_ =>
        {
            ranInside = scheduler.RunAll();
            notificationsInside = notifications;
        });

        enabled.Send(true);
        pump.Dispose();

        int ranAfter = scheduler.RunAll();

        await Assert.That(initial).IsFalse();
        await Assert.That(ranInside).IsEqualTo(1).Because("the refresh ran in the transaction of the change");
        await Assert.That(notificationsInside).IsEqualTo(0).Because("that transaction did not end");
        await Assert.That(a.CanExecute(null)).IsTrue().Because("the cell holds true after the transaction");
        await Assert.That(notifications).IsEqualTo(1);
        await Assert.That(ranAfter).IsEqualTo(1).Because("the refresh posted itself again for after that transaction");
        a.Dispose();
    }

    // The same nested drain, in a transaction that then fails. The refresh that waits must post
    // again also then. Before, it waited in the post queue of the transaction, which the failure
    // discarded. Nothing posted again, and the command kept the answer false until the next
    // change, although the cell held true.
    [Test]
    public async Task ARefreshThatWaitsForAChangingTransactionThatFailsPostsAgain()
    {
        QueueingScheduler scheduler = new();
        CellSink<bool> enabled = Cell.CreateSink(false);

        IBindableAction<int> a =
            Stream.CreateSink<int>().ToBindableActionImpl(scheduler: scheduler, isEnabledCell: enabled);

        bool initial = a.CanExecute(null);
        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;

        enabled.Send(true);

        int ranInside = 0;

        // This listener is newer than the listener of the command, thus it runs after it.
        IListener pump = enabled.Updates().Listen(v =>
        {
            if (!v)
            {
                ranInside = scheduler.RunAll();

                throw new InvalidOperationException("from the graph");
            }
        });

        try
        {
            await Assert.That(() => enabled.Send(false)).ThrowsExactly<InvalidOperationException>();
        }
        finally
        {
            pump.Dispose();
        }

        int ranAfter = scheduler.RunAll();

        await Assert.That(initial).IsFalse();
        await Assert.That(ranInside).IsEqualTo(1).Because("the refresh of true ran in the transaction of false");
        await Assert.That(ranAfter).IsEqualTo(1).Because("and posted itself again, although that transaction failed");
        await Assert.That(a.CanExecute(null)).IsTrue().Because("the refresh after the failure read the cell");
        await Assert.That(notifications).IsEqualTo(1);
        a.Dispose();
    }

    // After a change whose transaction failed, that transaction is over. Thus, a refresh that runs
    // in a different transaction reads the cell there. Before, the refresh waited also after the
    // failure, and a drain in a transaction made no progress.
    [Test]
    public async Task ARefreshInATransactionAfterAFailedChangeReadsTheCell()
    {
        QueueingScheduler scheduler = new();
        CellSink<bool> enabled = Cell.CreateSink(false);

        IBindableAction<int> a =
            Stream.CreateSink<int>().ToBindableActionImpl(scheduler: scheduler, isEnabledCell: enabled);

        bool initial = a.CanExecute(null);
        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;

        enabled.Send(true);

        IListener throws =
            enabled.Updates().Listen(static _ => throw new InvalidOperationException("from the graph"));

        try
        {
            enabled.Send(false);
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            throws.Dispose();
        }

        // A drain in a transaction, as a nested message loop runs it from a callback.
        int ranInside = 0;
        Transaction.RunVoid(() => ranInside = scheduler.RunAll());
        bool answerAfterTheDrain = a.CanExecute(null);
        int notificationsAfterTheDrain = notifications;
        int ranAfter = scheduler.RunAll();

        await Assert.That(initial).IsFalse();
        await Assert.That(ranInside).IsEqualTo(1).Because("the refresh ran in the transaction");
        await Assert.That(answerAfterTheDrain).IsTrue().Because("it read the cell, which holds true");
        await Assert.That(notificationsAfterTheDrain).IsEqualTo(1);
        await Assert.That(ranAfter).IsEqualTo(0).Because("and it did not post itself again");
        a.Dispose();
    }

    // A scheduler that gives each post to the transaction drops the post where that transaction
    // fails. That breaks the contract on IBindingScheduler.Post. The discard of the command clears
    // its flag, thus the next change posts again also with such a scheduler.
    [Test]
    public async Task AChangeAfterAFailedTransactionPostsAgainWithASchedulerThatDropsThePost()
    {
        CellSink<bool> enabled = Cell.CreateSink(false);

        IBindableAction<int> a =
            Stream.CreateSink<int>()
                .ToBindableActionImpl(scheduler: new PostDroppingScheduler(), isEnabledCell: enabled);

        bool initial = a.CanExecute(null);
        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;

        IListener throws =
            enabled.Updates().Listen(static _ => throw new InvalidOperationException("from the graph"));

        try
        {
            enabled.Send(true);
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            throws.Dispose();
        }

        bool afterTheFailure = a.CanExecute(null);

        enabled.Send(true);

        await Assert.That(initial).IsFalse();
        await Assert.That(afterTheFailure).IsFalse();
        await Assert.That(a.CanExecute(null)).IsTrue();
        await Assert.That(notifications).IsEqualTo(1);
        a.Dispose();
    }

#if NET
    // The reference assemblies of net472 do not have the read of the bytes that a thread allocated.
    // Thus, this test is for the newer target only. The command is the same code on each target.
    //
    // A change of the availability posts the one delegate that the command keeps, as a change of a
    // one-way value does. Thus, the command allocates no more than a one-way value of the same
    // type for the same change. Before, the command allocated a closure and a delegate more.
    [Test]
    public async Task AChangeOfTheEnablementAllocatesNoMoreThanAChangeOfAOneWayValue()
    {
        QueueingScheduler scheduler = new();
        CellSink<bool> enabled = Cell.CreateSink(false);
        CellSink<bool> shown = Cell.CreateSink(false);

        IBindableAction<int> command =
            Stream.CreateSink<int>().ToBindableActionImpl(scheduler: scheduler, isEnabledCell: enabled);

        IOneWayBindableValue<bool> value = shown.ToOneWayImpl(scheduler: scheduler);

        long forTheCommand = BytesForChanges(sink: enabled, scheduler: scheduler);
        long forTheValue = BytesForChanges(sink: shown, scheduler: scheduler);

        // The two subscriptions are weak. These calls keep the two objects alive until here.
        command.Dispose();
        value.Dispose();

        await Assert.That(forTheValue).IsGreaterThan(0).Because("the measurement can see an allocation");
        await Assert.That(forTheCommand).IsLessThanOrEqualTo(forTheValue);

        return;

        static long BytesForChanges(CellSink<bool> sink, QueueingScheduler scheduler)
        {
            const int changes = 1000;
            bool next = false;

            // The first changes pay for the code that the runtime compiles and for each queue that grows.
            for (int i = 0; i < changes; i++)
            {
                next = !next;
                sink.Send(next);
                scheduler.RunAll();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < changes; i++)
            {
                next = !next;
                sink.Send(next);
                scheduler.RunAll();
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }
#endif

    /// <summary>
    ///     Gives each post to the open transaction as a posted action. A transaction
    ///     that fails discards its posted actions, thus this scheduler drops them.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class PostDroppingScheduler : IBindingScheduler
    {
        /// <inheritdoc />
        public bool CheckAccess() => true;

        /// <inheritdoc />
        public void Post(Action action) => Transaction.Post(action);
    }
}
