---
title: Core concepts
---

# Core concepts

Sodium has a small vocabulary. Almost everything you build is a composition of four ideas.

## Stream

A `Stream<T>` is a sequence of discrete events, each carrying a value of type `T`. It has no
value between firings — asking "what is this stream's value right now?" is not a meaningful
question. Button clicks, keystrokes, and network responses are streams.

Create one with `Stream.Never<T>()` (fires nothing) or `Stream.CreateSink<T>()` (a stream you
push into from imperative code with `Send`).

The core operations:

| Operation | Meaning |
| --- | --- |
| `Map` | Transform each value. |
| `Filter` | Drop firings that fail a predicate. |
| `Merge` / `OrElse` | Combine two streams. `Merge` takes a function to resolve simultaneous firings; `OrElse` picks the left one. |
| `Hold` | Turn the stream into a cell that remembers the latest value. |
| `Snapshot` | On each firing, sample one or more cells or behaviors and combine. |
| `Gate` | Drop firings while a `Cell<bool>` is false. |
| `Collect` | Fold over the stream, carrying state. |
| `Calm` | Suppress firings equal to the previous one. |

## Cell

A `Cell<T>` holds a value that changes at discrete points in time. Unlike a stream it always
has a current value, so `Listen` on a cell fires immediately with that value and then on every
change.

Create one with `Cell.Constant(value)`, `Cell.CreateSink(initial)`, or — most often — by
calling `Hold` on a stream.

## Behavior

A `Behavior<T>` is a value defined at *every* point in time, not just at discrete steps. Cells
and behaviors are closely related: `Cell` is the discrete, stepwise view and `Behavior` the
continuous one. Most of the API is mirrored across both, and you can convert between them.

If you are unsure which to reach for, use `Cell`. Reach for `Behavior` when you genuinely need
a value that is defined continuously — for example when modelling time itself.

## Transaction

Everything happens inside a transaction, and this is the property that makes Sodium worth
using. Within one transaction the entire dependency graph updates **atomically**: no listener
ever observes a half-updated world, and a value derived from two sources that both changed
simultaneously is computed once, from the new values of both. This is the "no glitches"
guarantee.

Sending into a sink starts a transaction implicitly. To make several sends land in the *same*
transaction — so downstream sees one atomic change rather than several — run them inside
`Transaction.Run`.

`Transaction` also offers `IsActive`, `OnStart` and `Post` for work that must be scheduled
relative to transaction boundaries.

## Loops

Feedback — a cell whose new value depends on its own old value, routed through other logic —
needs a forward declaration. That is what `StreamLoop<T>`, `CellLoop<T>` and `BehaviorLoop<T>`
are for: create the loop, build the graph that refers to it, then close the loop. The
`Stream.Loop` / `Cell.Loop` helpers and their F# equivalents (`loopS`, `loopC`) wrap this in a
single call that hands you the placeholder and takes back the definition.

## Operational

`Operational` holds primitives that deliberately break the model's guarantees:
`Operational.Updates`, `Operational.Value`, `Operational.Split`, `Operational.Defer`.

The source describes them as "OPERATIONAL primitives, which are not part of the main Sodium
API" — they break the non-detectability of behavior steps. The rule stated there is that you
may use them only inside functions that do not let the caller detect those updates. In other
words: they are legitimate building blocks for library code, and a smell in application code.

> [!NOTE]
> This page is a map, not the territory. The per-operation semantics — especially `Snapshot`
> versus `Hold` ordering within a transaction, and the exact behaviour of `Switch` — deserve
> pages of their own with worked examples. Contributions welcome.
