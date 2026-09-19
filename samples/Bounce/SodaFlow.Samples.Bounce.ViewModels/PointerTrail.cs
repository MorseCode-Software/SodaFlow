namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     The last two positions of the pointer, and their times.
/// </summary>
/// <remarks>
///     This is sufficient to give the speed of the pointer at the release, and that speed makes a
///     correct throw. There are two samples and not one, because a velocity needs a difference.
///     There are not more samples, because a longer window gives a lower speed than the true speed
///     of a fast move.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal readonly record struct PointerTrail
{
    private PointerTrail(
        bool hasPrevious,
        double previousTime,
        double previousX,
        double previousY,
        double time,
        double x,
        double y)
    {
        this.HasPrevious = hasPrevious;
        this.PreviousTime = previousTime;
        this.PreviousX = previousX;
        this.PreviousY = previousY;
        this.Time = time;
        this.X = x;
        this.Y = y;
    }

    public static PointerTrail Empty => default;

    private bool HasPrevious { get; }

    private double PreviousTime { get; }

    private double PreviousX { get; }

    private double PreviousY { get; }

    private double Time { get; }

    public double X { get; }

    public double Y { get; }

    /// <summary>The horizontal speed across the last interval, in units per second.</summary>
    public double VelocityX => this.Velocity(this.X - this.PreviousX);

    /// <summary>The vertical speed across the last interval, in units per second.</summary>
    public double VelocityY => this.Velocity(this.Y - this.PreviousY);

    public PointerTrail Add(double time, double x, double y) =>
        new(
            hasPrevious: true,
            previousTime: this.Time,
            previousX: this.X,
            previousY: this.Y,
            time: time,
            x: x,
            y: y);

    private double Velocity(double delta)
    {
        double dt = this.Time - this.PreviousTime;

        // A pointer with one move, or with two moves at the same instant, has no usable speed.
        // A release then lets the ball fall.
        return !this.HasPrevious || dt <= 0.0 ? 0.0 : delta / dt;
    }
}
