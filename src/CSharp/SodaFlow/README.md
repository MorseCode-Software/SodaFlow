# SodaFlow

SodaFlow is a functional reactive programming library for .NET. It gives you `Stream` for
discrete events, `Cell` for values that change over time, and `Behavior` for values defined at
every instant, with transactions that update every derived value atomically and in dependency
order.

This is the package to install from C#. It holds the extension methods and static factories
of the C# API, over the types of SodaFlow.Core, and it pulls in SodaFlow.Core and
SodaFlow.Functional. It also carries an analyzer that reports the shapes of a loop that throw at run
time.

```bash
dotnet add package SodaFlow
```

## Documentation

- [Getting started](https://morsecode-software.github.io/SodaFlow/docs/getting-started.html)
- [Core concepts](https://morsecode-software.github.io/SodaFlow/docs/concepts.html)
- [Operation reference](https://morsecode-software.github.io/SodaFlow/docs/operations.html)
- [Which package do I install?](https://morsecode-software.github.io/SodaFlow/docs/packages.html)
- [API reference](https://morsecode-software.github.io/SodaFlow/api/index.html)

The source is at <https://github.com/MorseCode-Software/SodaFlow>, under the BSD 3-Clause license.
