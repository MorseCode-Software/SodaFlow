using System.Threading;
using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests;

[TestFixture]
public class TransactionTests
{
    [Test]
    public void Post()
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

        Assert.AreEqual(expected: 2, actual: value);
    }

    [Test]
    public void NestedPost()
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

        Assert.AreEqual(expected: 5, actual: cell.Sample());
    }

    [Test]
    public void PostInTransaction()
    {
        int value = 0;

        Transaction.RunVoid(() =>
        {
            StreamSink<int> s = Stream.CreateSink<int>();
            s.Send(2);
            Cell<int> c = s.Hold(1);
            Transaction.Post(() => value = c.Sample());
            Assert.AreEqual(expected: 0, actual: value);
        });

        Assert.AreEqual(expected: 2, actual: value);
    }

    [Test]
    public void PostInNestedTransaction()
    {
        int value = 0;

        Transaction.RunVoid(() =>
        {
            StreamSink<int> s = Stream.CreateSink<int>();
            s.Send(2);

            Transaction.RunVoid(() =>
            {
                Cell<int> c = s.Hold(1);
                Transaction.Post(() => value = c.Sample());
            });

            Assert.AreEqual(expected: 0, actual: value);
        });

        Assert.AreEqual(expected: 2, actual: value);
    }

    [Test]
    public void PostInNestedTransaction2()
    {
        int value = 0;

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

            Assert.AreEqual(expected: 0, actual: value);
        });

        Assert.AreEqual(expected: 2, actual: value);
    }

    [Test]
    public void IsActive()
    {
        bool isActive = Transaction.Run(Transaction.IsActive);

        Assert.IsTrue(isActive);
    }

    [Test]
    public void IsNotActive()
    {
        bool isActive = Transaction.IsActive();

        Assert.IsFalse(isActive);
    }

    [Test]
    public void IsNotActiveSeparateThread()
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

        Assert.IsFalse(isActive1);
        Assert.IsFalse(isActive2);
        Assert.IsFalse(isActive3);

        Assert.IsFalse(threadIsActive1);
        Assert.IsFalse(threadIsActive2);
        Assert.IsTrue(threadIsActive3);
        Assert.IsTrue(threadIsActive4);
        Assert.IsFalse(threadIsActive5);
    }

    [Test]
    public void StartHooksRunOnlyOnce()
    {
        int startHooksCount = 0;
        Transaction.OnStart(() => startHooksCount++);

        Transaction.RunVoid(static () =>
            Transaction.RunVoid(static () =>
            {
            }));

        Assert.That(actual: startHooksCount, expression: Is.EqualTo(1));
    }

    [Test]
    public void StartHooksRunOnlyOnceWithSample()
    {
        int startHooksCount = 0;
        Cell<int> cell = Cell.Constant(0);
        Transaction.OnStart(() => startHooksCount++);
        Transaction.RunVoid(() => Transaction.RunVoid(() => cell.Sample()));

        Assert.That(actual: startHooksCount, expression: Is.EqualTo(1));
    }
}
