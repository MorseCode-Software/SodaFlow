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

Execute lives on a new AsyncMapStatus<TInput, TResult>, which extends
AsyncMapStatus<TInput>, and MapAsyncImpl answers with that type. Thus, the count
of the type parameters a caller keeps says what that caller does: the
non-generic AsyncMapStatus for IsRunning and the disposal, AsyncMapStatus<TInput>
for Items also, and AsyncMapStatus<TInput, TResult> for Execute also.
AsyncMapStatus<TInput> is no longer sealed for this, and its constructor is
private protected, thus no code outside this assembly can extend it.

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

BREAKING: OnCompleted takes each item that ends in one transaction, as an
IReadOnlyList<AsyncEnd<TInput>>, in place of one AsyncQueuedItem and one
AsyncCompletion. An AsyncEnd holds those two values. The Publish property of
AsyncStrategyResult is an IReadOnlyList<AsyncQueuedItem<TInput>> in place of a
bool, and it names the items whose outcomes the pipeline publishes.

A cancellation can end more than one item at one moment: each Queued item that
it removes, and each Running item whose operation observes its token. One call
for each of those asked the strategy for one decision at a time, and each of
those decisions read a queue that held the other items which end at the same
moment. A strategy thus started an item that was about to end. One call over
the queue that holds no item of those ends is one decision, which is the rule
that Admit and OnCompleted already follow for one transaction.

A cancellation is not the one path to this. Each operation that awaits a
TaskCompletionSource ends on the thread that completes it, thus a listener which
completes two of those ends two items in the transaction of that send. A loader
that collects keys, asks one time, and answers each item that waits has that
shape.

The decision is one for each transaction, and the sends are not. A results sink
and an errors sink come from a caller, and a SodaFlow sink with no coalesce
function refuses a second send in one transaction. Thus, each outcome that the
pipeline publishes gets a transaction of its own, in the sequence of the
admissions, which is what a consumer of those streams saw before this release as
well.

A custom strategy that published every outcome returns ItemsOf(ended), which is
a static method on the base class, in place of true. One that published none
returns AsyncStrategyResult<TInput>.PublishNone in place of false. One that
decided for each item builds the list. A strategy that starts the next Queued
item must also test the queue, because more than one item can end at one
moment: Queue in this library starts nothing while an item is Running, and
QueuePerGroup starts one item for each group that becomes free.

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

BREAKING: the operation of a MapAsync call is a MapAsyncOperation<TInput,
TResult> delegate, where it was a Func<TInput, CancellationToken,
Task<TResult>>. It takes a third argument, a ResultFactory<TResult>,
and answers with what that factory makes:

  resultFactory.FromValue(value)        a value the operation has
  resultFactory.Construct(() => .) a function the pipeline calls

The second one is the cause for the change. The pipeline calls that function
in the transaction that sends the result, so a result that holds a cell, a
stream, or a view model built out of them comes into existence in that
transaction. An operation runs outside a transaction and cannot do that for
itself.

Keep such a function short, because it holds the transaction while it runs, and
put no effect in it that matters outside the value it returns: the pipeline
calls it only for an item that it publishes, and a strategy can decide not to
publish. A throw from it goes to the errors stream in place of a result.

A lambda takes the third parameter and compiles. A stored Func does not convert
to the delegate and needs its own edit.

BREAKING: Admit and OnCompleted take one more argument, the queue of the
pipeline: each item that it tracks, Queued or Running, in the sequence of their
admissions, as an IReadOnlyList<AsyncTrackedItem<TInput>>. An entry holds the
AsyncQueuedItem that the strategy received at the admission, which is the same
instance, and the status of that item now.

A strategy that schedules on the sequence alone thus keeps no queue of its own.
Two limits decide what the next item is: in Admit the list does not hold the
value that the pipeline admits now, and in OnCompleted it holds no item that
ends now. It does not change while the call runs, and an item that the call
starts is Queued in it.

Each custom strategy takes the new parameter, also one that does not read it.

Queue and QueuePerGroup in this library read that queue now and hold no queue of
their own. Their behavior does not change - the same sequence, the same
treatment of an item that a cancellation removed before its turn - and a
measurement of 5,000 items through each one gives the same time as before, in
one group and in 500 groups. The state type of Queue is gone with its queue, and
the state of QueuePerGroup holds the group comparer alone.


BREAKING: a strategy no longer reads a result. OnCompleted takes an
AsyncCompletion - the operation returned, it threw, with the exception, or a
cancellation stopped it - in place of an AsyncOutcome<TStrategyResult>. The
pipeline asks the strategy first and makes the result after, and only for an
item that it publishes, which is what the paragraph above describes.

The result type of a strategy existed to carry that value, so it is gone with
it:

  AsyncConcurrencyStrategyBase<TInput, TResult>
      becomes AsyncConcurrencyStrategyBase<TInput>
  AsyncConcurrencyStrategy<TInput, TResult, TState>
      becomes AsyncConcurrencyStrategy<TInput, TState>

MapAsyncImpl loses its TStrategyResult and its resultConverter with them. None
of the strategies in this library read a result, and a strategy that wants to
select what reaches a consumer can filter the results stream instead.

BREAKING: AsyncMapStatus<TInput> is a class, where it was a readonly struct, and
it now extends a new non-generic AsyncMapStatus that carries IsRunning and
Dispose. Items stays on the generic type, because only that part depends on the
input type.

The point of the split is the caller that never reads Items. It can hold an
AsyncMapStatus and does not have to name the input type to do it - a view model
that shows a busy indicator and disposes the pipeline is the usual case.

Source that names AsyncMapStatus<TInput> and reads its members needs no edit.
Anything compiled against 4.x does, because a struct and a class are not the
same type to the runtime, which is what makes this a major. Two smaller changes
come with the type: the value is a reference now, so default(AsyncMapStatus<T>)
is null where it was a zeroed value, and two of them compare by reference where
they compared field by field.

4.0.1

Adds the package icon that nuget.org shows beside this package.

One expression in MapAsyncUtility is written on a single line where it was split
across two. Nothing else changed, and nothing behaves differently.

Every package here ships this release together, so the dependency versions
move with it.

4.0.0

No API change of its own. This release moves to SodaFlow.Core 4.x, and
is a major because taking it obliges a consumer to take that.

Nothing in this package's own code changed but its layout: file-scoped
namespaces in place of braced ones, which moves every line left by four
columns and alters no behavior.

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
requires SodaFlow.Core 3.x, where it required 2.x before.

That ceiling is why this is not a minor version. Taking this release obliges
a consumer to take SodaFlow.Core 3.x as well, and one who names
SodaFlow.Core directly, or who uses anything removed there, cannot adopt it
without changing their own code. A version they cannot adopt is not a minor
one.

2.0.1

Adds the release notes below. 2.0.0 shipped without any, because the mechanism
that reads them from a file landed after that version was tagged. No code
change since 2.0.0.

2.0.0

No public API change. Major because this assembly calls SodaFlow.Core's
internals, and 2.0.0 of the core renamed them: ListenWeakImpl became
ListenImpl, while ListenImpl kept its name and changed meaning from the strong
listener to the weak one.

That makes the pairing load-bearing in both directions. Against a 1.0.0 core
this version throws MissingMethodException; a 1.0.0 of this package against a
2.x core binds to the strong listener instead and roots the whole async
pipeline for good. Upgrade the two together.

---

About this package

The engine behind MapAsync: the tracking, the concurrency strategies and the
AsyncMapStatus a caller holds, which is non-generic for IsRunning and the
disposal, generic in the input type for the tracked items also, and generic in the
input type and the result type for Execute also. An operation uses Execute to
drive a second pipeline. An operation answers with a
MapAsyncResult, which carries a value or a function that this engine calls in
the transaction that publishes. Not installed directly - take SodaFlow.Async for
C# or SodaFlow.FSharp.Async for F#, both of which bring it with them.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
