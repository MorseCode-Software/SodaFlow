---
title: Which package do I install?
---

# Which package do I install?

The .NET build publishes seven NuGet packages. Only two of them are things most people
install directly.

## The short answer

| You write | Install | And that is it |
| --- | --- | --- |
| C# | `SodaFlow` | Pulls in `SodaFlow.Core` and `SodaFlow.Functional` automatically. |
| F# | `SodaFlow.FSharp` | Pulls in `SodaFlow.Core` automatically. |

Add an async package **only** if you need to run `Task`-based work from a stream — see
[Asynchronous work](async.md).

| You also need | Install |
| --- | --- |
| `MapAsync` in C# | `SodaFlow.Async` |
| `MapAsync` in F# | `SodaFlow.FSharp.Async` |

## The full list

| Package | Assembly | Install directly? |
| --- | --- | --- |
| `SodaFlow` | `SodaFlow` | **Yes**, for C#. |
| `SodaFlow.FSharp` | `SodaFlow.FSharp` | **Yes**, for F#. |
| `SodaFlow.Core` | `SodaFlow.Core` | No — a dependency of both of the above. |
| `SodaFlow.Functional` | `SodaFlow.Functional` | Rarely — a dependency of `SodaFlow`. |
| `SodaFlow.Async` | `SodaFlow.Async` | Only if you need `MapAsync` in C#. |
| `SodaFlow.FSharp.Async` | `SodaFlow.FSharp.Async` | Only if you need `MapAsync` in F#. |
| `SodaFlow.Async.Core` | `SodaFlow.Core.Async` | No — a dependency of both async packages. |

## Why `SodaFlow` and `SodaFlow.Core` are separate

`SodaFlow.Core` is the engine. It declares the types you actually hold — `Stream<T>`,
`Cell<T>`, `Behavior<T>`, `Transaction`, the listener interfaces — along with the transaction
machinery and dependency graph that make updates atomic. It is language-neutral.

`SodaFlow` and `SodaFlow.FSharp` are thin language surfaces over that engine. C# gets
extension methods and static factory classes; F# gets modules of curried, pipeline-friendly
functions. Both are wrappers around the *same* core types, which is why a `Stream<T>` created
in a C# library can be consumed by F# code and vice versa.

You never reference `SodaFlow.Core` directly, because you would get types with no usable
operations on them — the core exposes its real work through `internal` members that the
wrappers reach via `InternalsVisibleTo`.

`SodaFlow.Functional` is a separate concern: it holds `Maybe<T>`, `Either<T1,T2>` and `Unit`,
the small functional vocabulary the C# API needs and C# does not ship with. It has no FRP in
it and can be used on its own. F# already has `option`, `Result` and `unit`, which is why
`SodaFlow.FSharp` does not depend on it.

## Versioning

Each package versions independently from its own git tag prefix, via
[MinVer](https://github.com/adamralph/minver). Pushing `dotnet-async-csharp-1.1.0` releases
`SodaFlow.Async` at 1.1.0 and leaves every other package exactly where its own last tag put
it. Do not expect the seven version numbers to move together — they are not meant to.

## Supported frameworks

Every package multi-targets `net472`, `net6.0` and `netstandard2.0`.
