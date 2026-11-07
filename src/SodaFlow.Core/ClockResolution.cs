using System;
using System.Diagnostics;

namespace SodaFlow;

/// <summary>
///     The resolution of a clock, as the clocks of the timer systems use it. It
///     is the step that <c>Now</c> rounds down to, or zero for the value as it is.
/// </summary>
internal static class ClockResolution
{
    /// <summary>
    ///     The resolution in the ticks of <see cref="DateTime" />, or zero for the
    ///     value as it is.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="resolution" /> is negative.
    /// </exception>
    internal static long Ticks(TimeSpan resolution) =>
        resolution < TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(
                paramName: nameof(resolution),
                actualValue: resolution,
                message: "A clock's resolution cannot be negative. Zero reads the clock as it is.")
            : resolution.Ticks;

    /// <summary>
    ///     The resolution in the ticks of a <see cref="Stopwatch" />, or zero for the
    ///     value as it is. A resolution below one tick of the stopwatch gives zero,
    ///     because the stopwatch cannot count below its own tick.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="resolution" /> is negative.
    /// </exception>
    internal static long StopwatchTicks(TimeSpan resolution) =>
        (long)(Ticks(resolution) * (Stopwatch.Frequency / (double)TimeSpan.TicksPerSecond));

    /// <summary>
    ///     Rounds <paramref name="time" /> down to a multiple of
    ///     <paramref name="resolutionTicks" />, which is not zero, and keeps its
    ///     <see cref="DateTime.Kind" />.
    /// </summary>
    internal static DateTime RoundDown(DateTime time, long resolutionTicks) =>
        new(ticks: time.Ticks - time.Ticks % resolutionTicks, kind: time.Kind);
}
