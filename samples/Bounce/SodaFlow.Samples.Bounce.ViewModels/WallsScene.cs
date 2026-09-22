using System.Collections.Generic;
using SodaFlow.Functional;
using SodaFlow.Time;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     Some balls in a box, with gravity. They bounce from the walls, the floor, and the
///     ceiling.
/// </summary>
/// <remarks>
///     <para>
///         Each ball is two axes that operate independently. The horizontal axis has no
///         acceleration and bounces between the walls. The vertical axis has gravity and bounces
///         between the ceiling and the floor. One axis knows nothing about the other axis, and one
///         ball knows nothing about a second ball.
///     </para>
///     <para>
///         One more ball is one more graph. There is no list of bodies for other code to read, no
///         shared array of positions, and no sequence for the updates. That is the difference
///         between a description of the movement and a step of the movement.
///     </para>
///     <para>
///         For that cause the damping is one cell for eight axes, and not an adjustment with a
///         copy in each axis. Each axis reads the cell at the moment of its bounce, and no code
///         tells an axis about a change.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class WallsScene : IScene
{
    /// <param name="timers">The clock for the position of each ball.</param>
    /// <param name="restitution">
    ///     The multiplier for the speed at a bounce. Each axis of each ball reads it. See
    ///     <see cref="BounceViewModel" />, which holds the value that the controls write.
    /// </param>
    /// <param name="restarts">Fires when the tab of this scene becomes the selected tab.</param>
    internal WallsScene(ITimerSystem<double> timers, Cell<double> restitution, Stream<Unit> restarts)
    {
        double now = timers.Time.Sample();

        // Below one each ball stops, and this scene has no pointer for a user to hold a ball
        // that stopped. Thus, the selection of the tab starts the scene again. The restart is an
        // input that the axes accept, and it is the input for a throw in the adjacent scene.
        Stream<double> restarted = restarts.Snapshot(b: timers.Time, f: static (_, time) => time);

        Ball[] balls = new Ball[Arrangement.Starts.Count];

        for (int i = 0; i < balls.Length; i++)
        {
            Arrangement.Start start = Arrangement.Starts[i];

            balls[i] =
                new Ball(
                    x: BouncingAxis.Create(
                        timers: timers,
                        initial: start.InitialX(now: now),
                        min: start.Radius,
                        max: Arrangement.Width - start.Radius,
                        restarts: restarted.Map(time => start.InitialX(now: time)),
                        restitution: restitution),
                    y: BouncingAxis.Create(
                        timers: timers,
                        initial: start.InitialY(now: now),
                        min: start.Radius,
                        max: Arrangement.Height - start.Radius,
                        restarts: restarted.Map(time => start.InitialY(now: time)),
                        restitution: restitution),
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
