using System;

namespace SodaFlow;

/// <summary>
///     One timer that a <see cref="TimerScheduler{T}" /> holds.
/// </summary>
/// <remarks>
///     Each language package derives a private type from this one that also
///     gives its own public <c>ITimer</c>. Thus, a timer is one object, and not a
///     handle around a second object.
/// </remarks>
/// <typeparam name="T">The type used to express a point in time.</typeparam>
internal abstract class ScheduledTimer<T>(in TimerScheduler<T> scheduler, in T time, in Action callback)
{
    /// <summary>The function that runs when the timer comes due.</summary>
    internal readonly Action Callback = callback;

    /// <summary>
    ///     The number of the timer in the sequence of construction. Timers at one
    ///     time fire in the sequence that the code set them.
    /// </summary>
    internal readonly long Seq = scheduler.NextSeq();

    /// <summary>The time at which the timer comes due.</summary>
    internal readonly T Time = time;

    private readonly TimerScheduler<T> scheduler = scheduler;

    /// <summary>
    ///     Removes the timer from its scheduler, so that it does not fire. This does
    ///     nothing when the timer fired, or when a call canceled it.
    /// </summary>
    internal void Cancel() => this.scheduler.Remove(this);
}
