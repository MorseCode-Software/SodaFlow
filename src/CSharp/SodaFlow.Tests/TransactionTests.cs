using System.Threading;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public class TransactionTests
{
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

        // Captured rather than asserted in place: Transaction.RunVoid takes an Action, so an
        // assertion inside it cannot be awaited. Seeded with a value the lambda must overwrite,
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

        // Captured rather than asserted in place: Transaction.RunVoid takes an Action, so an
        // assertion inside it cannot be awaited. Seeded with a value the lambda must overwrite,
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

        // Captured rather than asserted in place: Transaction.RunVoid takes an Action, so an
        // assertion inside it cannot be awaited. Seeded with a value the lambda must overwrite,
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
