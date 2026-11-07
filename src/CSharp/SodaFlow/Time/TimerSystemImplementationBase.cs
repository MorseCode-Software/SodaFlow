using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace SodaFlow.Time;

/// <summary>
///     A base for timer system implementations which supplies the scheduling,
///     leaving a derived type to supply only the clock.
/// </summary>
/// <typeparam name="T">
///     The type used to express a point in time. The constraint is the generic
///     <see cref="IComparable{T}" />, as on <see cref="TimerSystem{T}" />, thus a
///     comparison of two times boxes nothing.
/// </typeparam>
/// <remarks>
///     Derive from this and write <see cref="Now" /> and
///     <see cref="SubtractTimes" />. This type puts the timers in order, waits,
///     and fires them. <see cref="SystemClockTimerSystem" /> and
///     <see cref="SecondsTimerSystem" /> are the two implementations that ship.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public abstract class TimerSystemImplementationBase<T> : ITimerSystemImplementation<T>
    where T : IComparable<T>
{
    // The scheduling is in Core, and the F# package uses the same one. This class gives it the
    // clock of the derived type.
    private readonly TimerScheduler<T> scheduler;

    /// <summary>
    ///     Initializes a new instance of the
    ///     <see cref="TimerSystemImplementationBase{T}" /> class.
    /// </summary>
    protected TimerSystemImplementationBase() =>
        this.scheduler =
            new TimerScheduler<T>(
                comparer: Comparer<T>.Default,
                now: () => this.Now,
                subtractTimes: this.SubtractTimes);

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

    /// <inheritdoc />
    public void Start(Action<Exception> handleException) => this.scheduler.Start(handleException);

    /// <inheritdoc />
    public void Stop() => this.scheduler.Stop();

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
    /// <exception cref="ObjectDisposedException">
    ///     <see cref="Stop" /> ran. A timer set after that never fires, and a handle
    ///     for it tells the caller nothing.
    /// </exception>
    // ReSharper disable once InheritdocConsiderUsage
    public ITimer SetTimer(T time, Action callback)
    {
        SimpleTimer timer = new(scheduler: this.scheduler, time: time, callback: callback);

        return this.scheduler.TryAdd(timer) ? timer : throw new ObjectDisposedException(this.GetType().FullName);
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
    public void RunTimersTo(T now) => this.scheduler.RunTimersTo(now);

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

    // The timer of the scheduler, which is also the handle that SetTimer gives. Thus, a timer is
    // one object.
    private sealed class SimpleTimer(in TimerScheduler<T> scheduler, in T time, in Action callback)
        : ScheduledTimer<T>(scheduler: scheduler, time: time, callback: callback), ITimer
    {
        void ITimer.Cancel() => this.Cancel();

        public void Dispose() => this.Cancel();
    }
}
