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
    handleException: ReportUnhandled,
    operation: async (query, factory, token) => factory.FromValue(await SearchAsync(query, token)),
    strategy: AsyncConcurrencyStrategy.SwitchLatest());
```

You supply the source stream, two sinks (one for successes, one for failures), a handler for the
rare exception that cannot go to `errors`, the operation itself, and a concurrency strategy.
Successful values arrive on `results` in completion order; every exception arrives on `errors`.
Nothing throws into the transaction.

A listener can still throw while the pipeline delivers an outcome: one on `results`, on `errors`,
or on `Items`. Delivering an outcome is a single transaction, so it fails as a whole: a listener
that ran before the throw has seen the outcome, but no cell keeps it, and the item stays in the
queue. The pipeline then ends the item again without delivering anything, which lets the next item
start. After that it sends a `MapAsyncDeliveryException` on `errors`, in a transaction of its own,
with the listener's exception as its `InnerException`. When the outcome that did not get through
was itself an exception, from the operation or from a `factory.Construct` function, the report
also carries it as `UndeliveredError`, because neither `errors` nor the `Execute` task received
it. It is `None` when the outcome was a result: the property is a `Maybe<Exception>` in C# and an
`exn option` in F#. Each language package has its own sealed `MapAsyncDeliveryException`, derived
from `MapAsyncDeliveryExceptionBase` in SodaFlow.Core.Async, and a pipeline always sends the one
from the package that built it, so test for that type. An operation's own failure never arrives
wrapped, so a handler can tell the two apart. If the item has an `Execute` task, that task fails
with the same exception. If a listener on `errors` throws on that report as well, the pipeline
still carries on, and both exceptions go to `handleException` together in an `AggregateException`.

`handleException` is for exceptions that have no caller to go to and that `errors` cannot take:
the case above, the rarer one where ending the item fails too, and a bug in the pipeline itself.
The pipeline keeps running after each one. Log it, or fail fast if that is your policy, but do not
throw from it: a throw there ends the process, the same as a throw from the handler you give a
timer system. The pipeline never leaves an exception in a task that nobody observes.

The operation gets three arguments, not two: the input, a factory for its answer, and a
cancellation token. `factory.FromValue(value)` is the answer when you have a plain value, which
is most of the time. The other answer, `factory.Construct(() => ...)`, is for a result that
contains part of a SodaFlow graph — see [Building a result in the
transaction](#building-a-result-in-the-transaction).

## Concurrency strategies

The interesting decision is what happens when a firing arrives while earlier work is still in
flight. `AsyncConcurrencyStrategy` offers these answers:

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
    handleException: ReportUnhandled,
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
`CreateState` makes the first state for one `MapAsync` call, `Admit` decides what starts, and two
hooks handle the ends: `OnCompleted` for an operation that returned or threw, `OnCanceled` for a
cancellation.

**A strategy does not change its state; it returns the next one.** Every hook is handed the
current state and returns its decision together with the state for the next call. `Admit` and
`OnCanceled` return `new AsyncStrategyStep<TInput, TState>(state, start)`, and `OnCompleted`
returns `new AsyncStrategyResult<TInput, TState>(state, publish, next)`. Return the state you were
given when nothing changes.

The pipeline keeps that state and treats it like the rest of its graph. If the transaction a hook
ran in fails, the state the hook returned is discarded along with the queue edit, and the next
call gets the state from before. That only works if the state is a value you replace. A strategy
that changes an object inside its state and returns the same object keeps the change after a
failure, so build the state from immutable types: a record, a tuple, an immutable collection, a
`Maybe<T>`.

**`OnCompleted` gets one item.** It takes the `AsyncQueuedItem` that ended, an `AsyncCompletion`
saying which of the two ends it was, and the queue; it returns
`new AsyncStrategyResult<TInput, TState>(state, publish: bool, next: ...)`. One item and not a
list, because a
stream carries one value per transaction and the queue edit for that end happens in the same
transaction as its send — so a graph that reads `Items` and `results` together never sees two
instants where the pipeline made one. `publish: false` means nobody wants this result; the pipeline
sends nothing and builds nothing, which is how `SwitchLatest` drops a run that a newer send
replaced.

If you want a result per batch of inputs, say so in the types: one `MapAsync` from `TInput` to a
list, and a second from that list to a list of results. `MapAsync` is `TInput -> TResult` and does
not model two results at one instant.

**`OnCanceled` gets a list, and makes no publish decision.** `cancelAll` and `cancelMatching` can
end several queued items and a running one at the same instant, and a canceled item sends nothing to
`results` or `errors` — so they arrive as one call and one `Items` edit, rather than a sequence of
decisions each of which still sees the others. There is nothing to publish, so the return type
has no publish decision: an `AsyncStrategyStep<TInput, TState>`, the same as `Admit` returns, with
`AsyncToStart<TInput>.None` when nothing starts. This is where a queueing strategy starts the next
item after a cancellation frees its slot, and where a strategy that holds its current run in its
state returns a state without it.

`AsyncCompletion` says only that the operation returned or that it threw (with the exception), never
the result itself. That is what lets the pipeline build a result only for an item it is going to
publish; see below. If you want to decide what to publish by looking at a value, filter the
`results` stream downstream instead.

Every hook is also handed the pipeline's queue: every item it tracks, `Queued` or `Running`,
in admission order, as `IReadOnlyList<AsyncTrackedItem<TInput>>`. Each entry carries the
`AsyncQueuedItem` the strategy was given at admission — the same instance, so `ReferenceEquals`
works and `Cancel()` on it cancels that item — plus its current status. A strategy that schedules
on order alone needs no queue in its own state.

Two boundaries worth knowing, because they decide what "next" means:

- In `Admit`, the list does **not** include the value being admitted; the pipeline adds it after
  the call.
- In `OnCompleted` and `OnCanceled`, the list does **not** include the items that ended either;
  the pipeline removes all of them before the call, so you can take the first `Queued` item
  without checking whether it is one of the items you were told about.

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

A strategy can hear about the same end twice. When a listener throws while the pipeline delivers
an outcome, the transaction that called `OnCompleted` or `OnCanceled` fails, and the pipeline
calls it again for the same item in a new transaction, publishing nothing from that second call.
The first call's decision and its state both went with the failed transaction, so the second call
gets exactly what the first one got. A strategy that only reads its arguments gives the same
answer and needs no special handling. One that does something else in a hook, such as counting
calls in a field of its own, should not assume each item arrives once.

`Admit` is covered the same way. It runs in the input stream's transaction, before that
transaction commits. If a listener throws later in the same transaction, the value is not admitted
after all: the queue goes back, no operation starts, no hook is called for that value again, and
the state `Admit` returned is discarded.

`Cancel()` follows the transaction too. Every hook runs in a transaction, and a cancel asked for
inside one takes effect when that transaction commits; a transaction that fails cancels nothing.
Two things follow. The item's token still reads as not canceled for the rest of the hook. And an
exception thrown by a callback registered on that token goes to `handleException`, because by then
the transaction has finished and there is no caller left to throw to.

## Cancellation and status

`MapAsync` takes two optional cancellation streams: `cancelAll`, where any firing cancels
everything tracked, and `cancelMatching`, where a firing cancels tracked operations by input
value. By default (`cancelOnDispose: true`) disposing the returned status also cancels
whatever is in flight.

A cancellation from either stream takes effect once the transaction that fired the stream has
committed. If that transaction fails, nothing is canceled: a running operation keeps its token,
and a queued item stays queued. If a callback you registered on an operation's token throws while
the pipeline cancels it, the other items are still canceled, and the exception goes to
`handleException`.

A cancellation is a request, and every item ends on its own terms. Only entry into the queue is
synchronous with your input: an item enters `Items` in the same transaction as the value that
admitted it. A canceled item leaves in a later transaction. A queued item leaves right after the
transaction that fired the cancellation stream. A running item leaves when its operation ends,
which is when it honors its token, or when it finishes if it ignores the token. So a graph that
reads a cancellation stream together with `Items` or `IsRunning` sees the request first, with the
queue as it was, and the change after it.

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
    handleException: ReportUnhandled,
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
    handleException: ReportUnhandled,
    operation: static async (request, factory, token) =>
        factory.FromValue(await SaveAsync(request, token)),
    strategy: AsyncConcurrencyStrategy
        .QueuePerGroup<SaveRequest>()
        .Create(static request => request.DocumentId));

// The outer pipeline: its operation drives the inner one and awaits each save.
AsyncMapStatus<Batch, BatchReceipt> batcher = batches.MapAsync(
    results: batchesDone,
    errors: batchErrors,
    handleException: ReportUnhandled,
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
transactions. An operation starts only after the transaction that started it has finished and
released its lock, on the same thread, so even the code before its first `await` runs with no
transaction open, and a call to `Execute` there sends its value at once. `Execute` still accepts a
caller that has a transaction open: it defers the value to a transaction of its own, and the
pipeline admits it once the caller's transaction ends.

That ordering matters for any synchronous work at the start of an operation. If it ran inside the
transaction, it would hold the lock that every thread needs to start a transaction, so slow work
there would stall all of them, and waiting there on another thread that needs a transaction would
deadlock. It runs after the lock is released, so neither can happen.

**Everywhere else, do not use it.** Code that has a value for a pipeline sends that value on the
pipeline's source stream and reads the results stream. That is a pipeline's interface, and it keeps
the identity of a single value out of code that has no business tracking it. Reaching for `Execute`
from a view model or a command handler is a sign the graph should be wired up instead.

The value goes through `Admit` and `OnCompleted` like any other, so the inner strategy sees no
difference between it and a value that arrived on the source stream. The result reaches the inner
`results` stream as well — `Execute` gives you the same object, it does not divert it.

**The task finishes**, with one exception below. It carries the result when the operation returns
one and the strategy publishes it; it carries the operation's exception when it throws and the
strategy publishes that, and `errors` gets the same exception; it carries the
`MapAsyncDeliveryException` that `errors` gets when a listener throws while the pipeline delivers
the outcome; and it is canceled when a cancellation stops the value, when the strategy refuses it,
when the strategy declines to publish the outcome, or when the pipeline was already disposed.
Declining to publish means nobody wants the result any more, which is a cancellation that arrived
late, so the task treats it as one.

One thing can leave it pending, and it is outside the position `Execute` is for: a custom strategy
that parks a value in the queue forever without cancelling it. Nothing ends such an item, so nothing
completes its task. The documented way to refuse a value — cancel it in `Admit` and do not promote
it — does complete the task, as a cancellation, and every built-in strategy completes it.

A failing transaction is handled rather than excluded. When a transaction is open, `Execute`
defers the value into that transaction's post queue, and a transaction that fails discards the
queue, so the value never arrives. It makes no difference how it fails: a throw from the
transaction's body and a throw while it propagates end the same way. `Execute` registers a
cancellation for exactly that case, so the task ends as canceled. The same holds one level down:
if you call `Execute` from a posted action and that action's own transaction fails, the value is
not admitted and the task is canceled.

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

When there may be nothing to run, there is an overload taking a `Cell<Maybe<TInput>>` (a
`Cell<'TInput option>` in F#). If the cell holds `Some` at the instant of the send, the pipeline
admits its value and the task gives `Some` with the result. If it holds `None`, nothing is admitted,
the operation does not run, and the task gives `None` — it does not wait for the cell to fill. The
check happens in the same transaction as the read, so the guarantee above covers it too.

```csharp
// pendingRequest is a Cell<Maybe<SaveRequest>> that holds None while nothing is waiting.
Maybe<SaveReceipt> receipt = await saver.Execute(pendingRequest);
```

`None` means only that the cell held `None`. Every other outcome is the same as for the overloads
above: a cancellation still cancels the task, and an operation's exception still faults it. If
`TInput` is itself a `Maybe<T>`, a `Cell<TInput>` goes to the plain cell overload instead, which
admits `None` as an ordinary value, and this overload takes a `Cell<Maybe<Maybe<T>>>`.

In F#, the value and cell overloads are members on the returned status, the same as in C#. The
option overload is a type extension in the `SodaFlow.Async` module, so it is in scope wherever you
have opened that module to call `mapAsync`. The same rule about where to call any of them applies:

```fsharp
let! receipt = saver.Execute request
let! current = saver.Execute currentRequest
let! pending = saver.Execute pendingRequest // a Cell<SaveRequest option>; gives SaveReceipt option
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
    handleException: ReportUnhandled,
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
  pipeline builds the result, so a superseded or canceled item never constructs anything. Do not
  put effects that matter outside the returned value inside it.

If the function throws, the pipeline publishes that exception on `errors` instead of a result —
even though the strategy was told the operation succeeded, because it did. Construction is part
of publishing, not part of the operation.

> [!NOTE]
> `MapAsync` has one overload for each way the strategy can read the input: as it stands,
> through a converter, or not at all. The generated
> [API reference](../api/index.md) carries the full parameter contract for each; the canonical
> one is the overload that takes an `inputConverter`.
