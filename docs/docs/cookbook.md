---
title: Cookbook
---

# Cookbook

Recipes for things that come up constantly. Each one is short enough to read in full and
adapt.

## Ignore repeated values

`Calm` drops a firing whose value equals the one before it, so downstream work only happens on a
real change:

```csharp
Stream<string> meaningful = keystrokes.Calm();
```

It compares with `EqualityComparer<T>.Default`; overloads take an `IEqualityComparer<T>` or a
plain `Func<T, T, bool>` when default equality is not what you want.

`Calm` is about *values*, not about *timing* — it is not a debounce. Typing `ab`, deleting the
`b`, and typing it again produces `ab`, `a`, `ab`, all of which get through. To rate-limit a
fast source, gate or schedule it with a [timer](time.md) instead.

## Parse input, ignoring failures

`Choose` expresses "compute something that might not work, and only fire when it did" in one
step:

```csharp
Stream<int> numbers = input.Choose(s => s.TryParseInt32());
```

`TryParseInt32` is one of the [parsing helpers](functional.md) on `string`, which answer with a
`Maybe<T>` instead of a `bool` and an `out` parameter. Written out by hand the selector would be
`s => int.TryParse(s, out int n) ? Maybe.Some(n) : Maybe.None`.

`Map` followed by `FilterSome` is the same thing spelled out, and is what to reach for when the
intermediate `Stream<Maybe<T>>` is wanted for its own sake:

```csharp
Stream<int> numbers = input
    .Map(s => s.TryParseInt32())
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

`Accum` is a loop with the plumbing already done. It folds a stream into a **cell**, so there is
no `Hold` to add afterwards:

```csharp
Cell<int> total = amounts.Accum(0, static (v, acc) => v + acc);
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
`ListenOnceAsync` gives you a `Task<T>` you can `await`. Hold the listener `ListenOnce`
returns until the firing arrives — it is weak, so nothing else keeps your handler alive — or
call `ListenOnceStrong`, which roots the stream until then. See
[Listener lifetimes](lifetimes.md).

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
    operation: async (q, factory, token) => factory.FromValue(await SearchAsync(q, token)),
    strategy: AsyncConcurrencyStrategy.SwitchLatest());

Cell<bool> spinner = status.IsRunning;
```

`Calm` first so identical queries do not re-fire; `SwitchLatest` so only the newest request
survives. See [Asynchronous work](async.md).

## Keep a large list on screen

A cell holding a list rebuilds the whole list on every edit, which is right for a search result —
one answer that changes as a whole — and wrong for a hundred thousand accounts behind a page of
six. A `ReactiveCollection` is the other shape: one stage per question, each keeping itself
current rather than being rebuilt.

```csharp
ReactiveCollection<int, AccountIdentity, AccountState> accounts =
    ReactiveCollection.Create(initialEntries: seed, deposits);

// One stage per question. Each keeps itself current as edits arrive.
ReactiveCollection<int, AccountIdentity, AccountState> page = accounts
    .Filter(static (_, state) => !state.IsFrozen)
    .SortBy(static (identity, _) => identity.Number)
    .Slice(offsetCell: offset, limitCell: Cell.Constant(PageSize));

// One row object per key on the page, each bound to its own item.
MappedItems<AccountRowViewModel> rows = page.Map(
    project: key => new AccountRowViewModel(
        number: page.IdentityCell(key)
            .Map(static identity => identity.Match(i => i.Number, () => string.Empty))
            .ToOneWay(),
        balance: page.StateCell(key)
            .Map(static state => state.Match(s => s.Balance, () => 0L))
            .ToOneWay()),
    onEvicted: static row => row.Dispose());
```

Bind the view to `rows.Items`. The important part is that each row's bindings are built from
`IdentityCell` and `StateCell` *inside* the projection, so every row follows its own item. That
is what makes an edit reach the row it is about rather than the list: a deposit moves one
balance while the other rows on the page, and the list itself, stay put.

Both cells answer with a `Maybe<T>`, which is why each projection has a `Match` in it. A row can
outlive the item it is bound to — the key leaves the view, the cell stops having a value, and an
add under the same key later gives it one again — so the empty case is a state the row has to
render rather than an error.

Two things decide whether this pays. An item is two halves — an identity that cannot change and a
state that can — so a sort over the identity can never be disturbed by an edit to the state,
which is why the example sorts on `identity.Number`. And `Map` ends the chain rather than
continuing it: what comes back is objects, which have no identity or state left for a further
`Filter` or `SortBy` to work on.

Each criteria is a cell where it needs to move — `Filter` takes a criteria cell, `SortBy` an
order cell, `Slice` an offset cell — so turning the page or clicking a header re-files the stage
already there instead of building a second chain to choose between.

See [Reactive collections](collections.md) for what each stage costs, and the
[Accounts sample](samples.md) for this chain with a UI on it.

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
