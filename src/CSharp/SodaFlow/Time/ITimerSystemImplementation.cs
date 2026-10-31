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
    ///     Starts whatever machinery this implementation uses to notice that a timer
    ///     has come due.
    /// </summary>
    /// <param name="handleException">
    ///     Called with each exception from a wait for a timer, and from a timer that
    ///     fires. It must absorb the exception: the implementation does not have to
    ///     continue after this throws.
    /// </param>
    /// <remarks>
    ///     Called one time, from the <see cref="TimerSystem{T}" /> constructor. An
    ///     implementation which waits must wait on a thread of its own, and not on the
    ///     thread pool. Alarms stop fully when the pool cannot schedule that wait.
    /// </remarks>
    void Start(Action<Exception> handleException);

    /// <summary>
    ///     Stops the machinery that <see cref="Start" /> began.
    /// </summary>
    /// <remarks>
    ///     Called from <see cref="TimerSystem{T}.Dispose" />. After it, no timer
    ///     fires: not on the implementation's own thread, and not from
    ///     <see cref="RunTimersTo" />. A timer that <see cref="SetTimer" /> sets
    ///     after it never fires. A second call does nothing. An implementation that
    ///     waits on a thread of its own ends that thread here. It does not wait for
    ///     that thread when the call comes from the thread itself.
    /// </remarks>
    void Stop();

    /// <summary>
    ///     Sets a timer that runs the given callback at the given time.
    /// </summary>
    /// <param name="time">The time to run the callback at.</param>
    /// <param name="callback">The callback to run.</param>
    /// <returns>A handle to cancel the timer.</returns>
    ITimer SetTimer(T time, Action callback);

    /// <summary>
    ///     Fires each timer scheduled at or before <paramref name="now" />, on the
    ///     calling thread.
    /// </summary>
    /// <param name="now">The point in time to run timers up to.</param>
    void RunTimersTo(T now);
}
