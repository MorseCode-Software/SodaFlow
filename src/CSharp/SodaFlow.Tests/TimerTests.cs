using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using SodaFlow.Time;
using SodaFlow.Functional;

namespace SodaFlow.Tests
{
    [TestFixture]
    public class TimerTests
    {
        [Test]
        public void SimultaneousTimerEvents()
        {
            TimerSystem<DateTime> ts = new SystemClockTimerSystem(e => { });
            Behavior<DateTime> time = ts.Time;
            List<DateTime> l = new List<DateTime>();
            Transaction.RunVoid(
                () =>
                {
                    DateTime now = time.Sample();
                    Stream<DateTime> a1 = ts.At(Cell.Constant(Maybe.Some(now.AddMilliseconds(99))));
                    Stream<DateTime> a2 = ts.At(Cell.Constant(Maybe.Some(now.AddMilliseconds(100))));
                    Stream<DateTime> a3 = ts.At(Cell.Constant(Maybe.Some(now.AddMilliseconds(100))));
                    Stream<DateTime> m = a1.OrElse(a2).OrElse(a3);
                    m.ListenStrong(v => { lock (l) { l.Add(v); } });
                });

            // Wait for the alarms rather than assuming a fixed window is long enough. The alarms
            // are 99ms and 100ms out, so a flat 200ms sleep left about 100ms of slack, and a
            // loaded CI agent overran it: the run failed with zero events rather than the wrong
            // number, which is a delayed timer thread, not a coalescing bug. Waiting on the
            // condition makes a slow machine take longer instead of failing.
            //
            // The settle afterwards is what keeps the assertion meaningful: it still has to be
            // exactly two, so a third firing - a2 and a3 failing to coalesce - is caught rather
            // than being raced past.
            //
            // The lock is not incidental. l is written from the timer thread and read here, which
            // the original fixed sleep left unsynchronised.
            SpinWait.SpinUntil(
                () =>
                {
                    lock (l)
                    {
                        return l.Count >= 2;
                    }
                },
                TimeSpan.FromSeconds(10));
            Thread.Sleep(100);

            lock (l)
            {
                Assert.That(l.Count, Is.EqualTo(2));
            }
        }
    }
}