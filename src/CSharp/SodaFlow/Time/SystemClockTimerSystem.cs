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
        : this(now: now, resolution: TimeSpan.Zero, handleException: handleException)
    {
    }

    /// <summary>
    ///     Makes a timer system over <see cref="DateTime.Now" /> that gives its
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
    public SystemClockTimerSystem(TimeSpan resolution, Action<Exception> handleException)
        : this(now: static () => DateTime.Now, resolution: resolution, handleException: handleException)
    {
    }

    /// <summary>
    ///     Makes a timer system over <paramref name="now" /> that gives its value
    ///     in steps of <paramref name="resolution" />.
    /// </summary>
    /// <param name="now">The clock. The timer thread reads it frequently.</param>
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
    public SystemClockTimerSystem(Func<DateTime> now, TimeSpan resolution, Action<Exception> handleException)
        : base(implementation: new Implementation(now: now, resolution: resolution), handleException: handleException)
    {
    }

    internal sealed class Implementation(in Func<DateTime> now, in TimeSpan resolution)
        : TimerSystemImplementationBase<DateTime>
    {
        private readonly Func<DateTime> now = now;

        // Zero reads the clock as it is, with no arithmetic of its own.
        private readonly long resolutionTicks = ClockResolution.Ticks(resolution);

        public override DateTime Now
        {
            get
            {
                DateTime time = this.now();

                return this.resolutionTicks == 0
                    ? time
                    : ClockResolution.RoundDown(time: time, resolutionTicks: this.resolutionTicks);
            }
        }

        protected override TimeSpan SubtractTimes(DateTime first, DateTime second) => first - second;
    }
}
