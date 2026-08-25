---
title: F# and the API reference
---

# F# and the API reference

There is no generated API reference for `SodaFlow.FSharp` on this site. That is deliberate,
and it is worth explaining, because the F# surface is a first-class part of this library rather
than an afterthought.

## Why not

Two reasons, and the second is the blocking one.

**DocFX reads compiled metadata.** Pointed at an F# assembly it renders the *compiled* shape,
not the F# one: modules appear as static classes, curried functions appear as methods taking
`FSharpFunc<_,_>`, and idiomatic pipelines are nowhere to be seen. The result actively misleads
an F# reader.

**The assembly carries no documentation comments.** `SodaFlow.FSharp` has
`GenerateDocumentationFile` enabled, but not one of its source files contains a `///` comment,
so the XML it produces is empty. There is currently nothing to render.

## What to read instead

The F# surface is covered in the conceptual pages, which show C# and F# side by side —
start with [Getting started](getting-started.md). Beyond that, the source is short and
readable: fourteen files under `src/FSharp/SodaFlow.FSharp`.

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

## What it would take to fix this

1. Write `///` comments across `SodaFlow.FSharp`. This is the real work, and it is
   unavoidable whichever tool renders the result — it also improves IntelliSense for every F#
   consumer today, independently of this site.
2. Generate with [`fsdocs`](https://fsprojects.github.io/FSharp.Formatting/) rather than DocFX,
   and publish it under its own path.

Step 1 is worth doing on its own merits. Step 2 only makes sense afterwards.
