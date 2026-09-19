namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     A ball, as two positions that have a value at each instant.
/// </summary>
/// <remarks>
///     There is no <c>Update</c> here, and no code that moves a ball forward. A view draws a ball
///     from its position at the moment of the draw, and that is the purpose of
///     <see cref="Behavior{T}" />. Two views at different frame rates, and one view that stops,
///     show the same movement.
/// </remarks>
public sealed class Ball
{
    internal Ball(Behavior<double> x, Behavior<double> y, double radius, string color)
    {
        this.X = x;
        this.Y = y;
        this.Radius = radius;
        this.Color = color;
    }

    /// <summary>The distance from the center to the left edge, at each instant.</summary>
    private Behavior<double> X { get; }

    /// <summary>The distance from the center to the top edge, at each instant.</summary>
    private Behavior<double> Y { get; }

    public double Radius { get; }

    // ReSharper disable once CommentTypo
    /// <summary>The fill, as <c>#RRGGBB</c>, thus each UI framework can read it.</summary>
    public string Color { get; }

    /// <summary>The current position of this ball.</summary>
    /// <remarks>
    ///     A sample is the only method to read a behavior. A behavior has no <c>Listen</c> and no
    ///     <c>Updates</c>, because it has no discrete sequence of moments. A view samples at each
    ///     draw.
    /// </remarks>
    internal (double X, double Y) SampleAt() => (this.X.Sample(), this.Y.Sample());
}
