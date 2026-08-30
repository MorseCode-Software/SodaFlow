3.0.0

No code change. This release exists to move a dependency, and is a major
version because of what moving it does to a consumer.

Dependencies between these packages are now declared as ranges bounded at
the next major, so NuGet refuses a pairing which would fail rather than
resolving it and leaving the failure until the code runs. This package now
requires SodaFlow 3.x, SodaFlow.Async.Core 3.x and SodaFlow.Functional 2.x.

That ceiling is why this is not a minor version. Taking this release obliges
a consumer to take those majors as well, and one who names SodaFlow or
SodaFlow.Functional directly, or who uses anything removed there, cannot
adopt it without changing their own code. A version they cannot adopt is not
a minor one.

2.1.0

Now depends on SodaFlow, the C# API it extends.

MapAsync operates on a Stream<T>, and only SodaFlow's operations produce or
consume one usefully, so a consumer always needed both. Declaring it means one
install brings the whole surface instead of the second package being discovered
the first time someone tries to build a graph.

No API change, and nothing to do when upgrading from 2.0.0 beyond taking it.

---

About this package

Runs Task-based work from a stream: each firing starts an operation, successes
go to one sink and failures to another, and a concurrency strategy decides what
runs when.

  parallelStrategy      every firing starts at once
  queueStrategy         one at a time, in order
  switchLatestStrategy  a new firing supersedes whatever is in flight
  queuePerGroup         one independent queue per key

MapAsync returns an AsyncMapStatus<TInput>: IsRunning is a Cell<bool> that is
true while at least one invocation is actually running, updating glitch-free in
the same transaction as whatever caused it to change; Items lists everything
tracked with its status; disposing it tears the pipeline down.

Operations are handed a CancellationToken combining the item's own cancellation
with the strategy's. Honoring it is what makes cancellation take effect on work
already started - an operation that ignores it still runs to completion, and
cancellation then only means its result goes unpublished.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
