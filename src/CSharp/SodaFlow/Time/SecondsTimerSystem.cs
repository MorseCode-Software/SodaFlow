using System;
using JetBrains.Annotations;

namespace SodaFlow.Time;

/// <summary>
///     A timer system using the number of seconds since the application started.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class SecondsTimerSystem : TimerSystem<double>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SecondsTimerSystem" /> class, measuring time
    ///     as the number of seconds elapsed since this timer system was created.
    /// </summary>
    /// <param name="handleException">
    ///     Called with any exception raised while waiting for or firing timers.
    /// </param>
    // ReSharper disable once InheritdocConsiderUsage
    public SecondsTimerSystem(Action<Exception> handleException)
        : base(implementation: new Implementation(), handleException: handleException)
    {
    }

    private class Implementation : TimerSystemImplementationBase<double>
    {
        private readonly DateTime startTime = DateTime.Now;

        public override double Now => (DateTime.Now - this.startTime).TotalSeconds;

        protected override TimeSpan SubtractTimes(double first, double second) => TimeSpan.FromSeconds(first - second);
    }
}
