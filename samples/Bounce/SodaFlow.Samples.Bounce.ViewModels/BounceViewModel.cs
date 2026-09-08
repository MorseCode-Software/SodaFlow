using System;
using System.Collections.Generic;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Functional;
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
// ReSharper disable once InheritdocConsiderUsage
public sealed class BounceViewModel : IBounceViewModel
{
    private const double SmallestDamping = 0.1;

    private const double LargestDamping = 1.1;

    /// <summary>Where the slider starts: visibly lossy, so that turning damping on shows something.</summary>
    private const double InitialDamping = 0.7;

    private readonly IReadOnlyList<IDisposable> disposables;

    private BounceViewModel(
        IReadOnlyList<IScene> scenes,
        ITwoWayBindableValue<IScene> selectedScene,
        IOneWayBindableValue<string> selectedSummary,
        IOneWayBindableValue<bool> isDampingAvailable,
        ITwoWayBindableValue<bool> dampingEnabled,
        ITwoWayBindableValue<double> damping)
    {
        this.Scenes = scenes;
        this.SelectedScene = selectedScene;
        this.SelectedSummary = selectedSummary;
        this.IsDampingAvailable = isDampingAvailable;
        this.DampingEnabled = dampingEnabled;
        this.Damping = damping;

        this.disposables =
            new IDisposable[] { selectedScene, selectedSummary, isDampingAvailable, dampingEnabled, damping };
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

    /// <inheritdoc />
    public IOneWayBindableValue<bool> IsDampingAvailable { get; }

    /// <inheritdoc />
    public ITwoWayBindableValue<bool> DampingEnabled { get; }

    /// <inheritdoc />
    public ITwoWayBindableValue<double> Damping { get; }

    /// <inheritdoc />
    public double MinimumDamping => SmallestDamping;

    /// <inheritdoc />
    public double MaximumDamping => LargestDamping;

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

    /// <param name="handleException">
    ///     Called with anything raised while waiting for or firing a timer. Timer callbacks run
    ///     outside any call stack of yours, so an exception in one has nowhere else to go.
    /// </param>
    public static IBounceViewModel Create(Action<Exception> handleException)
    {
        SecondsTimerSystem timers = new(handleException);

        // One transaction for the whole construction, so that every scene starts from the same
        // instant rather than from whatever the clock said as each one was built.
        return Transaction.Run(() =>
        {
            // The checkbox and the slider are two values, and what the physics wants is one:
            // the multiplier a bounce applies. Turning damping off is the same as a multiplier
            // of one, so that is what the graph says, rather than the bounce asking twice.
            CellSink<bool> dampingEnabled = Cell.CreateSink(false);
            CellSink<double> damping = Cell.CreateSink(InitialDamping);

            Cell<double> restitution =
                dampingEnabled.Lift(c2: damping, f: static (enabled, value) => enabled ? value : 1.0);

            // A scene starts again when its tab becomes the selected one. That is a fact about
            // the selection rather than something a view has to remember to call, and it is
            // what a damped scene needs: below one every ball ends up at rest, and coming back
            // to the tab is what puts it on its feet again.
            //
            // The selection cannot exist until the scenes do, and the scenes want the stream,
            // so the stream is looped - declared now, defined once the selection is there. The
            // index passed to each scene is its position in the array just below.
            StreamLoop<int> activated = Stream.CreateLoop<int>();

            IScene simple = new SimpleScene(timers: timers, restarts: ActivatedAt(0));

            IScene walls =
                new WallsScene(timers: timers, restitution: restitution, restarts: ActivatedAt(1));

            IScene grab =
                new GrabScene(timers: timers, restitution: restitution, restarts: ActivatedAt(2));

            // Damped at the walls like the other two, but not at the impacts between balls -
            // those stay elastic whatever the slider says, because what they conserve is the point
            // of the scene. See CollisionScene.Reflected.
            IScene ricochets =
                new CollisionScene(timers: timers, restitution: restitution, restarts: ActivatedAt(3));

            IScene[] scenes = { simple, walls, grab, ricochets };

            // The simplest two-way case: the view is the only writer and the sink is the
            // authoritative value. No scheduler is passed, so the ambient one is resolved -
            // which the application pins at startup, so this does not care what thread it
            // is built on.
            CellSink<IScene> selected = Cell.CreateSink(scenes[0]);

            // Updates and not the cell itself, so the scene showing at startup is not restarted
            // the moment it is built.
            activated.Loop(selected.Updates().Map(scene => Array.IndexOf(array: scenes, value: scene)));

            return new BounceViewModel(
                scenes: scenes,
                selectedScene: selected.ToTwoWay(),
                selectedSummary: selected.Map(static scene => scene.Summary).ToOneWay(),

                // Which scenes damping applies to is known here because this is where it was
                // handed over, so the answer is which scenes those were rather than a flag
                // every scene has to carry.
                isDampingAvailable:
                selected
                    .Map(
                        scene =>
                            ReferenceEquals(objA: scene, objB: walls)
                            || ReferenceEquals(objA: scene, objB: grab)
                            || ReferenceEquals(objA: scene, objB: ricochets))
                    .ToOneWay(),
                dampingEnabled: dampingEnabled.ToTwoWay(),
                damping: damping.ToTwoWay());

            Stream<Unit> ActivatedAt(int index) => activated.Filter(i => i == index).Map(static _ => Unit.Value);
        });
    }
}
