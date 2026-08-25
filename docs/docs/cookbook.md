---
title: Cookbook
---

# Cookbook

The [book](https://www.manning.com/books/functional-reactive-programming)'s worked examples
live in this repository under [`book/`](https://github.com/SodiumFRP/sodium/tree/master/book),
most of them in C# and F# as well as Java. They are the best available long-form Sodium code,
and they compile.

| Example | Source | What it demonstrates |
| --- | --- | --- |
| Petrol pump | [`book/petrol-pump`](https://github.com/SodiumFRP/sodium/tree/master/book/petrol-pump) | The book's flagship example: a complete state machine driving real UI. |
| Fridgets | [`book/fridgets`](https://github.com/SodiumFRP/sodium/tree/master/book/fridgets) | Building composable widgets entirely out of streams and cells. |
| Patterns | [`book/patterns`](https://github.com/SodiumFRP/sodium/tree/master/book/patterns) | Small, focused examples of recurring FRP patterns. |
| Operational | [`book/operational`](https://github.com/SodiumFRP/sodium/tree/master/book/operational) | Correct use of the `Operational` primitives. |
| Continuous time | [`book/continuous-time`](https://github.com/SodiumFRP/sodium/tree/master/book/continuous-time) | Where `Behavior` earns its place over `Cell`. |
| Battle | [`book/battle`](https://github.com/SodiumFRP/sodium/tree/master/book/battle) | A larger simulation. |
| Real world | [`book/real-world`](https://github.com/SodiumFRP/sodium/tree/master/book/real-world) | Integrating FRP with I/O and existing imperative code. |

> [!NOTE]
> This page is an index into source that lives outside the docs. The intent is to grow it into
> real cookbook *pages* — each one a problem statement, the code inline with C#/F# tabs, and an
> explanation of why it is shaped that way — with the `book/` sources as the raw material.
> Adding one recipe is a well-sized first contribution.
