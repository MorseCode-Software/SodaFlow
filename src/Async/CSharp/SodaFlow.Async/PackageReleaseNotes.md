5.1.0

Adds an Execute overload that takes a Cell<Maybe<TInput>> and answers with a
Task<Maybe<TResult>>. Where the cell holds Some, the pipeline admits its value
and the Task gives Some with the result. Where the cell holds None, the pipeline
admits nothing, the operation does not run, and the Task gives None. The
overload does not wait for the cell to hold Some.

The overload reads the cell and tests it in the transaction that puts the value
in, as the Cell<TInput> overload does. Thus, the decision and the value come
from one instant. For an admitted value, the Task obeys each rule of Execute: a
cancellation cancels it, and an exception from the operation faults it. Thus,
None tells only that the cell held None.

SodaFlow.Async.Core cannot refer to Maybe, thus the overload is an extension
method in the new AsyncMapStatusExtensions class. Where TInput is itself a
Maybe<T>, a Cell<TInput> goes to the overload on the type, which admits None as
a value, and this overload takes a Cell<Maybe<Maybe<T>>>.

This release takes SodaFlow.Async.Core 5.0.1, which adds the internal path that
the overload uses.

5.0.0

Adds Execute, which puts one value into a pipeline and answers with the Task of
that value alone.

Execute has one purpose: the operation of a MapAsync pipeline calls a second
MapAsync pipeline with it, and waits for the result of that one value. Thus, an
operation can be a pipeline of its own, and the strategy of the inner pipeline
controls the inner work. An operation is an async method, thus it can await the
Task.

Other code does not use Execute. Code that has a value for a pipeline sends that
value on the source stream of the pipeline, and reads the results stream. That is
the interface of a pipeline, and it keeps the identity of one value out of code
that has no need of it.

The value goes through the strategy as a value from the source stream does, thus
the call obeys the concurrency rules of the pipeline, and the result reaches the
results stream as well. Execute gives the same object and does not divert it.

A second overload takes a Cell<TInput>. It reads the cell in the transaction that
puts the value in, thus the pipeline admits the value that the cell has at that
instant. A caller that samples the cell first, and then calls the other overload,
has two transactions, and the cell can take a new value between them.

The Task ends one time, in each condition. It gives the result where the
operation gives one and the strategy publishes it. It carries the exception
where the operation throws and the strategy publishes that, and the errors
stream gets the same exception. It is canceled where a cancellation stops the
value, where the strategy refuses the value, where the strategy does not publish
the outcome, and where the pipeline is disposed before the value is admitted. A
strategy that does not publish says that no code wants the result, which is a
cancellation at a later moment, thus the Task treats it as one.

One condition gives no end to the Task, and it is outside the position that Execute
is for. A strategy that keeps a value in the queue permanently, and does not
cancel that value, gives the pipeline no end to read. Each strategy in this
library ends each value, and the documented method for a strategy to refuse a
value cancels that value, thus that method ends the Task.

A transaction that fails cancels the Task. Where a transaction is open, Execute
defers the value into the post queue of that transaction. A throw while that
transaction propagates discards that queue, thus the value never enters the
pipeline, and Execute registers a cancellation for that condition. A throw from
the body of a transaction is different: that transaction still closes, the queue
still drains, and the pipeline admits the value.

A call with a transaction open is legal. The code of an operation before its first
await runs in the transaction that started that operation, thus Execute cannot ask
its caller for a thread with no transaction open. It defers the value to a
transaction of its own in that condition, and the pipeline admits the value after
the transaction of the caller ends. The overload that takes a cell reads that cell
in the transaction of the send, thus a deferral carries the read with it.

The Task runs its continuations asynchronously. The pipeline answers it in the
transaction that publishes, and a continuation on that thread would be in that
transaction, where a send is not legal.

MapAsync answers with a new AsyncMapStatus<TInput, TResult>, which extends the
AsyncMapStatus<TInput> that it answered with before. Thus, the count of the type
parameters a caller keeps says what that caller does: the non-generic
AsyncMapStatus for IsRunning and the disposal, AsyncMapStatus<TInput> for Items
also, and AsyncMapStatus<TInput, TResult> for Execute also. Source that names
AsyncMapStatus<TInput> or AsyncMapStatus for the answer needs no edit, because
the new type is a subclass of each.

Fixed: a cancellation now ends each item that it cancels, and the pipeline
stops tracking it. Two conditions left an item in the queue with no operation
behind it, and IsRunning and Items reported that item for the life of the
pipeline.

The first was a Queued item. A cancellation cancels the token of each item,
and a Queued item runs no operation, thus nothing observed that token. The end
of such an item waited for a promotion, and a promotion comes from the end of
a different item, thus an item that no other end followed stayed in the queue.

The second was a Running item, and it needed a particular shape of operation.
Cancel() runs the registrations of a token on the thread that calls it. Where
one of those ends the Task of the operation, and that Task runs its
continuations on the completing thread, the end of the item ran on that
thread, inside the callback of the listener for the cancellation stream. A
send is not legal there, thus the end threw, and the throw went into the
machinery of Cancel() where no code reports it. The strategy had the end of
that item and the pipeline kept it. An operation that awaits a plain
TaskCompletionSource, which a caller writes to wrap a callback API or to build
a gate, meets this. An operation that awaits Task.Delay with the token does
not, because that one schedules its continuation.

BREAKING: the queue that a strategy reads in Admit and in OnCompleted is the
queue as the pipeline holds it at the moment of the call, and not a snapshot
from the start of the transaction. OnCompleted no longer finds the item that
ends now: the pipeline removes that one before the call, thus a strategy can
take the first Queued item with no test against it. A strategy that filtered
that item out by hand must drop the filter, or it skips a real item. Admit is
unchanged in this: the value that the pipeline admits now is still absent,
because the pipeline adds it after the call.

BREAKING: a strategy now has two hooks for the ends of its items. OnCompleted
reads one item whose operation returned or threw, with the signature that it had
before the release: one AsyncQueuedItem, one AsyncCompletion, and the queue.
AsyncStrategyResult keeps its boolean Publish property. OnCanceled reads the
items that a cancellation ended, as an IReadOnlyList<AsyncQueuedItem<TInput>>.

A cancellation is what makes a list necessary. One send of cancelAll or of
cancelMatching can end more than one item at one instant: each Queued item that
it removes, and each Running item whose operation observes its token. Before this
release, one call for each of them asked the strategy to decide again, and each of
those decisions read a queue that held the other items which end at the same
instant. Thus, a strategy started an item that was about to end. OnCanceled gets
all of them in one call, over the queue that all of them leave.

OnCanceled makes no publish decision, because a canceled item sends nothing to
the results stream and nothing to the errors stream. Thus, it returns the items
to start, as an IReadOnlyList<AsyncToStart<TInput>>, which is what Admit returns.
Give AsyncStrategyResult<TInput>.None for no items.

OnCompleted reads one item and never a list. A stream carries one value for each
transaction, thus one such end is the most that a transaction can hold. The edit
of the queue for that end is in the transaction that sends it, or a graph that
reads Items and results together sees two instants where the pipeline made one. A
caller that wants a result for a batch of inputs asks for that in the types: one
MapAsync from TInput to a list of TInput, and a second from that list to a list
of results. MapAsync is TInput to TResult, and it does not model two results at
one instant.

AsyncCompletion has two ends now, Succeeded and Failed, and Match and MatchVoid
take two handlers. A cancellation is not one of those ends: it goes to OnCanceled,
which reads the items alone, because a canceled end carries nothing to read.

A call of MapAsync with a strategy from this library needs no edit for this. A
custom strategy adds OnCanceled, which the compiler asks for, because the method
is abstract: a default that starts nothing makes a queue stall where a
cancellation ends the item that runs. A strategy that starts the next Queued item
must test the queue in OnCanceled also, because more than one item can end at one
instant.

Fixed: a MapAsync call no longer stalls where the graph feeds results back
into inputs. A published result can fire the input stream in the transaction
that ended the previous item, thus OnCompleted and Admit run one after the
other in one transaction. Each read the queue from the start of that
transaction before, thus OnCompleted found nothing Queued and Admit found the
item that ended still Running. Neither started anything, and the new item
stayed Queued with no event to start it. Queue and QueuePerGroup had this, and
Parallel and SwitchLatest did not, because their Admit starts an item at each
call.

The queue is one value that the pipeline computes in one sequence of edits,
and the cell behind the Items property takes that value. Before, the strategy
read a sequential fold and the cell accumulated the edits of a transaction in
groups: all the removals, then the additions, then the promotions. The two
agreed for the inputs that occur, but agreement was a coincidence and not a
property. The cell still takes one new value for each transaction, at the end
of it, thus nothing sees a queue with some of the edits.

BREAKING: the operation that MapAsync takes is a MapAsyncOperation<TInput,
TResult> delegate, where it was a Func<TInput, CancellationToken,
Task<TResult>>. It takes a third argument, a factory, and answers with what that
factory makes:

  operation: async (query, factory, token) =>
      factory.FromValue(await SearchAsync(query, token))

FromValue carries a value the operation has. Construct carries a function
that the pipeline calls in the transaction that sends the result, which is what
a result that holds a cell or a stream needs:

  operation: async (request, factory, token) =>
  {
      Document document = await LoadAsync(request, token);

      return factory.Construct(() => new DocumentViewModel(document));
  }

Keep such a function short, because it holds the transaction while it runs, and
put no effect in it that matters outside the value it returns: the pipeline
calls it only for an item that it publishes. A throw from it goes to the errors
stream in place of a result.

A lambda takes the third parameter and compiles. A stored Func does not convert
to the delegate and needs its own edit.

BREAKING: a custom strategy takes one more argument in Admit and in
OnCompleted, the queue of the pipeline: each item that it tracks, Queued or
Running, in the sequence of their admissions, with the status of each one. A
strategy that schedules on that sequence alone keeps no queue of its own. In
Admit the list does not hold the value that the pipeline admits now, and in
OnCompleted it holds no item that ends now. A call of MapAsync with a strategy
from this library needs no edit for this.

BREAKING: MapAsync has three overloads, where it had nine. The six that are gone
each named a result type for the strategy, and a strategy no longer reads a
result: OnCompleted takes an AsyncCompletion, which says that the operation
returned, that it threw, with the exception, or that a cancellation stopped it.
The pipeline asks the strategy before it makes the result, and it makes the
result only for an item that it publishes.

The result type of a strategy is gone with the overloads, in
AsyncConcurrencyStrategy<TInput, TResult, TState>, which is
AsyncConcurrencyStrategy<TInput, TState> now, and in the
AsyncConcurrencyStrategy<TInput, TState> shorthand, which was the same type and
is not necessary. The remaining three overloads differ in how the strategy reads
the input: not at all, as it stands, or through an inputConverter.

A call that gives no converter needs no edit for this. A call with a
resultConverter drops that argument. A custom strategy changes its base class
and its OnCompleted signature, and one that reads results can filter the results
stream instead.

BREAKING, and the break is in SodaFlow.Async.Core 5.0.0, which this release
takes: the status that every MapAsync overload returns is a class where it was a
readonly struct, and it extends a new non-generic AsyncMapStatus that carries
IsRunning and Dispose. Items stays on the generic type.

A caller that never reads Items can now hold an AsyncMapStatus and does not
have to name the input type. A view model that shows a busy indicator and
disposes the pipeline is the usual case.

Source that names AsyncMapStatus<TInput> needs no edit. Anything compiled
against 4.x does, because a struct and a class are not the same type to the
runtime.

4.0.1

Adds the package icon that nuget.org shows beside this package. No source file
changed since 4.0.0.

Every package here ships this release together, so the dependency versions
move with it.

4.0.0

No API change of its own. This release moves to SodaFlow 4.x,
SodaFlow.Async.Core 4.x and SodaFlow.Functional 3.x, and is a major
because taking it obliges a consumer to take those.

Unit is a struct in SodaFlow.Functional 3.0.0, and this package's
AsyncConcurrencyStrategy fixes its unused type parameters to Unit, so an
assembly compiled against the old Unit will not bind against this. Recompile
rather than mix.

Requires System.ValueTuple 4.6.2, where it required 4.4.0. Nothing here
uses it differently: this repository named two versions of it, one in
the shipping projects and one in the test projects, and now names a
single version in both. A consumer does nothing about this; NuGet
resolves the higher floor.

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

An operation takes the input, a factory for its answer, and a token, and answers
with factory.FromValue(value) or factory.Construct(() => ...). The second
one runs in the transaction that publishes, for a result that holds part of a
graph.

MapAsync returns an AsyncMapStatus<TInput, TResult>: Execute puts one value in
and answers with the Task of that value alone, for an operation that drives a
second pipeline; IsRunning is a Cell<bool> that is
true while at least one invocation is actually running, updating glitch-free in
the same transaction as whatever caused it to change; Items lists everything
tracked with its status; disposing it tears the pipeline down. IsRunning and
disposal live on its non-generic base, AsyncMapStatus, so code that does not
read Items can hold that instead of naming the input type, and Items lives on
AsyncMapStatus<TInput> between the two.

Operations are handed a CancellationToken combining the item's own cancellation
with the strategy's. Honoring it is what makes cancellation take effect on work
already started - an operation that ignores it still runs to completion, and
cancellation then only means its result goes unpublished.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
