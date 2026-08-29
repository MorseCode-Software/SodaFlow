---
title: API Reference
---

# API Reference

These pages are generated from the XML documentation comments in the source, so they track
the code exactly. Pick a namespace from the sidebar, or start with the types you will use most:

| Type | Namespace | What it is |
| --- | --- | --- |
| @SodaFlow.Stream`1 | `SodaFlow` | A stream of discrete events. |
| @SodaFlow.Cell`1 | `SodaFlow` | A value that changes at discrete points in time. |
| @SodaFlow.Behavior`1 | `SodaFlow` | A value defined at every point in time. |
| @SodaFlow.Transaction | `SodaFlow` | The atomic unit in which all updates happen. |
| @SodaFlow.Operational | `SodaFlow` | Escape hatches out of the pure model. |

## How the API is split across assemblies

`SodaFlow.Core` declares the types you hold — `Stream<T>`, `Cell<T>`, `Behavior<T>` and
friends. `SodaFlow` layers the **C#-facing surface** on top of them, and almost all of it is
extension methods: `Map`, `Hold`, `Snapshot`, `Filter`, `Merge`, `Listen` and the rest live in
@SodaFlow.StreamExtensionMethods, @SodaFlow.CellExtensionMethods and
@SodaFlow.BehaviorExtensionMethods rather than on the types themselves.

If you are looking for a method and cannot find it on the type, look in that type's
`...ExtensionMethods` class. See [Which package do I install?](../docs/packages.md) for how
the assemblies map onto NuGet packages.

## F#

There is no generated reference for `SodaFlow.FSharp` — see
[F# and the API reference](../docs/fsharp-api.md) for why, and what the F# surface looks like
in the meantime.
