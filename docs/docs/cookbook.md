---
title: Cookbook
---

# Cookbook

Recipes for things that come up constantly. Each one is short enough to read in full and
adapt.

## Debounce user input

Suppress firings that are equal to the previous one, so downstream work only happens on real
changes:

```csharp
Stream<string> meaningful = keystrokes.Calm();
```

`Calm` compares with `EqualityComparer<T>.Default` by default; overloads take an
`IEqualityComparer<T>` or a plain `Func<T, T, bool>` when default equality is not what you
want.

## Parse input, ignoring failures

`Choose` expresses "compute something that might not work, and only fire when it did" in one
step:

```csharp
Stream<int> numbers = input.Choose(s => int.TryParse(s, out int n) ? Maybe.Some(n) : Maybe.None);
```

`Map` followed by `FilterSome` is the same thing spelled out, and is what to reach for when the
intermediate `Stream<Maybe<T>>` is wanted for its own sake:

```csharp
Stream<int> numbers = input
    .Map(s => int.TryParse(s, out int n) ? Maybe.Some(n) : Maybe.None)
    .FilterSome();
```

## Enable a control conditionally

`Gate` drops firings while a `Cell<bool>` is false — no branching, no shape change:

```csharp
Cell<bool> canSubmit = form.Map(f => f.IsValid);
Stream<Unit> submits = clicks.Gate(canSubmit);
```

## Combine several values into one

`Lift` for a fixed set:

```csharp
Cell<string> summary = firstName.Lift(lastName, (f, l) => $"{f} {l}");
```

`Lift` on a collection when the set is uniform:

```csharp
Cell<IReadOnlyList<int>> allValues = cells.Lift();
Cell<int> total = allValues.Map(vs => vs.Sum());
```

## Keep a running total

`Accum` is a loop with the plumbing already done:

```csharp
Stream<int> runningTotal = amounts.Accum(0, (v, acc) => v + acc);
Cell<int> total = runningTotal.Hold(0);
```

When each firing needs to emit something *different* from the state it carries, `Collect` is
the general form:

```csharp
// Emit a sequence number with each event.
Stream<string> numbered = events.Collect(
    1,
    (e, n) => (ReturnValue: $"{n}: {e}", State: n + 1));
```

## Update two values atomically

Two sends are two transactions, and downstream sees two updates. Wrap them so it sees one:

```csharp
Transaction.RunVoid(() =>
{
    x.Send(newX);
    y.Send(newY);
});
```

Anything lifted from both now fires exactly once, with both new values. See
[Transactions](transactions.md).

## Take the first of several sources

```csharp
Stream<Command> commands = new[] { fromKeyboard, fromMouse, fromNetwork }.OrElse();
```

`OrElse` is left-biased on simultaneity. If two can fire in the same transaction and you need
both values, use `Merge` with a combining function instead.

## Fire once, then never again

```csharp
Stream<Unit> firstLoad = dataArrived.Once();
```

For the imperative side of the same idea, `ListenOnce` unsubscribes itself, and
`ListenOnceAsync` gives you a `Task<T>` you can `await`.

## Time-stamp events

```csharp
SecondsTimerSystem timers = new SecondsTimerSystem(ex => Log.Error(ex));
Stream<double> clickTimes = clicks.Snapshot(timers.Time, (_, t) => t);
```

See [Time and timers](time.md) for alarms and deterministic testing.

## Search-as-you-type

The canonical async case: each keystroke cancels the in-flight request.

```csharp
StreamSink<SearchResult> results = Stream.CreateSink<SearchResult>();
StreamSink<Exception> errors = Stream.CreateSink<Exception>();

AsyncMapStatus<string> status = queries.Calm().MapAsync(
    results: results,
    errors: errors,
    operation: (q, token) => SearchAsync(q, token),
    strategy: AsyncConcurrencyStrategy.SwitchLatest());

Cell<bool> spinner = status.IsRunning;
```

`Calm` first so identical queries do not re-fire; `SwitchLatest` so only the newest request
survives. See [Asynchronous work](async.md).

## Longer worked examples

The [book](https://www.manning.com/books/functional-reactive-programming) (Blackheath & Jones)
is the long-form treatment of this model, and is worth reading both for the Sodium basics
SodaFlow inherits and for Functional Reactive Programming generally. Its worked examples live in
the upstream [Sodium repository](https://github.com/SodiumFRP/sodium) under
[`book/`](https://github.com/SodiumFRP/sodium/tree/master/book), most of them in C# and F# as
well as Java. They are written against Sodium's API, so the namespaces and package names differ
from SodaFlow's, but the model and the operation names carry over directly — they remain the
best available long-form examples, and they compile.

| Example | Source | Demonstrates |
| --- | --- | --- |
| Petrol pump | [`book/petrol-pump`](https://github.com/SodiumFRP/sodium/tree/master/book/petrol-pump) | The flagship example: a complete state machine driving real UI. |
| Fridgets | [`book/fridgets`](https://github.com/SodiumFRP/sodium/tree/master/book/fridgets) | Composable widgets built entirely from streams and cells. |
| Patterns | [`book/patterns`](https://github.com/SodiumFRP/sodium/tree/master/book/patterns) | Small, focused examples of recurring FRP patterns. |
| Operational | [`book/operational`](https://github.com/SodiumFRP/sodium/tree/master/book/operational) | Correct use of the `Operational` primitives. |
| Continuous time | [`book/continuous-time`](https://github.com/SodiumFRP/sodium/tree/master/book/continuous-time) | Where `Behavior` earns its place over `Cell`. |
| Battle | [`book/battle`](https://github.com/SodiumFRP/sodium/tree/master/book/battle) | A larger simulation. |
| Real world | [`book/real-world`](https://github.com/SodiumFRP/sodium/tree/master/book/real-world) | Integrating FRP with I/O and existing imperative code. |
