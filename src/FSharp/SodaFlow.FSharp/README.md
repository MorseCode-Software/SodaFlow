# SodaFlow.FSharp

SodaFlow is a functional reactive programming library for .NET. It gives you `Stream` for
discrete events, `Cell` for values that change over time, and `Behavior` for values defined at
every instant, with transactions that update every derived value atomically and in dependency
order.

This is the package to install from F#. It holds modules of curried functions that fit a
pipeline, over the same types of SodaFlow.Core that the C# API uses, so a stream made in C# can be
used from F# and the other way around. It pulls in SodaFlow.Core. It uses the `option`, `Result`,
and `unit` of F#, so it does not need SodaFlow.Functional.

```bash
dotnet add package SodaFlow.FSharp
```

## Documentation

- [Getting started](https://morsecode-software.github.io/SodaFlow/docs/getting-started.html)
- [F# and the API reference](https://morsecode-software.github.io/SodaFlow/docs/fsharp-api.html)
- [Core concepts](https://morsecode-software.github.io/SodaFlow/docs/concepts.html)
- [Which package do I install?](https://morsecode-software.github.io/SodaFlow/docs/packages.html)
- [API reference](https://morsecode-software.github.io/SodaFlow/api/index.html)

The source is at <https://github.com/MorseCode-Software/SodaFlow>, under the BSD 3-Clause license.
