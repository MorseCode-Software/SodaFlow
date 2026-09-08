using System.Collections.Generic;
using SodaFlow.Functional;
using SodaFlow.Time;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     The same box, with the balls available to be picked up and thrown.
/// </summary>
/// <remarks>
///     <para>
///         This is what the switching is for. A ball's position is either the pointer's or the one
///         its flight says, and <c>SwitchB</c> is what makes a single behavior out of the two - so
///         the position stays continuous across the moment of grabbing, and nothing has to copy a
///         value out of one representation and into another.
///     </para>
///     <para>
///         The throw is a restart: on release the ball resumes from where the pointer let go, at
///         the speed the pointer was moving. Both come from the pointer's trail, which is an
///         <c>Accum</c> over its movements rather than anything remembered on the side.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class GrabScene : IInteractiveScene
{
    private readonly CellSink<Maybe<int>> held;

    private readonly CellSink<Point> pointer;

    private readonly StreamSink<Unit> released;

    /// <param name="restitution">
    ///     What a bounce multiplies the speed by, the same cell the several-balls scene reads. See
    ///     <see cref="BounceViewModel" />, which owns the value the controls write.
    /// </param>
    /// <param name="restarts">Fires when this scene's tab becomes the selected one.</param>
    internal GrabScene(ITimerSystem<double> timers, Cell<double> restitution, Stream<Unit> restarts)
    {
        double now = timers.Time.Sample();

        Stream<double> restarted = restarts.Snapshot(b: timers.Time, f: static (_, time) => time);

        this.held = Cell.CreateSink(Maybe<int>.None);
        this.pointer = Cell.CreateSink(new Point(x: 0.0, y: 0.0));
        this.released = Stream.CreateSink<Unit>();

        // Where the pointer has been, timestamped as it moves. Accum keeps the last two, which is
        // all a velocity needs.
        Cell<PointerTrail> trail =
            this.pointer
                .Updates()
                .Snapshot(b: timers.Time, f: static (p, time) => new Point(x: p.X, y: p.Y, time: time))
                .Accum(
                    initialState: PointerTrail.Empty,
                    f: static (p, previous) => previous.Add(time: p.Time, x: p.X, y: p.Y));

        // What was let go of, and when. The snapshot of held reads the value it had when the
        // transaction opened, which is what lets Release clear it in the same transaction that
        // reports it.
        Stream<Throw> thrown =
            this.released
                .Snapshot(c1: this.held, c2: trail, f: static (_, index, t) => new Grabbed(index: index, trail: t))
                .Snapshot(
                    b: timers.Time,
                    f: static (grabbed, time) =>
                        grabbed.Index.Match(
                            onSome: index => Maybe.Some(new Throw(index: index, trail: grabbed.Trail, time: time)),
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

            // While a ball is held its free flight goes on running, unseen. The throw replaces it,
            // so what it did in the meantime never shows.
            Behavior<double> freeX =
                BouncingAxis.Position(
                    timers: timers,
                    flight: BouncingAxis.Flights(
                        timers: timers,
                        initial: Arrangement.InitialX(start: start, now: now),
                        min: minX,
                        max: maxX,
                        // A throw and a fresh start are the same kind of thing - a flight imposed
                        // from outside - so they arrive on one stream rather than the axis being
                        // told about two.
                        restarts: mine
                            .Map(t =>
                                new Flight(
                                    startTime: t.Time,
                                    position: Clamp(value: t.Trail.X, min: minX, max: maxX),
                                    velocity: t.Trail.VelocityX,
                                    acceleration: 0.0))
                            .OrElse(restarted.Map(time => Arrangement.InitialX(start: start, now: time))),
                        restitution: restitution));

            Behavior<double> freeY =
                BouncingAxis.Position(
                    timers: timers,
                    flight: BouncingAxis.Flights(
                        timers: timers,
                        initial: Arrangement.InitialY(start: start, now: now),
                        min: minY,
                        max: maxY,
                        restarts: mine
                            .Map(t =>
                                new Flight(
                                    startTime: t.Time,
                                    position: Clamp(value: t.Trail.Y, min: minY, max: maxY),
                                    velocity: t.Trail.VelocityY,
                                    acceleration: Arrangement.Gravity))
                            .OrElse(restarted.Map(time => Arrangement.InitialY(start: start, now: time))),
                        restitution: restitution));

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
        // Sampled before the transaction rather than inside it: which ball is under the pointer is
        // a question about where things are now, and the answer to it is what gets sent.
        Maybe<int> index = this.BallAt(x: x, y: y);

        Transaction.RunVoid(() =>
        {
            this.pointer.Send(new Point(x: x, y: y));
            this.held.Send(index);
        });
    }

    /// <inheritdoc />
    public void MoveTo(double x, double y) => this.pointer.Send(new Point(x: x, y: y));

    /// <inheritdoc />
    public void Release() =>
        // Both in one transaction. The throw's snapshot of held sees the value from before this
        // transaction, so clearing it here does not race the reading of it.
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

    /// <summary>The ball under the given point, preferring the one whose center is nearest.</summary>
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

    private readonly struct Point
    {
        public Point(double x, double y, double time = 0.0)
        {
            this.X = x;
            this.Y = y;
            this.Time = time;
        }

        public double X { get; }

        public double Y { get; }

        public double Time { get; }
    }

    private readonly struct Grabbed
    {
        public Grabbed(Maybe<int> index, PointerTrail trail)
        {
            this.Index = index;
            this.Trail = trail;
        }

        public Maybe<int> Index { get; }

        public PointerTrail Trail { get; }
    }

    private readonly struct Throw
    {
        public Throw(int index, PointerTrail trail, double time)
        {
            this.Index = index;
            this.Trail = trail;
            this.Time = time;
        }

        public int Index { get; }

        public PointerTrail Trail { get; }

        public double Time { get; }
    }
}
