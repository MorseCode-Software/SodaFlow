# SodaFlow

Functional Reactive Programming for .NET, in C# and F#.

SodaFlow gives you two composable primitives: `Stream` for discrete events and
`Cell` for values that change over time.  It also provides a transaction system that
guarantees every derived value updates atomically and in dependency order.  This means that there are no
glitches, no ordering bugs, and no manual subscription bookkeeping.

## Packages

| Package | Contents |
| --- | --- |
| `SodaFlow` | The C# API. Start here. |
| `SodaFlow.FSharp` | The idiomatic F# API. |
| `SodaFlow.Core` | Shared engine both language APIs build on. |
| `SodaFlow.Functional` | `Maybe`, `Either`, and `Unit` used across the API. |
| `SodaFlow.Async` | `async`/`Task` integration for C#. |
| `SodaFlow.FSharp.Async` | `async`/`Task` integration for F#. |
| `SodaFlow.Async.Core` | Shared engine for the async packages. |

Libraries target `net472`, `net60`, and `netstandard2.0`.

## Building

```
dotnet build src/SodaFlow.sln
dotnet test  src/CSharp/SodaFlow.Tests/SodaFlow.Tests.csproj
```

## Documentation

Reference documentation is generated with DocFX from `docs/`:

```
dotnet tool install -g docfx
docfx docs/docfx.json --serve
```

## Licence and attribution

SodaFlow is BSD 3-Clause licensed. See [LICENSE](LICENSE).

SodaFlow is derived from [Sodium](https://github.com/SodiumFRP/sodium), the
Functional Reactive Programming library by Stephen Blackheath and Anthony
Jones, and began as that project's .NET implementation. It retains the full
commit history of that implementation, back to the original C# port in 2014.

SodaFlow implements Sodium's **denotational semantics** unchanged. The formal
specification and its executable Haskell reference are vendored under
`denotational/` (Copyright (c) 2015 Stephen Blackheath, separately licensed —
see `denotational/LICENSE`), and `DenotationalSemanticsTests` asserts
conformance with 23 tests in C# and 23 in F#.

Sodium is Copyright (c) 2012-2015 Stephen Blackheath and Anthony Jones. The
BSD 3-Clause licence covering it applies to SodaFlow as well, and the LICENSE
file must be retained in all source and binary redistributions. See
[NOTICE](NOTICE) for the full derivation and third-party components.

SodaFlow is not endorsed by, affiliated with, or sponsored by the Sodium
authors or project.

If you want the theory rather than the API, Blackheath and Jones' book
[*Functional Reactive Programming*](https://www.manning.com/books/functional-reactive-programming)
(Manning) is the long-form treatment, and its concepts map directly onto this
library.
