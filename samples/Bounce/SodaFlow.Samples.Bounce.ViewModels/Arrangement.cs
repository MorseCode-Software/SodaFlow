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
        new[]
        {
            new Start(x: 70.0, y: 60.0, velocityX: 210.0, velocityY: 0.0, radius: 20.0, color: "#E2574C"),
            new Start(x: 240.0, y: 40.0, velocityX: -160.0, velocityY: 120.0, radius: 14.0, color: "#2D9CDB"),
            new Start(x: 360.0, y: 150.0, velocityX: 120.0, velocityY: -90.0, radius: 26.0, color: "#F2C94C"),
            new Start(x: 150.0, y: 220.0, velocityX: -240.0, velocityY: -40.0, radius: 11.0, color: "#27AE60")
        };

    /// <summary>The horizontal flight a ball begins with, or resumes with when thrown.</summary>
    public static Flight InitialX(Start start, double now) =>
        new(startTime: now, position: start.X, velocity: start.VelocityX, acceleration: 0.0);

    /// <summary>The vertical flight a ball begins with.</summary>
    public static Flight InitialY(Start start, double now) =>
        new(startTime: now, position: start.Y, velocity: start.VelocityY, acceleration: Gravity);

    internal readonly struct Start
    {
        public Start(double x, double y, double velocityX, double velocityY, double radius, string color)
        {
            this.X = x;
            this.Y = y;
            this.VelocityX = velocityX;
            this.VelocityY = velocityY;
            this.Radius = radius;
            this.Color = color;
        }

        public double X { get; }

        public double Y { get; }

        public double VelocityX { get; }

        public double VelocityY { get; }

        public double Radius { get; }

        public string Color { get; }
    }
}
