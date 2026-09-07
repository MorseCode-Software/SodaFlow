using System.Collections.Generic;
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
/// </remarks>
public sealed class WallsScene : IScene
{
    internal WallsScene(ITimerSystem<double> timers)
    {
        double now = timers.Time.Sample();
        Ball[] balls = new Ball[Arrangement.Starts.Count];

        for (int i = 0; i < balls.Length; i++)
        {
            Arrangement.Start start = Arrangement.Starts[i];

            balls[i] =
                new Ball(
                    x: BouncingAxis.Create(
                        timers: timers,
                        initial: Arrangement.InitialX(start: start, now: now),
                        min: start.Radius,
                        max: Arrangement.Width - start.Radius,
                        restitution: Cell.Constant(1.0)),
                    y: BouncingAxis.Create(
                        timers: timers,
                        initial: Arrangement.InitialY(start: start, now: now),
                        min: start.Radius,
                        max: Arrangement.Height - start.Radius,
                        restitution: Cell.Constant(1.0)),
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
        + "in a list something has to walk.";

    /// <inheritdoc />
    public double Width => Arrangement.Width;

    /// <inheritdoc />
    public double Height => Arrangement.Height;

    /// <inheritdoc />
    public IReadOnlyList<Ball> Balls { get; }
}
