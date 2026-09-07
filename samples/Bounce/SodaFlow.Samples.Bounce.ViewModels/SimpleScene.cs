using System.Collections.Generic;
using SodaFlow.Functional;
using SodaFlow.Time;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     The smallest thing that makes the point: one ball, falling and bouncing, on one axis.
/// </summary>
/// <remarks>
///     Read <see cref="BouncingAxis" /> alongside this. The ball's height is an equation, the
///     moment it reaches the floor is solved rather than detected, and the bounce replaces the
///     equation with the next one. Everything the busier scenes do is this, more than once.
/// </remarks>
public sealed class SimpleScene : IScene
{
    /// <summary>Downward, because the y axis of a screen points down.</summary>
    private const double Gravity = 900.0;

    private const double BallRadius = 18.0;

    /// <param name="restarts">Fires when this scene's tab becomes the selected one.</param>
    internal SimpleScene(ITimerSystem<double> timers, Stream<Unit> restarts)
    {
        double now = timers.Time.Sample();

        this.Balls =
            new[]
            {
                new Ball(
                    // Nothing moves it sideways, and a constant is a perfectly good behavior.
                    x: Behavior.Constant(this.Width / 2.0),
                    y: BouncingAxis.Position(
                        timers: timers,
                        flight: BouncingAxis.Flights(
                            timers: timers,
                            initial: Initial(now),
                            min: BallRadius,
                            max: this.Height - BallRadius,
                            restarts: restarts.Snapshot(timers.Time, (_, time) => Initial(time)),

                            // Elastic, and not offered as a choice: this scene is here to be the
                            // smallest thing that makes the point.
                            restitution: Cell.Constant(1.0))),
                    radius: BallRadius,
                    color: "#E2574C"),
            };
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

    /// <summary>The flight the ball begins with, and begins again with.</summary>
    private static Flight Initial(double time) =>
        new(startTime: time, position: BallRadius, velocity: 0.0, acceleration: Gravity);
}
