# Bounce

Balls bouncing in a box, as an argument for `Behavior<T>`.

The other two samples are about `Stream` and `Cell` — things that happen, and values that change
when they do. This one is about the third: a value that is defined at *every* instant rather than
between one change and the next. Time is the obvious example, and motion is what you get when you
build on it.

Inspired by the Bounce example in [Sodium](https://github.com/SodiumFRP/sodium), which SodaFlow is
derived from.

## Four scenes, smallest first

| Tab | What it adds |
| --- | --- |
| **One ball** | A single ball on one axis. The whole idea, with nothing else in the way. |
| **Several balls** | Four balls, each a pair of independent axes, bouncing off walls as well as the floor. |
| **Grab and throw** | Drag a ball and let go. Its position switches between the pointer's and its own flight. |
| **Ricochets** | The balls hit each other. The one scene where the axes are not independent. |

All but the first share a checkbox and a slider setting what a bounce does to a ball's speed. The
first is deliberately elastic, so that the smallest scene stays the smallest thing that makes the
point.

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
| [`CollisionScene.cs`](SodaFlow.Samples.Bounce.ViewModels/CollisionScene.cs) | One cell for the whole world, advanced event by event. The only coupled scene. |
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
    ? new Flight(StartTime: time, Position: bound, Velocity: 0.0, Acceleration: 0.0)
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

## Ricochets, and the one thing that couples

Every other scene is built out of things that do not interact. A ball is two axes that know
nothing of each other, and no ball knows of any other, which is what lets each one be a small
independent graph. A collision breaks both at once: it couples the two axes of two balls at a
single instant.

So this scene holds one cell containing every ball, rather than a cell per axis, and advances it
event by event — solve for the earliest thing that happens next, jump to it, apply it, solve
again. Between events each ball is still a plain equation, which is what the views sample.

**The collision is still solved, not detected.** Nothing steps time forward looking for overlap.
The moment two balls touch is a root of a quadratic, computed ahead and scheduled with `At`,
exactly as a wall bounce is — so two balls cannot pass through each other because a frame arrived
late.

That the moment is a *quadratic* is worth a sentence, because it is not obvious and it is what
makes the scene tractable. Each ball is curving under its own acceleration, so the position of
either one is quadratic in time and their separation looks like it should be worse. But every
ball has the *same* acceleration, so it cancels out of the difference: relative to each other they
move in straight lines, and "when are these two exactly touching" is

```
|dp + dv t| = r1 + r2
```

Give one ball a different acceleration and this scene needs a different solver — which is a
thing it could have, and the section after next is about why it does not.

## What the impact does

The standard elastic result. Only the component along the line joining the centers changes, and
the tangential component is left alone — which is what makes a glancing blow glance rather than
stop, and means the outgoing angles come from the geometry rather than from anything written down.

Mass is the radius squared: the balls are discs of one density, and area is what a disc has. It is
the ratio that shows. A big ball meeting a small one barely changes course while the small one
comes back hard; two equal balls meeting head on simply trade velocities.

Undamped, momentum and kinetic energy both survive an impact. Damped, momentum still does and
energy does not — and that asymmetry is not a choice, it is what the two conservation laws are.
The two impulses in a collision are equal and opposite by construction, so a ball can only take
momentum from another ball by giving it up itself, however much of the energy the impact throws
away. A wall is different: it is bolted to the world, so it can absorb momentum without appearing
to have any.

The slider reaches both kinds of impact — a wall, and one ball against another.

## Which is why nothing here is allowed to rest

Damping means a ball eventually runs out of bounce, and in the other scenes that ends with it
stopping — velocity zero, acceleration zero. This scene cannot do that, and the reason is
the pairwise solve above. A resting ball has a different acceleration from a falling one, their
separation stops being a straight line, and the quadratic that says when two balls touch becomes a
quartic. **Every ball has to share one acceleration or none of it works.**

So a ball that has damped away to nothing keeps bouncing, at a floor speed low enough that the
bounce is 0.22px high and lands every 44ms. A fifth of a pixel rounds away in nearly every frame:
what a settled ball in this scene looks like is a ball at rest, and what it is is a ball bouncing
too little to see. It also answers Zeno, which is the other job resting does elsewhere — the
interval between floor bounces stops shrinking rather than closing up forever.

How small that bounce can be is a straight trade against how much the clock is asked to do. The
amplitude falls with the *square* of the speed while the event rate only rises with it, so shrinking
it is cheap in appearance and linear in cost: four resting balls take 6.2s of processor over a 78s
run at 40px/s, 10.4s at 20, and 14.2s at 12.

Sideways needs none of this. That axis has no acceleration to begin with, so a ball that damps to a
horizontal standstill still matches its neighbors and costs nothing.

## Why not just solve the quartic

The obvious answer to a constraint like that is to drop it: let a resting ball keep its own
acceleration and solve the harder equation. The solve is not what stands in the way, and it is
worth being exact about that, because its cost is easy to overestimate.

Contact becomes `|dp + dv t + ½ da t²| = r1 + r2`, and squaring it gives a quartic in `t`. Measured
against the quadratic it would replace, it costs 128ns per pair against 10ns — twelve times as much
of very little. Six pairs at a thousand steps a second, which is the most a pile of balls can
force, is 0.08% of one core. It holds its accuracy too, sorted by how squarely the pair meets: the
radial share of the closing speed at contact, where one is head on and a thousandth is the graze a
settled ball is nearly always struck with.

| radial share of the closing speed | worst error over 10,000 contacts | contacts missed |
| --- | --- | --- |
| 1 (head on) | none | 0% |
| 0.01 | 2.1µs | 0% |
| 0.001 (a graze) | 17.4µs | 0% |

How it is solved is the part that matters. Not by the closed form: a quartic solved in radicals
loses most of its digits on the near-double roots a grazing contact produces, and around a resting
pile grazing contacts are most of them. Differentiating gives a cubic, which is far better behaved,
and the roots of that cubic cut the horizon into intervals on which the quartic is monotone — so
each interval holds at most one root, and a bracketed Newton cannot run away inside it. That is
where the numbers above come from. It is also worth noticing that sharing an acceleration is a
*pairwise* requirement rather than a global one: two resting balls agree with each other and two
falling balls agree with each other, so only a mixed pair would ever pay for the quartic at all.

What stands in the way is everything around the solve. Resting is a property of a contact rather
than of a ball — a ball rests only while something holds it up — so allowing it needs a rule for
waking as much as a rule for stopping. Against the floor that is easy, because the floor never
moves: a resting ball wakes when something hits it, and that is an event this scene already
computes. One layer up it stops being easy. A ball that comes to rest on *another ball* is held up
by something that can move, and whether it is held up at all depends on what is holding up the ball
beneath it. The forces in a stack are not decided pair by pair, and that is the point where a
demonstration turns into a physics engine. A second problem waits behind that one: nothing here
models friction or rolling, so a ball balanced on top of another would simply sit there.

Zeno would move rather than leave, as well. Resting on the floor is what would kill the endless
floor bounce, which is the whole prize — but a ball settling onto another ball has the same
infinite sequence of ever smaller impacts, now against a curved surface that is itself moving,
where coming back at a minimum speed has no equivalent that stays stable.

Set against all that, what it buys is small. The bounce it would remove is already too small to
see. The visible artifact that remains is the one described next, which comes from when the alarm
arrives rather than from how the contact was solved, so a better solve does not touch it. What is
left is the processor time above, and a paragraph of explanation.

A bounded version would work — rest allowed against the floor and never on another ball, so no
stack ever has to be reasoned about, and a ball that would have settled on another keeps its
minimum bounce as it does today. It is left undone on purpose. The quadratic is part of what this
scene is for: the sentence about a shared acceleration is what makes the event-driven solve worth
reading, and a scene that needed a root isolator to explain itself would demonstrate less than one
that needs the quadratic formula.

## The one thing the solve cannot fix

At the fastest impacts the balls visibly overlap before springing apart, and can briefly leave the
box altogether — 57px for one frame at the worst, measured over seventy-eight seconds — and it is
worth knowing that this is not the collision being computed loosely.

The moment of contact is exact. What is not instantaneous is being *told* about it: an alarm is a
wait on a real clock, and it fires a few milliseconds late. Until it does, both balls are still
following the equations they already had, so they carry on closing. The error is closing speed
multiplied by that lateness, and it corrects itself the moment the alarm arrives — the impact is
still resolved at the instant it truly happened, not at the instant the alarm ran, so nothing
downstream of it is wrong.

It shows here more than at a wall in the other scenes for a simple reason: two balls can close on
each other faster than either one approaches a wall. Gravity supplies most of that speed, which is
the one lever available — a scene with less of it, or none, shows the artifact proportionally less.

## A note on the physics

Balls do not collide with each other in the first three scenes. Each is two independent axes,
which is what keeps those graphs small enough to read. The fourth gives that up deliberately, and
is worth reading against the others for what it costs: one cell for the whole world instead of one
per axis, and a step that has to consider every pair.
