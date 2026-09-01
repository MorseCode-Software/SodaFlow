using System;

namespace SodaFlow.Time
{
    /// <summary>
    ///     A timer system using the current system clock.
    /// </summary>
    public class SystemClockTimerSystem : TimerSystem<DateTime>
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="SystemClockTimerSystem" /> class, measuring
        ///     time with <see cref="DateTime.Now" />.
        /// </summary>
        /// <param name="handleException">
        ///     Called with any exception raised while waiting for or firing timers.
        /// </param>
        public SystemClockTimerSystem(Action<Exception> handleException)
            : base(new Implementation(), handleException)
        {
        }

        internal class Implementation : TimerSystemImplementationBase<DateTime>
        {
            protected override TimeSpan SubtractTimes(DateTime first, DateTime second) => first - second;
            public override DateTime Now => DateTime.Now;
        }
    }
}
