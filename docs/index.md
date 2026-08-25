---
title: Sodium FRP for .NET
---

# Sodium FRP for .NET

Sodium is a Functional Reactive Programming library. It gives you two composable
primitives — a **stream** of discrete events and a **cell** holding a value over time —
and guarantees that anything you build out of them updates *atomically*, with no glitches
and no intermediate states that never logically existed.

```csharp
StreamSink<int> s = Stream.CreateSink<int>();
Cell<int> c = s.Hold(0);
IListener l = c.Listen(Console.WriteLine);

s.Send(2);   // prints 2
s.Send(9);   // prints 9

l.Unlisten();
```

This site documents the **.NET implementation** — the C# and F# packages built from
[`dotnet/src/Sodium`](https://github.com/SodiumFRP/sodium/tree/master/dotnet/src/Sodium).
Sodium also exists for Java, Scala, C++, Kotlin, TypeScript, and Rust; those live in their
own repositories under the [SodiumFRP organisation](https://github.com/SodiumFRP).

## Start here

- [Getting started](docs/getting-started.md) — install a package and run your first program.
- [Which package do I install?](docs/packages.md) — there are seven; this page picks one for you.
- [Core concepts](docs/concepts.md) — streams, cells, behaviors, and transactions.
- [Operation reference](docs/operations.md) — every operation, C# and F# side by side.
- [Cookbook](docs/cookbook.md) — recipes for the things that come up constantly.
- [API reference](api/index.md) — generated from the source.

## Going deeper

- [Transactions](docs/transactions.md) — atomicity, simultaneity, and why glitches cannot happen.
- [Feedback loops](docs/loops.md) — values that depend on their own past.
- [Switch and dynamic graphs](docs/switch.md) — changing the graph's shape at runtime.
- [Listener lifetimes](docs/lifetimes.md) — the most common way to break a Sodium program.
- [Time and timers](docs/time.md) — clocks, alarms, and deterministic tests.
- [Asynchronous work](docs/async.md) — running tasks without breaking the model.

## Elsewhere

- **Book** — [*Functional Reactive Programming*](https://www.manning.com/books/functional-reactive-programming)
  (Blackheath & Jones, Manning) is the long-form treatment. Its worked examples live in
  [`book/`](https://github.com/SodiumFRP/sodium/tree/master/book) in C#, F#, and Java.
- **Forum** — <https://sodiumfrp.discourse.group/>
- **Issues** — <https://github.com/SodiumFRP/sodium/issues>
