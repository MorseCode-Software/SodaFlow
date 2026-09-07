# Bounce

Balls bouncing in a box, as an argument for `Behavior<T>`.

The other two samples are about `Stream` and `Cell` — things that happen, and values that change
when they do. This one is about the third: a value that is defined at *every* instant rather than
between one change and the next. Time is the obvious example, and motion is what you get when you
build on it.

Inspired by the Bounce example in [Sodium](https://github.com/SodiumFRP/sodium), which SodaFlow is
derived from.

## Three scenes, smallest first

| Tab | What it adds |
| --- | --- |
| **One ball** | A single ball on one axis. The whole idea, with nothing else in the way. |
| **Several balls** | Four balls, each a pair of independent axes, bouncing off walls as well as the floor. |
| **Grab and throw** | Drag a ball and let go. Its position switches between the pointer's and its own flight. |

The last two share a checkbox and a slider setting what a bounce does to a ball's speed. The first
is deliberately elastic, so that the smallest scene stays the smallest thing that makes the point.

## The idea

A ball's height is not a number that something keeps updating. It is an equation:

```csharp
public double PositionAt(double time)
{
    double dt = time - this.StartTime;
    return this.Position + (this.Velocity * dt) + (0.5 * this.Acceleration * dt * dt);
}
```

Applied to `ITimerSystem<double>.Time`, that is a `Behavior<double>` — a position defined at every
instant, not at the instants a frame happened to land on.

Two things follow, and they are the point of the sample.

**The bounce is solved, not detected.** Nothing tests whether a ball has gone past an edge. The
moment it reaches one is a root of the quadratic, computed in advance and scheduled with `At`:

```csharp
Cell<Maybe<double>> nextBounce = flight.Map(f => NextBounceTime(f, min, max));

return timers.At(nextBounce)
    .Snapshot(flight, (time, f) => Reflect(f, time, min, max))
    .Hold(initial);
```

So a bounce cannot be missed by a frame arriving late, or skipped by one that never arrives.

**The graph feeds itself.** The next bounce is a function of the current flight, and the bounce
produces the next flight. `Cell.Loop` is what lets that be written as the circle it is — with no
sink, and nothing that has to remember to send anything.

**Switching is how a ball changes what it is following.** Each bounce replaces the equation, and
`SwitchB` flattens the cell of equations back into one continuous position:

```csharp
flight.Map(f => timers.Time.Map(f.PositionAt)).SwitchB()
```

The third scene uses the same operator for something else entirely — while a ball is held, its
position *is* the pointer's:

```csharp
isHeld.Map(h => h ? pointerX : freeX).SwitchB()
```

## Drawing it

The views hold no positions and accumulate nothing. Once a frame they ask where things are:

```csharp
IReadOnlyList<(double X, double Y)> positions = scene.SamplePositions();
```

One transaction for the frame, so every ball is read as of the same instant. Sampling is also the
only way to read a behavior — there is no `Listen` and no `Updates`, because there is no discrete
sequence of moments to subscribe to.

That makes the frame rate a question about smoothness and nothing else. The Avalonia head redraws
on a timer and the WPF head on `CompositionTarget.Rendering`; slow either one down and it is the
same motion sampled less often. Stall the thread and the balls are wherever they should be when it
resumes, rather than behind by however long it was stuck.

Which is why the balls need no disposal: no part of a view subscribes to them, so there is nothing
to release. The view model is still `IDisposable`, because the selection is not a behavior - it is
an ordinary cell behind an ordinary bindable property, and those do hold subscriptions.

## Where to look

| File | |
| --- | --- |
| [`Flight.cs`](SodaFlow.Samples.Bounce.ViewModels/Flight.cs) | The equation. All of the physics. |
| [`BouncingAxis.cs`](SodaFlow.Samples.Bounce.ViewModels/BouncingAxis.cs) | The feedback loop, the solved bounce, and the switch. Read this one. |
| [`GrabScene.cs`](SodaFlow.Samples.Bounce.ViewModels/GrabScene.cs) | Switching driven by input instead of by physics. |
| [`SceneView.cs`](SodaFlow.Samples.Bounce.Avalonia/SceneView.cs) | Sampling to draw, in about a hundred lines. |
| [`IBounceViewModel.cs`](SodaFlow.Samples.Bounce.ViewModels/IBounceViewModel.cs) | What the views bind to. |
| [`BounceViewModel.cs`](SodaFlow.Samples.Bounce.ViewModels/BounceViewModel.cs) | The shared clock, and the selection as a value rather than as control state. |

## The selection is a value

Which scene is showing is bound two-way, so it belongs to the view model rather than to the tab
control:

```xml
<TabControl ItemsSource="{Binding Scenes}"
            SelectedItem="{Binding SelectedScene.Value, Mode=TwoWay}">
```

That buys both directions with one thing. The summary above the tabs is a function of the
selection, so nothing has to notice a tab change and go and update a label; and setting
`SelectedScene.Value` changes which tab is showing, which is the same write a click performs.

There is no `Show(scene)` method beside it, and that is the point rather than an omission. A second
way to set the selection would be a second thing to keep in step with the first, which is the habit
this library exists to make unnecessary. Values that change are bindable values; things that happen
are bindable actions; neither wants a method next to it doing the same job procedurally.

## The data context is an interface

Both windows bind against
[`IBounceViewModel`](SodaFlow.Samples.Bounce.ViewModels/IBounceViewModel.cs) rather than the class:

```xml
x:DataType="viewModels:IBounceViewModel"                          <!-- Avalonia -->
d:DataContext="{d:DesignInstance viewModels:IBounceViewModel}"    <!-- WPF -->
```

XAML binds by name against whatever the data context happens to be, so saying what that is turns
the bound members into something a reader, a designer and the compiler can all find. It also keeps
the interface honest about what a view is entitled to: `Create` is not on it, because building a
view model is not something a view does with one.

## Damping, and why it needs rules at both ends

A checkbox and a slider set what a bounce multiplies the speed by, from 0.1 to 1.1 — below one a
ball loses speed and stops, at one it bounces forever, above one it gains speed and climbs to the
ceiling. One value, shared: the several-balls scene alone reads it from eight axes, and none of
them holds a copy or has to be told when it changes.

The multiplier is a `Cell<double>` read at the moment of each bounce, so moving the slider changes
the next bounce rather than the flight already under way. Turning the checkbox off is the same as a
multiplier of one, and that is what the graph says rather than making the bounce ask two questions:

```csharp
Cell<double> restitution = dampingEnabled.Lift(damping, (enabled, value) => enabled ? value : 1.0);
```

Damping cannot be only a multiplier, though, and this is the part worth reading `Reflect` for. Each
bounce is smaller than the last, and the interval to the next one shrinks just as fast, without ever
reaching zero — so solving for each bounce in turn means scheduling infinitely many of them in
finite time, and the ball never arrives at sitting still. The answer is a speed below which a bounce
becomes a stop:

```csharp
return Math.Abs(velocity) < RestSpeed && canRest
    ? new Flight(startTime: time, position: bound, velocity: 0.0, acceleration: 0.0)
    : ...
```

`canRest` is the other half. A ball stops against the floor, or on an axis with no acceleration at
all; a slow ball at the *ceiling* is not at rest, it is about to fall. A resting flight has no
velocity and no acceleration, so `NextBounceTime` finds nothing and the alarm disarms itself — the
ball costs nothing until something moves it.

Above one has the mirror-image problem, and it is worth knowing that it bites. The speed grows at
every bounce, so the bounces get closer together without limit, and once they are closer together
than the timer can service them the flight in force is older than the moment being drawn — the ball
is drawn wherever an equation it should have stopped following puts it, which is off the screen and
then off by millions of pixels. So there is a speed the gaining stops at, exactly as there is a
speed the losing stops at.

The rest speed is why a damped scene needs a way back, and the way back is the tab. A scene starts
again when its tab becomes the selected one, which is a fact about the selection rather than
something a view has to remember to call:

```csharp
activated.Loop(selected.Updates().Map(scene => Array.IndexOf(scenes, scene)));
```

That reaches each scene as `restarts`, the same input a throw arrives on in the grab scene — a
throw and a fresh start being the same kind of thing, a flight imposed from outside. `Updates` and
not the cell itself, so the scene showing at startup is not restarted the moment it is built.

The selection cannot exist until the scenes do, and the scenes want the stream, so the stream is
looped: `Stream.CreateLoop<int>()` declares it, and it is defined once the selection is there. The
same trick as `Cell.Loop` in `BouncingAxis`, for the same reason.

## A note on the physics

Balls do not collide with each other. Each is two independent axes, which is what keeps the graph
small enough to read.
