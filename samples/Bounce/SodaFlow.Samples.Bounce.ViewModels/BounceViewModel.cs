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
public sealed class BounceViewModel : IDisposable
{
    private readonly IReadOnlyList<IDisposable> disposables;

    private readonly CellSink<IScene> selected;

    private BounceViewModel(
        IReadOnlyList<IScene> scenes,
        CellSink<IScene> selected,
        ITwoWayBindableValue<IScene> selectedScene,
        IOneWayBindableValue<string> selectedSummary)
    {
        this.Scenes = scenes;
        this.selected = selected;
        this.SelectedScene = selectedScene;
        this.SelectedSummary = selectedSummary;

        this.disposables = new IDisposable[] { selectedScene, selectedSummary };
    }

    /// <summary>Smallest first, so that reading them in order is reading the idea in order.</summary>
    public IReadOnlyList<IScene> Scenes { get; }

    /// <summary>
    ///     Which scene is showing.
    /// </summary>
    /// <remarks>
    ///     Two-way, so it is a value rather than a fact about a control. The view writes it when a
    ///     tab is clicked, and anything here can write it to change the tab - see
    ///     <see cref="Show" /> - or read it to derive something, as
    ///     <see cref="SelectedSummary" /> does. Bind a tab control's selected item to
    ///     <c>SelectedScene.Value</c>.
    /// </remarks>
    public ITwoWayBindableValue<IScene> SelectedScene { get; }

    /// <summary>
    ///     The selected scene's description.
    /// </summary>
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
    public static BounceViewModel Create(Action<Exception> handleException)
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
                    selected: selected,
                    selectedScene: selected.ToTwoWay(),
                    selectedSummary: selected.Map(scene => scene.Summary).ToOneWay());
            });
    }

    /// <summary>Selects the given scene, as though its tab had been clicked.</summary>
    /// <remarks>
    ///     Sending into the sink rather than assigning <see cref="SelectedScene" />'s value, so
    ///     that this can be called from anywhere. The bindable property's setter is for the
    ///     binding engine and has to be used on the binding thread; the sink has no such rule.
    /// </remarks>
    public void Show(IScene scene) => this.selected.Send(scene);

    /// <summary>
    ///     Releases the bindable properties.
    /// </summary>
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
