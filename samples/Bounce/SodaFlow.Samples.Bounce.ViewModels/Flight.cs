using System;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     One unbroken stretch of motion along one axis: where a body was, how fast it was going,
///     and what it is accelerating at, from a moment in time onward.
/// </summary>
/// <remarks>
///     <para>
///         This is the whole of the physics, and it is worth noticing that it is a pure function
///         rather than a state that something has to keep stepping. <see cref="PositionAt" /> is
///         defined at every instant, not at the instants a frame happened to land on, which is
///         exactly what a <see cref="Behavior{T}" /> is for and exactly what a
///         <see cref="Cell{T}" /> could not express.
///     </para>
///     <para>
///         Because the position is an equation, the moment a body reaches a wall is something to
///         solve for rather than to notice after the fact. Nothing in this sample tests whether a
///         ball has gone past an edge, and no bounce can be missed by a frame arriving late or
///         skipped entirely.
///     </para>
/// </remarks>
internal readonly struct Flight
{
    public Flight(double startTime, double position, double velocity, double acceleration)
    {
        this.StartTime = startTime;
        this.Position = position;
        this.Velocity = velocity;
        this.Acceleration = acceleration;
    }

    /// <summary>The moment this stretch of motion began.</summary>
    public double StartTime { get; }

    /// <summary>The position at <see cref="StartTime" />.</summary>
    public double Position { get; }

    /// <summary>The velocity at <see cref="StartTime" />, in units per second.</summary>
    public double Velocity { get; }

    /// <summary>Constant acceleration, in units per second squared. Zero along a level axis.</summary>
    public double Acceleration { get; }

    /// <summary>The position at <paramref name="time" />.</summary>
    public double PositionAt(double time)
    {
        double dt = time - this.StartTime;
        return this.Position + (this.Velocity * dt) + (0.5 * this.Acceleration * dt * dt);
    }

    /// <summary>The velocity at <paramref name="time" />.</summary>
    public double VelocityAt(double time) => this.Velocity + (this.Acceleration * (time - this.StartTime));
}
