using System;
using System.Collections.Generic;
using System.Linq;
using SodaFlow.Functional;
using SodaFlow.Time;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     The same four balls, now able to hit each other.
/// </summary>
/// <remarks>
///     <para>
///         The other scenes get their shape from independence: a ball is two axes that know
///         nothing of each other, and no ball knows of any other. A collision is exactly the thing
///         that breaks both. It couples the two axes of two balls at one instant, so the flights
///         cannot be solved one at a time anymore.
///     </para>
///     <para>
///         What does not change is that the collision is solved rather than detected. Nothing here
///         steps time forward looking for overlap. The moment two balls touch is a root of a
///         quadratic, computed in advance and scheduled with <c>At</c>, exactly as a wall bounce
///         is - so a collision cannot be missed by a slow frame, and two balls never pass through
///         each other because the frame rate dropped.
///     </para>
///     <para>
///         That quadratic exists because of a fact worth noticing: every ball is under the same
///         gravity, so between any two of them the relative acceleration is zero. Their separation
///         is therefore a straight line in time however they are each curving, and "when are these
///         two exactly touching" is a quadratic rather than a quartic. Give one ball a different
///         acceleration and this scene needs a different solver.
///     </para>
///     <para>
///         So the state is one cell holding every ball at once, rather than a cell per axis. It
///         advances event by event: solve for the earliest thing that happens next - any ball
///         meeting any wall, or any two balls meeting each other - jump to it, apply it, and solve
///         again from there. Between events every ball is a plain equation, which is what the
///         views sample.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class CollisionScene : IScene
{
    /// <summary>
    ///     Two events closer together than this are treated as simultaneous and applied together.
    /// </summary>
    /// <remarks>
    ///     Three balls meeting at one instant is rare but not impossible, and applying only the
    ///     first of them would leave the others to fire again immediately. Resolving everything
    ///     that lands at the same moment keeps the step honest.
    /// </remarks>
    private const double SimultaneousWithin = 1e-9;

    /// <summary>Matches the interval <see cref="BouncingAxis" /> uses, for the same reason.</summary>
    private const double MinimumInterval = 1e-6;

    /// <param name="timers">The clock every ball's position is a function of.</param>
    /// <param name="restarts">Fires when this scene's tab becomes the selected one.</param>
    internal CollisionScene(ITimerSystem<double> timers, Stream<Unit> restarts)
    {
        double now = timers.Time.Sample();

        Cell<IReadOnlyList<Body>> world =
            Cell.Loop<IReadOnlyList<Body>>()
                .WithoutCaptures(
                    bodies =>
                    {
                        // Armed for the next thing that happens to anything, and disarmed when
                        // nothing will. Rescheduling is not a step anything performs: the target
                        // is a function of the world, so a new world is a new target.
                        Cell<Maybe<double>> nextEvent = bodies.Map(NextEventTime);

                        Stream<IReadOnlyList<Body>> stepped =
                            timers
                                .At(nextEvent)
                                .Snapshot(c: bodies, f: static (time, w) => Step(world: w, time: time));

                        return restarts
                            .Snapshot(b: timers.Time, f: static (_, time) => Initial(time))
                            .OrElse(stepped)
                            .Hold(Initial(now));
                    });

        Ball[] balls = new Ball[Arrangement.Starts.Count];

        for (int i = 0; i < balls.Length; i++)
        {
            balls[i] =
                new Ball(
                    x: Position(timers: timers, world: world, index: i, horizontal: true),
                    y: Position(timers: timers, world: world, index: i, horizontal: false),
                    radius: Arrangement.Starts[i].Radius,
                    color: Arrangement.Starts[i].Color);
        }

        this.Balls = balls;
    }

    /// <inheritdoc />
    public string Name => "Ricochets";

    /// <inheritdoc />
    public string Summary =>
        "The same four balls, now hitting each other as well as the walls. Every impact is solved "
        + "for ahead of time rather than noticed afterwards, and it is elastic: momentum and "
        + "energy both survive it, and the heavier ball gives way less.";

    /// <inheritdoc />
    public double Width => Arrangement.Width;

    /// <inheritdoc />
    public double Height => Arrangement.Height;

    /// <inheritdoc />
    public IReadOnlyList<Ball> Balls { get; }

    /// <summary>The world every ball starts from, and returns to when the tab is reselected.</summary>
    private static IReadOnlyList<Body> Initial(double time)
    {
        Body[] bodies = new Body[Arrangement.Starts.Count];

        for (int i = 0; i < bodies.Length; i++)
        {
            Arrangement.Start start = Arrangement.Starts[i];

            // The same gravity as every other scene, which is what keeps the relative
            // acceleration between any two balls at zero and the contact solve a quadratic. What
            // it costs is visible: see the note on the clock above ContactTime.
            bodies[i] =
                new Body(
                    x: Arrangement.InitialX(start: start, now: time),
                    y: Arrangement.InitialY(start: start, now: time),
                    radius: start.Radius);
        }

        return bodies;
    }

    /// <summary>One ball's position along one axis, following whichever equation is current.</summary>
    private static Behavior<double> Position(
        ITimerSystem<double> timers,
        Cell<IReadOnlyList<Body>> world,
        int index,
        bool horizontal) =>
        world
            .Map(
                w =>
                {
                    Flight flight = horizontal ? w[index].X : w[index].Y;

                    return timers.Time.Map(flight.PositionAt);
                })
            .SwitchB();

    /// <summary>When the next thing happens to anything, or none if nothing ever does.</summary>
    private static Maybe<double> NextEventTime(IReadOnlyList<Body> world)
    {
        double[] times = Events(world).Select(static e => e.Time).ToArray();

        return times.Length > 0 ? Maybe.Some(times.Min()) : Maybe.None;
    }

    /// <summary>
    ///     The world as it is the instant after <paramref name="time" />.
    /// </summary>
    /// <remarks>
    ///     Every ball is rebased onto that moment first, so that afterward they all share one
    ///     start time. That is what keeps the pairwise solve a quadratic: it can take the
    ///     separation and the relative velocity as of a single instant rather than reconciling
    ///     four equations that each began somewhere else.
    /// </remarks>
    private static IReadOnlyList<Body> Step(IReadOnlyList<Body> world, double time)
    {
        Body[] next = world.Select(body => body.RebasedTo(time)).ToArray();

        foreach (Event e in Events(world).Where(e => Math.Abs(e.Time - time) <= SimultaneousWithin))
        {
            if (e.Other < 0)
            {
                double limit = e.Horizontal ? Arrangement.Width : Arrangement.Height;

                next[e.Index] =
                    next[e.Index].Reflected(
                        horizontal: e.Horizontal,
                        min: next[e.Index].Radius,
                        max: limit - next[e.Index].Radius);
            }
            else
            {
                Collide(bodies: next, first: e.Index, second: e.Other);
            }
        }

        return next;
    }

    /// <summary>Everything that is going to happen, unordered: walls first, then pairs.</summary>
    private static IEnumerable<Event> Events(IReadOnlyList<Body> world)
    {
        for (int i = 0; i < world.Count; i++)
        {
            Body body = world[i];

            foreach (bool horizontal in new[] { true, false })
            {
                Flight flight = horizontal ? body.X : body.Y;
                double limit = horizontal ? Arrangement.Width : Arrangement.Height;

                Maybe<double> at =
                    WallTime(flight: flight, min: body.Radius, max: limit - body.Radius);

                if (at.Match(onSome: static _ => true, onNone: static () => false))
                {
                    yield return new Event(
                        time: at.Match(onSome: static t => t, onNone: static () => 0.0),
                        index: i,
                        other: -1,
                        horizontal: horizontal);
                }
            }
        }

        for (int i = 0; i < world.Count; i++)
        {
            for (int j = i + 1; j < world.Count; j++)
            {
                Maybe<double> at = ContactTime(first: world[i], second: world[j]);

                if (at.Match(onSome: static _ => true, onNone: static () => false))
                {
                    yield return new Event(
                        time: at.Match(onSome: static t => t, onNone: static () => 0.0),
                        index: i,
                        other: j,
                        horizontal: false);
                }
            }
        }
    }

    /// <summary>
    ///     When a ball next reaches a wall, or none if it never does.
    /// </summary>
    /// <remarks>
    ///     Mostly this is the solve <see cref="BouncingAxis" /> already does. The extra case is a
    ///     ball that is already past its bound and still heading out, which has no root ahead of it
    ///     and would otherwise sail away for good. That state should not arise, but an impact
    ///     resolved while a ball is against a wall can produce it, and a solver with no way back
    ///     from an impossible state is one bad rounding away from losing a ball.
    /// </remarks>
    private static Maybe<double> WallTime(Flight flight, double min, double max)
    {
        bool escaping =
            (flight.Position < min && flight.Velocity < 0.0)
            || (flight.Position > max && flight.Velocity > 0.0);

        return escaping
            ? Maybe.Some(flight.StartTime + MinimumInterval)
            : BouncingAxis.NextBounceTime(flight: flight, min: min, max: max);
    }

    /// <summary>
    ///     When two balls are next exactly touching, or none if they never are.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Worth knowing what this cannot do anything about. The moment is solved exactly, but
    ///         the graph only hears about it when an alarm fires, and an alarm is a wait on a real
    ///         clock - a few milliseconds late in practice. Until it fires the two balls are still
    ///         following the equations they had, so they carry on closing, and at the fastest
    ///         impacts they visibly overlap by a few pixels before springing apart. The error is
    ///         closing speed multiplied by that lateness, which is why it shows here and barely
    ///         shows at a wall in the other scenes: two balls can close on each other faster than
    ///         either one approaches a wall.
    ///     </para>
    ///     <para>
    ///         Both are under the same gravity, so the acceleration cancels out of the difference
    ///         and their separation moves in a straight line. What is left is
    ///         <c>|dp + dv t| = r1 + r2</c>, a quadratic in <c>t</c>, and the answer is its
    ///         smaller positive root. Balls already moving apart are ignored: they are either
    ///         separating or
    ///         have just been resolved, and either way the next thing to happen to them is not
    ///         this.
    ///     </para>
    /// </remarks>
    private static Maybe<double> ContactTime(Body first, Body second)
    {
        double startTime = first.X.StartTime;

        double dx = second.X.Position - first.X.Position;
        double dy = second.Y.Position - first.Y.Position;
        double dvx = second.X.Velocity - first.X.Velocity;
        double dvy = second.Y.Velocity - first.Y.Velocity;

        double contact = first.Radius + second.Radius;

        double a = dvx * dvx + dvy * dvy;
        double b = 2.0 * (dx * dvx + dy * dvy);
        double c = dx * dx + dy * dy - contact * contact;

        // Not moving relative to one another, or already moving apart. A pair that is
        // overlapping but separating needs nothing done to it either: it is on its way out.
        if (a < double.Epsilon || b >= 0.0)
        {
            return Maybe.None;
        }

        // Already overlapping and still closing. There is no root ahead - the roots bracket the
        // present - so the contact has to be taken now rather than solved for. Without this the
        // pair simply passes through each other, and never meets again because by then they are
        // separating.
        if (c <= 0.0)
        {
            return Maybe.Some(startTime + MinimumInterval);
        }

        double discriminant = b * b - 4.0 * a * c;

        if (discriminant < 0.0)
        {
            return Maybe.None;
        }

        double dt = (-b - Math.Sqrt(discriminant)) / (2.0 * a);

        return dt > MinimumInterval && !double.IsNaN(dt) && !double.IsInfinity(dt)
            ? Maybe.Some(startTime + dt)
            : Maybe.None;
    }

    /// <summary>
    ///     Resolves one impact, in place.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The standard elastic result. Only the component along the line joining the centers
    ///         changes; the tangential component is untouched, which is what makes a glancing blow
    ///         glance rather than stop. So the angle out follows from the geometry rather than
    ///         from anything written here.
    ///     </para>
    ///     <para>
    ///         Mass is the radius squared, because the balls are drawn as discs of one density and
    ///         area is what a disc has. It is the ratio that shows: a big ball meeting a small one
    ///         barely changes course while the small one comes back hard, and two equal balls
    ///         meeting head on simply exchange velocities.
    ///     </para>
    /// </remarks>
    private static void Collide(IList<Body> bodies, int first, int second)
    {
        Body a = bodies[first];
        Body b = bodies[second];

        double dx = b.X.Position - a.X.Position;
        double dy = b.Y.Position - a.Y.Position;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < double.Epsilon)
        {
            return;
        }

        double nx = dx / distance;
        double ny = dy / distance;

        // Positive while they are still closing. Zero or less means the impact has already been
        // resolved, and applying it twice would draw energy out of nothing.
        double approach = (a.X.Velocity - b.X.Velocity) * nx + (a.Y.Velocity - b.Y.Velocity) * ny;

        if (approach <= 0.0)
        {
            return;
        }

        double impulse = 2.0 * approach / (a.Mass + b.Mass);

        bodies[first] =
            a.WithVelocity(
                velocityX: a.X.Velocity - impulse * b.Mass * nx,
                velocityY: a.Y.Velocity - impulse * b.Mass * ny);

        bodies[second] =
            b.WithVelocity(
                velocityX: b.X.Velocity + impulse * a.Mass * nx,
                velocityY: b.Y.Velocity + impulse * a.Mass * ny);
    }

    /// <summary>One ball, as the pair of equations it is currently following.</summary>
    private readonly struct Body
    {
        public Body(Flight x, Flight y, double radius)
        {
            this.X = x;
            this.Y = y;
            this.Radius = radius;
        }

        public Flight X { get; }

        public Flight Y { get; }

        public double Radius { get; }

        /// <summary>Area, the balls being discs of a single density.</summary>
        public double Mass => this.Radius * this.Radius;

        /// <summary>The same motion, written as though it began at <paramref name="time" />.</summary>
        public Body RebasedTo(double time) =>
            new(
                x: new Flight(
                    startTime: time,
                    position: this.X.PositionAt(time),
                    velocity: this.X.VelocityAt(time),
                    acceleration: this.X.Acceleration),
                y: new Flight(
                    startTime: time,
                    position: this.Y.PositionAt(time),
                    velocity: this.Y.VelocityAt(time),
                    acceleration: this.Y.Acceleration),
                radius: this.Radius);

        public Body WithVelocity(double velocityX, double velocityY) =>
            new(
                x: new Flight(
                    startTime: this.X.StartTime,
                    position: this.X.Position,
                    velocity: velocityX,
                    acceleration: this.X.Acceleration),
                y: new Flight(
                    startTime: this.Y.StartTime,
                    position: this.Y.Position,
                    velocity: velocityY,
                    acceleration: this.Y.Acceleration),
                radius: this.Radius);

        /// <summary>
        ///     The same ball with one axis reversed, having just reached a wall.
        /// </summary>
        /// <remarks>
        ///     Elastic, and unlike <see cref="BouncingAxis" /> this never lets a ball come to rest.
        ///     Resting sets the acceleration to zero, and a ball with a different acceleration to
        ///     its neighbors would break the very thing that makes the pairwise solve a quadratic.
        ///     Nothing loses speed in this scene, so nothing needs to be allowed to stop.
        /// </remarks>
        public Body Reflected(bool horizontal, double min, double max)
        {
            Flight flight = horizontal ? this.X : this.Y;

            // Clamped as well as reversed. On the ordinary path the ball is exactly on the bound
            // and this changes nothing; it matters only when it arrived here already past it.
            Flight reflected =
                new(
                    startTime: flight.StartTime,
                    position: Math.Min(val1: Math.Max(val1: flight.Position, val2: min), val2: max),
                    velocity: -flight.Velocity,
                    acceleration: flight.Acceleration);

            return horizontal
                ? new Body(x: reflected, y: this.Y, radius: this.Radius)
                : new Body(x: this.X, y: reflected, radius: this.Radius);
        }
    }

    /// <summary>
    ///     Something about to happen: a wall for one ball, or a meeting of two.
    /// </summary>
    /// <remarks>
    ///     <see cref="Other" /> below zero means a wall, in which case <see cref="Horizontal" />
    ///     says which axis. It is a struct rather than two types because the only thing anything
    ///     does with these is take the earliest.
    /// </remarks>
    private readonly struct Event
    {
        public Event(double time, int index, int other, bool horizontal)
        {
            this.Time = time;
            this.Index = index;
            this.Other = other;
            this.Horizontal = horizontal;
        }

        public double Time { get; }

        public int Index { get; }

        public int Other { get; }

        public bool Horizontal { get; }
    }
}
