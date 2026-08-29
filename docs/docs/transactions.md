---
title: Transactions
---

# Transactions

The transaction is the reason to use SodaFlow rather than an event bus or a stream of callbacks.
Everything else in the library is in service of this one property.

## The guarantee

Within a transaction, the entire dependency graph updates **atomically**. No listener ever
observes a partially-updated world, and a value derived from two sources that both changed
simultaneously is computed once, from the new value of both.

The failure mode this eliminates is called a *glitch*. Consider a value derived from two others
that always move together:

```csharp
CellSink<int> x = Cell.CreateSink(1);
Cell<int> doubled = x.Map(v => v * 2);
Cell<string> label = x.Lift(doubled, (a, b) => $"{a} -> {b}");
```

In a naive propagation system, sending `2` into `x` might briefly produce `"2 -> 2"` — the new
`x` combined with the stale `doubled` — before settling on `"2 -> 4"`. That intermediate state
never logically existed, but a listener would see it, and code written against it would be
subtly wrong in ways that only show up under load.

SodaFlow does not produce it. `label` fires once, with `"2 -> 4"`.

This is not achieved by ordering listeners carefully. It comes from the dependency graph: each
node has a rank, updates propagate in rank order within the transaction, and nothing fires
until everything it depends on has settled.

## When transactions start

Sending into a sink starts one implicitly and closes it when the send returns. Most of the
time you never think about this.

You need an **explicit** transaction in three situations.

### Making several sends atomic

Two sends means two transactions, and downstream sees two separate updates:

```csharp
firstName.Send("Ada");     // downstream fires
lastName.Send("Lovelace"); // downstream fires again
```

Wrap them, and downstream sees one:

# [C#](#tab/csharp)

```csharp
Transaction.RunVoid(() =>
{
    firstName.Send("Ada");
    lastName.Send("Lovelace");
});
```

# [F#](#tab/fsharp)

```fsharp
runT (fun () ->
    firstName |> sendC "Ada"
    lastName |> sendC "Lovelace")
```

---

Anything lifted from both cells now fires exactly once, with both new values. This matters
whenever a pair of values must never be observed half-changed — coordinates, ranges,
credentials, a selection and its context.

### Building a graph that must be observed from its first moment

Build the graph **and attach its listeners inside one `Transaction.Run`**. A graph assembled
outside a transaction is assembled across a series of implicit ones — every operation opening
and closing its own — and any firing that happens between two of them has nowhere to land.

`Values` is where this bites hardest, because a `Values` stream fires during the transaction in
which it was **obtained**, not when it is listened to. Getting the stream is itself enough to
spend that firing:

```csharp
// Wrong: obtaining Values opened an implicit transaction and the initial
// firing happened inside it, before anything was listening.
Stream<int> v = c.Values();
IListener l = v.ListenStrong(Console.WriteLine);   // current value already missed
```

Obtain it and subscribe in the same transaction, and the firing has somewhere to go:

```csharp
// Right.
IListener l = Transaction.Run(() => c.Values().ListenStrong(Console.WriteLine));
```

The failure is silent — you still receive every later change, so the stream looks like it works
and merely skipped its first value. Every `Values` test in the suite is wrapped this way.

The same reasoning applies to a graph built in pieces. Wrap the whole construction, including
any `Hold` whose initial value comes from a `Values`, and any listeners you want attached from
the start:

```csharp
Cell<IReadOnlyList<Item>> items = Transaction.Run(() =>
{
    // build the graph, close any loops, attach listeners
    ...
});
```

Transactions are globally serialized, so doing it this way also closes the window between
construction and subscription in which another thread could send something you would never see.

Loops are the other case that requires an explicit transaction: one must be created and closed
inside a single transaction, which is exactly what the functional form (`Stream.Loop`,
`Cell.Loop`, `Behavior.Loop`) handles for you.

If all you need is "the current value, then every change", `ListenStrong` on a cell already delivers
the current value first and needs none of this.

### Reading a consistent snapshot

`Sample` reads a cell's current value immediately. Several `Sample` calls outside a transaction
can straddle an update and give you an inconsistent picture; inside one, they cannot.

## `Post` and `OnStart`

`Transaction.Post(action)` defers work until after the current transaction closes — or runs it
immediately if there is no current transaction. Use it for side effects that must not run
inside the propagation: touching UI, writing to disk, sending into another sink.

Sending into a sink from inside a listener is the classic mistake. The XML docs on `ListenStrong` say
it directly: neither `StreamSinkExtensionMethods.Send` nor `CellSinkExtensionMethods.Send` may
be called from inside a handler. If you need that, `Post` it.

```csharp
IListener l = s.ListenStrong(v => Transaction.Post(() => other.Send(v)));
```

`Transaction.OnStart(action)` runs an action whenever any transaction begins. Its stated
purpose is implementing a time or alarm system, and that is what `TimerSystem` uses it for:
the hook is where a clock advances itself in step with transactions. An action registered here
may start transactions of its own without the hooks recursing. Instrumentation is a reasonable
secondary use, but this is a low-level integration point rather than a debugging aid.

`Transaction.IsActive()` reports whether a transaction is currently running, which is
occasionally useful in library code that must behave differently depending on its caller.

## Simultaneity

Two streams fire "simultaneously" when they fire in the same transaction. This is common when
both derive from a shared source.

`Merge` requires you to say what happens then — its second argument combines the two values:

```csharp
Stream<int> both = a.Merge(b, (x, y) => x + y);
```

`OrElse` is the shorthand for "take the left one and drop the right":

```csharp
Stream<int> either = a.OrElse(b);
```

There is no version that fires twice. Two firings in one transaction is precisely what the
model forbids, and being forced to state the resolution is the point rather than an
inconvenience — it surfaces a decision you were making implicitly anyway.

The same reasoning explains the `coalesce` argument on `Stream.CreateSink`. If your imperative
code might send twice within one transaction, that overload says how to combine them:

```csharp
StreamSink<int> s = Stream.CreateSink<int>((older, newer) => newer);
```

Without it, sending twice in one transaction is an error rather than a silent last-write-wins.
