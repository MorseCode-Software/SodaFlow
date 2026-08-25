---
title: Getting started
---

# Getting started

## Install

Most people want one package. If you write C#, that is `SodiumFRP`; if you write F#, it is
`SodiumFRP.FSharp`. Both pull in the core engine as a dependency.

# [C#](#tab/csharp)

```bash
dotnet add package SodiumFRP
```

# [F#](#tab/fsharp)

```bash
dotnet add package SodiumFRP.FSharp
```

---

If you are not sure — or you have seen `SodiumFRP.Core` and wondered what it is for — read
[Which package do I install?](packages.md).

## Your first program

A `StreamSink` is a stream you can push values into from ordinary imperative code. `Hold`
turns a stream into a cell that remembers the most recent value. `Listen` subscribes.

# [C#](#tab/csharp)

```csharp
using System;
using Sodium.Frp;

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
open Sodium.Frp

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
`Sodium.Frp.StreamExtensionMethods` and friends. `using Sodium.Frp;` brings them all into
scope. Construction goes through static factories: `Stream.CreateSink<T>()`,
`Cell.CreateSink(initial)`, `Cell.Constant(value)`.

**F# offers two spellings of everything.** There are qualified module functions —
`Stream.map`, `Cell.listen`, `StreamSink.send` — and a set of suffixed aliases in an
`[<AutoOpen>]` module, so `open Sodium.Frp` alone gives you `mapS`, `listenC`, `sendS`,
`sinkS`. The suffix names the type the function operates on: `S` for stream, `C` for cell,
`B` for behavior, `L` for listener. They are the same functions; pick whichever reads better
and stay consistent.

## Next

- [Core concepts](concepts.md) — what streams, cells, behaviors and transactions actually mean.
- [Listener lifetimes](lifetimes.md) — when to hold a listener and when to dispose it.
- [Asynchronous work](async.md) — running tasks from a stream without breaking the model.
- [Cookbook](cookbook.md) — complete worked examples.
