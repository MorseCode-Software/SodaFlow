using System.Collections.Generic;
using SodaFlow.Functional;
using SodaFlow.Time;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     Several balls in a box, under gravity, bouncing off the walls, the floor and the ceiling.
/// </summary>
/// <remarks>
///     <para>
///         Each ball is two independent axes: a horizontal one with no acceleration, bouncing
///         between the walls, and a vertical one under gravity, bouncing between the ceiling and
///         the floor. Neither axis knows about the other, and no ball knows about any other ball.
///     </para>
///     <para>
///         Adding a ball adds a graph. There is no list of bodies that something walks, no shared
///         array of positions, and no order in which anything has to be updated - which is the
///         difference between describing motion and stepping it.
///     </para>
///     <para>
///         Which is also why the damping is one cell shared by eight axes rather than a setting
///         each of them keeps a copy of. Every axis reads it at the moment it bounces, and none of
///         them has to be told when it changes.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class WallsScene : IScene
{
    /// <param name="restitution">
    ///     What a bounce multiplies the speed by, shared by every axis of every ball. See
    ///     <see cref="BounceViewModel" />, which owns the value the controls write.
    /// </param>
    /// <param name="restarts">Fires when this scene's tab becomes the selected one.</param>
    internal WallsScene(ITimerSystem<double> timers, Cell<double> restitution, Stream<Unit> restarts)
    {
        double now = timers.Time.Sample();

        // Damped below one, every ball ends up at rest, and this scene has no pointer to pick a
        // settled one up with - so coming back to it is what puts it back on its feet. The
        // restart is the input the axes already accept: the same one a throw arrives on in the
        // scene next door.
        Stream<double> restarted = restarts.Snapshot(b: timers.Time, f: static (_, time) => time);

        Ball[] balls = new Ball[Arrangement.Starts.Count];

        for (int i = 0; i < balls.Length; i++)
        {
            Arrangement.Start start = Arrangement.Starts[i];

            balls[i] =
                new Ball(
                    x: BouncingAxis.Position(
                        timers: timers,
                        flight: BouncingAxis.Flights(
                            timers: timers,
                            initial: Arrangement.InitialX(start: start, now: now),
                            min: start.Radius,
                            max: Arrangement.Width - start.Radius,
                            restarts: restarted.Map(time => Arrangement.InitialX(start: start, now: time)),
                            restitution: restitution)),
                    y: BouncingAxis.Position(
                        timers: timers,
                        flight: BouncingAxis.Flights(
                            timers: timers,
                            initial: Arrangement.InitialY(start: start, now: now),
                            min: start.Radius,
                            max: Arrangement.Height - start.Radius,
                            restarts: restarted.Map(time => Arrangement.InitialY(start: start, now: time)),
                            restitution: restitution)),
                    radius: start.Radius,
                    color: start.Color);
        }

        this.Balls = balls;
    }

    /// <inheritdoc />
    public string Name => "Several balls";

    /// <inheritdoc />
    public string Summary =>
        "Four balls, each a pair of independent axes. Adding one adds a graph rather than an entry "
        + "in a list something has to walk, and the damping is one cell all eight axes read.";

    /// <inheritdoc />
    public double Width => Arrangement.Width;

    /// <inheritdoc />
    public double Height => Arrangement.Height;

    /// <inheritdoc />
    public IReadOnlyList<Ball> Balls { get; }
}
