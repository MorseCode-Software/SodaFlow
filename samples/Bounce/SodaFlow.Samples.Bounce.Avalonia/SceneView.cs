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
///     Draws a scene, once per frame, by asking it where its balls are.
/// </summary>
/// <remarks>
///     <para>
///         This is the whole of the animation, and it is worth noticing what it does not contain.
///         There is no position held here, nothing accumulating a delta, and no dependence on the
///         interval between frames. The view asks where things are at the instant it is drawing and
///         draws them there.
///     </para>
///     <para>
///         The timer only decides how often to ask. Slow it down and the motion is the same motion,
///         sampled less often; stall the thread and the balls are wherever they should be when it
///         resumes, not behind by however long it was stuck. That is the practical difference
///         between a behavior and a value something has to keep up to date.
///     </para>
/// </remarks>
public sealed class SceneView : Control
{
    public static readonly StyledProperty<IScene?> SceneProperty =
        AvaloniaProperty.Register<SceneView, IScene?>(nameof(SceneView.Scene));

    private static readonly IBrush BoxBrush = new SolidColorBrush(Color.FromRgb(r: 250, g: 250, b: 252));

    private static readonly IPen BoxPen = new Pen(new SolidColorBrush(Color.FromRgb(r: 208, g: 212, b: 220)));

    private readonly Dictionary<string, IBrush> brushes = new();

    private readonly DispatcherTimer timer;

    public SceneView()
    {
        // Roughly sixty times a second, which is a choice about smoothness and nothing else. The
        // simulation does not know or care what this is set to.
        this.timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16.0) };
        this.timer.Tick += (_, _) => this.InvalidateVisual();
    }

    public IScene? Scene
    {
        get => this.GetValue(SceneView.SceneProperty);
        set => this.SetValue(SceneView.SceneProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        IScene? scene = this.Scene;

        if (scene is null)
        {
            return;
        }

        context.DrawRectangle(
            brush: SceneView.BoxBrush,
            pen: SceneView.BoxPen,
            rect: new Rect(x: 0.0, y: 0.0, width: scene.Width, height: scene.Height));

        // One transaction for the frame, so every ball is drawn as of the same instant.
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
        if (!this.brushes.TryGetValue(color, out IBrush? brush))
        {
            brush = new SolidColorBrush(Color.Parse(color));
            this.brushes.Add(key: color, value: brush);
        }

        return brush;
    }
}
