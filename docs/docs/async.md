---
title: Asynchronous work
---

# Asynchronous work

Sodium's model is synchronous and atomic: a transaction runs to completion and everything
settles. A `Task` does not fit that shape. `MapAsync` is the bridge — it runs asynchronous work
in response to stream firings and feeds the outcome back in as ordinary streams.

It lives in a separate package; see [Which package do I install?](packages.md).

```bash
dotnet add package SodiumFRP.Async
```

## The shape of it

```csharp
using Sodium.Frp;
using Sodium.Frp.Async;

StreamSink<string> queries = Stream.CreateSink<string>();
StreamSink<SearchResult> results = Stream.CreateSink<SearchResult>();
StreamSink<Exception> errors = Stream.CreateSink<Exception>();

AsyncMapStatus<string> status = queries.MapAsync(
    results: results,
    errors: errors,
    operation: (query, token) => SearchAsync(query, token),
    strategy: AsyncConcurrencyStrategy.SwitchLatest());
```

You supply the source stream, two sinks (one for successes, one for failures), the operation
itself, and a concurrency strategy. Successful values arrive on `results` in completion order;
every exception arrives on `errors`. Nothing throws into the transaction.

## Concurrency strategies

The interesting decision is what happens when a firing arrives while earlier work is still in
flight. `AsyncConcurrencyStrategy` offers four answers:

| Strategy | Behaviour |
| --- | --- |
| `Parallel()` | Every firing starts its own operation immediately; results arrive in completion order. |
| `Queue()` | At most one operation runs at a time; later firings queue and run in order. |
| `QueuePerGroup<TInput>().Create(getGroup)` | Queued within a group, concurrent across groups. |
| `SwitchLatest()` | A new firing cancels whatever is in flight and takes its place. |

`SwitchLatest` is what you almost always want for search-as-you-type. `QueuePerGroup` is the
one to reach for when operations are only mutually exclusive per entity — per user, per
document, per connection.

You can also write your own by subclassing `AsyncConcurrencyStrategy<TInput, TResult, TState>`.
The `AsyncConcurrencyStrategy<TState>` and `AsyncConcurrencyStrategy<TInput, TState>` shorthands
exist so you do not have to spell out `Unit` for the parts you do not care about.

## Cancellation and status

`MapAsync` takes two optional cancellation streams: `cancelAll`, where any firing cancels
everything tracked, and `cancelMatching`, where a firing cancels tracked operations by input
value. By default (`cancelOnDispose: true`) disposing the returned status also cancels
whatever is in flight.

The returned `AsyncMapStatus<TInput>` is itself reactive, which makes progress reporting
straightforward: `IsRunning` is a `Cell<bool>`, and `Items` is a
`Cell<IReadOnlyList<AsyncItem<TInput>>>` describing what is queued and running. Bind them
directly to your UI. Disposing the status tears the pipeline down.

> [!NOTE]
> `MapAsync` has nine overloads, differing in whether the strategy inspects the input, the
> result, both, or neither, and whether converters are needed. The generated
> [API reference](../api/index.md) carries the full parameter contract for each; the canonical
> one is the four-type-parameter overload.
