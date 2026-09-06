using System;
using System.Collections.Generic;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Time;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     The three scenes, which one is showing, and the clock they all share.
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
///         is what a simulation wants. <see cref="SystemClockTimerSystem" /> is the other one that
///         ships, and answers in <see cref="DateTime" /> for scheduling against real calendar
///         times.
///     </para>
///     <para>
///         Note which parts of this are bindable and which are not. The balls are behaviors, read
///         by sampling, and bind to nothing - there is no sequence of changes for a binding to
///         follow. Which scene is selected is an ordinary changing value, so it is an ordinary cell
///         exposed as an ordinary bindable property, exactly as in the other samples.
///     </para>
/// </remarks>
public sealed class BounceViewModel : IBounceViewModel
{
    private readonly IReadOnlyList<IDisposable> disposables;

    private BounceViewModel(
        IReadOnlyList<IScene> scenes,
        ITwoWayBindableValue<IScene> selectedScene,
        IOneWayBindableValue<string> selectedSummary)
    {
        this.Scenes = scenes;
        this.SelectedScene = selectedScene;
        this.SelectedSummary = selectedSummary;

        this.disposables = new IDisposable[] { selectedScene, selectedSummary };
    }

    /// <inheritdoc />
    /// <remarks>Smallest first, so that reading them in order is reading the idea in order.</remarks>
    public IReadOnlyList<IScene> Scenes { get; }

    /// <inheritdoc />
    /// <remarks>
    ///     There is deliberately no method beside this that also sets the selection. A second way in
    ///     is a second thing to keep in step with the first, and it is the habit this library exists
    ///     to make unnecessary: the bindable property is the way in, as a bindable action is for
    ///     something that happens rather than something that is.
    /// </remarks>
    public ITwoWayBindableValue<IScene> SelectedScene { get; }

    /// <inheritdoc />
    /// <remarks>
    ///     Here to make the point that the selection is part of the graph rather than something the
    ///     view keeps to itself: this is a function of it, and nothing has to notice a tab change
    ///     and go and update a label.
    /// </remarks>
    public IOneWayBindableValue<string> SelectedSummary { get; }

    /// <param name="handleException">
    ///     Called with anything raised while waiting for or firing a timer. Timer callbacks run
    ///     outside any call stack of yours, so an exception in one has nowhere else to go.
    /// </param>
    public static IBounceViewModel Create(Action<Exception> handleException)
    {
        SecondsTimerSystem timers = new(handleException);

        // One transaction for the whole construction, so that every scene starts from the same
        // instant rather than from whatever the clock said as each one was built.
        return Transaction.Run(
            () =>
            {
                IScene[] scenes = { new SimpleScene(timers), new WallsScene(timers), new GrabScene(timers) };

                // The simplest two-way case: the view is the only writer and the sink is the
                // authoritative value. No scheduler is passed, so one is captured from the
                // synchronization context in force here - build this on the UI thread.
                CellSink<IScene> selected = Cell.CreateSink(scenes[0]);

                return new BounceViewModel(
                    scenes: scenes,
                    selectedScene: selected.ToTwoWay(),
                    selectedSummary: selected.Map(scene => scene.Summary).ToOneWay());
            });
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Only the bindables need this. The scenes do not: their balls are behaviors, and nothing
    ///     subscribes to a behavior.
    /// </remarks>
    public void Dispose()
    {
        foreach (IDisposable disposable in this.disposables)
        {
            disposable.Dispose();
        }
    }
}
