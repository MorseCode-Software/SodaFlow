4.0.0

No API change of its own. This release moves to SodaFlow.Async.Core 4.x
and SodaFlow.FSharp 4.x, and is a major because taking it obliges a
consumer to take those.

Nothing in this package's own code changed but its formatting.

3.0.0

No code change. This release exists to move a dependency, and is a major
version because of what moving it does to a consumer.

Dependencies between these packages are now declared as ranges bounded at
the next major, so NuGet refuses a pairing which would fail rather than
resolving it and leaving the failure until the code runs. This package now
requires SodaFlow.FSharp 3.x and SodaFlow.Async.Core 3.x.

That ceiling is why this is not a minor version. Taking this release obliges
a consumer to take those majors as well, and one who names SodaFlow.FSharp
directly, or who uses anything removed there, cannot adopt it without
changing their own code. A version they cannot adopt is not a minor one.

2.1.0

Now depends on SodaFlow.FSharp, the F# API it extends.

mapAsync operates on a Stream<'T>, and only those operations produce or consume
one usefully, so a consumer always needed both. Declaring it means one install
brings the whole surface instead of the second package being discovered the
first time someone tries to build a graph.

No API change, and nothing to do when upgrading from 2.0.0 beyond taking it.

---

About this package

Runs Task-based work from a stream: each firing starts an operation, successes
go to one sink and failures to another, and a concurrency strategy decides what
runs when.

  parallelStrategy       every firing starts at once
  queueStrategy          one at a time, in order
  switchLatestStrategy   a new firing supersedes whatever is in flight
  queuePerGroupStrategy  one independent queue per key

F# has neither overloading nor optional parameters on let-bound functions, so
what C# spells as nine MapAsync overloads is four named functions here:
mapAsync, mapAsyncWithInputConverter, mapAsyncWithResultConverter and
mapAsyncWithConverters. They differ only in how this call's own types reach the
types the strategy is written against. Cancellation arguments are explicit
rather than defaulted - pass None, None and true for the common case.

Each returns an AsyncMapStatus<'TInput>: IsRunning is a Cell<bool> true while at
least one invocation is actually running, Items lists everything tracked with
its status, and disposing it tears the pipeline down.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
