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
document, per connection. You supply the function that says which group a firing belongs to:

```csharp
// Saves to the same document run one at a time and in order. Saves to different
// documents run concurrently, because they cannot conflict.
AsyncMapStatus<SaveRequest> status = saves.MapAsync(
    results: saved,
    errors: saveErrors,
    operation: static async (request, factory, token) =>
        factory.FromValue(await SaveAsync(request, token)),
    strategy: AsyncConcurrencyStrategy
        .QueuePerGroup<SaveRequest>()
        .Create(static request => request.DocumentId));
```

The group key is compared with `EqualityComparer<TGroup>.Default`, so any type that equates
sensibly will do.

You can also write your own by subclassing `AsyncConcurrencyStrategy<TInput, TState>`, with the
`AsyncConcurrencyStrategy<TState>` shorthand when the strategy does not read the input either.
`CreateState` makes the bookkeeping for one `MapAsync` call, `Admit` decides what starts, and
`OnCompleted` decides which outcomes the pipeline publishes and what starts next.

`OnCompleted` is called once per transaction, with every item that ended in it as an
`IReadOnlyList<AsyncEnd<TInput>>`. Usually that is one item. A cancellation is the case where it
is not: `cancelAll` and `cancelMatching` can end several queued items and a running one at the
same instant, and batching them into one call means you make a single decision against the queue
they all left, rather than a sequence of decisions each of which still sees the others. Return
`ItemsOf(ended)` to publish every outcome, `AsyncStrategyResult<TInput>.PublishNone` to publish
none, or a list you build yourself to publish some — `SwitchLatest` does the last of these, since
only the newest run should reach `results`.

Each `AsyncEnd` carries the item and an `AsyncCompletion` — the operation returned, it threw (with
the exception), or a cancellation stopped it — and never the result itself. That is what lets the
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
- In `OnCompleted`, the list does **not** include any item in `ended` either; the pipeline
  removes all of them before the call, so you can take the first `Queued` item without checking
  whether it is one of the items you were told about.

The list is the queue as the pipeline holds it at the moment of the call, not a snapshot from the
start of the transaction. It does not change while your callback runs, and an item your decision
promotes still reads as `Queued` in it — but an edit made **earlier in the same transaction** is
already there. That matters when the graph feeds results back into inputs: publishing a result can
fire the input stream inside the transaction that completed the previous item, so `OnCompleted`
and `Admit` run one after the other in a single transaction. Each sees what the other did, which
is what stops a queueing strategy from stalling because both believed an item was still running.

`Queue` and `QueuePerGroup` are written this way: neither keeps a queue in its own state, because
the pipeline already has one in the right order. `Queue` starts an item when nothing is `Running`
and, when items end, starts the first item still `Queued` — but only if nothing is `Running`,
since a cancellation can end queued items while another is still going. `QueuePerGroup` does the
same test per group, once for each group an end leaves free.

## Cancellation and status

`MapAsync` takes two optional cancellation streams: `cancelAll`, where any firing cancels
everything tracked, and `cancelMatching`, where a firing cancels tracked operations by input
value. By default (`cancelOnDispose: true`) disposing the returned status also cancels
whatever is in flight.

The returned status is itself reactive, which makes progress reporting straightforward:
`IsRunning` is a `Cell<bool>`, and `Items` is a `Cell<IReadOnlyList<AsyncItem<TInput>>>` describing
what is queued and running. Bind them directly to your UI. Disposing the status tears the pipeline
down.

How much of the status you hold is your choice, and you make it by how many type parameters you
keep. The declared return type is `AsyncMapStatus<TInput, TResult>`, which carries everything;
assigning it to `AsyncMapStatus<TInput>` keeps `Items` and drops `Execute`; assigning it to the
non-generic `AsyncMapStatus` keeps `IsRunning` and disposal and names no types at all. So a view
model that shows a busy indicator and owns the pipeline's lifetime can hold the narrowest one:

```csharp
AsyncMapStatus status = queries.MapAsync(
    results: results,
    errors: errors,
    operation: async (query, factory, token) => factory.FromValue(await SearchAsync(query, token)),
    strategy: AsyncConcurrencyStrategy.SwitchLatest());

Cell<bool> busy = status.IsRunning;
```

## Calling a pipeline from inside an operation

`Execute` exists for one situation: an operation of one `MapAsync` pipeline needs to run a *second*
`MapAsync` pipeline and wait for that one value's result. It is the way to nest pipelines, and that
is the whole of it.

The reason it is needed is that the inner work has its own concurrency rules. Suppose each incoming
request needs several documents saved, and saves to one document must not overlap. The outer
pipeline handles requests; the inner one owns the save queue. The operation of the outer pipeline
calls the inner one, and the inner strategy decides when each save actually runs:

```csharp
// The inner pipeline: saves to the same document run one at a time.
AsyncMapStatus<SaveRequest, SaveReceipt> saver = saveRequests.MapAsync(
    results: saved,
    errors: saveErrors,
    operation: static async (request, factory, token) =>
        factory.FromValue(await SaveAsync(request, token)),
    strategy: AsyncConcurrencyStrategy
        .QueuePerGroup<SaveRequest>()
        .Create(static request => request.DocumentId));

// The outer pipeline: its operation drives the inner one and awaits each save.
AsyncMapStatus<Batch, BatchReceipt> batcher = batches.MapAsync(
    results: batchesDone,
    errors: batchErrors,
    operation: async (batch, factory, _) =>
    {
        List<SaveReceipt> receipts = [];

        foreach (SaveRequest request in batch.Requests)
        {
            // Queues behind anything already saving that document.
            receipts.Add(await saver.Execute(request));
        }

        return factory.FromValue(new BatchReceipt(receipts));
    },
    strategy: AsyncConcurrencyStrategy.Parallel());
```

Note the discarded token in that outer operation. `Execute` takes no `CancellationToken`, so an
outer item's token cannot reach the inner pipeline: cancelling the outer item does not cancel the
inner work it started. If you need that, cancel the inner pipeline through its own `cancelAll` or
`cancelMatching`.

An operation is an `async` method, so it can await the task — and it does not have to worry about
transactions. The code of an operation before its first `await` actually runs *inside* the
transaction that started it, so `Execute` cannot demand a caller with no transaction open. When one
is open it defers the value to a transaction of its own, and the pipeline admits it once the
caller's transaction ends.

**Everywhere else, do not use it.** Code that has a value for a pipeline sends that value on the
pipeline's source stream and reads the results stream. That is a pipeline's interface, and it keeps
the identity of a single value out of code that has no business tracking it. Reaching for `Execute`
from a view model or a command handler is a sign the graph should be wired up instead.

The value goes through `Admit` and `OnCompleted` like any other, so the inner strategy sees no
difference between it and a value that arrived on the source stream. The result reaches the inner
`results` stream as well — `Execute` gives you the same object, it does not divert it.

**The task finishes**, with one exception below. It carries the result when the operation returns
one and the strategy publishes it; it carries the operation's exception when it throws and the
strategy publishes that, and `errors` gets the same exception; and it is cancelled when a
cancellation stops the value, when the strategy refuses it, when the strategy declines to publish
the outcome, or when the pipeline was already disposed. Declining to publish means nobody wants the
result any more, which is a cancellation that arrived late, so the task treats it as one.

The exception is a custom strategy that parks a value in the queue forever without cancelling it.
Nothing ends such an item, so nothing completes its task. The documented way to refuse a value —
cancel it in `Admit` and do not promote it — does complete the task, as a cancellation. Every
built-in strategy completes it.

There is a second overload taking a `Cell<TInput>`, for when the value to run is whatever the cell
holds at that moment — again, from inside an operation:

```csharp
// currentRequest is a Cell<SaveRequest> that some other part of the graph keeps up to date.
SaveReceipt receipt = await saver.Execute(currentRequest);
```

It reads the cell inside the same transaction that puts the value in, so the pipeline admits the
cell's value at that instant. Sampling the cell yourself and passing the result to the other
overload is two transactions, and the cell can change between them. When the call defers — because
a transaction was open — the read and the send travel together into the deferred transaction, so
the guarantee still holds.

In F#, both overloads are members on the returned status, the same as in C#, and the same rule about
where to call them applies:

```fsharp
let! receipt = saver.Execute request
let! current = saver.Execute currentRequest
```

## Building a result in the transaction

An operation runs outside any transaction — that is the point of it — and the pipeline publishes
what it returns in a transaction of its own. That is fine for a plain value. It is not fine when
the result *contains* part of a graph: a cell, a stream, or a view model built out of them has to
come into existence inside the transaction that sends it, or the first listener can see it before
it is connected.

That is what the second answer is for:

```csharp
StreamSink<DocumentViewModel> documents = Stream.CreateSink<DocumentViewModel>();
StreamSink<Exception> documentErrors = Stream.CreateSink<Exception>();

AsyncMapStatus status = requests.MapAsync(
    results: documents,
    errors: documentErrors,
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
