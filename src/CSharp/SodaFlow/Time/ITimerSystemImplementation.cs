using System;
using JetBrains.Annotations;

namespace SodaFlow.Time;

/// <summary>
///     An interface for implementations of FRP timer systems.
/// </summary>
/// <typeparam name="T">The underlying type of the timer's time values.</typeparam>
[PublicAPI]
public interface ITimerSystemImplementation<T>
{
    /// <summary>
    ///     Gets the current time according to this implementation's clock.
    /// </summary>
    /// <value>The current point in time.</value>
    T Now { get; }

    /// <summary>
    ///     Starts whatever machinery this implementation uses to notice that a timer has come due.
    /// </summary>
    /// <param name="handleException">
    ///     Called with any exception raised while waiting for or firing timers. It is expected to
    ///     absorb the exception: the implementation is not required to keep running if this throws.
    /// </param>
    /// <remarks>
    ///     Called once, from the <see cref="TimerSystem{T}" /> constructor. An implementation which
    ///     waits should do so on a thread it owns rather than on the thread pool, since alarms
    ///     stop being delivered entirely if that wait cannot be scheduled.
    /// </remarks>
    void Start(Action<Exception> handleException);

    /// <summary>
    ///     Set a timer that will execute the specified callback at the specified time.
    /// </summary>
    /// <param name="time">The time at which to execute the callback.</param>
    /// <param name="callback">The callback to execute.</param>
    /// <returns>A handle that can be used to cancel the timer.</returns>
    ITimer SetTimer(T time, Action callback);

    /// <summary>
    ///     Fires every timer scheduled at or before <paramref name="now" />, on the calling thread.
    /// </summary>
    /// <param name="now">The point in time to run timers up to.</param>
    void RunTimersTo(T now);
}
