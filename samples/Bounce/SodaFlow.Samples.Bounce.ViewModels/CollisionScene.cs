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
///         The other scenes get their shape because their parts operate independently. A ball is
///         two axes, and one axis knows nothing about the other axis. One ball knows nothing about
///         a second ball. A collision removes the two conditions. It connects the two axes of two
///         balls at one instant. Thus, this scene cannot calculate one flight at a time.
///     </para>
///     <para>
///         This scene calculates a collision and does not look for one. No code here moves time
///         forward to find an overlap. The moment when two balls touch is a root of a quadratic.
///         The scene calculates that root first and then schedules it with <c>At</c>, as it does
///         for a wall. Thus, a slow frame cannot cause a missed collision, and a low frame rate
///         cannot let two balls move through each other.
///     </para>
///     <para>
///         One fact makes that quadratic possible. Each ball has the same gravity, thus the
///         acceleration between any two balls is zero. The distance between them is a straight
///         line in time, although each one moves on a curve. Thus, the time when the two touch is
///         a quadratic and not a quartic. A ball with a different acceleration makes this scene
///         necessitate a different solver.
///     </para>
///     <para>
///         Thus the state is one cell that holds each ball, and not one cell for each axis. The
///         scene moves forward one event at a time. It calculates the first event that occurs
///         next, moves to that time, applies the event, and calculates again. An event is a ball
///         that touches a wall, or two balls that touch each other. Between two events each ball
///         is an equation, and a view samples that equation.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class CollisionScene : IScene
{
    /// <summary>
    ///     The scene applies two events together when the time between them is less than this.
    /// </summary>
    /// <remarks>
    ///     Three balls can touch at one instant. That is not frequent, but it is possible. If
    ///     the scene applies only the first event, the other events fire again immediately. Thus,
    ///     the scene applies each event at the same moment together.
    /// </remarks>
    private const double SimultaneousWithin = 1e-9;

    /// <summary>This is the interval that <see cref="BouncingAxis" /> uses, for the same
    /// cause.</summary>
    private const double MinimumInterval = 1e-6;

    /// <summary>
    ///     The speed of a damped ball when it moves up from the floor, at each arrival speed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A ball in this scene cannot stop fully, and the collision solver sets that rule
    ///         for the scene. A ball that stops has no velocity and no acceleration. A ball with
    ///         an acceleration different from the balls near it makes the distance between that
    ///         pair a curve. Then the code calculates the contact with a quartic and not a
    ///         quadratic. Each ball must have the same acceleration, or none of this operates.
    ///     </para>
    ///     <para>
    ///         Thus a ball that damping stopped continues to bounce at this speed. With this
    ///         gravity that speed moves it 0.22px from the floor each 44ms. That is one fifth of a
    ///         pixel, and almost each frame rounds it away. The height falls with the square of
    ///         this number, but the number of events rises only with the number itself. Thus, you
    ///         can make the bounce invisible. The cost of the events is the limit. Four balls that
    ///         stopped cost 6.2s of processor time over a run of 78s at 40px/s, 10.4s at this
    ///         speed of 20, and 14.2s at 12. This minimum speed also answers Zeno, which is the
    ///         second function of a stop. The interval between two bounces on the floor stops
    ///         becoming smaller here, and does not become zero.
    ///     </para>
    /// </remarks>
    private const double MinimumFloorBounce = 20.0;

    /// <summary>
    ///     The distance from a wall at which the scene bounces a ball immediately, and does not
    ///     schedule the bounce.
    /// </summary>
    /// <remarks>
    ///     A ball that an impact sends at a wall that is very near touches that wall in less
    ///     than one millisecond. No alarm is that accurate. The alarm comes some milliseconds
    ///     after that, and the ball is then twelve pixels through the wall. Three balls that
    ///     stopped and then collide show this, because the three move below the floor together.
    ///
    ///     A bounce here applies the change at the same instant. The bounce then occurs before the
    ///     true position by this distance. For that cause this limit is a distance and not a time.
    ///     A user cannot see one pixel at each speed, but one millisecond is one pixel for a slow
    ///     ball and ten pixels for a fast ball.
    /// </remarks>
    private const double TouchingDistance = 1.0;

    /// <summary>
    ///     The time to wait before the scene examines a pair of balls that overlap.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A pair that overlaps has no root after the current time, thus the scene has no
    ///         value to calculate and must examine the pair again. One more step is usually
    ///         sufficient, because it moves the two balls apart and the next step finds a correct
    ///         contact. A group of balls against a wall needs more than one step. A move of one
    ///         pair sends a ball into a third ball, and the code can correct only one pair at a
    ///         time.
    ///     </para>
    ///     <para>
    ///         Thus this value limits the cost of that correction. At the smallest possible
    ///         value the simulation can use one second of the clock on a group of balls, one
    ///         microsecond at a time, and the scene stops. At one millisecond a group of balls
    ///         costs at most one thousand steps each second, and the clock stays current. One
    ///         millisecond is also the minimum time for an alarm. Thus, a smaller value
    ///         asks for a step that the timer cannot supply.
    ///     </para>
    /// </remarks>
    private const double OverlapRetry = 0.001;

    /// <summary>
    ///     The maximum speed of a ball, at each value that the damping supplies.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the opposite of <see cref="MinimumFloorBounce" />, and
    ///         <see cref="BouncingAxis" /> has one for the same cause. Above a restitution of one,
    ///         each impact gives back more than it took. Thus, the speed increases with no limit
    ///         and the events become closer together with no limit. When they are closer together
    ///         than the minimum time for an alarm, the graph learns about a bounce after the ball
    ///         moved through the wall. The balls then go out of the box and do not come back.
    ///     </para>
    ///     <para>
    ///         The value 2000px/s is the value in <see cref="BouncingAxis" />. It is a limit that
    ///         a correct run does not get to, and not a limit that changes a correct run. With
    ///         each part of the energy of this scene in its lightest ball, that ball moves at
    ///         approximately 2050px/s, and that is the maximum that an elastic run can make. Thus,
    ///         only a run that receives energy gets to this limit.
    ///     </para>
    /// </remarks>
    private const double MaximumSpeed = 2000.0;

    /// <param name="timers">The clock that gives the position of each ball.</param>
    /// <param name="restitution">
    ///     The value that a bounce multiplies the speed by. The other two damped scenes read the
    ///     same cell. Here it applies to the two types of impact: a wall, and one ball against a
    ///     second ball.
    /// </param>
    /// <param name="restarts">Fires when a user selects the tab of this scene.</param>
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
                        // This alarm is set for the next event, and it is not set when no event
                        // occurs. No code reschedules it. The world gives the target, thus a new
                        // world gives a new target.
                        Cell<Maybe<double>> nextEvent = bodies.Map(NextEventTime);

                        // The two lambdas give the list as their return type. Step and Initial
                        // give arrays, and a stream of arrays is not a stream of lists.
                        Stream<IReadOnlyList<Body>> stepped =
                            timers
                                .At(nextEvent)
                                .Snapshot(
                                    c1: bodies,
                                    c2: restitution,
                                    f: static IReadOnlyList<Body> (time, w, e) =>
                                        Step(world: w, time: time, restitution: e));

                        return restarts
                            .Snapshot(b: timers.Time, f: static IReadOnlyList<Body> (_, time) => Initial(time))
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

    /// <summary>The world that each ball starts from, and returns to when a user selects the tab
    /// again.</summary>
    private static Body[] Initial(double time)
    {
        Body[] bodies = new Body[Arrangement.Starts.Count];

        for (int i = 0; i < bodies.Length; i++)
        {
            Arrangement.Start start = Arrangement.Starts[i];

            // This is the gravity of each other scene. It keeps the acceleration between any
            // two balls at zero and keeps the contact a quadratic. The comment on the clock
            // above ContactTime gives the cost.
            bodies[i] =
                new Body(
                    X: start.InitialX(now: time),
                    Y: start.InitialY(now: time),
                    Radius: start.Radius);
        }

        return bodies;
    }

    /// <summary>The position of one ball on one axis, from the current equation.</summary>
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

    /// <summary>The time of the next event, or none when no event occurs.</summary>
    private static Maybe<double> NextEventTime(IReadOnlyList<Body> world)
    {
        double[] times = [.. Events(world).Select(static e => e.Time)];

        return times.Length > 0 ? Maybe.Some(times.Min()) : Maybe.None;
    }

    /// <summary>
    ///     The world at the instant after <paramref name="time" />.
    /// </summary>
    /// <remarks>
    ///     This method first moves each ball to that moment, thus each ball then has the same
    ///     start time. That keeps a pair a quadratic, because the method can use the distance and
    ///     the velocity at one instant. Without this, the method must use four equations that each
    ///     start at a different time.
    /// </remarks>
    private static Body[] Step(IReadOnlyList<Body> world, double time, double restitution)
    {
        Body[] next = [.. world.Select(body => body.RebasedTo(time))];

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

        // An impact can send a ball into a wall that the ball is against, and that puts the ball
        // out of the box. WallTime finds that and schedules a reflection, but only for the next
        // instant. An alarm needs some milliseconds, and at these speeds a user sees the ball out
        // of the box. A correction here has no cost and removes that interval. The code in
        // WallTime then covers only the condition that this code cannot correct.
        for (int i = 0; i < next.Length; i++)
        {
            next[i] = Bounded(Contained(body: next[i], restitution: restitution));
        }

        return next;
    }

    /// <summary>The same ball, with its speed at or below <see cref="MaximumSpeed" />.</summary>
    /// <remarks>
    ///     This method multiplies the full velocity and does not limit one axis. Thus, a ball at
    ///     the maximum speed keeps its direction. It runs after the impacts and not in them,
    ///     because the impulse keeps the momentum constant. It is better to keep that step
    ///     unchanged and to apply the limit where a reader can see it.
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
    ///     The same ball, reflected from each wall that it is out of and moves away from.
    /// </summary>
    private static Body Contained(Body body, double restitution)
    {
        foreach (bool horizontal in new[] { true, false })
        {
            Flight flight = horizontal ? body.X : body.Y;
            double limit = horizontal ? Arrangement.Width : Arrangement.Height;
            double min = body.Radius;
            double max = limit - body.Radius;

            // This tests for a ball at a bound and also for a ball on the incorrect side of a
            // bound, and the first condition is the one that a user sees. A ball on the floor that
            // an impact sends down is on the bound and not through it. Before, no code here
            // changed such a ball. It moved down and came back on its own bounce, after one alarm.
            // Three balls that an impact sends down together move below the floor for a time that
            // a user sees.
            //
            // A floor does not operate in that manner. It pushes back at the same instant, and a
            // reflection here does that. Reflected changes the direction only when the ball moves
            // away, thus a ball that stopped and moves up through this code has no cost.
            bool atOrBeyond =
                flight.Position <= min + TouchingDistance || flight.Position >= max - TouchingDistance;

            if (atOrBeyond)
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

    /// <summary>Each event that occurs next, in no sequence. Walls come first, then pairs of
    /// balls.</summary>
    private static IEnumerable<Event> Events(IReadOnlyList<Body> world)
    {
        for (int i = 0; i < world.Count; i++)
        {
            Body body = world[i];

            foreach (bool horizontal in new[] { true, false })
            {
                Flight flight = horizontal ? body.X : body.Y;
                double limit = horizontal ? Arrangement.Width : Arrangement.Height;

                if (WallTime(flight: flight, min: body.Radius, max: limit - body.Radius)
                    .TryGetValue(out double at))
                {
                    yield return new Event(Time: at, Index: i, Other: -1, Horizontal: horizontal);
                }
            }
        }

        for (int i = 0; i < world.Count; i++)
        {
            for (int j = i + 1; j < world.Count; j++)
            {
                if (ContactTime(first: world[i], second: world[j]).TryGetValue(out double at))
                {
                    yield return new Event(Time: at, Index: i, Other: j, Horizontal: false);
                }
            }
        }
    }

    /// <summary>
    ///     The time when a ball next touches a wall, or none when it touches no wall.
    /// </summary>
    /// <remarks>
    ///     This is almost the same as <see cref="BouncingAxis" /> does. It adds one condition: a
    ///     ball on the incorrect side of its bound that continues to move away. Such a ball has
    ///     no root after the current time, and it moves away permanently. That state must not
    ///     occur, but an impact that the scene applies while a ball is against a wall can make
    ///     it. A solver with no correction for an incorrect state loses a ball at the first
    ///     rounding error.
    /// </remarks>
    private static Maybe<double> WallTime(Flight flight, double min, double max)
    {
        // This tests only that the ball is out of the box, in each direction of movement. A ball
        // on the incorrect side of a bound cannot correct itself. NextBounceTime finds the moment
        // when a ball touches the bound, and from the incorrect side there can be no such moment.
        // A ball that stopped is the clearest example. It rises 0.22px, thus a collision that
        // sends it one third of a pixel below the floor stops it from a touch on the floor. While
        // it moves up, it also does not move away. No code scheduled it, and it moved down for
        // half a second before other code found it.
        //
        // A test of the position alone, and not of the position and the direction, corrects that.
        // It cannot occur again, because the step that answers this puts the ball back on the
        // correct side. Thus, the next test finds it in the box.
        bool leavingAtBound =
            (flight.Position <= min && flight.Velocity < 0.0)
            || (flight.Position >= max && flight.Velocity > 0.0);

        bool outside = flight.Position < min || flight.Position > max;

        return outside || leavingAtBound
            ? Maybe.Some(flight.StartTime + MinimumInterval)
            : BouncingAxis.NextBounceTime(flight: flight, min: min, max: max);
    }

    /// <summary>
    ///     The time when two balls next touch, or none when they do not touch.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This method cannot correct one thing. It calculates the moment accurately, but the
    ///         graph learns about that moment only when an alarm fires. An alarm is a wait on a
    ///         true clock, and it is some milliseconds late. Until it fires, the two balls follow
    ///         the equations that they had and continue to move together. At the fastest impacts
    ///         they overlap by some pixels before they move apart. The error is the speed of
    ///         approach multiplied by the time that the alarm adds. For that cause it shows here
    ///         and almost never at a wall in the other scenes. Two balls can approach each other
    ///         faster than one ball approaches a wall.
    ///     </para>
    ///     <para>
    ///         The two balls have the same gravity, thus the acceleration is not in the
    ///         difference and the distance between them is a straight line. That gives
    ///         <c>|dp + dv t| = r1 + r2</c>, which is a quadratic in <c>t</c>. The answer is its
    ///         smaller positive root. This method discards a pair that moves apart. Such a pair
    ///         moves away, or the scene applied its impact, and in each condition the next event
    ///         for them is not a contact.
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

        // The two balls do not move relative to each other, or they move apart. A pair that
        // overlaps and moves apart also needs no change, because it moves away.
        if (a < double.Epsilon || b >= 0.0)
        {
            return Maybe.None;
        }

        // The two balls overlap and continue to move together. There is no root after the
        // current time, because the roots are on the two sides of it. Thus, the scene must apply
        // the contact now and cannot calculate a time for it. Without this code the pair moves
        // through each other and does not touch again, because the two then move apart.
        if (c <= 0.0)
        {
            return Maybe.Some(startTime + OverlapRetry);
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
    ///     Applies one impact to the two balls.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the usual result. Only the part along the line between the two centers
    ///         changes. The part at a right angle to that line does not change, and that makes an
    ///         impact at an angle continue at an angle. Thus, the geometry gives the angle of
    ///         departure, and no code here sets it.
    ///     </para>
    ///     <para>
    ///         The momentum is the same at each restitution, because the two impulses are equal
    ///         and opposite because this method makes them so. The energy is the same only at a
    ///         restitution of one. That is the difference between the two parts of the damping. A
    ///         wall can remove momentum because it is attached to the world, and one ball cannot
    ///         remove momentum from a second ball.
    ///     </para>
    ///     <para>
    ///         The mass is the square of the radius, because the scene draws each ball as a disc
    ///         of one density and a disc has an area. The ratio is what a user sees. A large ball
    ///         that touches a small ball changes its direction by a small angle, and the small
    ///         ball comes back at a high speed. Two equal balls that touch directly interchange
    ///         their velocities.
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

        // This value is positive while the two balls move together. A value of zero or less
        // means that the scene applied the impact. A second impact makes energy from nothing.
        double approach = (a.X.Velocity - b.X.Velocity) * nx + (a.Y.Velocity - b.Y.Velocity) * ny;

        if (approach > 0.0)
        {
            // This is one plus the restitution. At a restitution of one the value is 2, which is
            // elastic, and the momentum and the energy do not change. Below one the pair keeps
            // its momentum, because the two impulses are equal and opposite at each value, and
            // the pair loses energy. That is the function of damping.
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

        // A change of the approach direction is not sufficient, and it is not always possible.
        // More than one ball that touches at the same time sends the others into more overlaps,
        // and the impulse for those overlaps is complete. At each velocity, this code moves a pair
        // that overlaps apart. That makes the second step above stop, because each step increases
        // the distance between the two balls.
        //
        // This code divides the movement by mass. Thus, the heavier ball moves less here, for the
        // cause that makes it move less from the impulse.
        double overlap = a.Radius + b.Radius - distance;

        if (overlap <= 0.0)
        {
            return;
        }

        double firstShare = overlap * (b.Mass / (a.Mass + b.Mass));
        double secondShare = overlap - firstShare;

        // A wall between the two balls is the condition that stopped this code before. A move of
        // a ball through a wall does nothing, because the step that keeps each ball in the box
        // returns it to the same position and the pair overlaps again. That is a loop with no
        // end. Thus, the other ball gets the distance that the box refuses to give to the
        // first ball.
        double firstRoom = Room(body: bodies[first], directionX: -nx, directionY: -ny);
        double secondRoom = Room(body: bodies[second], directionX: nx, directionY: ny);

        if (firstShare > firstRoom)
        {
            secondShare += firstShare - firstRoom;
            firstShare = firstRoom;
        }

        if (secondShare > secondRoom)
        {
            firstShare = Math.Min(val1: firstShare + (secondShare - secondRoom), val2: firstRoom);
            secondShare = secondRoom;
        }

        bodies[first] = bodies[first].MovedBy(x: -nx * firstShare, y: -ny * firstShare);
        bodies[second] = bodies[second].MovedBy(x: nx * secondShare, y: ny * secondShare);
    }

    /// <summary>
    ///     The distance that the given ball can move in the given direction before it touches a
    ///     wall.
    /// </summary>
    /// <remarks>
    ///     This is zero for a ball at the wall that it moves to, or through that wall. The
    ///     direction is a unit vector, thus the answer is a distance in the units of this
    ///     scene.
    /// </remarks>
    private static double Room(Body body, double directionX, double directionY) =>
        Math.Max(
            val1: 0.0,
            val2: Math.Min(
                val1: RoomOnAxis(
                    position: body.X.Position,
                    direction: directionX,
                    min: body.Radius,
                    max: Arrangement.Width - body.Radius),
                val2: RoomOnAxis(
                    position: body.Y.Position,
                    direction: directionY,
                    min: body.Radius,
                    max: Arrangement.Height - body.Radius)));

    /// <summary>The distance that one axis of a ball can move before that axis goes out of the
    /// box.</summary>
    private static double RoomOnAxis(double position, double direction, double min, double max)
    {
        if (direction > 0.0)
        {
            return (max - position) / direction;
        }

        return direction < 0.0 ? (min - position) / direction : double.PositiveInfinity;
    }

    /// <summary>One ball, as the two equations that it follows now.</summary>
    // ReSharper disable once InheritdocConsiderUsage
    private readonly record struct Body(Flight X, Flight Y, double Radius)
    {
        /// <summary>The area. Each ball is a disc of one density.</summary>
        public double Mass => this.Radius * this.Radius;

        /// <summary>The same movement, written as if it started at
        /// <paramref name="time" />.</summary>
        public Body RebasedTo(double time) =>
            this with
            {
                X = new Flight(
                    StartTime: time,
                    Position: this.X.PositionAt(time),
                    Velocity: this.X.VelocityAt(time),
                    Acceleration: this.X.Acceleration),
                Y = new Flight(
                    StartTime: time,
                    Position: this.Y.PositionAt(time),
                    Velocity: this.Y.VelocityAt(time),
                    Acceleration: this.Y.Acceleration)
            };

        /// <summary>The same ball at a new position, with the movement that it had.</summary>
        public Body MovedBy(double x, double y) =>
            this with
            {
                X = this.X with { Position = this.X.Position + x },
                Y = this.Y with { Position = this.Y.Position + y }
            };

        public Body WithVelocity(double velocityX, double velocityY) =>
            this with
            {
                X = this.X with { Velocity = velocityX },
                Y = this.Y with { Velocity = velocityY }
            };

        /// <summary>
        ///     The same ball with one axis in the opposite direction, after it reached a
        ///     wall.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <see cref="BouncingAxis" /> lets a ball stop, and this method does not. See
        ///         <see cref="MinimumFloorBounce" /> for the cause, and for the state of a ball
        ///         that stopped in this scene. The horizontal axis is different and needs no
        ///         minimum, because that axis has no acceleration. Thus, a ball that damping
        ///         stops on that axis has the same acceleration as the balls near it, and costs
        ///         nothing.
        ///     </para>
        /// </remarks>
        public Body Reflected(bool horizontal, double min, double max, double restitution)
        {
            Flight flight = horizontal ? this.X : this.Y;

            // This applies only when the ball moves away. A ball on the incorrect side of a
            // bound that moves back touches that bound as it returns, and the code reports that
            // as a bounce. A change of direction there sends the ball out again, and above a
            // restitution of one it goes out at a higher speed each time. That is the movement
            // that sent balls out of the box.
            // This code selects the bound that the ball is at, and does not compare against one
            // bound. On the usual path the position is on the bound with a rounding error, thus
            // each side of the bound must count as an arrival.
            bool atMax = Math.Abs(flight.Position - max) < Math.Abs(flight.Position - min);
            bool leaving = atMax ? flight.Velocity > 0.0 : flight.Velocity < 0.0;

            // This limit applies in each direction. On the usual path the ball is on the bound
            // and this changes nothing. It is necessary when the ball comes to a position
            // through the bound. A ball that moves back must also return to the correct side. A
            // ball that stays out of the box moves one hundred pixels away from the box.
            double position = Math.Min(val1: Math.Max(val1: flight.Position, val2: min), val2: max);
            double velocity = leaving ? -flight.Velocity * restitution : flight.Velocity;

            bool onTheFloor = Math.Abs(position - max) < Math.Abs(position - min);

            // This applies only while the scene removes energy. With no damping the speed does
            // not fall to this value, and an increase to the minimum supplies energy.
            if (!horizontal && onTheFloor && restitution < 1.0 && Math.Abs(velocity) < MinimumFloorBounce)
            {
                velocity = -MinimumFloorBounce;
            }

            Flight reflected = flight with { Position = position, Velocity = velocity };

            return horizontal ? this with { X = reflected } : this with { Y = reflected };
        }
    }

    /// <summary>
    ///     An event that occurs next. It is a wall for one ball, or a contact between two
    ///     balls.
    /// </summary>
    /// <remarks>
    ///     A value of <see cref="Other" /> below zero means a wall, and <see cref="Horizontal" />
    ///     then gives the axis. This is one struct and not two types, because the only operation
    ///     on these values is to find the first one.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    private readonly record struct Event(double Time, int Index, int Other, bool Horizontal);
}

/// <summary>
///     Tests a calculated moment and reads it in one step.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Maybe{T}" /> has a Match operation and no read operation, and that is not
///         clear where other code uses the answer directly. A test for a value, and then a second
///         read of that value, needs a default value that the code cannot use. This is the
///         <c>TryGet</c> shape that .NET uses for the same question.
///     </para>
///     <para>
///         Allocation-free, which is why it is this rather than <c>ToEnumerable</c>. That returns a
///         one-element array for a value, and <c>Events</c> asks this fourteen times for four balls
///         on each step of the world, and that step occurs at each bounce. The tuple is a struct
///         and the two lambdas are static, thus nothing here uses the heap.
///     </para>
///     <para>
///         <c>file</c> because <see cref="CollisionScene" /> is the only caller, and an extension
///         on a type with this many uses must not show on each <see cref="Maybe{T}" /> in the
///         assembly for one method.
///     </para>
/// </remarks>
file static class MaybeExtensions
{
    extension(Maybe<double> maybe)
    {
        /// <summary>The moment, if there is one.</summary>
        public bool TryGetValue(out double value)
        {
            (bool hasValue, double found) =
                maybe.Match(onSome: static v => (true, v), onNone: static () => (false, 0.0));

            value = found;
            return hasValue;
        }
    }
}
