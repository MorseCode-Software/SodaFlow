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
        : base(implementation: new Implementation(), handleException: handleException)
    {
    }

    private class Implementation : TimerSystemImplementationBase<double>
    {
        // A Stopwatch, and not two readings of DateTime.Now. DateTime.Now moves with the system clock.
        // Thus, a change of daylight saving time or a correction of the clock moved this count by the
        // same quantity. A Stopwatch counts the time that goes by and nothing else.
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();

        public override double Now => this.stopwatch.Elapsed.TotalSeconds;

        protected override TimeSpan SubtractTimes(double first, double second) => TimeSpan.FromSeconds(first - second);
    }
}
