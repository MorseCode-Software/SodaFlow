using System;
using SodaFlow.Functional;
using SodaFlow.Time;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     One axis of movement. It bounces between a minimum bound and a maximum bound
///     continuously.
/// </summary>
/// <remarks>
///     <para>
///         This shape is the full idea of the sample. A <see cref="Cell{T}" /> holds the current
///         <see cref="Flight" />, which is the equation that the body follows now. The position is
///         that equation at the current time. Each bounce replaces the equation, and
///         <c>SwitchB</c> makes the behavior follow the current equation.
///     </para>
///     <para>
///         The feedback is the important part. The next bounce is a function of the current
///         flight, and the bounce makes the next flight. Thus the graph refers to itself.
///         <c>Cell.Loop</c> makes this shape possible, and there is no sink. The simulation uses
///         only FRP feedback, thus no code must send a value.
///     </para>
/// </remarks>
internal static class BouncingAxis
{
    /// <summary>
    ///     The minimum time between one bounce and the next bounce. Two bounces cannot be nearer
    ///     in time than this value. Thus the code does not read the root of the last bounce as a
    ///     new bounce at the same moment.
    /// </summary>
    private const double MinimumInterval = 1e-6;

    /// <summary>
    ///     The speed below which a damped body stops and does not bounce again.
    /// </summary>
    /// <remarks>
    ///     This value makes the damping finite. A multiplier below one at each bounce makes the
    ///     bounces nearer in time as fast as it makes them smaller, and the interval between them
    ///     does not become zero. Thus a simulation that calculates each bounce in sequence
    ///     schedules bounces continuously and the body does not stop. The solution, here and in
    ///     other code, is a rule that a sufficiently low speed is a stop.
    /// </remarks>
    private const double RestSpeed = 25.0;

    /// <summary>
    ///     The maximum speed that a bounce can give to a body.
    /// </summary>
    /// <remarks>
    ///     This is the second part of the problem that <see cref="RestSpeed" /> corrects. A
    ///     multiplier above one at each bounce increases the speed with no limit, thus the bounces
    ///     become nearer in time with no limit. When they are nearer in time than the timer can do
    ///     them, the current flight is older than the moment on the screen. The code then draws the
    ///     body at the position from an equation that is no longer correct. A maximum speed keeps
    ///     the bounces sufficiently far apart for the timer. Thus the simulation can apply a
    ///     multiplier above one correctly.
    /// </remarks>
    private const double MaximumSpeed = 2000.0;

    /// <summary>
    ///     Builds the position along one axis. The result is a behavior with a value at each
    ///     moment, and it bounces between <paramref name="min" /> and <paramref name="max" />.
    /// </summary>
    /// <param name="timers">
    ///     The clock for the position, and the source of the alarms for each bounce.
    /// </param>
    /// <param name="initial">The flight that the body follows before a bounce or a restart.</param>
    /// <param name="min">The minimum bound. On an axis that points down it is the ceiling.</param>
    /// <param name="max">The maximum bound. On an axis that points down it is the floor.</param>
    /// <param name="restarts">
    ///     Flights from other code. They have priority above a bounce in the same transaction.
    ///     The release of a thrown ball comes here, and so does the restart of a scene that damping
    ///     stopped. The scene with one ball sends no flights and gives a stream that never
    ///     fires.
    /// </param>
    /// <param name="restitution">
    ///     The multiplier for the speed at each bounce. A value of one is a fully elastic bounce,
    ///     a lower value decreases the speed, and a higher value increases it. The code reads this
    ///     at the moment of the bounce, thus a change applies to the next bounce and not to the
    ///     current flight.
    /// </param>
    public static Behavior<double> Create(
        ITimerSystem<double> timers,
        Flight initial,
        double min,
        double max,
        Stream<Flight> restarts,
        Cell<double> restitution) =>
        Position(
            timers: timers,
            flight: Flights(
                timers: timers,
                initial: initial,
                min: min,
                max: max,
                restarts: restarts,
                restitution: restitution));

    /// <summary>
    ///     The equation that applies at each moment. Each bounce replaces the equation before
    ///     it.
    /// </summary>
    private static Cell<Flight> Flights(
        ITimerSystem<double> timers,
        Flight initial,
        double min,
        double max,
        Stream<Flight> restarts,
        Cell<double> restitution) =>
        Cell.Loop<Flight>()
            .WithoutCaptures(flight =>
            {
                // This alarm is set for the moment when the current flight touches a bound, and
                // it is off when the flight touches no bound. No code does a step to set the
                // alarm again. The target is a function of the flight, thus a new flight is a new
                // target.
                Cell<Maybe<double>> nextBounce = flight.Map(f => NextBounceTime(flight: f, min: min, max: max));

                Stream<Flight> bounced =
                    timers
                        .At(nextBounce)
                        .Snapshot(
                            c1: flight,
                            c2: restitution,
                            f: (time, f, e) =>
                                Reflect(flight: f, time: time, min: min, max: max, restitution: e));

                return restarts.OrElse(bounced).Hold(initial);
            });

    /// <summary>
    ///     The position, which follows the current flight.
    /// </summary>
    private static Behavior<double> Position(ITimerSystem<double> timers, Cell<Flight> flight) =>
        // Each flight becomes its own behavior, which is a function of time only. SwitchB makes
        // the cell of behaviors into one continuous position.
        flight.Map(f => timers.Time.Map(f.PositionAt)).SwitchB();

    /// <summary>
    ///     The time when the given flight next touches a bound, or none when it touches no
    ///     bound.
    /// </summary>
    internal static Maybe<double> NextBounceTime(Flight flight, double min, double max) =>
        TimeToReach(flight: flight, bound: min).Earlier(TimeToReach(flight: flight, bound: max));

    /// <summary>
    ///     Selects between two moments, and each moment can be missing.
    /// </summary>
    /// <remarks>
    ///     The two calculations here have the same result: two candidate moments, and each one
    ///     can be missing. The answer is the first of the two. This is an extension member and not
    ///     a static method with two maybes, because the subject of the question is one of the two
    ///     moments. It is private, thus the name of the second moment stays in this file.
    /// </remarks>
    extension(Maybe<double> first)
    {
        /// <summary>The first of the two moments, or the one moment that is available.</summary>
        private Maybe<double> Earlier(Maybe<double> second) =>
            first.Match(
                onSome: a => second.Match(
                    onSome: b => Maybe.Some(Math.Min(val1: a, val2: b)),
                    onNone: () => Maybe.Some(a)),
                onNone: () => second);
    }

    /// <summary>
    ///     The flight that starts where the given flight touches a bound, in the opposite
    ///     direction.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <paramref name="restitution" /> is the full damping. A value of one bounces with
    ///         no loss, a value below one decreases the speed, and a value above one increases
    ///         it.
    ///     </para>
    ///     <para>
    ///         Below <see cref="RestSpeed" /> the body stops, and the damping needs that rule.
    ///         The body stops only at a position where a stop is possible: at the far bound of an
    ///         axis that accelerates to that bound, which is the floor, or at each position on an
    ///         axis with no acceleration. A slow body at the ceiling does not stop, because it
    ///         falls next.
    ///     </para>
    ///     <para>
    ///         Above <see cref="MaximumSpeed" /> the speed does not increase, and a multiplier
    ///         above one needs that rule, for the opposite cause.
    ///     </para>
    /// </remarks>
    private static Flight Reflect(Flight flight, double time, double min, double max, double restitution)
    {
        double position = flight.PositionAt(time);
        double bound = Math.Abs(position - min) < Math.Abs(position - max) ? min : max;
        double velocity = Clamp(-flight.VelocityAt(time) * restitution);

        bool canRest =
            Math.Abs(flight.Acceleration) < double.Epsilon || Math.Abs(bound - max) < double.Epsilon;

        return Math.Abs(velocity) < RestSpeed && canRest
            ? new Flight(StartTime: time, Position: bound, Velocity: 0.0, Acceleration: 0.0)
            : new Flight(
                StartTime: time,
                Position: bound,
                Velocity: velocity,
                Acceleration: flight.Acceleration);
    }

    /// <summary>The given speed, in its direction, with a limit of <see cref="MaximumSpeed" />.</summary>
    private static double Clamp(double velocity) =>
        velocity > MaximumSpeed
            ? MaximumSpeed
            : velocity < -MaximumSpeed
                ? -MaximumSpeed
                : velocity;

    /// <summary>
    ///     The time until the flight touches the bound, or none when it does not touch the bound
    ///     in forward time.
    /// </summary>
    private static Maybe<double> TimeToReach(Flight flight, double bound)
    {
        double offset = flight.Position - bound;

        if (Math.Abs(flight.Acceleration) < double.Epsilon)
        {
            // There is no acceleration, thus the position is a straight line and there is one
            // crossing.
            return Math.Abs(flight.Velocity) < double.Epsilon
                ? Maybe.None
                : Reached(flight: flight, dt: -offset / flight.Velocity);
        }

        // 0.5at^2 + vt + offset = 0. The roots are the two moments when the body is at the
        // bound.
        double discriminant = flight.Velocity * flight.Velocity - 2.0 * flight.Acceleration * offset;

        if (discriminant < 0.0)
        {
            return Maybe.None;
        }

        double root = Math.Sqrt(discriminant);

        return Reached(flight: flight, dt: (-flight.Velocity - root) / flight.Acceleration)
            .Earlier(Reached(flight: flight, dt: (-flight.Velocity + root) / flight.Acceleration));
    }

    /// <summary>
    ///     The absolute time at <paramref name="dt" /> seconds into the flight, when that time is
    ///     sufficiently far forward to be a new bounce.
    /// </summary>
    private static Maybe<double> Reached(Flight flight, double dt) =>
        dt > MinimumInterval && !double.IsNaN(dt) && !double.IsInfinity(dt)
            ? Maybe.Some(flight.StartTime + dt)
            : Maybe.None;
}
