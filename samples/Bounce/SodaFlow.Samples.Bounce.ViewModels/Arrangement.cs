using System.Collections.Generic;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     The box and the balls in it. The two scenes with the same layout read this.
/// </summary>
/// <remarks>
///     This is here because the interactive scene and the plain scene start from the same layout,
///     and one scene is not a subclass of the other. The two are different in the source of the
///     position of a ball, and an inheritance relation does not give that difference
///     correctly.
/// </remarks>
internal static class Arrangement
{
    /// <summary>This is positive, because the y-axis of a screen points down.</summary>
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

    /// <summary>
    ///     The initial flights of a ball, as members of the start that gives them.
    /// </summary>
    /// <remarks>
    ///     This is an extension block and not a set of methods on <see cref="Start" />. Thus the
    ///     record keeps its one subject, which is the initial position of a ball, and the code that
    ///     makes a <see cref="Flight" /> stays here with <see cref="Gravity" />. The vertical
    ///     flight uses that gravity. The call reads as a member call with each shape, and that is
    ///     the purpose.
    /// </remarks>
    extension(Start start)
    {
        /// <summary>The initial horizontal flight of a ball, and its flight after a
        /// throw.</summary>
        public Flight InitialX(double now) =>
            new(StartTime: now, Position: start.X, Velocity: start.VelocityX, Acceleration: 0.0);

        /// <summary>The initial vertical flight of a ball.</summary>
        public Flight InitialY(double now) =>
            new(StartTime: now, Position: start.Y, Velocity: start.VelocityY, Acceleration: Gravity);
    }

    /// <summary>The initial position of one ball, its speed, and its appearance.</summary>
    // ReSharper disable once InheritdocConsiderUsage
    internal readonly record struct Start(
        double X,
        double Y,
        double VelocityX,
        double VelocityY,
        double Radius,
        string Color);
}
