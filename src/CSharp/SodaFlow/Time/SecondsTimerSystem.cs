using System;
using System.Diagnostics;
using JetBrains.Annotations;

namespace SodaFlow.Time;

/// <summary>
///     A timer system that uses the number of seconds after the start of the
///     process.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class SecondsTimerSystem : TimerSystem<double>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SecondsTimerSystem" /> class,
    ///     measuring time as the number of seconds after the construction of this
    ///     timer system.
    /// </summary>
    /// <param name="handleException">
    ///     Called with each exception from a wait for a timer, and from a timer that
    ///     fires.
    /// </param>
    // ReSharper disable once InheritdocConsiderUsage
    public SecondsTimerSystem(Action<Exception> handleException)
        : this(resolution: TimeSpan.Zero, handleException: handleException)
    {
    }

    /// <summary>
    ///     Makes a timer system whose clock gives its value in steps of
    ///     <paramref name="resolution" />.
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
    public SecondsTimerSystem(TimeSpan resolution, Action<Exception> handleException)
        : base(implementation: new Implementation(resolution), handleException: handleException)
    {
    }

    private sealed class Implementation(in TimeSpan resolution) : TimerSystemImplementationBase<double>
    {
        // A Stopwatch, and not two readings of DateTime.Now. DateTime.Now moves with the system clock.
        // Thus, a change of daylight saving time or a correction of the clock moved this count by the
        // same quantity. A Stopwatch counts the time that goes by and nothing else.
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();

        // Zero reads the stopwatch as it is. That path is the one from before resolutions, with no
        // arithmetic of its own.
        private readonly long resolutionTicks = ClockResolution.StopwatchTicks(resolution);

        public override double Now =>
            this.resolutionTicks == 0
                ? this.stopwatch.Elapsed.TotalSeconds
                : this.RoundDown(this.stopwatch.ElapsedTicks);

        // In the ticks of the stopwatch, so that the step is accurate. A division of the seconds
        // as a double can move the step by the error of the division.
        private double RoundDown(long ticks) =>
            (ticks - ticks % this.resolutionTicks) / (double)Stopwatch.Frequency;

        protected override TimeSpan SubtractTimes(double first, double second) => TimeSpan.FromSeconds(first - second);
    }
}
