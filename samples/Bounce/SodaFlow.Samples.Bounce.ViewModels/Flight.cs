namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     One continuous interval of movement along one axis. It gives the position of a body, its
///     speed, and its acceleration, from one moment forward.
/// </summary>
/// <remarks>
///     <para>
///         This is the full physics, and it is a pure function and not a state that other code
///         must step. <see cref="PositionAt" /> has a value at each instant, and not only at the
///         instants of a frame. That is the purpose of a <see cref="Behavior{T}" />, and a
///         <see cref="Cell{T}" /> cannot give it.
///     </para>
///     <para>
///         The position is an equation, thus the code calculates the moment when a body touches a
///         wall and does not find it after the event. No code in this sample tests for a ball
///         through an edge, and a late frame or a frame that does not occur cannot remove a
///         bounce.
///     </para>
/// </remarks>
/// <param name="StartTime">The moment at the start of this interval of movement.</param>
/// <param name="Position">The position at <paramref name="StartTime" />.</param>
/// <param name="Velocity">The velocity at <paramref name="StartTime" />, in units per second.</param>
/// <param name="Acceleration">
///     The constant acceleration, in units per second squared. It is zero on a level axis.
/// </param>
// ReSharper disable once InheritdocConsiderUsage
internal readonly record struct Flight(double StartTime, double Position, double Velocity, double Acceleration)
{
    /// <summary>The position at <paramref name="time" />.</summary>
    public double PositionAt(double time)
    {
        double dt = time - this.StartTime;
        return this.Position + this.Velocity * dt + 0.5 * this.Acceleration * dt * dt;
    }

    /// <summary>The velocity at <paramref name="time" />.</summary>
    public double VelocityAt(double time) => this.Velocity + this.Acceleration * (time - this.StartTime);
}
