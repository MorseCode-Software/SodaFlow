using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SodaFlow.Functional;
using SodaFlow.Time;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public sealed class TimerTests
{
    [Test]
    public async Task SimultaneousTimerEvents()
    {
        TimerSystem<DateTime> ts =
            new SystemClockTimerSystem(static _ =>
            {
            });

        Behavior<DateTime> time = ts.Time;
        List<DateTime> l = [];

        Transaction.RunVoid(() =>
        {
            DateTime now = time.Sample();
            Stream<DateTime> a1 = ts.At(Cell.Constant(Maybe.Some(now.AddMilliseconds(99))));
            Stream<DateTime> a2 = ts.At(Cell.Constant(Maybe.Some(now.AddMilliseconds(100))));
            Stream<DateTime> a3 = ts.At(Cell.Constant(Maybe.Some(now.AddMilliseconds(100))));
            Stream<DateTime> m = a1.OrElse(a2).OrElse(a3);

            m.ListenStrong(v =>
            {
                lock (l)
                {
                    l.Add(v);
                }
            });
        });

        // This waits for the alarms, and does not use a constant window. The alarms are 99ms and
        // 100ms in the future. Thus, a constant sleep of 200ms left approximately 100ms of margin,
        // and a CI agent with a high load used more than that margin. The run then gave zero events,
        // and not an incorrect count. The cause was a late timer thread, and not an error in the
        // coalesce. A wait on the condition makes a slow machine use more time, and it does not
        // fail.
        //
        // The wait after this is what keeps the assertion useful. The count must be two. Thus, the
        // test sees a third firing when a2 and a3 do not coalesce, and does not run before it.
        //
        // The lock is necessary, because the timer thread writes l and this code reads it. The
        // initial test with a constant sleep had no lock.
        SpinWait.SpinUntil(
            condition: () =>
            {
                lock (l)
                {
                    return l.Count >= 2;
                }
            },
            timeout: TimeSpan.FromSeconds(10));

        Thread.Sleep(100);

        int count;

        // Read with the lock held, and asserted out of it. A lock body permits no await, and the lock
        // is here to make the read safe, and not the assertion.
        lock (l)
        {
            count = l.Count;
        }

        await Assert.That(count).IsEqualTo(2);
    }
}
