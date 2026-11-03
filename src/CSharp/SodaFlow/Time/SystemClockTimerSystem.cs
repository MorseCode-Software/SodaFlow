using System;
using JetBrains.Annotations;

namespace SodaFlow.Time;

/// <summary>
///     A timer system that measures time as a <see cref="DateTime" /> from a clock.
///     The default clock is <see cref="DateTime.Now" />, the local time.
/// </summary>
/// <remarks>
///     <para>
///         Local time moves with the clock of the machine. A change of daylight saving
///         time or a correction of the clock moves it by the same quantity. A timer
///         across that change then waits for its time on the moved clock. Use
///         <see cref="UtcClockTimerSystem" /> for a wait or a deadline, and this type
///         for a time that a screen shows.
///     </para>
///     <para>
///         The constructor with a clock takes any function that gives a
///         <see cref="DateTime" />. A test gives one that it moves by hand. The timer
///         system then fires its timers at each transaction that starts after the
///         clock passes them.
///     </para>
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class SystemClockTimerSystem : TimerSystem<DateTime>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SystemClockTimerSystem" />
    ///     class, measuring time with <see cref="DateTime.Now" />.
    /// </summary>
    /// <param name="handleException">
    ///     Called with each exception from a wait for a timer, and from a timer that
    ///     fires.
    /// </param>
    // ReSharper disable once InheritdocConsiderUsage
    public SystemClockTimerSystem(Action<Exception> handleException)
        : this(now: static () => DateTime.Now, handleException: handleException)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="SystemClockTimerSystem" />
    ///     class, measuring time with <paramref name="now" />.
    /// </summary>
    /// <param name="now">
    ///     Gives the current time. The timer thread reads it frequently, thus its
    ///     cost must be low. It must also move forward sufficiently for the clock to
    ///     get to each scheduled time.
    /// </param>
    /// <param name="handleException">
    ///     Called with each exception from a wait for a timer, and from a timer that
    ///     fires.
    /// </param>
    // ReSharper disable once InheritdocConsiderUsage
    public SystemClockTimerSystem(Func<DateTime> now, Action<Exception> handleException)
        : base(implementation: new Implementation(now), handleException: handleException)
    {
    }

    internal sealed class Implementation(in Func<DateTime> now) : TimerSystemImplementationBase<DateTime>
    {
        private readonly Func<DateTime> now = now;

        public override DateTime Now => this.now();

        protected override TimeSpan SubtractTimes(DateTime first, DateTime second) => first - second;
    }
}
