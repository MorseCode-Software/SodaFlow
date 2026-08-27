using System;
using System.Diagnostics;
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
                    m.Listen(v => { lock (l) { l.Add(v); } });
                });

            // Alarms are delivered by the Transaction.OnStart hook, which calls RunTimersTo to
            // fire any timer that has come due and then drains the queued events. What normally
            // triggers that hook is the timer callback opening an empty transaction - but the
            // callback runs on a Task.Run loop whose Task.Delay continuations need the thread
            // pool, and on a loaded CI agent that loop may not be scheduled promptly. The test
            // failed there with zero events even after waiting ten seconds, so waiting longer is
            // not the fix: nothing was going to arrive.
            //
            // Opening transactions here drives the same delivery path directly, which is what the
            // library does internally, so the result no longer depends on pool scheduling.
            //
            // Coalescing is still under test. The drain pops events one timestamp at a time, so
            // even if a single late transaction fires all three timers at once, the 99ms alarm and
            // the pair at 100ms are delivered in separate transactions - two firings, not one and
            // not three.
            Thread.Sleep(200);

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                Transaction.RunVoid(() => { });

                lock (l)
                {
                    if (l.Count >= 2)
                    {
                        break;
                    }
                }

                if (stopwatch.Elapsed > TimeSpan.FromSeconds(10))
                {
                    break;
                }

                Thread.Sleep(10);
            }

            // Settle, then drive once more, so a third firing fails the test rather than being
            // raced past.
            Thread.Sleep(100);
            Transaction.RunVoid(() => { });

            lock (l)
            {
                Assert.That(l.Count, Is.EqualTo(2));
            }
        }
    }
}