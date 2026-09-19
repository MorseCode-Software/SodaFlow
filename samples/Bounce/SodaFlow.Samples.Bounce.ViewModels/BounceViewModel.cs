using System;
using System.Collections.Generic;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Functional;
using SodaFlow.Time;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     The three scenes, the scene that shows now, and the clock of all of them.
/// </summary>
/// <remarks>
///     <para>
///         There is one timer system for the full sample. It is the source of
///         <see cref="ITimerSystem{T}.Time" />, and the position of each ball is a function of that
///         time. It holds a background thread that fires the alarms of the bounces. That thread
///         runs while the process runs. For that cause there is one timer system, and not one timer
///         system for each scene.
///     </para>
///     <para>
///         <see cref="SecondsTimerSystem" /> measures the time in seconds from its construction,
///         and that is necessary for a simulation. <see cref="SystemClockTimerSystem" /> is the
///         second timer system in the library, and it gives a <see cref="DateTime" /> for a
///         schedule on true calendar times.
///     </para>
///     <para>
///         See which parts of this are bindable and which parts are not. The balls are behaviors.
///         Code reads them with a sample, and they bind to nothing, because there is no sequence of
///         changes for a binding. The selected scene is a usual value that changes, thus it is a
///         usual cell and a usual bindable property, as in the other samples.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
public sealed class BounceViewModel : IBounceViewModel
{
    private const double SmallestDamping = 0.1;

    private const double LargestDamping = 1.1;

    /// <summary>The initial position of the slider. It has a large loss, thus damping shows a
    /// result on the screen.</summary>
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

        this.disposables = [selectedScene, selectedSummary, isDampingAvailable, dampingEnabled, damping];
    }

    /// <inheritdoc />
    /// <remarks>The smallest scene is first, thus the sequence of the scenes is the sequence of
    /// the idea.</remarks>
    public IReadOnlyList<IScene> Scenes { get; }

    /// <inheritdoc />
    /// <remarks>
    ///     There is no other method that also sets the selection. A second path is a second item
    ///     to keep in agreement with the first, and this library makes that unnecessary. The
    ///     bindable property is the path for a value, and a bindable action is the path for an
    ///     event.
    /// </remarks>
    public ITwoWayBindableValue<IScene> SelectedScene { get; }

    /// <inheritdoc />
    /// <remarks>
    ///     This shows that the selection is part of the graph and not a value that only the view
    ///     holds. This property is a function of the selection, thus no code must see a change of
    ///     tab and then update a label.
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
    ///     Only the bindables use this. The scenes do not use it, because their balls are
    ///     behaviors and no code subscribes to a behavior.
    /// </remarks>
    public void Dispose()
    {
        foreach (IDisposable disposable in this.disposables)
        {
            disposable.Dispose();
        }
    }

    /// <param name="handleException">
    ///     This receives each exception from a wait on a timer and from a timer that fires. A
    ///     timer callback does not run on a call stack of the caller, thus there is no other
    ///     destination for its exception.
    /// </param>
    public static IBounceViewModel Create(Action<Exception> handleException)
    {
        SecondsTimerSystem timers = new(handleException);

        // There is one transaction for the full construction, thus each scene starts at the same
        // moment and not at the time of its own construction.
        return Transaction.Run(() =>
        {
            // The checkbox and the slider are two values, and the physics uses one value: the
            // multiplier of a bounce. A state with no damping is the same as a multiplier of one,
            // thus the graph gives that value and the bounce does not read two values.
            CellSink<bool> dampingEnabled = Cell.CreateSink(false);
            CellSink<double> damping = Cell.CreateSink(InitialDamping);

            Cell<double> restitution =
                dampingEnabled.Lift(c2: damping, f: static (enabled, value) => enabled ? value : 1.0);

            // A scene starts again when its tab becomes the selected tab. That is a property of
            // the selection, and no view must remember to call it. This is necessary for a
            // damped scene, because below one each ball stops, and the selection of the tab starts
            // the scene again.
            //
            // The selection is not possible before the scenes, and the stream is necessary for
            // the scenes. Thus this code loops the stream: it declares the stream now and defines
            // it after the selection is available. The index for each scene is its position in
            // the array below.
            return Stream.Loop<int>()
                .WithCaptures(activatedLoop =>
                {
                    IScene simple = new SimpleScene(timers: timers, restarts: ActivatedAt(0));

                    IScene walls =
                        new WallsScene(timers: timers, restitution: restitution, restarts: ActivatedAt(1));

                    IScene grab =
                        new GrabScene(timers: timers, restitution: restitution, restarts: ActivatedAt(2));

                    // This scene damps at the walls, as the other two scenes do, but it does not
                    // damp at the impacts between balls. Those impacts stay elastic at each
                    // position of the slider, because the conservation is the purpose of the
                    // scene. See CollisionScene.Reflected.
                    IScene ricochets =
                        new CollisionScene(timers: timers, restitution: restitution, restarts: ActivatedAt(3));

                    IScene[] scenes = [simple, walls, grab, ricochets];

                    // This is the most simple two-way condition. The view is the only writer and
                    // the sink holds the correct value. This code gives no scheduler, thus the
                    // graph finds the ambient scheduler. The sample sets that scheduler at its
                    // start, thus the thread of this construction has no effect.
                    CellSink<IScene> selected = Cell.CreateSink(scenes[0]);

                    // This uses the updates and not the cell, thus the scene at the start does
                    // not start again at its construction.
                    return
                    (
                        Stream: selected.Updates().Map(scene => Array.IndexOf(array: scenes, value: scene)),
                        Captures: new BounceViewModel(
                            scenes: scenes,
                            selectedScene: selected.ToTwoWay(),
                            selectedSummary: selected.Map(static scene => scene.Summary).ToOneWay(),

                            // This code knows the scenes with damping, because this code gave the
                            // damping to them. Thus the answer is the set of those scenes, and no
                            // scene holds a flag.
                            isDampingAvailable:
                            selected
                                .Map(scene =>
                                    ReferenceEquals(objA: scene, objB: walls)
                                    || ReferenceEquals(objA: scene, objB: grab)
                                    || ReferenceEquals(objA: scene, objB: ricochets))
                                .ToOneWay(),
                            dampingEnabled: dampingEnabled.ToTwoWay(),
                            damping: damping.ToTwoWay())
                    );

                    Stream<Unit> ActivatedAt(int index) =>
                        activatedLoop.Filter(i => i == index).Map(static _ => Unit.Value);
                })
                .Captures;
        });
    }
}
