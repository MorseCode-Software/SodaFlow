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
public sealed class WallsScene : IScene
{
    /// <param name="restitution">
    ///     What a bounce multiplies the speed by, shared by every axis of every ball. See
    ///     <see cref="BounceViewModel" />, which owns the value the controls write.
    /// </param>
    /// <param name="relaunches">
    ///     Puts the balls back where they started, going as fast as they started.
    /// </param>
    internal WallsScene(ITimerSystem<double> timers, Cell<double> restitution, Stream<Unit> relaunches)
    {
        double now = timers.Time.Sample();

        // Damped below one, every ball ends up at rest, and this scene has no pointer to pick a
        // settled one up with - so without a way back it would be a tab that goes permanently
        // still. A relaunch is the way back, and it is the restart stream the axes already accept
        // rather than anything new: the same input a throw uses in the scene next door.
        Stream<double> relaunched = relaunches.Snapshot(timers.Time, (_, time) => time);

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
                            restarts: relaunched.Map(time => Arrangement.InitialX(start: start, now: time)),
                            restitution: restitution)),
                    y: BouncingAxis.Position(
                        timers: timers,
                        flight: BouncingAxis.Flights(
                            timers: timers,
                            initial: Arrangement.InitialY(start: start, now: now),
                            min: start.Radius,
                            max: Arrangement.Height - start.Radius,
                            restarts: relaunched.Map(time => Arrangement.InitialY(start: start, now: time)),
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
        + "in a list something has to walk, and the damping is one cell all eight axes read. "
        + "Switching it puts them back where they started.";

    /// <inheritdoc />
    public double Width => Arrangement.Width;

    /// <inheritdoc />
    public double Height => Arrangement.Height;

    /// <inheritdoc />
    public IReadOnlyList<Ball> Balls { get; }
}
