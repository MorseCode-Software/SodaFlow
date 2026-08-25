---
title: Feedback loops
---

# Feedback loops

Some values depend on their own past. A running total, a state machine, a position updated by
velocity — each needs its previous value to compute its next one, which is a circular reference
that ordinary construction cannot express: you would have to pass the value to itself before it
exists.

Loops resolve this with a forward declaration. Declare a placeholder, build the graph that
refers to it, then say what it was a reference to.

## The functional form

This is the idiom to use. `Stream.Loop<T>()`, `Cell.Loop<T>()` and `Behavior.Loop<T>()` hand
you the placeholder, take back the definition, and close the loop for you — including the
explicit transaction it needs.

# [C#](#tab/csharp)

```csharp
StreamSink<int> s = Stream.CreateSink<int>();

// A running total: each firing adds to the total so far.
Stream<int> total = Stream.Loop<int>()
    .WithoutCaptures(l => s.Snapshot(l.Hold(0), (n, o) => n + o));

using (total.Listen(Console.WriteLine))
{
    s.Send(1);   // 1
    s.Send(2);   // 3
    s.Send(3);   // 6
}
```

# [F#](#tab/fsharp)

```fsharp
let s = sinkS<int> ()

// A running total: each firing adds to the total so far.
let total =
    loopWithNoCapturesS (fun l -> s |> snapshotC (l |> holdS 0) (+))

use _ = total |> listenS (printfn "%d")
s |> sendS 1   // 1
s |> sendS 2   // 3
s |> sendS 3   // 6
```

---

Read the C# version inside out: `l` is the placeholder for the very stream being defined,
`l.Hold(0)` turns it into a cell of the running total starting at zero, and each firing of `s`
snapshots that cell and adds to it. The result *is* `l`, which is what `WithoutCaptures`
resolves.

## Capturing extra values

Sometimes the loop body produces something else you want alongside the looped value.
`WithCaptures` returns both:

```csharp
StreamSink<int> s = Stream.CreateSink<int>();

(Stream<int> total, Stream<int> doubled) = Stream.Loop<int>()
    .WithCaptures(l => (
        Stream: s.Snapshot(l.Hold(0), (n, o) => n + o),
        Captures: s.Map(v => 2 * v)));
```

`Stream` is what the loop resolves to; `Captures` is anything else you want out. In F# the
equivalent is `loopS`, which returns a struct tuple of the looped value and the captures, while
`loopWithNoCapturesS` is the shorthand when there is nothing extra.

## The explicit form

`StreamLoop<T>`, `CellLoop<T>` and `BehaviorLoop<T>` are the underlying mechanism. You will
meet them in older code and in the book's examples:

```csharp
Transaction.RunVoid(() =>
{
    StreamLoop<int> l = Stream.CreateLoop<int>();
    Stream<int> total = s.Snapshot(l.Hold(0), (n, o) => n + o);
    l.Loop(total);
});
```

Three rules apply, and the library enforces all three with exceptions:

| Mistake | Message |
| --- | --- |
| Creating a loop outside a transaction | `Loop must be created within an explicit transaction.` |
| Never calling `Loop` | `Loop was not looped.` |
| Calling `Loop` twice | `Loop was looped more than once.` |
| Closing it in a different transaction | `Loop must be looped in the same transaction that it was created in.` |

The functional form exists because it makes all four impossible. Prefer it; use the explicit
form only when the functional one genuinely cannot express what you need.

## `Sample` inside a loop

Calling `Sample` on a looped cell while still constructing the loop asks for a value that does
not exist yet. Use `SampleLazy` (`sampleLazyC`) instead — it defers the read until the loop has
closed and there is something to read.

The same reasoning is why `Hold` has a `HoldLazy` counterpart: when a loop's initial value
itself depends on the loop, it must be deferred.

## Where this shows up

Accumulators are the common case, and `Accum` and `Collect` are loops with the plumbing already
done:

```csharp
Stream<int> total   = s.Accum(0, (v, acc) => v + acc);
Stream<string> outp = s.Collect(0, (v, st) => (ReturnValue: $"#{st}", State: st + 1));
```

Reach for those first. Write the loop by hand when the feedback path runs through logic that
`Accum` and `Collect` cannot express — typically when it passes through other cells, or through
a `Switch`.
