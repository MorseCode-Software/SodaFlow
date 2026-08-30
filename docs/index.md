---
title: SodaFlow for .NET
---

# SodaFlow for .NET

SodaFlow is a Functional Reactive Programming library. It gives you two composable
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

SodaFlow is based on [Sodium](https://github.com/SodiumFRP/sodium), the Functional Reactive
Programming library by Stephen Blackheath and Anthony Jones, and began as that project's .NET
implementation. The model documented here — streams, cells, behaviors, and transactions — is
Sodium's — including its [denotational semantics](docs/semantics.md), which SodaFlow
implements unchanged — and the credit for it belongs there. Sodium's implementations for Java, Scala, C++,
Kotlin, TypeScript, and Rust live under the
[SodiumFRP organization](https://github.com/SodiumFRP).

## Start here

- [Getting started](docs/getting-started.md) — install a package and run your first program.
- [Which package do I install?](docs/packages.md) — there are ten; this page picks one for you.
- [Core concepts](docs/concepts.md) — streams, cells, behaviors, and transactions.
- [Operation reference](docs/operations.md) — every operation, C# and F# side by side.
- [Cookbook](docs/cookbook.md) — recipes for the things that come up constantly.
- [Sample applications](docs/samples.md) — two full WPF and Avalonia apps over a shared view model.
- [API reference](api/index.md) — generated from the source.

## Going deeper

- [Transactions](docs/transactions.md) — atomicity, simultaneity, and why glitches cannot happen.
- [Feedback loops](docs/loops.md) — values that depend on their own past.
- [Switch and dynamic graphs](docs/switch.md) — changing the graph's shape at runtime.
- [Listener lifetimes](docs/lifetimes.md) — the most common way to break a SodaFlow program.
- [Time and timers](docs/time.md) — clocks, alarms, and deterministic tests.
- [Asynchronous work](docs/async.md) — running tasks without breaking the model.
- [Data binding](docs/bindable.md) — exposing a graph to XAML as properties and commands.
- [Denotational semantics](docs/semantics.md) — SodaFlow implements Sodium's formal
  specification, and is tested against it.

## Elsewhere

- **Book** — [*Functional Reactive Programming*](https://www.manning.com/books/functional-reactive-programming)
  (Blackheath & Jones, Manning) is the best reference for both the Sodium model SodaFlow is
  built on and for Functional Reactive Programming in general. Almost everything it teaches
  applies directly here. Its worked examples live in
  [`book/`](https://github.com/SodiumFRP/sodium/tree/master/book) in C#, F#, and Java.
- **Issues** — <https://github.com/MorseCode-Software/SodaFlow/issues>
- **Sodium** — the upstream project SodaFlow is based on:
  <https://github.com/SodiumFRP/sodium>. Its user forum, which covers the shared model rather
  than SodaFlow specifically, is at <https://sodiumfrp.discourse.group/>.
