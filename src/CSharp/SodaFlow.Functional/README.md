# SodaFlow.Functional

`Maybe<T>`, `Either<T1, T2>`, and `Unit`, the small functional vocabulary that the C# API of
SodaFlow uses and C# doesn't ship with. It has no functional reactive programming in it, and it can
be used on its own. SodaFlow depends on it, so a C# project rarely installs it directly. F# has
`option`, `Result`, and `unit`, so the F# packages don't use it.

```bash
dotnet add package SodaFlow.Functional
```

## Documentation

- [Maybe, Either and Unit](https://morsecode-software.github.io/SodaFlow/docs/functional.html)
- [Which package do I install?](https://morsecode-software.github.io/SodaFlow/docs/packages.html)
- [API reference](https://morsecode-software.github.io/SodaFlow/api/index.html)

The source is at <https://github.com/MorseCode-Software/SodaFlow>, under the BSD 3-Clause license.
