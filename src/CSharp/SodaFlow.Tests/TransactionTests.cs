using System;
using System.Threading;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public sealed class TransactionTests
{
    /// <summary>
    ///     Runs an action in one transaction, and then sends on a sink whose listener throws. Thus,
    ///     the transaction fails while it propagates, which is the failure that drops a posted
    ///     action.
    /// </summary>
    /// <param name="inTransaction">Runs with the failing transaction open.</param>
    /// <returns>The exception that the transaction gave.</returns>
    private static Exception RunFailingTransaction(Action inTransaction)
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        IListener listener = sink.ListenStrong(static _ => throw new InvalidOperationException("listener"));

        try
        {
            Transaction.RunVoid(() =>
            {
                inTransaction();

                sink.Send(1);
            });
        }
        catch (Exception e)
        {
            return e;
        }
        finally
        {
            listener.Unlisten();
        }

        throw new InvalidOperationException("The transaction did not fail.");
    }

    [Test]
    public async Task PostWithReleaseRunsTheReleaseWhereTheTransactionFails()
    {
        bool ran = false;
        Exception? cause = null;

        Exception thrown =
            RunFailingTransaction(() => Transaction.Post(action: () => ran = true, onFailure: e => cause = e));

        await Assert.That(ran).IsFalse().Because("a transaction that fails does not run a posted action");

        await Assert.That(cause)
            .IsSameReferenceAs(thrown)
            .Because("the release gets the exception of the transaction");
    }

    [Test]
    public async Task PostWithReleaseRunsNoReleaseWhereTheActionCompletes()
    {
        bool ran = false;
        bool released = false;

        Transaction.RunVoid(() => Transaction.Post(action: () => ran = true, onFailure: _ => released = true));

        await Assert.That(ran).IsTrue();

        await Assert.That(released)
            .IsFalse()
            .Because("an action that completes keeps its promise, thus the release does not run");
    }

    [Test]
    public async Task PostWithReleaseRunsTheReleaseWhereTheActionThrows()
    {
        InvalidOperationException thrown = new("the action fails");
        Exception? cause = null;

        try
        {
            Transaction.RunVoid(() => Transaction.Post(action: () => throw thrown, onFailure: e => cause = e));
        }
        catch (Exception)
        {
            // The throw of the action fails the transaction, and this test is about the release.
        }

        await Assert.That(cause)
            .IsSameReferenceAs(thrown)
            .Because("the release gets the exception of the action where the action throws");
    }

    [Test]
    public async Task PostWithReleaseWithNoTransactionRunsTheActionAndNoRelease()
    {
        bool ran = false;
        bool released = false;

        Transaction.Post(action: () => ran = true, onFailure: _ => released = true);

        await Assert.That(ran).IsTrue().Because("a post with no transaction open runs the action now");
        await Assert.That(released).IsFalse();
    }

    [Test]
    public async Task PostWithReleaseWithNoTransactionRunsTheReleaseWhereTheActionThrows()
    {
        InvalidOperationException thrown = new("the action fails");
        Exception? cause = null;
        Exception? fromPost = null;

        try
        {
            Transaction.Post(action: () => throw thrown, onFailure: e => cause = e);
        }
        catch (Exception e)
        {
            fromPost = e;
        }

        await Assert.That(cause).IsSameReferenceAs(thrown).Because("the release gets the cause");

        await Assert.That(fromPost)
            .IsSameReferenceAs(thrown)
            .Because("the caller still gets the exception of the action");
    }

    [Test]
    public async Task PostWithReleaseKeepsTheThrowFromTheRelease()
    {
        NotSupportedException fromRelease = new("the release fails");

        Exception thrown =
            RunFailingTransaction(() => Transaction.Post(action: static () => { }, onFailure: _ => throw fromRelease));

        await Assert.That(thrown)
            .IsTypeOf<AggregateException>()
            .Because("a throw from the release does not replace the exception that caused it");

        AggregateException aggregate = (AggregateException)thrown;

        await Assert.That(aggregate.InnerExceptions.Count).IsEqualTo(2);

        await Assert.That(aggregate.InnerExceptions[0])
            .IsTypeOf<InvalidOperationException>()
            .Because("the cause comes first");

        await Assert.That(aggregate.InnerExceptions[1]).IsSameReferenceAs(fromRelease);
    }

    [Test]
    public async Task Post()
    {
        Cell<int> cell =
            Transaction.Run(static () =>
            {
                StreamSink<int> s = Stream.CreateSink<int>();
                s.Send(2);
                return s.Hold(1);
            });

        int value = 0;
        Transaction.Post(() => value = cell.Sample());

        await Assert.That(value).IsEqualTo(2);
    }

    [Test]
    public async Task NestedPost()
    {
        Cell<int> cell =
            Transaction.Run(static () =>
            {
                StreamSink<int> s = Stream.CreateSink<int>();
                s.Send(2);

                Transaction.Post(() =>
                {
                    s.Send(3);
                    Transaction.Post(() => s.Send(5));
                });

                Transaction.Post(() => s.Send(4));
                return s.Hold(1);
            });

        await Assert.That(cell.Sample()).IsEqualTo(5);
    }

    [Test]
    public async Task PostInTransaction()
    {
        int value = 0;

        // Captured, and not asserted at that point: Transaction.RunVoid takes an Action, and no code
        // can await an assertion in it. It starts with a value that the lambda must overwrite,
        // so a lambda which never ran fails here rather than passing.
        int valueInsideTransaction = -1;

        Transaction.RunVoid(() =>
        {
            StreamSink<int> s = Stream.CreateSink<int>();
            s.Send(2);
            Cell<int> c = s.Hold(1);
            Transaction.Post(() => value = c.Sample());
            valueInsideTransaction = value;
        });

        await Assert.That(valueInsideTransaction).IsEqualTo(0);
        await Assert.That(value).IsEqualTo(2);
    }

    [Test]
    public async Task PostInNestedTransaction()
    {
        int value = 0;

        // Captured, and not asserted at that point: Transaction.RunVoid takes an Action, and no code
        // can await an assertion in it. It starts with a value that the lambda must overwrite,
        // so a lambda which never ran fails here rather than passing.
        int valueInsideTransaction = -1;

        Transaction.RunVoid(() =>
        {
            StreamSink<int> s = Stream.CreateSink<int>();
            s.Send(2);

            Transaction.RunVoid(() =>
            {
                Cell<int> c = s.Hold(1);
                Transaction.Post(() => value = c.Sample());
            });

            valueInsideTransaction = value;
        });

        await Assert.That(valueInsideTransaction).IsEqualTo(0);
        await Assert.That(value).IsEqualTo(2);
    }

    [Test]
    public async Task PostInNestedTransaction2()
    {
        int value = 0;

        // Captured, and not asserted at that point: Transaction.RunVoid takes an Action, and no code
        // can await an assertion in it. It starts with a value that the lambda must overwrite,
        // so a lambda which never ran fails here rather than passing.
        int valueInsideTransaction = -1;

        Transaction.RunVoid(() =>
        {
            StreamSink<int> s = Stream.CreateSink<int>();
            s.Send(2);

            Transaction.Run(() =>
            {
                Cell<int> c = s.Hold(1);
                Transaction.Post(() => value = c.Sample());
                return Unit.Value;
            });

            valueInsideTransaction = value;
        });

        await Assert.That(valueInsideTransaction).IsEqualTo(0);
        await Assert.That(value).IsEqualTo(2);
    }

    [Test]
    public async Task IsActive()
    {
        bool isActive = Transaction.Run(Transaction.IsActive);

        await Assert.That(isActive).IsTrue();
    }

    [Test]
    public async Task IsNotActive()
    {
        bool isActive = Transaction.IsActive();

        await Assert.That(isActive).IsFalse();
    }

    [Test]
    public async Task IsNotActiveSeparateThread()
    {
        bool? threadIsActive1 = null;
        bool? threadIsActive2 = null;
        bool? threadIsActive3 = null;
        bool? threadIsActive4 = null;
        bool? threadIsActive5 = null;

        new Thread(() =>
        {
            threadIsActive1 = Transaction.IsActive();
            Thread.Sleep(500);
            threadIsActive2 = Transaction.IsActive();

            Transaction.RunVoid(() =>
            {
                threadIsActive3 = Transaction.IsActive();
                Thread.Sleep(500);
                threadIsActive4 = Transaction.IsActive();
            });

            threadIsActive5 = Transaction.IsActive();
        }).Start();

        Thread.Sleep(250);
        bool isActive1 = Transaction.IsActive();
        Thread.Sleep(500);
        bool isActive2 = Transaction.IsActive();
        Thread.Sleep(500);
        bool isActive3 = Transaction.IsActive();

        await Assert.That(isActive1).IsFalse();
        await Assert.That(isActive2).IsFalse();
        await Assert.That(isActive3).IsFalse();

        await Assert.That(threadIsActive1).IsFalse();
        await Assert.That(threadIsActive2).IsFalse();
        await Assert.That(threadIsActive3).IsTrue();
        await Assert.That(threadIsActive4).IsTrue();
        await Assert.That(threadIsActive5).IsFalse();
    }

    [Test]
    public async Task StartHooksRunOnlyOnce()
    {
        int startHooksCount = 0;
        Transaction.OnStart(() => startHooksCount++);

        Transaction.RunVoid(static () =>
            Transaction.RunVoid(static () =>
            {
            }));

        await Assert.That(startHooksCount).IsEqualTo(1);
    }

    [Test]
    public async Task StartHooksRunOnlyOnceWithSample()
    {
        int startHooksCount = 0;
        Cell<int> cell = Cell.Constant(0);
        Transaction.OnStart(() => startHooksCount++);
        Transaction.RunVoid(() => Transaction.RunVoid(() => cell.Sample()));

        await Assert.That(startHooksCount).IsEqualTo(1);
    }
}
