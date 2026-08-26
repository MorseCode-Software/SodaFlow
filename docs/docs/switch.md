---
title: Switch and dynamic graphs
---

# Switch and dynamic graphs

Everything else in SodaFlow builds a graph whose *shape* is fixed once constructed. `Switch` is
what lets the shape change at runtime: a list of items where each row has its own streams, a
wizard where each step wires up differently, a connection that is replaced when it drops.

The idea is simple even though the consequences are not. If you have a cell holding *another*
reactive value, `Switch` flattens it — and the result tracks whichever inner value the outer
cell currently holds.

## The three flavours

| You have | Call | You get |
| --- | --- | --- |
| `Cell<Cell<T>>` | `SwitchC()` | `Cell<T>` following the currently-held cell. |
| `Cell<Stream<T>>` | `SwitchS()` | `Stream<T>` firing whatever the currently-held stream fires. |
| `Cell<Behavior<T>>` | `SwitchB()` | `Behavior<T>` following the currently-held behavior. |

The same three exist on `Behavior<...>` as the outer container. In F# the aliases are
`switchC` / `switchS` / `switchB` for a cell outer, and `switchCB` / `switchSB` / `switchBB`
for a behavior outer.

## A worked example

Say the user picks which of several sensors to display:

# [C#](#tab/csharp)

```csharp
Cell<Sensor> selected = selection.Hold(defaultSensor);

// Each sensor exposes its own Cell<double> of readings.
Cell<Cell<double>> nested = selected.Map(sensor => sensor.Reading);

// Flatten: always the reading of whichever sensor is selected right now.
Cell<double> reading = nested.SwitchC();
```

# [F#](#tab/fsharp)

```fsharp
let selected = selection |> holdS defaultSensor

// Each sensor exposes its own Cell<double> of readings.
let nested = selected |> mapC (fun sensor -> sensor.Reading)

// Flatten: always the reading of whichever sensor is selected right now.
let reading = nested |> switchC
```

---

The `Map` producing a nested cell followed by a `Switch` to flatten it is the standard shape.
If you find yourself with a `Cell<Cell<T>>` and no idea how you got there, this is usually the
answer to what you meant.

## The subtlety in `SwitchS`

`SwitchC` behaves the way intuition suggests. `SwitchS` has one rule that intuition does not
supply, and the XML documentation states it precisely:

> When the cell changes value, the output stream will fire the simultaneous firing (if one
> exists) from the stream which the cell held **at the beginning of the transaction**.

So in the transaction where the switch itself happens, you still get the *old* stream's firing,
not the new one's. The switch takes effect for subsequent transactions.

This is the only choice that preserves the model. A transaction is atomic: within it, the graph
has one consistent shape, decided when the transaction opened. If switching took effect
immediately, the same transaction would have two different graph shapes depending on when you
looked, and the atomicity guarantee would be gone.

Practically: if you switch to a new stream and it fires in that same transaction, you will not
see that firing. You will see it from the next transaction onward.

## Resource lifetime

`Switch` is where FRP graphs start allocating and releasing at runtime. Its own wiring is
handled for you: the subscription to the outer cell and the subscription to the currently
selected inner value are both tied to the lifetime of the stream or cell `Switch` returns, so
they are released when the result is.

What `Switch` does *not* do is unlisten anything you created. A subscription you make inside a
branch follows the ordinary rules — `Listen` roots it in the source's keep-alive set, so it
outlives the branch and keeps firing; `ListenWeak` lives as long as you hold the reference. The
outer cell moving off a branch does not release it. If a subscription should not survive its
branch, its lifetime has to be tied to something that ends with the branch. See
[Listener lifetimes](lifetimes.md).

## When not to reach for it

`Switch` is the most expensive construct in the library, both computationally and in how hard
the resulting code is to follow. Before using it, check whether the graph shape really needs to
change:

- Selecting among a **fixed, known** set of sources? `Lift` them all and pick with a `Map`.
  Simpler, and everything stays statically wired.
- Enabling and disabling a source? `Gate` does that without changing the shape.
- Merging several sources where only one is active at a time? `Merge` or `OrElse` over the
  collection, with the inactive ones gated off.

Reach for `Switch` when the set itself is genuinely dynamic — rows added and removed, sensors
discovered at runtime, connections replaced. That is what it is for, and nothing else does it.
