using System.Collections.Generic;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     The box and the balls in it, shared by the two scenes that use the same layout.
/// </summary>
/// <remarks>
///     Here so that the interactive scene and the plain one start from the same arrangement
///     without one of them being written as a subclass of the other. They differ in what a ball's
///     position follows, which is not a difference an inheritance relationship expresses well.
/// </remarks>
internal static class Arrangement
{
    /// <summary>Downward, because the y-axis of a screen points down.</summary>
    public const double Gravity = 900.0;

    public const double Width = 480.0;

    public const double Height = 320.0;

    public static IReadOnlyList<Start> Starts { get; } =
    [
        new(X: 70.0, Y: 60.0, VelocityX: 210.0, VelocityY: 0.0, Radius: 20.0, Color: "#E2574C"),
        new(X: 240.0, Y: 40.0, VelocityX: -160.0, VelocityY: 120.0, Radius: 14.0, Color: "#2D9CDB"),
        new(X: 360.0, Y: 150.0, VelocityX: 120.0, VelocityY: -90.0, Radius: 26.0, Color: "#F2C94C"),
        new(X: 150.0, Y: 220.0, VelocityX: -240.0, VelocityY: -40.0, Radius: 11.0, Color: "#27AE60")
    ];

    /// <summary>The horizontal flight a ball begins with, or resumes with when thrown.</summary>
    public static Flight InitialX(Start start, double now) =>
        new(StartTime: now, Position: start.X, Velocity: start.VelocityX, Acceleration: 0.0);

    /// <summary>The vertical flight a ball begins with.</summary>
    public static Flight InitialY(Start start, double now) =>
        new(StartTime: now, Position: start.Y, Velocity: start.VelocityY, Acceleration: Gravity);

    /// <summary>Where one ball begins, how fast, and what it looks like.</summary>
    // ReSharper disable once InheritdocConsiderUsage
    internal readonly record struct Start(
        double X,
        double Y,
        double VelocityX,
        double VelocityY,
        double Radius,
        string Color);
}
