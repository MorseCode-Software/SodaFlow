using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SodaFlow.Time
{
    /// <summary>
    ///     A base for timer system implementations which supplies the scheduling, leaving a derived type
    ///     to supply only the clock.
    /// </summary>
    /// <typeparam name="T">The type used to express a point in time.</typeparam>
    /// <remarks>
    ///     Derive from this and implement <see cref="Now" /> and <see cref="SubtractTimes" />; the
    ///     ordering, waiting and firing of timers is handled here. <see cref="SystemClockTimerSystem" />
    ///     and <see cref="SecondsTimerSystem" /> are the two implementations that ship.
    /// </remarks>
    public abstract class TimerSystemImplementationBase<T> : ITimerSystemImplementation<T>
        where T : IComparable
    {
        private readonly object lockObject = new object();
        private readonly SortedSet<SimpleTimer> timers = new SortedSet<SimpleTimer>();

        // Signaled whenever the timer set changes, to wake the timer thread so it can recompute
        // how long to wait. An AutoResetEvent rather than a CancellationTokenSource: a signal
        // raised while the thread is between computing its wait and entering it is latched, so the
        // next wait returns immediately instead of sleeping through the change. The previous
        // design allocated a fresh CancellationTokenSource on every iteration and never disposed
        // one.
        private readonly AutoResetEvent timersChanged = new AutoResetEvent(false);

        private long nextSeq;

        private TimeSpan TimeUntilNext(T now)
        {
            while (true)
            {
                SimpleTimer fired = null;
                TimeSpan waitTime;
                lock (this.lockObject)
                {
                    if (this.timers.Count < 1)
                    {
                        waitTime = TimeSpan.FromSeconds(1000);
                    }
                    else
                    {
                        // How long till the first timer?
                        SimpleTimer timer = this.timers.First();
                        waitTime = this.SubtractTimes(timer.Time, now);
                        if (waitTime <= TimeSpan.Zero)
                        {
                            waitTime = TimeSpan.Zero;
                            fired = timer;
                            this.timers.Remove(fired);
                        }
                    }
                }

                if (fired != null)
                {
                    fired.Callback();
                }
                else
                {
                    return waitTime;
                }
            }
        }

        /// <summary>
        ///     Returns how much time separates two points on this implementation's clock.
        /// </summary>
        /// <param name="first">The later point in time.</param>
        /// <param name="second">The earlier point in time.</param>
        /// <returns>
        ///     The interval from <paramref name="second" /> to <paramref name="first" />, negative if
        ///     <paramref name="first" /> is the earlier of the two.
        /// </returns>
        /// <remarks>
        ///     Used to work out how long to wait for the next timer, so it must return a real duration
        ///     rather than a comparison result.
        /// </remarks>
        protected abstract TimeSpan SubtractTimes(T first, T second);

        // A dedicated thread rather than Task.Run.
        //
        // Nothing else fires alarms: the Transaction.OnStart hook calls RunTimersTo, but only when
        // some transaction happens to start, so an application that is merely waiting depends
        // entirely on this loop. Running it on the thread pool made that dependency a liveness
        // hazard - every iteration needed a pool thread, once to start and again for each
        // Task.Delay continuation, and a cancellation only queued that continuation. With the pool
        // saturated the loop simply never ran, and alarms were never fired at all.
        //
        // That is not theoretical. Reproduced by saturating the pool with blocking work: eight of
        // eight runs delivered zero events after waiting two seconds for alarms a hundred
        // milliseconds out, against zero of eight unstarved. It had been failing intermittently on
        // CI, always with zero events rather than the wrong number, which is the signature of the
        // timers never firing rather than of a miscount.
        //
        // A background thread cannot be starved by pool work, and WaitOne serves as both the timed
        // wait and the wake. StreamListenerManager already takes this approach for its sweeper.
        /// <summary>
        ///     Starts the thread which waits for timers to come due and fires them.
        /// </summary>
        /// <param name="handleException">
        ///     Called with any exception raised while waiting for or firing timers. The loop continues
        ///     afterward, so this should absorb rather than rethrow.
        /// </param>
        /// <remarks>
        ///     Called once by the <see cref="TimerSystem{T}" /> constructor. The thread runs for the
        ///     lifetime of the process and is a background thread, so it does not keep the process
        ///     alive.
        /// </remarks>
        public void Start(Action<Exception> handleException)
        {
            Thread timerThread = new Thread(
                () =>
                {
                    while (true)
                    {
                        try
                        {
                            TimeSpan waitTime = this.TimeUntilNext(this.Now);
                            if (waitTime > TimeSpan.Zero)
                            {
                                this.timersChanged.WaitOne(waitTime);
                            }
                        }
                        catch (Exception e)
                        {
                            handleException(e);
                        }
                    }
                    // ReSharper disable once FunctionNeverReturns - This is a timer loop.  It should run until the application ends.
                })
            {
                Name = "SodaFlow Timer Thread",
                IsBackground = true
            };

            timerThread.Start();
        }

        /// <summary>
        ///     Schedules <paramref name="callback" /> to run once the clock reaches
        ///     <paramref name="time" />.
        /// </summary>
        /// <param name="time">The time at which to run the callback.</param>
        /// <param name="callback">The callback to run.</param>
        /// <returns>A handle which can be used to cancel the timer before it fires.</returns>
        /// <remarks>
        ///     A time already in the past fires at the next opportunity rather than being dropped. The
        ///     callback runs on the timer thread, or on whichever thread called
        ///     <see cref="RunTimersTo" />, and never while this instance's internal lock is held.
        /// </remarks>
        public ITimer SetTimer(T time, Action callback)
        {
            SimpleTimer timer = new SimpleTimer(this, time, callback);
            lock (this.lockObject)
            {
                this.timers.Add(timer);
            }

            // Signaled outside the lock. Canceling the old token source was done while holding
            // it, and cancellation runs its callbacks synchronously, so the waiting loop could
            // resume inline on this thread and re-enter TimeUntilNext while the caller still held
            // the lock.
            this.timersChanged.Set();
            return timer;
        }

        /// <summary>
        ///     Fires every timer scheduled at or before <paramref name="now" />, on the calling thread.
        /// </summary>
        /// <param name="now">The point in time to run timers up to.</param>
        /// <remarks>
        ///     Called from the transaction start hook, which is what lets alarms be delivered by a
        ///     transaction that happens to start rather than only by the timer thread.
        /// </remarks>
        public void RunTimersTo(T now) => this.TimeUntilNext(now);

        /// <summary>
        ///     Gets the current time according to this implementation's clock.
        /// </summary>
        /// <value>The current point in time.</value>
        /// <remarks>
        ///     Read frequently by the timer thread, so it should be cheap and must move forward
        ///     monotonically enough that scheduled times are eventually reached.
        /// </remarks>
        public abstract T Now { get; }

        private class SimpleTimer : ITimer, IComparable<SimpleTimer>
        {
            private readonly TimerSystemImplementationBase<T> implementation;
            private readonly long seq;

            internal readonly T Time;
            internal readonly Action Callback;

            internal SimpleTimer(TimerSystemImplementationBase<T> implementation, T time, Action callback)
            {
                this.implementation = implementation;
                this.Time = time;
                this.Callback = callback;

                lock (implementation.lockObject)
                {
                    this.seq = implementation.nextSeq++;
                }
            }

            public void Cancel()
            {
                lock (this.implementation.lockObject)
                {
                    this.implementation.timers.Remove(this);
                }
            }

            public int CompareTo(SimpleTimer o)
            {
                int timeComparison = this.Time.CompareTo(o.Time);
                return timeComparison != 0 ? timeComparison : this.seq.CompareTo(o.seq);
            }

            public void Dispose()
            {
                this.Cancel();
            }
        }
    }
}