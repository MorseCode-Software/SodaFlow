---
title: F# and the API reference
---

# F# and the API reference

There is no generated API reference for `SodaFlow.FSharp` on this site. That is deliberate,
and it is worth explaining, because the F# surface is a first-class part of this library rather
than an afterthought.

## Why not

**DocFX reads compiled metadata.** Pointed at an F# assembly it renders the *compiled* shape,
not the F# one: modules appear as static classes, curried functions appear as methods taking
`FSharpFunc<_,_>`, and idiomatic pipelines are nowhere to be seen. The result actively misleads
an F# reader.

The renderer is the whole reason, not a lack of comments. `SodaFlow.FSharp` and `SodaFlow.FSharp.Async` are fully documented
with `///` comments, so the XML they emit is complete — every module, type and function carries a
summary along with its parameters, return value and remarks. Your editor shows all of it today;
what is missing is a renderer that presents the F# surface as F#.

## What to read instead

The F# surface is covered in the conceptual pages, which show C# and F# side by side —
start with [Getting started](getting-started.md). Beyond that, the source is short, documented and
readable: one file per type under `src/FSharp/SodaFlow.FSharp`.

Two spellings exist for every operation:

```fsharp
// Qualified module functions.
let c = s |> Stream.hold 0
let l = c |> Cell.listen (printfn "%d")

// Suffixed aliases, from the [<AutoOpen>] module in Shorthand.fs.
let c = s |> holdS 0
let l = c |> listenC (printfn "%d")
```

The suffix names the type operated on — `S` stream, `C` cell, `B` behavior, `L` listener,
`T` transaction. `Shorthand.fs` is the single file listing every alias, and it doubles as the best
available index of the F# API.
