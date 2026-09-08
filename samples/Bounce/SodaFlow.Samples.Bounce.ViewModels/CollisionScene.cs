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

    /// <summary>
    ///     The speed a damped ball leaves the floor with, however slowly it arrived.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A ball in this scene is never allowed to come properly to rest, and that is the
    ///         collision solver dictating terms to the rest of the scene. Resting means no velocity
    ///         and no acceleration, and a ball with a different acceleration to its neighbors would
    ///         make the separation between that pair curve - turning the contact solve from a
    ///         quadratic into a quartic. Every ball has to share one acceleration or none of this
    ///         works.
    ///     </para>
    ///     <para>
    ///         So a ball that has damped away to nothing keeps bouncing at this speed instead, which
    ///         under this gravity carries it 0.9px off the floor every 89ms. That is under a pixel,
    ///         which is what a settled ball in this scene amounts to, and slow enough that four of
    ///         them resting together do not flood the clock with events. It also
    ///         answers Zeno, which is the other thing resting exists to do: the interval between
    ///         floor bounces stops shrinking here rather than closing up forever.
    ///     </para>
    /// </remarks>
    private const double MinimumFloorBounce = 40.0;

    /// <summary>
    ///     The speed no ball is allowed past, however much the damping keeps handing it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The mirror of <see cref="MinimumFloorBounce" />, and the same reason
    ///         <see cref="BouncingAxis" /> has one. Above a restitution of one every impact returns
    ///         more than it took, so the speed grows without bound and the events close up without
    ///         limit - and once they are closer together than an alarm can be delivered, the graph
    ///         is being told about a bounce after the ball has already gone through the wall. The
    ///         balls leave the box and do not come back.
    ///     </para>
    ///     <para>
    ///         2000px/s matches <see cref="BouncingAxis" />. It is chosen to be out of reach rather
    ///         than to bite: with all of this scene's energy concentrated in its lightest ball, that
    ///         ball would be doing about 2050px/s, and that is the theoretical worst an elastic run
    ///         can produce. So the ceiling is only ever reached by a run that is being fed.
    ///     </para>
    /// </remarks>
    private const double MaximumSpeed = 2000.0;

    /// <param name="timers">The clock every ball's position is a function of.</param>
    /// <param name="restitution">
    ///     What a bounce multiplies the speed by, the same cell the other two damped scenes read.
    ///     It reaches both kinds of impact here - a wall, and one ball against another.
    /// </param>
    /// <param name="restarts">Fires when this scene's tab becomes the selected one.</param>
    internal CollisionScene(
        ITimerSystem<double> timers,
        Cell<double> restitution,
        Stream<Unit> restarts)
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
                                .Snapshot(
                                    c1: bodies,
                                    c2: restitution,
                                    f: static (time, w, e) => Step(world: w, time: time, restitution: e));

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
        + "for ahead of time rather than noticed afterwards, and the heavier ball gives way less. "
        + "Undamped the impacts conserve momentum and energy both; damped they still conserve "
        + "momentum, because two balls cannot take it from each other.";

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
    private static IReadOnlyList<Body> Step(IReadOnlyList<Body> world, double time, double restitution)
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
                        max: limit - next[e.Index].Radius,
                        restitution: restitution);
            }
            else
            {
                Collide(bodies: next, first: e.Index, second: e.Other, restitution: restitution);
            }
        }

        // An impact can drive a ball into a wall it was already resting against, which leaves it
        // outside the box. WallTime would catch that and schedule a reflection, but only for the
        // next instant, and an alarm takes a few milliseconds to arrive - long enough at these
        // speeds to see the ball outside. Putting it right here costs nothing and removes the
        // window entirely; what remains of the rescue in WallTime is the case this cannot reach.
        for (int i = 0; i < next.Length; i++)
        {
            next[i] = Bounded(Contained(body: next[i], restitution: restitution));
        }

        return next;
    }

    /// <summary>The same ball, with its speed held to <see cref="MaximumSpeed" />.</summary>
    /// <remarks>
    ///     The whole velocity is scaled rather than either axis clipped, so a ball held at the
    ///     ceiling keeps the direction it was travelling. Applied after the impacts rather than
    ///     inside them: the impulse conserves momentum exactly, and it is worth leaving that alone
    ///     and doing the clamping somewhere it can be seen.
    /// </remarks>
    private static Body Bounded(Body body)
    {
        double vx = body.X.Velocity;
        double vy = body.Y.Velocity;
        double speed = Math.Sqrt(vx * vx + vy * vy);

        if (speed <= MaximumSpeed)
        {
            return body;
        }

        double scale = MaximumSpeed / speed;

        return body.WithVelocity(velocityX: vx * scale, velocityY: vy * scale);
    }

    /// <summary>
    ///     The same ball, reflected off any wall it is currently outside of and still leaving.
    /// </summary>
    private static Body Contained(Body body, double restitution)
    {
        foreach (bool horizontal in new[] { true, false })
        {
            Flight flight = horizontal ? body.X : body.Y;
            double limit = horizontal ? Arrangement.Width : Arrangement.Height;
            double min = body.Radius;
            double max = limit - body.Radius;

            // Inclusive on purpose; see the note in WallTime.
            bool escaping =
                (flight.Position <= min && flight.Velocity < 0.0)
                || (flight.Position >= max && flight.Velocity > 0.0);

            if (escaping)
            {
                body = body.Reflected(
                    horizontal: horizontal,
                    min: min,
                    max: max,
                    restitution: restitution);
            }
        }

        return body;
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
        // Note the inclusive comparison. A ball sitting exactly on a bound and moving out of it
        // gets no bounce from NextBounceTime - the root is zero distance away, which the minimum
        // interval rejects - so on an axis with no acceleration nothing would ever turn it round
        // and it would leave the box for good. Rare, and it happens: an impact resolved at the
        // instant a ball is against a wall produces exactly this.
        bool escaping =
            (flight.Position <= min && flight.Velocity < 0.0)
            || (flight.Position >= max && flight.Velocity > 0.0);

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
    ///         The standard result. Only the component along the line joining the centers changes;
    ///         the tangential component is untouched, which is what makes a glancing blow glance
    ///         rather than stop. So the angle out follows from the geometry rather than from
    ///         anything written here.
    ///     </para>
    ///     <para>
    ///         Momentum survives at any restitution, because the two impulses are equal and
    ///         opposite by construction. Energy survives only at one. That is the difference
    ///         between the two halves of the damping: a wall can take momentum away because it is
    ///         bolted to the world, and two balls cannot take it from each other.
    ///     </para>
    ///     <para>
    ///         Mass is the radius squared, because the balls are drawn as discs of one density and
    ///         area is what a disc has. It is the ratio that shows: a big ball meeting a small one
    ///         barely changes course while the small one comes back hard, and two equal balls
    ///         meeting head on simply exchange velocities.
    ///     </para>
    /// </remarks>
    private static void Collide(IList<Body> bodies, int first, int second, double restitution)
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

        // One plus the restitution: at one this is the elastic 2, and momentum and energy both
        // come through untouched. Below it the pair keeps its momentum - the two impulses are equal
        // and opposite whatever this number is - and gives up energy, which is what damping means.
        double impulse = (1.0 + restitution) * approach / (a.Mass + b.Mass);

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
        ///     <para>
        ///         Unlike <see cref="BouncingAxis" /> this never lets a ball stop falling. See
        ///         <see cref="MinimumFloorBounce" /> for why it may not, and what a settled ball is
        ///         here instead. Sideways is different and needs no floor: that axis has no
        ///         acceleration to begin with, so a ball that damps to a horizontal standstill still
        ///         matches its neighbors and costs nothing.
        ///     </para>
        /// </remarks>
        public Body Reflected(bool horizontal, double min, double max, double restitution)
        {
            Flight flight = horizontal ? this.X : this.Y;

            // Only if it is actually on its way out. A ball that has ended up beyond a bound and
            // is coming back still meets that bound on the way in, and the solve reports it as a
            // bounce - reversing there would throw the ball straight back out, and above a
            // restitution of one it would leave harder each time. That is the oscillation that
            // walked balls out of the box.
            // Decided by which bound it is at rather than by comparing against one, because on
            // the ordinary path the position lands on the bound to within a rounding error and
            // either side of it has to count as arriving.
            bool atMax = Math.Abs(flight.Position - max) < Math.Abs(flight.Position - min);
            bool leaving = atMax ? flight.Velocity > 0.0 : flight.Velocity < 0.0;

            // Clamped whichever way this goes. On the ordinary path the ball is exactly on the
            // bound and this changes nothing; it matters when it arrived here already past it,
            // and a ball on its way back in has to be put back on the legal side too - leaving it
            // outside is what let one wander a hundred pixels clear of the box.
            double position = Math.Min(val1: Math.Max(val1: flight.Position, val2: min), val2: max);
            double velocity = leaving ? -flight.Velocity * restitution : flight.Velocity;

            bool onTheFloor = Math.Abs(position - max) < Math.Abs(position - min);

            // Only while something is actually being taken away. Undamped, the speed never decays
            // toward this and forcing it up to the minimum would be handing out energy.
            if (!horizontal && onTheFloor && restitution < 1.0 && Math.Abs(velocity) < MinimumFloorBounce)
            {
                velocity = -MinimumFloorBounce;
            }

            Flight reflected =
                new(
                    startTime: flight.StartTime,
                    position: position,
                    velocity: velocity,
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
