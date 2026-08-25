---
title: Core concepts
---

# Core concepts

SodaFlow has a small vocabulary. Almost everything you build is a composition of four ideas, and
the whole library follows once you have them.

## Stream

A `Stream<T>` is a sequence of discrete events, each carrying a value of type `T`. It has no
value between firings — "what is this stream's value right now?" is not a meaningful question.
Button clicks, keystrokes, network responses, and timer expiries are streams.

Create one with `Stream.Never<T>()`, which fires nothing, or `Stream.CreateSink<T>()`, which
you push into from imperative code with `Send`.

## Cell

A `Cell<T>` holds a value that changes at discrete points in time. Unlike a stream it always
has a current value, so `Listen` on a cell fires immediately with that value and then again on
every change. Cells are what you bind UI to.

Create one with `Cell.Constant(value)`, `Cell.CreateSink(initial)`, or — most often — by
calling `Hold` on a stream. `Hold` is the bridge from the discrete world to the stateful one:

```csharp
Cell<int> count = clicks.Accum(0, (_, n) => n + 1).Hold(0);
```

## Behavior

A `Behavior<T>` is a value defined at *every* point in time, not just at discrete steps. Cells
and behaviors are two views of the same underlying idea: `Cell` is the discrete, stepwise one
and `Behavior` the continuous one.

The API surfaces this asymmetry deliberately. A cell has `Updates`, `Values`, and `Listen`; a
behavior has none of them. You cannot ask a behavior when it changed, because a continuous
value does not have a well-defined set of change moments — and being able to detect them would
break the model's guarantees. `c.AsBehavior()` converts freely in the direction that discards
information; going the other way requires the `Operational` primitives and their caveats.

If you are unsure which to reach for, use `Cell`. Reach for `Behavior` when the value genuinely
is continuous — [time](time.md) being the canonical case.

## Transaction

Everything happens inside a transaction, and this is the property that makes SodaFlow worth
using. Within one transaction the entire dependency graph updates **atomically**: no listener
observes a half-updated world, and a value derived from two sources that both changed
simultaneously is computed once, from the new value of both.

That is the "no glitches" guarantee, and it is the thing an event bus cannot give you.
[Transactions](transactions.md) covers what it buys you, when you need an explicit one, and
what simultaneity means.

## How they fit together

```
imperative code
      │  Send
      ▼
  StreamSink ──────────────┐
      │                    │
      │ Map, Filter,       │
      │ Merge, Snapshot    │
      ▼                    │
   Stream ── Hold ──────► Cell ── AsBehavior ──► Behavior
      │                    │                        │
      │ Listen             │ Listen, Lift, Calm     │ Sample, Lift
      ▼                    ▼                        ▼
  side effects         UI binding              continuous values
```

Streams carry events, `Hold` turns them into state, and `Snapshot` reads that state back when
the next event arrives. Almost every SodaFlow program is that cycle, sometimes closed into a
[feedback loop](loops.md).

## Where to go next

| Topic | Page |
| --- | --- |
| Every operation, C# and F# side by side | [Operation reference](operations.md) |
| Atomicity, simultaneity, `Post` | [Transactions](transactions.md) |
| Values that depend on their own past | [Feedback loops](loops.md) |
| Graphs whose shape changes at runtime | [Switch](switch.md) |
| Clocks, alarms, deterministic tests | [Time and timers](time.md) |
| Subscriptions and garbage collection | [Listener lifetimes](lifetimes.md) |
| `Maybe`, `Either`, `Unit` | [SodaFlow.Functional](functional.md) |
