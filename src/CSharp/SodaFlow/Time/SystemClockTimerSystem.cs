using System;
using JetBrains.Annotations;

namespace SodaFlow.Time;

/// <summary>
///     A timer system using the current system clock.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class SystemClockTimerSystem : TimerSystem<DateTime>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SystemClockTimerSystem" /> class, measuring
    ///     time with <see cref="DateTime.Now" />.
    /// </summary>
    /// <param name="handleException">
    ///     Called with any exception raised while waiting for or firing timers.
    /// </param>
    // ReSharper disable once InheritdocConsiderUsage
    public SystemClockTimerSystem(Action<Exception> handleException)
        : base(implementation: new Implementation(), handleException: handleException)
    {
    }

    internal class Implementation : TimerSystemImplementationBase<DateTime>
    {
        public override DateTime Now => DateTime.Now;
        protected override TimeSpan SubtractTimes(DateTime first, DateTime second) => first - second;
    }
}
