---
title: Transactions
---

# Transactions

The transaction is the reason to use Sodium rather than an event bus or a stream of callbacks.
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

Sodium does not produce it. `label` fires once, with `"2 -> 4"`.

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

```csharp
Transaction.RunVoid(() =>
{
    firstName.Send("Ada");
    lastName.Send("Lovelace");
});
```

Anything lifted from both cells now fires exactly once, with both new values. This matters
whenever a pair of values must never be observed half-changed — coordinates, ranges,
credentials, a selection and its context.

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

### Building a graph that must be observed from its first moment

`Values` only delivers its initial firing inside the transaction in which it was obtained, and
loops must be created and closed inside one transaction. Both require `Transaction.Run`.

### Reading a consistent snapshot

`Sample` reads a cell's current value immediately. Several `Sample` calls outside a transaction
can straddle an update and give you an inconsistent picture; inside one, they cannot.

## `Post` and `OnStart`

`Transaction.Post(action)` defers work until after the current transaction closes — or runs it
immediately if there is no current transaction. Use it for side effects that must not run
inside the propagation: touching UI, writing to disk, sending into another sink.

Sending into a sink from inside a listener is the classic mistake. The XML docs on `Listen` say
it directly: neither `StreamSinkExtensionMethods.Send` nor `CellSinkExtensionMethods.Send` may
be called from inside a handler. If you need that, `Post` it.

```csharp
IListener l = s.Listen(v => Transaction.Post(() => other.Send(v)));
```

`Transaction.OnStart(action)` runs an action whenever any transaction begins. It is a
diagnostic and instrumentation hook — counting transactions, logging, asserting invariants in
tests — not something application logic should depend on.

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
