using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SodaFlow.Samples.Bounce.ViewModels;

namespace SodaFlow.Samples.Bounce.Wpf;

/// <summary>
///     Draws a scene one time for each frame, and reads the positions of its balls.
/// </summary>
/// <remarks>
///     <para>
///         The Avalonia head has the same class, and the difference between the two is
///         important. There a timer selects the time of each draw. Here it is
///         <see cref="CompositionTarget.Rendering" />, which WPF raises as it composes each frame.
///         The two selections do not go to the simulation. The difference between the two
///         frameworks is the time of the read, and the answer is the same for each caller and
///         each frequency.
///     </para>
///     <para>
///         No code here holds a position, adds a delta, or reads the interval from the last
///         frame.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class SceneView : FrameworkElement
{
    public static readonly DependencyProperty SceneProperty =
        DependencyProperty.Register(
            name: nameof(Scene),
            propertyType: typeof(IScene),
            ownerType: typeof(SceneView),
            typeMetadata: new FrameworkPropertyMetadata(
                defaultValue: null,
                flags: FrameworkPropertyMetadataOptions.AffectsMeasure |
                       FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush BoxBrush = Freeze(new SolidColorBrush(Color.FromRgb(r: 250, g: 250, b: 252)));

    private static readonly Pen BoxPen =
        Freeze(new Pen(brush: Freeze(new SolidColorBrush(Color.FromRgb(r: 208, g: 212, b: 220))), thickness: 1.0));

    private readonly Dictionary<string, Brush> brushes = new();

    public SceneView()
    {
        this.Loaded += (_, _) => CompositionTarget.Rendering += this.OnRendering;
        this.Unloaded += (_, _) => CompositionTarget.Rendering -= this.OnRendering;
    }

    public IScene? Scene
    {
        get => (IScene?)this.GetValue(SceneProperty);
        set => this.SetValue(dp: SceneProperty, value: value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        this.Scene is { } scene ? new Size(width: scene.Width, height: scene.Height) : default;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (this.Scene is not { } scene)
        {
            return;
        }

        drawingContext.DrawRectangle(
            brush: BoxBrush,
            pen: BoxPen,
            rectangle: new Rect(x: 0.0, y: 0.0, width: scene.Width, height: scene.Height));

        // There is one transaction for the frame, thus the code draws each ball at the same
        // instant.
        IReadOnlyList<(double X, double Y)> positions = scene.SamplePositions();

        for (int i = 0; i < positions.Count; i++)
        {
            Ball ball = scene.Balls[i];

            drawingContext.DrawEllipse(
                brush: this.BrushFor(ball.Color),
                pen: null,
                center: new Point(x: positions[i].X, y: positions[i].Y),
                radiusX: ball.Radius,
                radiusY: ball.Radius);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (this.Scene is IInteractiveScene scene)
        {
            Point point = e.GetPosition(this);
            scene.Grab(x: point.X, y: point.Y);
            this.CaptureMouse();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (this.Scene is IInteractiveScene scene && this.IsMouseCaptured)
        {
            Point point = e.GetPosition(this);
            scene.MoveTo(x: point.X, y: point.Y);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (this.Scene is IInteractiveScene scene)
        {
            scene.Release();
            this.ReleaseMouseCapture();
        }
    }

    /// <summary>These are frozen, thus the render thread can use them with no marshaling.</summary>
    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private void OnRendering(object? sender, EventArgs e) => this.InvalidateVisual();

    private Brush BrushFor(string color)
    {
        if (!this.brushes.TryGetValue(key: color, value: out Brush? brush))
        {
            brush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));
            this.brushes.Add(key: color, value: brush);
        }

        return brush;
    }
}
