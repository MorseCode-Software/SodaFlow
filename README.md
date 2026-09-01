[![Build status](https://ci.appveyor.com/api/projects/status/ydsbwm9udk9yd1mk/branch/main?svg=true)](https://ci.appveyor.com/project/jam40jeff/sodaflow/branch/main)
[![Coverage Status](https://coveralls.io/repos/github/MorseCode-Software/SodaFlow/badge.svg)](https://coveralls.io/github/MorseCode-Software/SodaFlow)
[![Total Downloads](https://img.shields.io/nuget/dt/SodaFlow.Core.svg)](http://www.nuget.org/packages/SodaFlow.Core/)
[![Latest Stable Version](https://img.shields.io/nuget/v/SodaFlow.Core.svg)](http://www.nuget.org/packages/SodaFlow/)

# SodaFlow

Functional Reactive Programming for .NET, in C# and F#.

SodaFlow gives you two composable primitives: `Stream` for discrete events and
`Cell` for values that change over time.  It also provides a transaction system that
guarantees every derived value updates atomically and in dependency order.  This means that there are no
glitches, no ordering bugs, and no manual subscription bookkeeping.

## Packages

| C# Package | F# Package | Contents |
| --- | --- | --- |
| `SodaFlow`<br>[![Latest Stable Version](https://img.shields.io/nuget/v/SodaFlow.svg)](http://www.nuget.org/packages/SodaFlow/) | `SodaFlow.FSharp`<br>[![Latest Stable Version](https://img.shields.io/nuget/v/SodaFlow.FSharp.svg)](http://www.nuget.org/packages/SodaFlow.FSharp/) | The core FRP library. |
| `SodaFlow.Functional`<br>[![Latest Stable Version](https://img.shields.io/nuget/v/SodaFlow.Functional.svg)](http://www.nuget.org/packages/SodaFlow.Functional/) | N/A | `Maybe`, `Either`, and `Unit` used across the C# API. |
| `SodaFlow.Async`<br>[![Latest Stable Version](https://img.shields.io/nuget/v/SodaFlow.Async.svg)](http://www.nuget.org/packages/SodaFlow.Async/) | `SodaFlow.FSharp.Async`<br>[![Latest Stable Version](https://img.shields.io/nuget/v/SodaFlow.FSharp.Async.svg)](http://www.nuget.org/packages/SodaFlow.FSharp.Async/) | `async`/`Task` integration. |
| `SodaFlow.Bindable.ObjectModel`<br>[![Latest Stable Version](https://img.shields.io/nuget/v/SodaFlow.Bindable.ObjectModel.svg)](http://www.nuget.org/packages/SodaFlow.Bindable.ObjectModel/) | `SodaFlow.FSharp.Bindable.ObjectModel`<br>[![Latest Stable Version](https://img.shields.io/nuget/v/SodaFlow.FSharp.Bindable.ObjectModel.svg)](http://www.nuget.org/packages/SodaFlow.FSharp.Bindable.ObjectModel/) | Bindable support for XAML UIs. |

All libraries target `net472`, `net60`, and `netstandard2.0`.

NOTE: `Sodium.Functional` is only needed by the C# libraries, as it provides types that are built-in to F#.  Whereas C# uses `Maybe`, `Either`, and `Unit` from this package,
the F# libraries simply use the built-in types `option`, discriminated unions, and `unit` respectively.

## Building

```
dotnet build src/SodaFlow.slnx
dotnet test  src/CSharp/SodaFlow.Tests/SodaFlow.Tests.csproj
```

## Documentation

Reference documentation is generated with DocFX from `docs/`:

```
dotnet tool install -g docfx
docfx docs/docfx.json --serve
```

## License and attribution

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
BSD 3-Clause license covering it applies to SodaFlow as well, and the LICENSE
file must be retained in all source and binary redistributions. See
[NOTICE](NOTICE) for the full derivation and third-party components.

SodaFlow is not endorsed by, affiliated with, or sponsored by the Sodium
authors or project.

If you want the theory rather than the API, Blackheath and Jones' book
[*Functional Reactive Programming*](https://www.manning.com/books/functional-reactive-programming)
(Manning) is the long-form treatment, and its concepts map directly onto this
library.
