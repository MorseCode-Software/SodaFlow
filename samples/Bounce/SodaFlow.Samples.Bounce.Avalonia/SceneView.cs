using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SodaFlow.Samples.Bounce.ViewModels;

namespace SodaFlow.Samples.Bounce.Avalonia;

/// <summary>
///     Draws a scene one time for each frame, and reads the positions of its balls.
/// </summary>
/// <remarks>
///     <para>
///         This is the full animation. See the code that it does not contain. It holds no
///         position, it adds no delta, and it does not use the interval between two frames. The
///         view reads the positions at the instant of the draw and draws the balls there.
///     </para>
///     <para>
///         The timer selects only the frequency of the read. A lower frequency gives the same
///         movement with fewer samples. A thread that stops gives the correct positions when it
///         continues, and the positions are not late by the interval of the stop. That is the
///         difference between a behavior and a value that other code must keep current.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class SceneView : Control
{
    public static readonly StyledProperty<IScene?> SceneProperty =
        AvaloniaProperty.Register<SceneView, IScene?>(nameof(Scene));

    private static readonly IBrush BoxBrush = new SolidColorBrush(Color.FromRgb(r: 250, g: 250, b: 252));

    private static readonly IPen BoxPen = new Pen(new SolidColorBrush(Color.FromRgb(r: 208, g: 212, b: 220)));

    private readonly Dictionary<string, IBrush> brushes = new();

    private readonly DispatcherTimer timer;

    public SceneView()
    {
        // This is approximately sixty times each second, which is only a selection about the
        // smoothness. The simulation does not use this value.
        this.timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16.0) };
        this.timer.Tick += (_, _) => this.InvalidateVisual();
    }

    public IScene? Scene
    {
        get => this.GetValue(SceneProperty);
        set => this.SetValue(property: SceneProperty, value: value);
    }

    public override void Render(DrawingContext context)
    {
        IScene? scene = this.Scene;

        if (scene is null)
        {
            return;
        }

        context.DrawRectangle(
            brush: BoxBrush,
            pen: BoxPen,
            rect: new Rect(x: 0.0, y: 0.0, width: scene.Width, height: scene.Height));

        // There is one transaction for the frame, thus the code draws each ball at the same
        // instant.
        IReadOnlyList<(double X, double Y)> positions = scene.SamplePositions();

        for (int i = 0; i < positions.Count; i++)
        {
            Ball ball = scene.Balls[i];

            context.DrawEllipse(
                brush: this.BrushFor(ball.Color),
                pen: null,
                center: new Point(x: positions[i].X, y: positions[i].Y),
                radiusX: ball.Radius,
                radiusY: ball.Radius);
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        this.Scene is { } scene ? new Size(width: scene.Width, height: scene.Height) : default;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        this.timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        this.timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (this.Scene is IInteractiveScene scene)
        {
            Point point = e.GetPosition(this);
            scene.Grab(x: point.X, y: point.Y);
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (this.Scene is IInteractiveScene scene)
        {
            Point point = e.GetPosition(this);
            scene.MoveTo(x: point.X, y: point.Y);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (this.Scene is IInteractiveScene scene)
        {
            scene.Release();
            e.Pointer.Capture(null);
        }
    }

    private IBrush BrushFor(string color)
    {
        if (!this.brushes.TryGetValue(key: color, value: out IBrush? brush))
        {
            brush = new SolidColorBrush(Color.Parse(color));
            this.brushes.Add(key: color, value: brush);
        }

        return brush;
    }
}
