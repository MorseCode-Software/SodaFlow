---
title: Asynchronous work
---

# Asynchronous work

SodaFlow's model is synchronous and atomic: a transaction runs to completion and everything
settles. A `Task` does not fit that shape. `MapAsync` is the bridge — it runs asynchronous work
in response to stream firings and feeds the outcome back in as ordinary streams.

It lives in a separate package; see [Which package do I install?](packages.md).

```bash
dotnet add package SodaFlow.Async
```

## The shape of it

```csharp
using SodaFlow;
using SodaFlow.Async;

StreamSink<string> queries = Stream.CreateSink<string>();
StreamSink<SearchResult> results = Stream.CreateSink<SearchResult>();
StreamSink<Exception> errors = Stream.CreateSink<Exception>();

AsyncMapStatus<string> status = queries.MapAsync(
    results: results,
    errors: errors,
    operation: async (query, factory, token) => factory.FromValue(await SearchAsync(query, token)),
    strategy: AsyncConcurrencyStrategy.SwitchLatest());
```

You supply the source stream, two sinks (one for successes, one for failures), the operation
itself, and a concurrency strategy. Successful values arrive on `results` in completion order;
every exception arrives on `errors`. Nothing throws into the transaction.

The operation gets three arguments, not two: the input, a factory for its answer, and a
cancellation token. `factory.FromValue(value)` is the answer when you have a plain value, which
is most of the time. The other answer, `factory.Construct(() => ...)`, is for a result that
contains part of a SodaFlow graph — see [Building a result in the
transaction](#building-a-result-in-the-transaction).

## Concurrency strategies

The interesting decision is what happens when a firing arrives while earlier work is still in
flight. `AsyncConcurrencyStrategy` offers four answers:

| Strategy | Behavior |
| --- | --- |
| `Parallel()` | Every firing starts its own operation immediately; results arrive in completion order. |
| `Queue()` | At most one operation runs at a time; later firings queue and run in order. |
| `QueuePerGroup<TInput>().Create(getGroup)` | Queued within a group, concurrent across groups. |
| `SwitchLatest()` | A new firing cancels whatever is in flight and takes its place. |

`SwitchLatest` is what you almost always want for search-as-you-type. `QueuePerGroup` is the
one to reach for when operations are only mutually exclusive per entity — per user, per
document, per connection.

You can also write your own by subclassing `AsyncConcurrencyStrategy<TInput, TState>`, with the
`AsyncConcurrencyStrategy<TState>` shorthand when the strategy does not read the input either.
`CreateState` makes the bookkeeping for one `MapAsync` call, `Admit` decides what starts, and
`OnCompleted` decides whether the pipeline publishes the item and what starts next.

`OnCompleted` receives an `AsyncCompletion` — the operation returned, it threw (with the
exception), or a cancellation stopped it — and never the result itself. That is what lets the
pipeline build a result only for an item it is going to publish; see below. If you want to decide
what to publish by looking at a value, filter the `results` stream downstream instead.

Both callbacks are also handed the pipeline's queue: every item it tracks, `Queued` or `Running`,
in admission order, as `IReadOnlyList<AsyncTrackedItem<TInput>>`. Each entry carries the
`AsyncQueuedItem` the strategy was given at admission — the same instance, so `ReferenceEquals`
works and `Cancel()` on it cancels that item — plus its current status. A strategy that schedules
on order alone needs no queue in its own state.

Two boundaries worth knowing, because they decide what "next" means:

- In `Admit`, the list does **not** include the value being admitted; the pipeline adds it after
  the call.
- In `OnCompleted`, the list **still** includes the item that just ended, so skip it when picking
  what to start next.

It is a snapshot taken at the start of the transaction, so it does not change while your callback
runs, and an item your decision promotes still reads as `Queued` in it.

`Queue` and `QueuePerGroup` are written this way: neither keeps a queue in its own state, because
the pipeline already has one in the right order. `Queue` starts an item when nothing is `Running`
and, on completion, starts the first item still `Queued`. `QueuePerGroup` does the same test per
group.

## Cancellation and status

`MapAsync` takes two optional cancellation streams: `cancelAll`, where any firing cancels
everything tracked, and `cancelMatching`, where a firing cancels tracked operations by input
value. By default (`cancelOnDispose: true`) disposing the returned status also cancels
whatever is in flight.

The returned `AsyncMapStatus<TInput>` is itself reactive, which makes progress reporting
straightforward: `IsRunning` is a `Cell<bool>`, and `Items` is a
`Cell<IReadOnlyList<AsyncItem<TInput>>>` describing what is queued and running. Bind them
directly to your UI. Disposing the status tears the pipeline down.

Only `Items` depends on the input type. `IsRunning` and disposal live on a non-generic base,
`AsyncMapStatus`, so a view model that shows a busy indicator and owns the pipeline's lifetime
can hold that and never name the input type:

```csharp
AsyncMapStatus status = queries.MapAsync(
    results: results,
    errors: errors,
    operation: async (query, factory, token) => factory.FromValue(await SearchAsync(query, token)),
    strategy: AsyncConcurrencyStrategy.SwitchLatest());

Cell<bool> busy = status.IsRunning;
```

## Building a result in the transaction

An operation runs outside any transaction — that is the point of it — and the pipeline publishes
what it returns in a transaction of its own. That is fine for a plain value. It is not fine when
the result *contains* part of a graph: a cell, a stream, or a view model built out of them has to
come into existence inside the transaction that sends it, or the first listener can see it before
it is connected.

That is what the second answer is for:

```csharp
AsyncMapStatus status = requests.MapAsync(
    results: results,
    errors: errors,
    operation: async (request, factory, token) =>
    {
        // Outside the transaction: the slow part.
        Document document = await LoadAsync(request, token);

        // Inside the transaction that publishes: the graph.
        return factory.Construct(() => new DocumentViewModel(document));
    },
    strategy: AsyncConcurrencyStrategy.SwitchLatest());
```

The function you hand to `Construct` runs once, in the transaction that sends the result,
so anything it builds is part of that same instant. Two things follow from that:

- **Keep it short.** It holds the transaction while it runs. Do the waiting before it.
- **It may not run at all.** The strategy decides whether the item publishes *before* the
  pipeline builds the result, so a superseded or cancelled item never constructs anything. Do not
  put effects that matter outside the returned value inside it.

If the function throws, the pipeline publishes that exception on `errors` instead of a result —
even though the strategy was told the operation succeeded, because it did. Construction is part
of publishing, not part of the operation.

> [!NOTE]
> `MapAsync` has three overloads, differing in whether the strategy reads the input as it stands,
> through a converter, or not at all. The generated
> [API reference](../api/index.md) carries the full parameter contract for each; the canonical
> one is the overload that takes an `inputConverter`.
