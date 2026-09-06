using System;
using System.Collections.Generic;
using SodaFlow.Time;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     The three scenes, and the clock they all share.
/// </summary>
/// <remarks>
///     <para>
///         One timer system for the whole application. It is the source of
///         <see cref="ITimerSystem{T}.Time" />, which every ball's position is a function of, and
///         it owns a background thread that fires the alarms the bounces are scheduled on. That
///         thread runs for as long as the process does, which is why there is one of these rather
///         than one per scene.
///     </para>
///     <para>
///         <see cref="SecondsTimerSystem" /> measures time in seconds since it was created, which
///         is what a simulation wants.  <see cref="SystemClockTimerSystem" /> is the other one
///         that ships, and answers in <see cref="DateTime" /> for scheduling against real calendar
///         times.
///     </para>
/// </remarks>
public sealed class BounceViewModel
{
    private BounceViewModel(IReadOnlyList<IScene> scenes) => this.Scenes = scenes;

    /// <summary>Smallest first, so that reading them in order is reading the idea in order.</summary>
    public IReadOnlyList<IScene> Scenes { get; }

    /// <param name="handleException">
    ///     Called with anything raised while waiting for or firing a timer. Timer callbacks run
    ///     outside any call stack of yours, so an exception in one has nowhere else to go.
    /// </param>
    public static BounceViewModel Create(Action<Exception> handleException)
    {
        SecondsTimerSystem timers = new(handleException);

        // One transaction for the whole construction, so that every scene starts from the same
        // instant rather than from whatever the clock said as each one was built.
        return Transaction.Run(
            () => new BounceViewModel(
                new IScene[]
                {
                    new SimpleScene(timers), new WallsScene(timers), new GrabScene(timers),
                }));
    }
}
