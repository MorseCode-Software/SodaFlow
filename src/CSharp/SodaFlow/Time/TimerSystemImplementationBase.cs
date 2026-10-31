using System;
using System.Collections.Generic;
using System.Threading;
using JetBrains.Annotations;

namespace SodaFlow.Time;

/// <summary>
///     A base for timer system implementations which supplies the scheduling,
///     leaving a derived type to supply only the clock.
/// </summary>
/// <typeparam name="T">The type used to express a point in time.</typeparam>
/// <remarks>
///     Derive from this and write <see cref="Now" /> and
///     <see cref="SubtractTimes" />. The This type puts the timers in order,
///     waits, and fires them. <see cref="SystemClockTimerSystem" /> and
///     <see cref="SecondsTimerSystem" /> are the two implementations that ship.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public abstract class TimerSystemImplementationBase<T> : ITimerSystemImplementation<T>
    where T : IComparable
{
    private readonly object lockObject = new();
    private readonly SortedSet<SimpleTimer> timers = [];

    // Signaled when the timer set changes, to wake the timer thread so it can recompute
    // how long to wait. An AutoResetEvent, and not a CancellationTokenSource. A signal latches
    // after the thread calculates its wait and before that wait starts. Thus, the next wait
    // returns immediately, and does not sleep through the change. The previous code allocated a
    // new CancellationTokenSource at each step, and disposed of none of them.
    private readonly AutoResetEvent timersChanged = new(false);

    private long nextSeq;

    // Set by Stop, with the lock. The timer thread reads it at each step, out of the lock, thus
    // it is volatile.
    private volatile bool stopped;

    // The thread that Start made, for Stop to wait for. Null before Start.
    private Thread? timerThread;

    // True while the set holds a timer. Each change of the set writes it in the lock. RunTimersTo
    // reads it out of the lock, and skips the lock in a transaction with no timer set, which is
    // most of them. A timer that a different thread adds at that moment fires from the timer
    // thread, or at the next transaction.
    private volatile bool hasTimers;

    /// <inheritdoc />
    public void Start(Action<Exception> handleException)
    {
        // A dedicated thread rather than Task.Run.
        //
        // No other code fires alarms. The <c>Transaction.OnStart</c> handler calls RunTimersTo, but only when
        // some transaction happens to start, thus code that only waits is dependent
        // fully on this loop. A run on the thread pool made that dependency dangerous for liveness.
        // Each step needed a pool thread, one time to start and again for each Task.Delay
        // continuation. A cancellation only put that continuation in the queue. With the pool
        // saturated the loop simply never ran, and alarms never fired.
        //
        // That is not theoretical. To reproduce it, fill the pool with blocking work. Eight of eight
        // runs then gave zero events after a wait of two seconds for alarms a hundred milliseconds
        // in the future. With a pool that was not full, zero of eight runs did that. CI gave the
        // same incorrect result: always zero events, and not an incorrect count. That is
        // the signature of timers that never fire, and not of a miscount.
        //
        // Pool work cannot starve a background thread, and WaitOne is the timed
        // wait and the wake. StreamListenerManager takes this approach for its sweeper.
        Thread thread =
            new(() =>
            {
                while (!this.stopped)
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

                // The thread ends the event. SetTimer signals it only in the lock and only before
                // the stop, thus nothing signals it after this.
                lock (this.lockObject)
                {
                    this.timersChanged.Dispose();
                }
            }) { Name = "SodaFlow Timer Thread", IsBackground = true };

        this.timerThread = thread;
        thread.Start();
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (this.lockObject)
        {
            if (this.stopped)
            {
                return;
            }

            this.stopped = true;
            this.timersChanged.Set();
        }

        // A wait for the thread, so that no timer fires after this returns. Not from the thread
        // itself: the listener of an alarm runs on it, and a disposal there must not wait for it.
        if (this.timerThread is { } thread && thread != Thread.CurrentThread)
        {
            thread.Join();
        }
    }

    /// <summary>
    ///     Schedules <paramref name="callback" /> to run when the clock reaches
    ///     <paramref name="time" />.
    /// </summary>
    /// <param name="time">The time at which to run the callback.</param>
    /// <param name="callback">The callback to run.</param>
    /// <returns>A handle to cancel the timer before it fires.</returns>
    /// <remarks>
    ///     A time before now fires at the next opportunity, and the timer system does
    ///     not drop it. The callback runs on the timer thread, or on whichever thread
    ///     called <see cref="RunTimersTo" />, and never while this instance's internal
    ///     lock is held.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    public ITimer SetTimer(T time, Action callback)
    {
        SimpleTimer timer = new(implementation: this, time: time, callback: callback);

        lock (this.lockObject)
        {
            this.timers.Add(timer);
            this.hasTimers = true;

            // Signaled in the lock, because the timer thread disposes the event in the lock when
            // it ends, and a signal after that throws. The initial code signaled out of the lock,
            // because its CancellationTokenSource ran its callbacks synchronously. An
            // AutoResetEvent runs nothing, thus the lock is safe here.
            if (!this.stopped)
            {
                this.timersChanged.Set();
            }
        }

        return timer;
    }

    /// <summary>
    ///     Fires each timer scheduled at or before <paramref name="now" />, on the
    ///     calling thread.
    /// </summary>
    /// <param name="now">The point in time to run timers up to.</param>
    /// <remarks>
    ///     The <c>Transaction.OnStart</c> handler calls this. Thus, alarms can start
    ///     at a transaction that happens to start rather than only by the timer
    ///     thread.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    public void RunTimersTo(T now)
    {
        if (this.hasTimers)
        {
            this.TimeUntilNext(now);
        }
    }

    /// <summary>
    ///     Gets the current time according to this implementation's clock.
    /// </summary>
    /// <value>The current point in time.</value>
    /// <remarks>
    ///     The timer thread reads this frequently, thus its cost must be low. It must
    ///     also move forward sufficiently for the clock to get to each scheduled time.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    public abstract T Now { get; }

    private TimeSpan TimeUntilNext(T now)
    {
        while (true)
        {
            SimpleTimer? fired = null;
            TimeSpan waitTime;

            lock (this.lockObject)
            {
                // A stopped implementation fires nothing, also from RunTimersTo.
                if (this.stopped || this.timers.Count < 1)
                {
                    waitTime = TimeSpan.FromSeconds(1000);
                }
                else
                {
                    // How long to the first timer? Min is a property of the set. First is the
                    // LINQ method, which allocates an enumerator at each call, and this runs at
                    // each wake of the timer thread.
                    // ReSharper disable once NullableWarningSuppressionIsUsed - The set holds at least one timer, thus
                    // Min is not null.
                    SimpleTimer timer = this.timers.Min!;
                    waitTime = this.SubtractTimes(first: timer.Time, second: now);

                    if (waitTime <= TimeSpan.Zero)
                    {
                        waitTime = TimeSpan.Zero;
                        fired = timer;
                        this.timers.Remove(fired);
                        this.hasTimers = this.timers.Count > 0;
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
    /// <param name="first">The latter point in time.</param>
    /// <param name="second">The earlier point in time.</param>
    /// <returns>
    ///     The interval from <paramref name="second" /> to <paramref name="first" />,
    ///     negative if <paramref name="first" /> is the earlier of the two.
    /// </returns>
    /// <remarks>
    ///     This calculates the wait for the next timer, thus it must return a true
    ///     interval rather than a compare result.
    /// </remarks>
    protected abstract TimeSpan SubtractTimes(T first, T second);

    private class SimpleTimer : ITimer, IComparable<SimpleTimer>
    {
        internal readonly Action Callback;

        internal readonly T Time;
        private readonly TimerSystemImplementationBase<T> implementation;
        private readonly long seq;

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

        public int CompareTo(SimpleTimer? o)
        {
            if (o is null)
            {
                return 1;
            }

            int timeComparison = this.Time.CompareTo(o.Time);
            return timeComparison != 0 ? timeComparison : this.seq.CompareTo(o.seq);
        }

        public void Cancel()
        {
            lock (this.implementation.lockObject)
            {
                this.implementation.timers.Remove(this);
                this.implementation.hasTimers = this.implementation.timers.Count > 0;
            }
        }

        public void Dispose() => this.Cancel();
    }
}
