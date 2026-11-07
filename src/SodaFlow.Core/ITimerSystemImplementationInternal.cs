using System;

namespace SodaFlow;

/// <summary>
///     The members of a timer system implementation that
///     <see cref="TimerSystemInternal{T,TTimer}" /> uses.
/// </summary>
/// <remarks>
///     Each language package has its own public <c>ITimerSystemImplementation</c>,
///     with its own function types and its own <c>ITimer</c>. Core cannot name
///     these types. Thus, each package gives an object that passes these members
///     to its public interface.
/// </remarks>
/// <typeparam name="T">The type used to express a point in time.</typeparam>
/// <typeparam name="TTimer">The type of the handle that cancels a timer.</typeparam>
internal interface ITimerSystemImplementationInternal<T, TTimer>
{
    /// <summary>Gets the current time of the clock.</summary>
    T Now { get; }

    /// <summary>Starts the wait for the timers.</summary>
    void Start(Action<Exception> handleException);

    /// <summary>Stops the wait for the timers. After this, no timer fires.</summary>
    void Stop();

    /// <summary>Sets a timer that runs <paramref name="callback" /> at <paramref name="time" />.</summary>
    TTimer SetTimer(T time, Action callback);

    /// <summary>Cancels a timer that <see cref="SetTimer" /> set.</summary>
    void Cancel(TTimer timer);

    /// <summary>Fires each timer at or before <paramref name="time" />, on the calling thread.</summary>
    void RunTimersTo(T time);
}
