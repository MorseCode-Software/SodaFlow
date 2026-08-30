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
with the strategy's. Honouring it is what makes cancellation take effect on work
already started - an operation that ignores it still runs to completion, and
cancellation then only means its result goes unpublished.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
