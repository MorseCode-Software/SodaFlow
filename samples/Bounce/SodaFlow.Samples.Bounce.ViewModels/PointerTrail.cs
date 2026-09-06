namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     The last two places the pointer was, and when.
/// </summary>
/// <remarks>
///     Enough to answer how fast it was moving when it let go, which is the whole reason a throw
///     feels like a throw. Two samples rather than one because a velocity needs a difference, and
///     rather than more because a longer window makes a flick read as slower than it was.
/// </remarks>
internal readonly struct PointerTrail
{
    private PointerTrail(bool hasPrevious, double previousTime, double previousX, double previousY, double time, double x, double y)
    {
        this.HasPrevious = hasPrevious;
        this.PreviousTime = previousTime;
        this.PreviousX = previousX;
        this.PreviousY = previousY;
        this.Time = time;
        this.X = x;
        this.Y = y;
    }

    public static PointerTrail Empty { get; } = default;

    public bool HasPrevious { get; }

    public double PreviousTime { get; }

    public double PreviousX { get; }

    public double PreviousY { get; }

    public double Time { get; }

    public double X { get; }

    public double Y { get; }

    /// <summary>Horizontal speed over the last interval, in units per second.</summary>
    public double VelocityX => this.Velocity(this.X - this.PreviousX);

    /// <summary>Vertical speed over the last interval, in units per second.</summary>
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

        // A pointer that has moved once, or twice in the same instant, has no speed worth
        // reporting. Releasing then simply drops the ball.
        return !this.HasPrevious || dt <= 0.0 ? 0.0 : delta / dt;
    }
}
