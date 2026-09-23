using System;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Time;

/// <summary>
///     A source of time, and of streams which fire at times drawn from it.
/// </summary>
/// <typeparam name="T">
///     The type used to express a point in time, such as <see cref="DateTime" /> for
///     <see cref="SystemClockTimerSystem" /> or <c>double</c> for
///     <see cref="SecondsTimerSystem" />.
/// </typeparam>
/// <remarks>
///     <see cref="TimerSystem{T}" /> is the implementation in this library.
///     <see cref="SystemClockTimerSystem" /> and <see cref="SecondsTimerSystem" /> are two that this
///     library supplies, over the system clock and over the count of seconds. Alarms from
///     <see cref="At" /> get
///     to the graph in a transaction of their own, thus other code only has to listen to receive
///     them beyond listening.
/// </remarks>
[PublicAPI]
public interface ITimerSystem<T>
    where T : IComparable<T>
{
    /// <summary>
    ///     Gets a behavior giving the current clock time.
    /// </summary>
    Behavior<T> Time { get; }

    /// <summary>
    ///     A timer that fires at the specified time.
    /// </summary>
    /// <param name="t">The time to fire at.</param>
    /// <returns>A stream which fires at the specified time.</returns>
    Stream<T> At(Cell<Maybe<T>> t);
}
