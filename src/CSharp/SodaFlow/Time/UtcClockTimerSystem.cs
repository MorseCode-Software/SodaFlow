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
        : base(now: static () => DateTime.UtcNow, handleException: handleException)
    {
    }
}
