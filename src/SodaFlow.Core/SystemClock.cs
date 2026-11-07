using System;

namespace SodaFlow;

/// <summary>
///     The clock of the timer systems that measure time as a
///     <see cref="DateTime" />. It reads the local clock, the UTC clock, or a
///     clock that a caller gives.
/// </summary>
internal sealed class SystemClock(in Func<DateTime> now, in TimeSpan resolution)
{
    private readonly Func<DateTime> now = now;

    // Zero reads the clock as it is, with no arithmetic of its own.
    private readonly long resolutionTicks = ClockResolution.Ticks(resolution);

    /// <summary>Gets the current time, rounded down to the resolution.</summary>
    internal DateTime Now
    {
        get
        {
            DateTime time = this.now();

            return this.resolutionTicks == 0
                ? time
                : ClockResolution.RoundDown(time: time, resolutionTicks: this.resolutionTicks);
        }
    }

    /// <summary>Gives the interval from <paramref name="second" /> to <paramref name="first" />.</summary>
    internal static TimeSpan Subtract(DateTime first, DateTime second) => first - second;
}
