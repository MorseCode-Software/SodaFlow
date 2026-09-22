using System.Collections.Generic;
using SodaFlow.Functional;
using SodaFlow.Time;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     The same box. A user can hold a ball and then throw it.
/// </summary>
/// <remarks>
///     <para>
///         This is the purpose of the switch. The position of a ball is the position of the
///         pointer or the position from its flight, and <c>SwitchB</c> makes one behavior from the
///         two. Thus, the position stays continuous at the moment when a user holds the ball, and
///         no code copies a value between the two.
///     </para>
///     <para>
///         The throw is a restart. At the release the ball continues from the position of the
///         pointer, at the speed of the pointer. The trail of the pointer gives the two values.
///         That trail is an <c>Accum</c> across the movements of the pointer, and not a value in
///         other code.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class GrabScene : IInteractiveScene
{
    private readonly CellSink<Maybe<int>> held;

    private readonly CellSink<Point> pointer;

    private readonly StreamSink<Unit> released;

    /// <param name="timers">The clock for the position of each ball.</param>
    /// <param name="restitution">
    ///     The multiplier for the speed at a bounce. It is the cell that the scene with more
    ///     balls reads. See <see cref="BounceViewModel" />, which holds the value that the controls
    ///     write.
    /// </param>
    /// <param name="restarts">Fires when the tab of this scene becomes the selected tab.</param>
    internal GrabScene(ITimerSystem<double> timers, Cell<double> restitution, Stream<Unit> restarts)
    {
        double now = timers.Time.Sample();

        Stream<double> restarted = restarts.Snapshot(b: timers.Time, f: static (_, time) => time);

        this.held = Cell.CreateSink(Maybe<int>.None);
        this.pointer = Cell.CreateSink(new Point(X: 0.0, Y: 0.0));
        this.released = Stream.CreateSink<Unit>();

        // The positions of the pointer, with a time at each move. Accum keeps the last two
        // positions, and a velocity uses only those two.
        Cell<PointerTrail> trail =
            this.pointer
                .Updates()
                .Snapshot(b: timers.Time, f: static (p, time) => p with { Time = time })
                .Accum(
                    initialState: PointerTrail.Empty,
                    f: static (p, previous) => previous.Add(time: p.Time, x: p.X, y: p.Y));

        // The ball at the release, and the time of the release. The snapshot of held reads the
        // value from the start of the transaction. Thus, Release can clear that value in the
        // transaction that reports it.
        Stream<Throw> thrown =
            this.released
                .Snapshot(c1: this.held, c2: trail, f: static (_, index, t) => new Grabbed(Index: index, Trail: t))
                .Snapshot(
                    b: timers.Time,
                    f: static (grabbed, time) =>
                        grabbed.Index.Match(
                            onSome: index => Maybe.Some(new Throw(Index: index, Trail: grabbed.Trail, Time: time)),
                            onNone: static () => Maybe<Throw>.None))
                .FilterSome();

        Ball[] balls = new Ball[Arrangement.Starts.Count];

        for (int i = 0; i < balls.Length; i++)
        {
            int index = i;
            Arrangement.Start start = Arrangement.Starts[index];
            double minX = start.Radius;
            double maxX = Arrangement.Width - start.Radius;
            double minY = start.Radius;
            double maxY = Arrangement.Height - start.Radius;

            Stream<Throw> mine = thrown.Filter(t => t.Index == index);

            // While a user holds a ball, its free flight continues and no user sees it. The
            // throw replaces that flight, thus the screen does not show the flight in that
            // interval.
            Behavior<double> freeX =
                BouncingAxis.Create(
                    timers: timers,
                    initial: start.InitialX(now: now),
                    min: minX,
                    max: maxX,
                    // A throw and a new start are the same type of event, which is a flight from
                    // other code. Thus, the two come on one stream, and no code tells the axis
                    // about two streams.
                    restarts: mine
                        .Map(t =>
                            new Flight(
                                StartTime: t.Time,
                                Position: Clamp(value: t.Trail.X, min: minX, max: maxX),
                                Velocity: t.Trail.VelocityX,
                                Acceleration: 0.0))
                        .OrElse(restarted.Map(time => start.InitialX(now: time))),
                    restitution: restitution);

            Behavior<double> freeY =
                BouncingAxis.Create(
                    timers: timers,
                    initial: start.InitialY(now: now),
                    min: minY,
                    max: maxY,
                    restarts: mine
                        .Map(t =>
                            new Flight(
                                StartTime: t.Time,
                                Position: Clamp(value: t.Trail.Y, min: minY, max: maxY),
                                Velocity: t.Trail.VelocityY,
                                Acceleration: Arrangement.Gravity))
                        .OrElse(restarted.Map(time => start.InitialY(now: time))),
                    restitution: restitution);

            Cell<bool> isHeld =
                this.held.Map(m => m.Match(onSome: h => h == index, onNone: static () => false));

            Behavior<double> pointerX =
                this.pointer.Map(p => Clamp(value: p.X, min: minX, max: maxX)).AsBehavior();

            Behavior<double> pointerY =
                this.pointer.Map(p => Clamp(value: p.Y, min: minY, max: maxY)).AsBehavior();

            balls[index] =
                new Ball(
                    x: isHeld.Map(h => h ? pointerX : freeX).SwitchB(),
                    y: isHeld.Map(h => h ? pointerY : freeY).SwitchB(),
                    radius: start.Radius,
                    color: start.Color);
        }

        this.Balls = balls;
    }

    /// <inheritdoc />
    public string Name => "Grab and throw";

    /// <inheritdoc />
    public string Summary =>
        "Drag a ball and let go. While held, its position is the pointer's; otherwise it is its "
        + "flight's, and SwitchB is what makes those one behavior rather than two states to "
        + "reconcile. Damping sets what a bounce does to a ball's speed: below one it settles, "
        + "above one it climbs.";

    /// <inheritdoc />
    public double Width => Arrangement.Width;

    /// <inheritdoc />
    public double Height => Arrangement.Height;

    /// <inheritdoc />
    public IReadOnlyList<Ball> Balls { get; }

    /// <inheritdoc />
    public void Grab(double x, double y)
    {
        // This code samples before the transaction and not in it. The ball below the pointer is a
        // question about the current positions, and this code sends the answer.
        Maybe<int> index = this.BallAt(x: x, y: y);

        Transaction.RunVoid(() =>
        {
            this.pointer.Send(new Point(X: x, Y: y));
            this.held.Send(index);
        });
    }

    /// <inheritdoc />
    public void MoveTo(double x, double y) => this.pointer.Send(new Point(X: x, Y: y));

    /// <inheritdoc />
    public void Release() =>
        // The two operations are in one transaction. The snapshot of held in the throw reads the
        // value from before this transaction, thus a clear here cannot race that read.
        Transaction.RunVoid(() =>
        {
            this.released.Send(Unit.Value);
            this.held.Send(Maybe<int>.None);
        });

    private static double Clamp(double value, double min, double max) =>
        value < min
            ? min
            : value > max
                ? max
                : value;

    /// <summary>The ball below the given point. The ball with the nearest center has
    /// priority.</summary>
    private Maybe<int> BallAt(double x, double y)
    {
        Maybe<int> found = Maybe<int>.None;
        double best = double.MaxValue;

        for (int i = 0; i < this.Balls.Count; i++)
        {
            Ball ball = this.Balls[i];
            (double ballX, double ballY) = ball.SampleAt();
            double dx = ballX - x;
            double dy = ballY - y;
            double distance = dx * dx + dy * dy;

            if (distance <= ball.Radius * ball.Radius && distance < best)
            {
                best = distance;
                found = Maybe.Some(i);
            }
        }

        return found;
    }

    private readonly record struct Point(double X, double Y, double Time = 0.0);

    private readonly record struct Grabbed(Maybe<int> Index, PointerTrail Trail);

    private readonly record struct Throw(int Index, PointerTrail Trail, double Time);
}
