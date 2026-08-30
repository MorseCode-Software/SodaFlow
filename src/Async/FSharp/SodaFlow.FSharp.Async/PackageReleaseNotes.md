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
