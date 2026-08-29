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

That is now the only reason. `SodaFlow.FSharp` and `SodaFlow.FSharp.Async` are fully documented
with `///` comments, so the XML they emit is complete — every module, type and function carries a
summary along with its parameters, return value and remarks. Your editor shows all of it today;
what is missing is a renderer that presents the F# surface as F#.

## What to read instead

The F# surface is covered in the conceptual pages, which show C# and F# side by side —
start with [Getting started](getting-started.md). Beyond that, the source is short, documented and
readable: fourteen files under `src/FSharp/SodaFlow.FSharp`.

Two spellings exist for every operation:

```fsharp
// Qualified module functions.
let c = s |> Stream.hold 0
let l = c |> Cell.listenStrong (printfn "%d")

// Suffixed aliases, from the [<AutoOpen>] module in Shorthand.fs.
let c = s |> holdS 0
let l = c |> listenStrongC (printfn "%d")
```

The suffix names the type operated on — `S` stream, `C` cell, `B` behavior, `L` listener,
`T` transaction. `Shorthand.fs` is the single file listing every alias, and it doubles as the best
available index of the F# API.

## What it would take to fix this

Generate with [`fsdocs`](https://fsprojects.github.io/FSharp.Formatting/) rather than DocFX, and
publish it under its own path. `fsdocs` reads F# source and signatures rather than compiled
metadata, so it renders modules as modules and curried functions in their F# form, and it picks up
the same `///` comments already in the source.

The prerequisite — writing those comments — is done. What remains is wiring the generator into the
docs build.
