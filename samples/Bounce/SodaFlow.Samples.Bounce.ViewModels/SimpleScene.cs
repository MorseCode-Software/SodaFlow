using System.Collections.Generic;
using SodaFlow.Functional;
using SodaFlow.Time;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     The smallest scene that shows the idea: one ball that falls and bounces, on one axis.
/// </summary>
/// <remarks>
///     Read <see cref="BouncingAxis" /> with this. The height of the ball is an equation, the
///     code calculates the moment when the ball touches the floor and does not find it after the
///     event, and the bounce replaces the equation. The larger scenes do this more than one
///     time.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class SimpleScene : IScene
{
    /// <summary>This is positive, because the y-axis of a screen points down.</summary>
    private const double Gravity = 900.0;

    private const double BallRadius = 18.0;

    /// <param name="timers">The clock for the position of each ball.</param>
    /// <param name="restarts">Fires when the tab of this scene becomes the selected tab.</param>
    internal SimpleScene(ITimerSystem<double> timers, Stream<Unit> restarts)
    {
        double now = timers.Time.Sample();

        this.Balls =
        [
            new Ball(
                // No force moves the ball horizontally, and a constant is a correct behavior.
                x: Behavior.Constant(this.Width / 2.0),
                y: BouncingAxis.Create(
                    timers: timers,
                    initial: Initial(now),
                    min: BallRadius,
                    max: this.Height - BallRadius,
                    restarts: restarts.Snapshot(b: timers.Time, f: static (_, time) => Initial(time)),

                    // This is elastic and there is no control for it, because this scene is
                    // the smallest scene that shows the idea.
                    restitution: Cell.Constant(1.0)),
                radius: BallRadius,
                color: "#E2574C")
        ];
    }

    /// <inheritdoc />
    public string Name => "One ball";

    /// <inheritdoc />
    public string Summary =>
        "A single ball on one axis. Its height is a function of time, so the floor is reached at "
        + "a moment solved from the equation rather than noticed by a frame.";

    /// <inheritdoc />
    public double Width => 320.0;

    /// <inheritdoc />
    public double Height => 320.0;

    /// <inheritdoc />
    public IReadOnlyList<Ball> Balls { get; }

    /// <summary>The initial flight of the ball, and its flight at each restart.</summary>
    private static Flight Initial(double time) =>
        new(StartTime: time, Position: BallRadius, Velocity: 0.0, Acceleration: Gravity);
}
