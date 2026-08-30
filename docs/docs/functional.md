---
title: Maybe, Either and Unit
---

# Maybe, Either and Unit

`SodaFlow.Functional` is the small functional vocabulary the C# API needs and C# does not ship
with. It contains no FRP at all and can be used on its own. F# already has `option`, `Result`
and `unit`, which is why `SodaFlow.FSharp` does not depend on it — the F# API uses the
built-in types throughout.

The package ships as `SodaFlow.Functional` and comes in automatically with `SodaFlow`.

## `Maybe<T>`

A value that may or may not be present, without using `null` to say so.

```csharp
using SodaFlow.Functional;

Maybe<int> some = Maybe.Some(42);
Maybe<int> none = Maybe.None;
```

You consume it by matching rather than by testing and unwrapping, so the empty case cannot be
forgotten:

```csharp
string text = value.Match(
    onSome: v => $"got {v}",
    onNone: () => "nothing");
```

The full set:

| Member | Purpose |
| --- | --- |
| `Match(onSome, onNone)` | Both cases, producing a value. |
| `MatchVoid(onSome, onNone)` | Both cases, producing nothing. |
| `MatchSome(onSome)` | Act only when present. |
| `MatchNone(onNone)` | Act only when absent. |
| `MatchAsync`, `MatchAsyncVoid`, `MatchSomeAsync`, `MatchNoneAsync` | The same, returning `Task`. |
| `HasValue()` | A plain boolean test, for when matching is overkill. |

`Maybe<T>` is a struct with value equality and `==` / `!=` defined, and `Maybe.None` converts
implicitly to `Maybe<T>` for any `T`, so you rarely spell out the type argument.

### Where the FRP API uses it

Two places, and both are worth knowing:

`Stream.FilterSome()` turns a `Stream<Maybe<T>>` into a `Stream<T>`, dropping the empties. It
is the idiomatic way to combine "compute something that might fail" with "only fire when it
worked":

```csharp
Stream<int> parsed = input
    .Map(s => int.TryParse(s, out int n) ? Maybe.Some(n) : Maybe.None)
    .FilterSome();
```

`ITimerSystem<T>.At` takes a `Cell<Maybe<T>>`, where `Some` arms an alarm and `None` disarms
it. See [Time and timers](time.md).

`Stream.Choose(f)` is `Map` and `FilterSome` in one step, for when the intermediate
`Stream<Maybe<T>>` is not wanted for its own sake:

```csharp
Stream<int> parsed = input.Choose(s => int.TryParse(s, out int n) ? Maybe.Some(n) : Maybe.None);
```

The F# equivalents are `filterSomeS` and `chooseS`, and they work on `option` rather than
`Maybe`.

## `Either<T1, T2>`

A value that is exactly one of several alternatives. SodaFlow's version goes up to eight:
`Either<T1, T2>` through `Either<T1, ..., T8>`.

```csharp
Either<int, string> e = Either<int, string>.First(42);
```

There are also standalone constructors — `Either.First(v)`, `Either.Second(v)`, on through
`Either.Eighth(v)` — which produce a positional wrapper that converts to whichever `Either`
arity you need, so type inference does the work:

```csharp
Either<int, string> e = Either.First(42);
```

Consumption is by matching, with one function per alternative:

```csharp
string described = e.Match(
    n => $"number {n}",
    s => $"text {s}");
```

Because every case must be supplied, adding an alternative later is a compile error at each
consumption site rather than a silent fallthrough.

> [!NOTE]
> `Either.cs` carries no XML documentation comments — 542 public members across eight arities,
> all undocumented — so its [API reference](../api/index.md) pages render bare. The shape is
> entirely regular, and this page is currently the better description of it.

## `Unit`

A type with exactly one value, `Unit.Value`. It is what you use for a stream that carries no
information beyond the fact that it fired — a button click, a tick, a request to refresh.

```csharp
StreamSink<Unit> clicked = Stream.CreateSink<Unit>();
clicked.Send(Unit.Value);
```

`Stream<Unit>` says "something happened" without inventing a payload nobody reads. It appears
throughout the async API too, where `AsyncConcurrencyStrategy` fixes its unused type parameters
to `Unit` — see [Asynchronous work](async.md).

`MapTo` is the usual way to produce one:

```csharp
Stream<Unit> anyChange = changes.MapTo(Unit.Value);
```

`Unit` has value equality: all instances are equal, and `GetHashCode` returns a constant.
