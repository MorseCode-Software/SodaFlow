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

It also orders, with no value sorting before every value — the same order `Nullable<T>` gets from
`Comparer<T>.Default`, so `OrderBy` and `Array.Sort` put the empties first and then sort the rest
by `T`. There are deliberately no `<` and `>` operators to go with it: `Nullable<T>` has them, and
they answer `false` in both directions when either side is absent, so `!(a < b)` stops meaning
`a >= b`.

### Building one

`Maybe.Some(v)` and `Maybe.None` are the two type constructors. A handful of helpers cover the
shapes that otherwise turn into an `if` around them:

| Member | Purpose |
| --- | --- |
| `Maybe.SomeIf(condition, value)` | The value when the condition holds, nothing when it does not. |
| `Maybe.SomeIf(condition, () => value)` | The same, producing the value only if it is wanted. |
| `Maybe.SomeNotNull(reference)` | The reference unless it is `null`. |
| `Maybe.SomeNotNull(nullable)` | The value of a `T?` if it has one. |

`SomeNotNull` is deliberately not what `Some` does. `Some(null)` contains `null` — a present
value that happens to be null — which is what lets `Maybe<string>` tell "no value" apart from
"the value null". `SomeNotNull` is the bridge from the older convention where `null` *is* the
absence.

The same conversions read better at the end of a chain as `value.ToMaybe()`, and `ToNullable()`
converts a `Maybe<T>` of a value type back to `T?`.

### Working with one

| Member | Purpose |
| --- | --- |
| `Map(f)` / `Select(f)` | Transform the value if there is one. |
| `Bind(f)` / `SelectMany(f, g)` | Transform it into another `Maybe<T>`, flattening. |
| `Where(predicate)` | Keep the value only if it satisfies the predicate. |
| `Flatten()` | Collapse a `Maybe<Maybe<T>>`. |
| `ValueOr(fallback)` / `ValueOr(() => fallback)` | The value, or a fallback. |
| `ValueOrDefault()` | The value, or `default(T)`. |
| `ValueOrThrow(() => new ...)` | The value, or throw the exception you name. |
| `OrElse(other)` / `OrElse(() => other)` | This value if present, otherwise another `Maybe<T>`. |
| `Lift(b, f)`, `Lift(b, c, f)`, `Lift(b, c, d, f)` | Combine two, three or four, running `f` only if all are present. |
| `ToEnumerable()` | A sequence of one element, or none. |

`Select`, `SelectMany` and `Where` are the names the compiler looks for, so query syntax works
over a `Maybe<T>` directly:

```csharp
Maybe<int> area = from w in "12".TryParseInt32()
                  from h in "7".TryParseInt32()
                  where w > 0 && h > 0
                  select w * h;
```

`Lift` says the same thing without the query, and matches the `Lift` on `Lazy<T>`:

```csharp
Maybe<int> area = "12".TryParseInt32().Lift("7".TryParseInt32(), (w, h) => w * h);
```

`ValueOrThrow` is the intended escape hatch at a boundary where the absence really is a
failure. It still makes the caller answer for the empty case, by making them say what the
failure is.

### Across an `await`

`Maybe<T>` already had `MatchAsync` for consuming a value asynchronously. `MapAsync`,
`BindAsync` and `WhereAsync` are the composing side, for when the work itself is asynchronous:

```csharp
Maybe<User> user = await id.TryParseInt32().BindAsync(i => repository.FindAsync(i));
```

The same operators also exist on `Task<Maybe<T>>`, so a chain that starts asynchronously can be
continued without awaiting in the middle of it and parenthesizing everything before:

```csharp
Maybe<string> name = await id.TryParseInt32()
    .BindAsync(i => repository.FindAsync(i))
    .Map(u => u.Name)
    .Where(n => n.Length > 0);
```

The `Async` suffix marks the overload whose function returns a task; the unsuffixed ones take a
plain function and only their *subject* is asynchronous. `Match`, `ValueOr`, `ValueOrDefault`,
`ValueOrThrow` and `OrElse` are there on `Task<Maybe<T>>` too, for ending the chain.

Nothing runs on the empty path, and it returns a single cached completed task per type rather
than allocating one per miss.

There is deliberately no conversion from `Maybe<Task<T>>` to `Task<Maybe<T>>`. That shape almost
always means `Map` was used where `MapAsync` was meant, and offering the repair would make the
mistake easier to keep.

### Sequences

| Member | Purpose |
| --- | --- |
| `WhereSome()` | The values from an `IEnumerable<Maybe<T>>` which have one. |
| `Choose(selector)` | Map and filter in one step, keeping what the selector produced. |
| `AllSomeOrNone()` / `AllSomeOrNone(selector)` | All of the values, or nothing if any is missing. |
| `FirstOrNone()`, `LastOrNone()`, `SingleOrNone()`, `ElementAtOrNone(i)` | The LINQ `OrDefault` operators, answering with a `Maybe<T>`. |
| `MinOrNone()`, `MaxOrNone()` | The smallest or largest element, or nothing to compare. Overloads take a comparer or a selector. |
| `AggregateOrNone(f)` | The seedless `Aggregate`, without throwing on an empty sequence. |

```csharp
IEnumerable<int> numbers = lines.Choose(l => l.TryParseInt32());
```

`Choose` keeps whatever it can get; `AllSomeOrNone` fails the whole result if any element
produces nothing. Which you want depends on whether one bad line invalidates the rest.

The `OrNone` operators exist because `FirstOrDefault` over an `IEnumerable<int>` returns zero
both for an empty sequence and for one whose first element is zero, and because `Min` and
seedless `Aggregate` throw outright rather than answer. `SingleOrNone` still throws when there is
more than one element, exactly as `SingleOrDefault` does: that is not a missing answer, it is a
contradicted assumption.

`MinOrNone` and `MaxOrNone` skip `null` elements, as `Min` and `Max` do — `Comparer<T>.Default`
sorts `null` before everything, so otherwise one null element would be the answer for every
sequence of a reference type. A sequence of nothing but nulls has nothing to compare and gives
no value, where LINQ gives `null`.

Every one of these treats a `null` sequence as empty, as the package already did.

### Parsing and lookup

`TryParse` returning a `bool` and an `out` parameter cannot be composed. Each of these wraps the
framework method of the same name and answers with a `Maybe<T>` instead:

```csharp
Maybe<int> port = settings.TryGetValue("port").Bind(s => s.TryParseInt32());
```

* `TryParseByte`, `TryParseSByte`, `TryParseInt16`, `TryParseUInt16`, `TryParseInt32`,
  `TryParseUInt32`, `TryParseInt64`, `TryParseUInt64`, `TryParseSingle`, `TryParseDouble`,
  `TryParseDecimal` — each with an overload taking `NumberStyles` and an `IFormatProvider`.
* `TryParseBoolean`, `TryParseChar`, `TryParseGuid`, `TryParseGuidExact`, `TryParseDateTime`,
  `TryParseDateTimeExact`, `TryParseDateTimeOffset`, `TryParseTimeSpan`, `TryParseUri`.
* `TryParseEnum<TEnum>()` and `TryParseDefinedEnum<TEnum>()`, each with an `ignoreCase`
  overload.
* `TryGetValue(key)` on `IReadOnlyDictionary<TKey, TValue>`.

The overloads which take no `IFormatProvider` use the current culture, because the methods they
wrap do. Pass `CultureInfo.InvariantCulture` for text that is not meant to follow the user's
culture.

`TryParseEnum` inherits a trap from `Enum.TryParse`: a string of digits parses to that number
whether or not the enumeration declares it, so `"37".TryParseEnum<Color>()` succeeds. Use
`TryParseDefinedEnum` for input from outside the program — but not for a `[Flags]` enumeration,
where a combination of declared flags is valid without being declared itself.

### Any other `Try...` method

`Maybe.FromTryGet` adapts anything of that shape, including methods this package has never
heard of:

```csharp
Maybe<int> n = Maybe.FromTryGet<string, int>(text, int.TryParse);
```

Both type arguments have to be written out, because a method group carries no type of its own
for them to be inferred from. There are overloads for methods taking none, one, two or three
inputs ahead of the `out` parameter, and the delegate types they take — `TryGet<TResult>` and
friends — are public, so a method of that shape can be stored and passed around as one.

### Where the FRP API uses it

Two places, and both are worth knowing:

`Stream.FilterMaybe()` turns a `Stream<Maybe<T>>` into a `Stream<T>`, dropping the empties. It
is the idiomatic way to combine "compute something that might fail" with "only fire when it
worked":

```csharp
Stream<int> parsed = input
    .Map(s => s.TryParseInt32())
    .FilterMaybe();
```

`ITimerSystem<T>.At` takes a `Cell<Maybe<T>>`, where `Some` arms an alarm and `None` disarms
it. See [Time and timers](time.md).

The F# equivalent of `FilterMaybe` is `filterOptionS`, and it works on `option` rather than
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

`TryGetFirst()` through `TryGetEighth()` answer with a `Maybe<T>` for one case, `IsFirst()`
through `IsEighth()` test which case is held without reaching the value, and `MapFirst(f)`
through `MapEighth(f)` transform one case and pass the rest through.

`Either<T1, T2>` also has `Swap()`, which exchanges the two cases. It is how you reach the first
case with an operation that only addresses the second:

```csharp
Either<string, string> described = e.Swap().MapSecond(n => n.ToString()).Swap();
```

There is no `Swap` beyond two cases. With three or more there is no single exchange to make, and
naming one of the several reorderings `Swap` would make the others look unavailable rather than
unnamed.

The shape is entirely regular across all eight arities, so the
[API reference](../api/index.md) is the place to go for the full member list.

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
