5.0.0

BREAKING: an operation takes three arguments, where it took two. The second one
is a ResultFactory<'TResult>, and the operation answers with what
that factory makes:

  let operation
      (query: string)
      (factory: ResultFactory<SearchResult>)
      (token: CancellationToken)
      =
      task {
          let! r = searchAsync query token
          return factory.FromValue r
      }

FromValue carries a value the operation has. Construct carries a function
that the pipeline calls in the transaction that sends the result, which is what
a result that holds a cell or a stream needs. Keep such a function short,
because it holds the transaction while it runs, and put no effect in it that
matters outside the value it returns: the pipeline calls it only for an item
that it publishes. A throw from it goes to the errors stream in place of a
result.

BREAKING: a custom strategy takes one more argument in Admit and in
OnCompleted, the queue of the pipeline: each item that it tracks, Queued or
Running, in the sequence of their admissions, with the status of each one. A
strategy that schedules on that sequence alone keeps no queue of its own. In
Admit the list does not hold the value that the pipeline admits now, and in
OnCompleted it still holds the item that ends now. A call of MapAsync with a
strategy from this library needs no edit for this.

BREAKING: mapAsyncWithResultConverter and mapAsyncWithConverters are gone, and
mapAsync and mapAsyncWithInputConverter stay. A strategy no longer reads a
result: OnCompleted takes an AsyncCompletion, which says that the operation
returned, that it threw, with the exception, or that a cancellation stopped it.
The pipeline asks the strategy before it makes the result, and it makes the
result only for an item that it publishes.

The result type of a strategy is gone with those two functions.
AsyncConcurrencyStrategy<'TInput, 'TResult, 'TState> is
AsyncConcurrencyStrategy<'TInput, 'TState> now, and the
AsyncConcurrencyStrategy<'TInput, 'TState> abbreviation of this package, which
was the same type, is not necessary. A call to one of the two functions that
stay needs no edit for this. A custom strategy changes its base type and its
OnCompleted signature.

BREAKING, and the break is in SodaFlow.Async.Core 5.0.0, which this release
takes: the AsyncMapStatus<'TInput> that mapAsync and its siblings return is a
class where it was a readonly struct, and it extends a new non-generic
AsyncMapStatus that carries IsRunning and Dispose. Items stays on the generic
type.

Code that binds the result and reads its members needs no edit. Code compiled
against 4.x does, because a struct and a class are not the same type to the
runtime. A function that only watches IsRunning or disposes the pipeline can
annotate its parameter AsyncMapStatus and leave the input type out.

4.0.1

Adds the package icon that nuget.org shows beside this package. No source file
changed since 4.0.0.

Every package here ships this release together, so the dependency versions
move with it.

4.0.0

No API change of its own. This release moves to SodaFlow.Async.Core 4.x
and SodaFlow.FSharp 4.x, and is a major because taking it obliges a
consumer to take those.

Nothing in this package's own code changed but its formatting.

BREAKING: requires FSharp.Core 11.0.100, where it required 4.5.2. A
consumer still on FSharp.Core 4.x cannot take this release. Nothing in
this package's own code turns on anything that changed between those
versions - it compiles against 11.0.100 unaltered - but the floor is
written into the package, so the requirement is real whether or not the
code exercises it. It moves because the shipping projects and the test
projects now compile against one version of FSharp.Core instead of
disagreeing about it.

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

An operation takes the input, a factory for its answer, and a token, and answers
with factory.FromValue value or factory.Construct (fun () -> ...). The
second one runs in the transaction that publishes, for a result that holds part
of a graph.

Each returns an AsyncMapStatus<'TInput>: IsRunning is a Cell<bool> true while at
least one invocation is actually running, Items lists everything tracked with
its status, and disposing it tears the pipeline down. IsRunning and disposal
live on its non-generic base, AsyncMapStatus, which is what code that does not
read Items can hold.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
