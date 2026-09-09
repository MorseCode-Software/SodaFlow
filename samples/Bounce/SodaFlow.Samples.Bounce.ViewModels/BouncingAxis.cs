using System;
using SodaFlow.Functional;
using SodaFlow.Time;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     One axis of motion, bouncing between two bounds forever.
/// </summary>
/// <remarks>
///     <para>
///         The shape here is the whole idea of the sample. A <see cref="Cell{T}" /> holds the
///         current <see cref="Flight" /> - the equation the body is following right now - and the
///         position is that equation applied to the current time. Every bounce replaces the
///         equation, and <c>SwitchB</c> makes the resulting behavior follow whichever equation is
///         current.
///     </para>
///     <para>
///         The feedback is the interesting part. The next bounce is a function of the current
///         flight, the bounce is what produces the next flight, and so the graph refers to itself.
///         <c>Cell.Loop</c> is what lets it be written that way, and no sink is involved: the
///         simulation runs on pure FRP feedback, so nothing has to remember to send anything.
///     </para>
/// </remarks>
internal static class BouncingAxis
{
    /// <summary>
    ///     How soon after a bounce another one is believed. Two bounces cannot be separated by
    ///     less than this, which is what keeps the solved root of the bounce just taken from
    ///     being read as a fresh one at the same instant.
    /// </summary>
    private const double MinimumInterval = 1e-6;

    /// <summary>
    ///     The speed below which a damped body stops rather than bouncing again.
    /// </summary>
    /// <remarks>
    ///     This is what makes damping finite. Multiply the speed by less than one at every bounce
    ///     and the bounces get closer together as fast as they get smaller, without the interval
    ///     between them ever reaching zero - so a simulation that solves for each one in turn will
    ///     schedule them forever and never arrive at the body sitting still. Deciding that slowly
    ///     enough is stopped is how that is answered, here as everywhere else.
    /// </remarks>
    private const double RestSpeed = 25.0;

    /// <summary>
    ///     The speed a bounce will not take a body past.
    /// </summary>
    /// <remarks>
    ///     The other end of the same problem <see cref="RestSpeed" /> answers. Multiply the speed
    ///     by more than one at every bounce, and it grows without bound, so the bounces get closer
    ///     together without limit - and once they are closer together than the timer can service
    ///     them, the flight in force is older than the moment being drawn and the body is drawn
    ///     wherever an equation it should have stopped following says. A speed the gaining stops at
    ///     keeps the bounces far enough apart to stay ahead of, which is what makes a multiplier
    ///     above one something the simulation can honor rather than merely accept.
    /// </remarks>
    private const double MaximumSpeed = 2000.0;

    /// <summary>
    ///     Builds the position along one axis: a behavior defined at every instant, bouncing
    ///     between <paramref name="min" /> and <paramref name="max" />.
    /// </summary>
    /// <param name="timers">
    ///     The clock the position is a function of, and the source of the alarms each bounce is
    ///     scheduled on.
    /// </param>
    /// <param name="initial">The flight the body is following before any bounce or restart.</param>
    /// <param name="min">The lower bound, which is the ceiling on an axis that points down.</param>
    /// <param name="max">The upper bound, which is the floor on an axis that points down.</param>
    /// <param name="restarts">
    ///     Flights imposed from outside, which take precedence over a bounce arriving in the same
    ///     transaction. Releasing a thrown ball arrives here, and so does relaunching a scene that
    ///     has damped itself to a standstill; the one-ball scene has nothing to impose and passes a
    ///     stream that never fires.
    /// </param>
    /// <param name="restitution">
    ///     What the speed is multiplied by at each bounce. One is a perfectly elastic bounce, less
    ///     loses speed, more gains it. Read at the moment of the bounce, so changing it affects the
    ///     next bounce rather than the flight already under way.
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
    ///     The equation in force at each moment, each bounce replacing the one before it.
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
                // Armed for the instant the current flight reaches a bound, disarmed when it never
                // will. Rescheduling is not a step anything performs - the target is a function of
                // the flight, so a new flight is a new target.
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
    ///     The position, following whichever flight is current.
    /// </summary>
    private static Behavior<double> Position(ITimerSystem<double> timers, Cell<Flight> flight) =>
        // Each flight becomes its own behavior - a function of time and nothing else - and
        // SwitchB flattens the cell of them back into a single continuous position.
        flight.Map(f => timers.Time.Map(f.PositionAt)).SwitchB();

    /// <summary>
    ///     When the given flight next reaches a bound, or none if it never does.
    /// </summary>
    internal static Maybe<double> NextBounceTime(Flight flight, double min, double max)
    {
        Maybe<double> toMin = TimeToReach(flight: flight, bound: min);
        Maybe<double> toMax = TimeToReach(flight: flight, bound: max);

        return toMin.Match(
            onSome: a => toMax.Match(onSome: b => Maybe.Some(Math.Min(val1: a, val2: b)), onNone: () => Maybe.Some(a)),
            onNone: () => toMax);
    }

    /// <summary>
    ///     The flight that begins where the given one meets a bound, going the other way.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <paramref name="restitution" /> is the whole of the damping: one bounces without
    ///         loss, less than one loses speed, more than one gains it.
    ///     </para>
    ///     <para>
    ///         Below <see cref="RestSpeed" /> the body stops instead, which is the part damping
    ///         cannot do without. It stops only where stopping is possible: against the far bound
    ///         of an axis that accelerates towards it - the floor - or anywhere on an axis with no
    ///         acceleration at all. A slow body at the ceiling is not at rest, it is about to fall.
    ///     </para>
    ///     <para>
    ///         Above <see cref="MaximumSpeed" /> it gains no more, which is the part a multiplier
    ///         over one cannot do without, and for the mirror-image reason.
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
            ? new Flight(startTime: time, position: bound, velocity: 0.0, acceleration: 0.0)
            : new Flight(
                startTime: time,
                position: bound,
                velocity: velocity,
                acceleration: flight.Acceleration);
    }

    /// <summary>The given speed, in the direction it is going, held to <see cref="MaximumSpeed" />.</summary>
    private static double Clamp(double velocity) =>
        velocity > MaximumSpeed
            ? MaximumSpeed
            : velocity < -MaximumSpeed
                ? -MaximumSpeed
                : velocity;

    /// <summary>
    ///     How long until the flight reaches the bound, or none if it does not reach it going
    ///     forward in time.
    /// </summary>
    private static Maybe<double> TimeToReach(Flight flight, double bound)
    {
        double offset = flight.Position - bound;

        if (Math.Abs(flight.Acceleration) < double.Epsilon)
        {
            // No acceleration, so the position is a straight line and there is one crossing.
            return Math.Abs(flight.Velocity) < double.Epsilon
                ? Maybe.None
                : Reached(flight: flight, dt: -offset / flight.Velocity);
        }

        // 0.5at^2 + vt + offset = 0, whose roots are the two moments the body is at the bound.
        double discriminant = flight.Velocity * flight.Velocity - 2.0 * flight.Acceleration * offset;

        if (discriminant < 0.0)
        {
            return Maybe.None;
        }

        double root = Math.Sqrt(discriminant);
        Maybe<double> first = Reached(flight: flight, dt: (-flight.Velocity - root) / flight.Acceleration);
        Maybe<double> second = Reached(flight: flight, dt: (-flight.Velocity + root) / flight.Acceleration);

        return first.Match(
            onSome: a => second.Match(onSome: b => Maybe.Some(Math.Min(val1: a, val2: b)), onNone: () => Maybe.Some(a)),
            onNone: () => second);
    }

    /// <summary>
    ///     The absolute time <paramref name="dt" /> seconds into the flight, if that is far enough
    ///     ahead to be a bounce that has not already been taken.
    /// </summary>
    private static Maybe<double> Reached(Flight flight, double dt) =>
        dt > MinimumInterval && !double.IsNaN(dt) && !double.IsInfinity(dt)
            ? Maybe.Some(flight.StartTime + dt)
            : Maybe.None;
}
