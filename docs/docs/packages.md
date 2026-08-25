---
title: Which package do I install?
---

# Which package do I install?

The .NET build publishes seven NuGet packages. Only two of them are things most people
install directly.

## The short answer

| You write | Install | And that is it |
| --- | --- | --- |
| C# | `SodiumFRP` | Pulls in `SodiumFRP.Core` and `Sodium.Functional` automatically. |
| F# | `SodiumFRP.FSharp` | Pulls in `SodiumFRP.Core` automatically. |

Add an async package **only** if you need to run `Task`-based work from a stream — see
[Asynchronous work](async.md).

| You also need | Install |
| --- | --- |
| `MapAsync` in C# | `SodiumFRP.Async` |
| `MapAsync` in F# | `SodiumFRP.FSharp.Async` |

## The full list

| Package | Assembly | Install directly? |
| --- | --- | --- |
| `SodiumFRP` | `Sodium.Frp` | **Yes**, for C#. |
| `SodiumFRP.FSharp` | `Sodium.FSharp.Frp` | **Yes**, for F#. |
| `SodiumFRP.Core` | `Sodium.Core.Frp` | No — a dependency of both of the above. |
| `Sodium.Functional` | `Sodium.Functional` | Rarely — a dependency of `SodiumFRP`. |
| `SodiumFRP.Async` | `Sodium.Frp.Async` | Only if you need `MapAsync` in C#. |
| `SodiumFRP.FSharp.Async` | `Sodium.FSharp.Frp.Async` | Only if you need `MapAsync` in F#. |
| `SodiumFRP.Async.Core` | `Sodium.Core.Frp.Async` | No — a dependency of both async packages. |

## Why `SodiumFRP` and `SodiumFRP.Core` are separate

`SodiumFRP.Core` is the engine. It declares the types you actually hold — `Stream<T>`,
`Cell<T>`, `Behavior<T>`, `Transaction`, the listener interfaces — along with the transaction
machinery and dependency graph that make updates atomic. It is language-neutral.

`SodiumFRP` and `SodiumFRP.FSharp` are thin language surfaces over that engine. C# gets
extension methods and static factory classes; F# gets modules of curried, pipeline-friendly
functions. Both are wrappers around the *same* core types, which is why a `Stream<T>` created
in a C# library can be consumed by F# code and vice versa.

You never reference `SodiumFRP.Core` directly, because you would get types with no usable
operations on them — the core exposes its real work through `internal` members that the
wrappers reach via `InternalsVisibleTo`.

`Sodium.Functional` is a separate concern: it holds `Maybe<T>`, `Either<T1,T2>` and `Unit`,
the small functional vocabulary the C# API needs and C# does not ship with. It has no FRP in
it and can be used on its own. F# already has `option`, `Result` and `unit`, which is why
`SodiumFRP.FSharp` does not depend on it.

## Versioning

Each package versions independently from its own git tag prefix, via
[MinVer](https://github.com/adamralph/minver). Pushing `dotnet-async-csharp-1.1.0` releases
`SodiumFRP.Async` at 1.1.0 and leaves every other package exactly where its own last tag put
it. Do not expect the seven version numbers to move together — they are not meant to.

## Supported frameworks

Every package multi-targets `net472`, `net6.0` and `netstandard2.0`.
