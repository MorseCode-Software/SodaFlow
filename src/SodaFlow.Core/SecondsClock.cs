using System;
using System.Diagnostics;

namespace SodaFlow;

/// <summary>
///     The clock of the timer systems that measure time as the number of seconds
///     after their construction.
/// </summary>
internal sealed class SecondsClock
{
    // A Stopwatch, and not two readings of DateTime.Now. DateTime.Now moves with the system clock.
    // Thus, a change of daylight saving time or a correction of the clock moved this count by the
    // same quantity. A Stopwatch counts the time that goes by and nothing else.
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();

    // Zero reads the stopwatch as it is. That path is the one from before resolutions, with no
    // arithmetic of its own.
    private readonly long resolutionTicks;

    // This type has its own constructor, and not a primary constructor with in parameters. F# cannot
    // give a value to an in parameter of a constructor, and the F# package makes this type.
    internal SecondsClock(TimeSpan resolution) => this.resolutionTicks = ClockResolution.StopwatchTicks(resolution);

    /// <summary>Gets the seconds after the construction, rounded down to the resolution.</summary>
    internal double Now =>
        this.resolutionTicks == 0
            ? this.stopwatch.Elapsed.TotalSeconds
            : this.RoundDown(this.stopwatch.ElapsedTicks);

    /// <summary>Gives the interval from <paramref name="second" /> to <paramref name="first" />.</summary>
    internal static TimeSpan Subtract(double first, double second) => TimeSpan.FromSeconds(first - second);

    // In the ticks of the stopwatch, so that the step is accurate. A division of the seconds
    // as a double can move the step by the error of the division.
    private double RoundDown(long ticks) =>
        (ticks - ticks % this.resolutionTicks) / (double)Stopwatch.Frequency;
}
