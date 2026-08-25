---
title: API Reference
---

# API Reference

These pages are generated from the XML documentation comments in the source, so they track
the code exactly. Pick a namespace from the sidebar, or start with the types you will use most:

| Type | Namespace | What it is |
| --- | --- | --- |
| @Sodium.Frp.Stream`1 | `Sodium.Frp` | A stream of discrete events. |
| @Sodium.Frp.Cell`1 | `Sodium.Frp` | A value that changes at discrete points in time. |
| @Sodium.Frp.Behavior`1 | `Sodium.Frp` | A value defined at every point in time. |
| @Sodium.Frp.Transaction | `Sodium.Frp` | The atomic unit in which all updates happen. |
| @Sodium.Frp.Operational | `Sodium.Frp` | Escape hatches out of the pure model. |

## How the API is split across assemblies

`Sodium.Core.Frp` declares the types you hold — `Stream<T>`, `Cell<T>`, `Behavior<T>` and
friends. `Sodium.Frp` layers the **C#-facing surface** on top of them, and almost all of it is
extension methods: `Map`, `Hold`, `Snapshot`, `Filter`, `Merge`, `Listen` and the rest live in
@Sodium.Frp.StreamExtensionMethods, @Sodium.Frp.CellExtensionMethods and
@Sodium.Frp.BehaviorExtensionMethods rather than on the types themselves.

If you are looking for a method and cannot find it on the type, look in that type's
`...ExtensionMethods` class. See [Which package do I install?](../docs/packages.md) for how
the assemblies map onto NuGet packages.

## F#

There is no generated reference for `SodiumFRP.FSharp` — see
[F# and the API reference](../docs/fsharp-api.md) for why, and what the F# surface looks like
in the meantime.
