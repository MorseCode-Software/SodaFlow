using System;
using System.Collections.Generic;
using System.Threading;

namespace SodaFlow;

/// <summary>
///     The scheduling of the timer system implementations: it puts the timers in
///     order, waits on a thread of its own, and fires them.
/// </summary>
/// <remarks>
///     The <c>TimerSystemImplementationBase</c> of each language package gives
///     this object its clock, and passes each member of the implementation to it.
///     Before, each package had a copy of this code.
/// </remarks>
/// <typeparam name="T">The type used to express a point in time.</typeparam>
internal sealed class TimerScheduler<T>
{
    private readonly object lockObject = new();
    private readonly Func<T> now;
    private readonly Func<T, T, TimeSpan> subtractTimes;
    private readonly SortedSet<ScheduledTimer<T>> timers;

    // A change to the timer set signals this, to wake the timer thread. The thread then calculates
    // the new wait. This is an AutoResetEvent, and not a CancellationTokenSource. A signal latches
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

    // True while the set holds a timer. The flag is a copy of the set's count for the one reader
    // out of the lock. Code in the lock reads the set, not the flag. Each change of the set
    // writes the flag in the same lock: TryAdd, TimeUntilNext when a timer fires, and Remove.
    // RunTimersTo reads the flag out of the lock, and skips the lock in a transaction with no
    // timer set, which is most of them. A timer that a different thread adds at that moment
    // fires from the timer thread, or at the next transaction.
    private volatile bool hasTimers;

    /// <summary>Makes a scheduler over a clock.</summary>
    /// <param name="comparer">Puts two times in order.</param>
    /// <param name="now">Gives the current time. The timer thread reads it at each step.</param>
    /// <param name="subtractTimes">Gives the interval from its second time to its first.</param>
    internal TimerScheduler(IComparer<T> comparer, Func<T> now, Func<T, T, TimeSpan> subtractTimes)
    {
        this.now = now;
        this.subtractTimes = subtractTimes;
        this.timers = new SortedSet<ScheduledTimer<T>>(new TimerComparer(comparer));
    }

    /// <summary>Gives the number of the next timer.</summary>
    internal long NextSeq()
    {
        lock (this.lockObject)
        {
            return this.nextSeq++;
        }
    }

    /// <summary>
    ///     Adds <paramref name="timer" />, and wakes the timer thread. Gives false,
    ///     and adds nothing, after <see cref="Stop" />.
    /// </summary>
    internal bool TryAdd(ScheduledTimer<T> timer)
    {
        lock (this.lockObject)
        {
            // In the lock, because Stop sets the flag in it.
            if (this.stopped)
            {
                return false;
            }

            this.timers.Add(timer);
            this.hasTimers = true;

            // Signaled in the lock, because the timer thread disposes the event in the lock when
            // it ends, and a signal after that throws. The initial code signaled out of the lock,
            // because its CancellationTokenSource ran its callbacks synchronously. An
            // AutoResetEvent runs nothing, thus the lock is safe here.
            this.timersChanged.Set();
        }

        return true;
    }

    /// <summary>Removes <paramref name="timer" />, so that it does not fire.</summary>
    internal void Remove(ScheduledTimer<T> timer)
    {
        // This does not signal the timer thread, on purpose. A signal to calculate a deadline that
        // moved forward gains nothing, and with an AutoResetEvent it has a cost. The signal of the
        // TryAdd that replaces this timer latches for the next wait. Thus, one replacement causes
        // the thread to calculate one time, and not two times.
        lock (this.lockObject)
        {
            this.timers.Remove(timer);
            this.hasTimers = this.timers.Count > 0;
        }
    }

    /// <summary>
    ///     Fires each timer at or before <paramref name="time" />, on the calling
    ///     thread.
    /// </summary>
    internal void RunTimersTo(T time)
    {
        if (this.hasTimers)
        {
            this.TimeUntilNext(time);
        }
    }

    /// <summary>Starts the timer thread.</summary>
    /// <param name="handleException">
    ///     Called with each exception from a wait for a timer, and from a timer that
    ///     fires.
    /// </param>
    internal void Start(Action<Exception> handleException)
    {
        // A dedicated thread, and not the thread pool.
        //
        // No other code fires alarms. The transaction hook calls RunTimersTo, but only when a
        // transaction starts. Thus, code that only waits is dependent on this loop. A run on the
        // pool made that dependency dangerous for liveness. Each step needed a pool thread: one
        // time to start, and again for each Task.Delay continuation. A cancellation only put that
        // continuation in the queue. With a full pool the loop never ran, and no alarm fired.
        //
        // To reproduce it, fill the pool with blocking work. Eight of eight runs then sent zero
        // events after a wait of two seconds, for alarms one hundred milliseconds in the future.
        // With a pool that was not full, zero of eight runs did that. CI gave the same result:
        // always zero events, and not an incorrect count. That is the signature of timers that
        // never fire, and not of a miscount.
        //
        // Pool work cannot starve a background thread, and WaitOne is the timed wait and the
        // wake. StreamListenerManager takes this approach for its sweeper.
        Thread thread =
            new(() =>
            {
                while (!this.stopped)
                {
                    try
                    {
                        TimeSpan waitTime = this.TimeUntilNext(this.now());

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

                // The thread ends the event. TryAdd signals it only in the lock and only before
                // the stop, thus nothing signals it after this.
                lock (this.lockObject)
                {
                    this.timersChanged.Dispose();
                }
            }) { Name = "SodaFlow Timer Thread", IsBackground = true };

        this.timerThread = thread;
        thread.Start();
    }

    /// <summary>
    ///     Stops the timer thread. After this, no timer fires, and a second call does
    ///     nothing.
    /// </summary>
    internal void Stop()
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

    private TimeSpan TimeUntilNext(T time)
    {
        while (true)
        {
            ScheduledTimer<T>? fired = null;
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
                    // Min is a property of the set. First is the LINQ method, which allocates an
                    // enumerator at each call, and this runs at each wake of the timer thread.
                    // ReSharper disable once NullableWarningSuppressionIsUsed - The set holds at least one timer, thus
                    // Min is not null.
                    ScheduledTimer<T> timer = this.timers.Min!;
                    waitTime = this.subtractTimes(arg1: timer.Time, arg2: time);

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

    // In the sequence of time, and in the sequence of construction for one time.
    private sealed class TimerComparer(in IComparer<T> comparer) : IComparer<ScheduledTimer<T>>
    {
        private readonly IComparer<T> comparer = comparer;

        public int Compare(ScheduledTimer<T>? x, ScheduledTimer<T>? y)
        {
            if (ReferenceEquals(objA: x, objB: y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            int timeComparison = this.comparer.Compare(x: x.Time, y: y.Time);
            return timeComparison != 0 ? timeComparison : x.Seq.CompareTo(value: y.Seq);
        }
    }
}
