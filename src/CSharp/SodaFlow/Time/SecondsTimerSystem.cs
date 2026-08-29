using System;

namespace SodaFlow.Time
{
    /// <summary>
    ///     A timer system using the number of seconds since the application started.
    /// </summary>
    public sealed class SecondsTimerSystem : TimerSystem<double>
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="SecondsTimerSystem" /> class, measuring time
        ///     as the number of seconds elapsed since this timer system was created.
        /// </summary>
        /// <param name="handleException">
        ///     Called with any exception raised while waiting for or firing timers.
        /// </param>
        public SecondsTimerSystem(Action<Exception> handleException)
            : base(new Implementation(), handleException)
        {
        }

        private class Implementation : TimerSystemImplementationBase<double>
        {
            private readonly DateTime startTime;

            public Implementation() => this.startTime = DateTime.Now;

            protected override TimeSpan SubtractTimes(double first, double second) =>
                TimeSpan.FromSeconds(first - second);

            public override double Now => (DateTime.Now - this.startTime).TotalSeconds;
        }
    }
}