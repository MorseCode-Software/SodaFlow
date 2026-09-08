namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     A ball, as a pair of positions that are defined at every instant.
/// </summary>
/// <remarks>
///     There is deliberately no <c>Update</c> here, and nothing that advances anything. A view
///     draws a ball by asking where it is at the moment it is drawing, which is what
///     <see cref="Behavior{T}" /> is for. Two views running at different frame rates, or one view
///     that stalls, see the same motion.
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

    /// <summary>The center's distance from the left edge, at any instant.</summary>
    private Behavior<double> X { get; }

    /// <summary>The center's distance from the top edge, at any instant.</summary>
    private Behavior<double> Y { get; }

    public double Radius { get; }

    // ReSharper disable once CommentTypo
    /// <summary>The fill, as <c>#RRGGBB</c>, so that either UI framework can read it.</summary>
    public string Color { get; }

    /// <summary>Where this ball is now.</summary>
    /// <remarks>
    ///     Sampling is how a behavior is read, and it is the only way: a behavior has no
    ///     <c>Listen</c> and no <c>Updates</c>, because there is no discrete sequence of moments
    ///     for it to offer. A view samples when it draws.
    /// </remarks>
    internal (double X, double Y) SampleAt() => (this.X.Sample(), this.Y.Sample());
}
