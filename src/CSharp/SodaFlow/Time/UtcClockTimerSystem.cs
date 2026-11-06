using System;
using JetBrains.Annotations;

namespace SodaFlow.Time;

/// <summary>
///     A timer system that measures time with <see cref="DateTime.UtcNow" />.
/// </summary>
/// <remarks>
///     UTC does not move at a change of daylight saving time, thus a timer set five
///     minutes ahead waits five minutes at each date. Use this type for a wait or a
///     deadline. Use <see cref="SystemClockTimerSystem" /> for a time that a screen
///     shows, or convert a value from <c>Time</c> with <c>ToLocalTime</c>.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class UtcClockTimerSystem : SystemClockTimerSystem
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="UtcClockTimerSystem" /> class.
    /// </summary>
    /// <param name="handleException">
    ///     Called with each exception from a wait for a timer, and from a timer that
    ///     fires.
    /// </param>
    // ReSharper disable once InheritdocConsiderUsage
    public UtcClockTimerSystem(Action<Exception> handleException)
        : this(resolution: TimeSpan.Zero, handleException: handleException)
    {
    }

    /// <summary>
    ///     Makes a timer system over <see cref="DateTime.UtcNow" /> that gives its
    ///     value in steps of <paramref name="resolution" />.
    /// </summary>
    /// <param name="resolution">
    ///     The step that each value of the clock rounds down to, or
    ///     <see cref="TimeSpan.Zero" /> for the value as it is. The constructor
    ///     refuses a negative step.
    /// </param>
    /// <param name="handleException">
    ///     The handler for an exception from a timer callback.
    /// </param>
    /// <remarks>
    ///     <see cref="ITimerSystem{T}.Time" /> updates only when the value of the
    ///     clock differs from the last one. Thus, with a resolution, it holds its
    ///     value through the transactions in one step, and each of those
    ///     transactions skips the update. An alarm fires in the first transaction
    ///     whose clock value is at or after its time. Thus, an alarm can fire up to
    ///     one step late, with its own time as before.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="resolution" /> is negative.
    /// </exception>
    // ReSharper disable once InheritdocConsiderUsage - The base documents a parameter this one does not have.
    public UtcClockTimerSystem(TimeSpan resolution, Action<Exception> handleException)
        : base(now: static () => DateTime.UtcNow, resolution: resolution, handleException: handleException)
    {
    }
}
