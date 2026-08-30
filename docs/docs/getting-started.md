---
title: Getting started
---

# Getting started

## Install

Most people want one package. If you write C#, that is `SodaFlow`; if you write F#, it is
`SodaFlow.FSharp`. Both pull in the core engine as a dependency.

# [C#](#tab/csharp)

```bash
dotnet add package SodaFlow
```

# [F#](#tab/fsharp)

```bash
dotnet add package SodaFlow.FSharp
```

---

If you are not sure — or you have seen `SodaFlow.Core` and wondered what it is for — read
[Which package do I install?](packages.md).

## Your first program

A `StreamSink` is a stream you can push values into from ordinary imperative code. `Hold`
turns a stream into a cell that remembers the most recent value. `Listen` subscribes.

# [C#](#tab/csharp)

```csharp
using System;
using SodaFlow;

StreamSink<int> s = Stream.CreateSink<int>();
Cell<int> c = s.Hold(0);
IListener l = c.Listen(Console.WriteLine);

s.Send(2);
s.Send(9);

l.Unlisten();
```

Output:

```
0
2
9
```

# [F#](#tab/fsharp)

```fsharp
open SodaFlow

let s = sinkS<int> ()
let c = s |> holdS 0
let l = c |> listenC (printfn "%d")

s |> sendS 2
s |> sendS 9

l |> unlistenL
```

Output:

```
0
2
9
```

---

Note the `0` before `2`. `Listen` on a cell fires immediately with the cell's current value,
then again on every change. Listening to a *stream* does not do this — a stream has no
"current value" to report.

## The shape of the API

The two languages present the same model very differently, and it is worth knowing which
you are reading before you copy an example.

**C# leans on extension methods.** `Stream<T>` and `Cell<T>` themselves carry almost nothing;
`Map`, `Hold`, `Snapshot`, `Filter`, `Merge`, `Listen` and the rest are extension methods in
`SodaFlow.StreamExtensionMethods` and friends. `using SodaFlow;` brings them all into
scope. Construction goes through static factories: `Stream.CreateSink<T>()`,
`Cell.CreateSink(initial)`, `Cell.Constant(value)`.

**F# offers two spellings of everything.** There are qualified module functions —
`Stream.map`, `Cell.listen`, `StreamSink.send` — and a set of suffixed aliases in an
`[<AutoOpen>]` module, so `open SodaFlow` alone gives you `mapS`, `listenC`, `sendS`,
`sinkS`. The suffix names the type the function operates on: `S` for stream, `C` for cell,
`B` for behavior, `L` for listener. They are the same functions; pick whichever reads better
and stay consistent.

## Build your graph inside a transaction

The example above is small enough not to care, but as soon as a graph is more than a couple of
operations, construct it — and attach its listeners — inside a single `Transaction.Run`:

```csharp
Cell<Report> report = Transaction.Run(() =>
{
    // build the graph, close any loops, attach listeners
    ...
});
```

Built outside one, a graph is built across a series of implicit transactions, one per
operation, and a firing that occurs between two of them is simply lost. `Values` makes this
easy to hit: the stream fires during the transaction in which it was *obtained*, so obtaining
it outside an explicit transaction spends that firing before anything is listening. You still
get every later change, which is what makes the omission easy to miss.

See [Transactions](transactions.md).

## Next

- [Transactions](transactions.md) — why graph construction belongs inside `Transaction.Run`.
- [Core concepts](concepts.md) — what streams, cells, behaviors and transactions actually mean.
- [Operation reference](operations.md) — every operation, in both languages.
- [Cookbook](cookbook.md) — recipes for common problems.
- [Listener lifetimes](lifetimes.md) — when to hold a listener and when to dispose it.
- [Asynchronous work](async.md) — running tasks from a stream without breaking the model.
- [Data binding](bindable.md) — exposing a graph to XAML as properties and commands.
- [Sample applications](samples.md) — two full WPF and Avalonia apps to read end to end.
