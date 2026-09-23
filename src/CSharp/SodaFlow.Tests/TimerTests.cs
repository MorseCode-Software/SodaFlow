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

        // This waits for the alarms, and does not use a constant window. The alarms
        // are 99ms and 100ms out, so a flat 200ms sleep left approximately 100ms of margin, and a
        // loaded CI agent overran it: the run gave zero events and not the incorrect
        // number, which is a delayed timer thread, not a coalescing bug. Waiting on the
        // condition makes a slow machine use more time, and it does not fail.
        //
        // The wait after this is what keeps the assertion useful: it must be
        // two, thus a third firing - a2 and a3 failing to coalesce - is caught rather
        // than a race over it.
        //
        // The lock is not incidental. l is written from the timer thread and read here, which
        // the initial test with a constant sleep had no lock.
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

        // Read with the lock held and asserted out of it: await is not allowed in a lock body, and
        // the lock is here to make the read safe rather than the assertion.
        lock (l)
        {
            count = l.Count;
        }

        await Assert.That(count).IsEqualTo(2);
    }
}
