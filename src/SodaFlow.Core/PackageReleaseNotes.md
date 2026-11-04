6.0.0

BREAKING: SampleLazy returns a LazySample<T>, and HoldLazy, AccumLazy, CollectLazy, and ConstantLazy take one, where
each used a Lazy<T>. Only SodaFlow reads a LazySample, at the end of the transaction that asked for it, so a
transaction that fails can forget the value the sample took. A Lazy<T> keeps the first value it gives, and that was
the defect below. Map transforms a LazySample, Lift combines two, three, or four, and LazySample.Defer makes one from
a function, for an initial value that is not a sample of the graph. A function given to any of them runs once for
each value, however many cells start from the sample, and runs again after a transaction that failed, so it must be
pure and must not send. A sample that forces itself, which a loop that is not closed can cause, now throws an
InvalidOperationException whose message says so; before, Lazy<T> reported a ValueFactory that accessed its own Value.

A send on a sink from outside a transaction allocates two objects fewer: the transaction's send queue holds an entry
with the sink and the value as fields, where it held a closure and a delegate, and the send enters the transaction
through a static function with a state, where it captured the sink and the value in a second closure. Measured on a
send to a sink with one listener, .NET 10: 192 to 183 ns and 680 to 544 bytes.

A transaction whose first entry in the priority queue has a deep rank no longer walks every empty bucket below that
rank. The queue starts each transaction empty with its floor at zero, so a send whose only listener sat at rank one
thousand, as the second source of a merge with a deep stream does, walked a thousand buckets before it ran. The empty
floor is now above the ceiling, and the first entry sets it. Measured on such a send with the merge's chain at 10,
100, and 1000 maps, .NET 10: 409, 852, and 4965 ns to 341, 347, and 352 ns. A send to a sink with one listener does
not change.

Merge and OrElse on a list of streams, and mergeAll and orElseAll in F#, now go through one node. Each input keeps
its value of the transaction in a slot of its own, and one action at the output, queued when the first value of the
transaction arrives, folds the slots in list order and sends once. Before, the list became a balanced tree of pairwise
merges, each a coalesce node, so a value crossed about log2 n nodes on its way out. The rule for simultaneous values
does not change: the value from the stream earlier in the list is the left argument of the function, whatever the
order the inputs fired in, and a stream that fires more than once in a transaction folds its own firings first, in
their order. Measured on an edit through the first of 1, 2, 8, and 32 edit streams of a collection, which merges them
this way from SodaFlow.Core.Collections 3.0.0, .NET 10: 1.52, 1.71, 2.10, and 2.58 µs with the tree, and 1.51, 1.65,
1.70, and 1.67 µs with the node.

Lift of two to eight cells or behaviors, Lift of a list, Apply, SnapshotLatest, MergeMaybes, and Merge of two streams
with a function each go through one node. Before, the inputs of each sent into a pulse stream, a coalesce on that
stream made the one firing of the transaction, and a listener on the coalesce sent to the output, so an update
crossed three nodes and three listener hops for one piece of work. Apply brought each input in through Value, which
is itself a stream and a coalesce, and had a coalesce of its own on the output, six nodes in all. Now each input
listens into the output node, and a gate queues one action there at the first update of the transaction; the action
reads what each input captured and sends once. The lift of a list also stops allocating per update: it sent a
closure for each input that updated and combined them into one delegate, and it now keeps a slot for each input.
Nothing a caller can see changes: one firing per transaction, the captured value of each input that updated and the
current value of each that did not, and a transaction that fails discards the queued action. Measured on a send to
one input, .NET 10, time and bytes allocated: Lift of two 627 to 430 ns and 1,304 to 928 bytes, Lift of six 640 to
445 ns, both inputs of a Lift of two in one transaction 922 to 694 ns, Lift of a list of six 697 to 475 ns and 1,496
to 976 bytes, Apply 760 to 442 ns on the argument and 769 to 463 ns on the function, Merge of two with a function
326 to 276 ns, SnapshotLatest 421 to 280 ns on the stream and 405 to 265 ns on the cell, and Merge of two types 457
to 309 ns. On .NET Framework the figures are about twice those and move the same way.

Fixed: a strong listener on a stream with more than one input is rooted by every root of that stream, so it lives while
any of them does. Before, Lift, Apply, Merge, OrElse, MergeMaybes, SwitchB, SwitchC, and SwitchS took the keep-alive set
of their first input only. A listener on a lift whose first cell the application dropped, while it kept sending into the
second, was collected and stopped without a message, and a switch over a constant cell,
Cell.Constant(s).SwitchS().ListenStrong(h), was an unrooted cycle from the start. A switch now moves its listeners to
the root of the inner it selects on each switch, so the listener lives while the outer cell or the selected inner lives,
and Unlisten still releases the listener from every root. The cost is one small object per such stream and one entry per
root per strong listener. Measured on .NET 10, a lift of two cells builds for 2,746 bytes where it built for 2,682, a
lift of six for 4,482 where 4,258, a merge of two streams for 1,694 where 1,590, and a switch for 56 bytes more. The
memory tests pin a lift, a merge, and a switch, each with its first root dropped and its other root kept.

Changed: a throw from the body of Transaction.Run or RunVoid fails the transaction that the call opened. Before, the
transaction still closed: the sends made before the throw ran, every cell they reached took its new value, and the
exception was thrown after that. Now no send made before the throw reaches the graph, each cell keeps its previous
value, a Post with a release queued before the throw gets its release with the exception as the cause, and the throw
continues to the caller. A throw from a nested Run reaches the body of the root transaction and gives the same
result. This is the behavior that a throw from a listener during propagation already had, and the one that "atomic"
promises; code that relied on the partial commit has to send before the code that can throw, in a transaction of its
own.

Added: DependencyCycleException, an InvalidOperationException, is what a loop that closes on itself without a delay
throws, where before it threw a bare System.Exception with the same message, which a caller could not catch apart from
anything else and a test could only match by its text. The message is unchanged. The internal priority queue's
invariant checks, which no caller can reach, throw InvalidOperationException for the same reason; nothing in the
library throws a bare Exception any more.

Added: UtcClockTimerSystem, a timer system over DateTime.UtcNow, and a constructor on SystemClockTimerSystem that
takes the clock as a Func<DateTime>; the existing constructor still reads DateTime.Now. SystemClockTimerSystem counts
in local time, which moves with the machine's clock: a timer set five minutes ahead across the autumn daylight saving
change waited sixty-five, and one set across the spring change fired an hour early, and Time stepped back an hour.
UTC does not move, so a wait or a deadline belongs on the UTC one and the local one is for a time a screen shows.
The clock constructor is also the shortest way to put a clock a test moves by hand under a real TimerSystem<DateTime>.

Building a graph costs less. A stream kept two lists from its construction, one of the listeners attached to it and
one in the cleanup registry, and a stream in a chain has exactly one of each; both now hold the first listener in a
field and make the list only at the second. A node did the same with the hash set of its listeners, which was a set
object and two arrays for the one listener a node in a chain has; it now holds its first listener in a field and makes
the set at the second link. The internal Listen that every single-input operator calls once passes its arguments to
the transaction as a state instead of capturing them in a closure. Measured in a new benchmark, GraphBuildBenchmarks,
per thousand nodes built in one transaction, in one session, .NET 10: a chain of maps 1.57 ms and 909 KB before, 1.06
ms and 488 KB after; a map with a hold 2.23 ms and 1,442 KB to 1.90 ms and 1,200 KB; a lift of two cells 4.82 ms and
3,089 KB to 3.90 ms and 2,935 KB; a constant cell 793 to 645 µs; a sink alone 442 to 388 µs. .NET Framework 4.7.2
moves in proportion: the chain 1.52 to 1.25 ms over the node and the Listen change alone. The benchmark has an arm for
each common shape, so a later change to construction can be measured against this one.

A hold, a lazy hold, and the state under Accum, Collect, and Calm no longer allocate delegates for the actions a
transaction runs at its end. A behavior, a stream, a lazy sample, and that state are their own commit, discard, and
sample actions, which the transaction holds as objects where it held delegates, and the state under Accum, Collect, and
Calm, which also allocated a delegate in every transaction that fired, now allocates nothing for the protocol. A lazy
sample of a behavior reads the behavior itself in the sample phase, and a sample of a behavior through a function, which
Map, Calm, and the switches take as their initial value, is one object where it was two samples with a closure each.
Value, which is under Cell.Listen, Cell.ListenStrong, Cell.Values, and both switches, is one stream: its updates
arrive at a bare node that links into the output, and the right-wins coalesce runs on the output node, where before a
second stream with its own listener did that one node later. Measured in GraphBuildBenchmarks, per thousand, .NET 10: a
map of one cell 2.22 ms and 2,078 KB to 1.78 ms and 1,340 KB, an accum 2.30 ms and 2,191 KB to 1.94 ms and 1,585 KB,
ListenStrong on one cell 4.84 ms and 2,880 KB to 3.41 ms and 2,224 KB, a switch of a constant cell 36.2 ms and 10,019 KB
to 27.9 ms and 7,491 KB, a lift of two cells 3.68 ms and 2,750 KB to 3.21 ms and 2,441 KB, and a filter view of a
collection of ten 75.5 ms and 17,160 KB to 66.0 ms and 15,483 KB. A send that switches the outer cell of a switch costs
3.79 µs and 4,345 B to 3.03 µs and 3,260 B, and a send into a lift of two is unchanged at 437 ns and 928 B.

A switch builds for half of what it did and switches for less than two thirds. The value of a behavior is a listener
object that delivers to a consumer, and Value is that object with its output stream as the consumer, so Cell.Listen,
Cell.ListenStrong, and Cell.Values no longer allocate a delegate for the send in each transaction that fires. SwitchB
and SwitchC are that object on the outer behavior, and each behavior they select gets one whose consumer sends into the
output, where before the switch built a Value and a listener on it for the outer behavior and again for every selection,
with a closure and two delegates for the commit. SwitchC selects the behavior of each cell through a function on the
switch, where before it mapped the outer cell to a cell of behaviors that only the switch read. Measured in
GraphBuildBenchmarks, per thousand, .NET 10: a switch of a constant cell 27.9 ms and 7,491 KB to 11.9 ms and 3,756 KB,
and ListenStrong on one cell 3.41 ms and 2,224 KB to 3.27 ms and 2,010 KB. In LiftBenchmarks, a send that switches the
outer cell of a switch costs 3.03 µs and 3,260 B to 1.16 µs and 1,858 B on .NET 10 and 4.41 µs and 3,329 B to 1.92 µs
and 1,890 B on .NET Framework.

A constant costs about a hundred bytes. Cell.Constant built a never stream and a behavior that listened to it, with
the node, the registry entry, the link, and the listener of a real hold, for a stream that cannot fire: 627 µs and
957 KB per thousand constants on .NET 10. A constant behavior now holds only its value, and Updates makes the never
stream at the first ask, in the transaction: 12 µs and 102 KB per thousand. Nothing observable changes; a constant's
Updates is still a stream that never fires.

A lift allocates less. Each input of a lift, a snapshot of the latest values, or a merge of maybes had two closures
and two delegates between its stream and the shared gate; it now has one object that holds its slot and is its own
listener action. A lift's output is built through one object in place of three delegates, an array, and a second
lambda for the lazy initial value. GraphBuildBenchmarks, per thousand two-cell lifts, .NET 10: 3.81 ms and 2,998 KB
before, 3.61 ms and 2,688 KB after; .NET Framework 4.7.2: 5.27 to 4.61 ms and 3,036 to 2,721 KB. A lift of six
cells, which the benchmark now has an arm for, gains more: 11.15 ms and 5,010 KB to 10.27 ms and 4,470 KB on .NET 10,
and 12.91 to 11.88 ms on .NET Framework 4.7.2.

Fixed: CleanupNow runs the cleanup action at the call, one time, as its documentation said. Before, it only dropped
the reference to the object, so the action ran at the next garbage collection and sweep, on the cleanup thread, and
nothing a caller could do made it run sooner. A second call now does nothing, and the collection of the object after
the call runs nothing. The action sits behind a guard that the registry and the Cleanup share, and the guard holds no
reference to either, so the collected path works as before.

Fixed: MutableListener.SetListener stops the listener it replaces, and ClearListener stops the current one, as the
documentation on both language surfaces said they did. Before, each only changed the field, so a caller who followed
the documentation kept every listener it replaced, and the only code that did not leak was the library's own
SwitchB and SwitchS, which stopped the old listener by hand. A set of the listener that is already the target stops
nothing. Unlisten is unchanged.

Fixed: a cell with a lazy initial value, a looped cell, the state of AccumLazy or CollectLazy, and the first value of
a bindable no longer keep a value from a transaction that failed after its sample phase. The sample phase, which runs
after every send and every listener, read the pending update of the cell being sampled, and a lazy initial value was
forced there. A transaction can still fail after that, when a lazy initial value throws, and the forced value stayed:
a cell started from a value its source never took, and a per-key cell of a collection, which its cache keeps, showed
an item from a snapshot the failure had discarded. Each of them now forgets what the failed transaction gave it and
reads the committed state at the next use.

Fixed: a transaction that fails no longer leaves state behind that breaks the graph after it. A transaction fails when
something throws while it runs: a listener, a function given to an operation such as Map, or a loop that was never
looped. Sending twice in one transaction to a sink that has no combining function fails it too. Several operations keep
state that only the end of a successful transaction resets, and a failure skipped that reset. The state stayed set, and
from then on:

- A stream that fired in the failed transaction kept the firing. Each listener added after that got it, along with
  every later firing of the stream, because nothing cleared the list again.
- A cell, whether a sink, a Hold, a Map, or a Lift, kept the update that the failed transaction was about to apply, and
  never updated again. Sample went on giving the value from before the failure, whatever was sent to it afterward.
- Accum, Collect, and Calm stopped committing their state, so each later firing started from the state they had before
  the failure.
- A stream sink, and each operation that combines the firings of one transaction into one, such as Merge or Updates,
  went on treating the failed transaction's value as one it was still combining, and fired nothing more. A sink that
  had been sent to twice threw "Send was called more than once in a transaction" on every send after that.

Each of these now discards what the failed transaction left, and the next transaction starts from what the last
successful one committed. A transaction that succeeds does exactly what it did before.

Not changed: a listener that ran before the throw has already seen the failed transaction's values, and nothing takes
them back.

Fixed: Lift, Apply, SnapshotLatest, and the Merge of two streams of different types no longer use a value from a
transaction that failed. Each of these keeps the value of an input that updated in the current transaction in a slot,
and the step that puts the inputs together reads the slots and clears them. A transaction that failed between the
capture and that step never ran it, so the slot kept a value the input never committed, and the next update of another
input used it: a lift of 1 and 10 that saw 100 arrive in a failed transaction gave 120 at the next update of its other
input, not 21, and an apply gave the function from the failed transaction to every later value until the function cell
updated again. A snapshot of the latest values also kept the firing of its own stream from the failed transaction and
sent it at the next update of a cell alone. Each slot now records the transaction that filled it, and gives nothing to
any other, so the step reads the committed value of the behavior instead. This is the rest of the failed-transaction
work in this release: the first part reset the state of each operation at the failure, and this part covers the
values in flight between two operations.

The record costs 8 bytes per transaction, the id on the transaction object, and no measurable time on .NET 10. On .NET
Framework 4.7.2, measured back to back against the code before this change, a send to one input of a lift of two cells
went from 1,241 to 1,265 ns and of six cells from 1,262 to 1,306 ns; a send into a SnapshotLatest or a Merge of two
types is unchanged within 12 ns. LiftBenchmarks in SodaFlow.Benchmarks measures it.

Fixed: SwitchB and SwitchC no longer keep a switch that a failed transaction made. The handler that follows the outer
behavior moved the inner listener to the new behavior as soon as it ran, with nothing to put it back. A transaction
that failed after that point discarded the outer behavior's update but not the move, so the switched value went on
following a behavior the outer one did not hold: with the outer cell still holding c1, an update of c2 reached the
output and an update of c1 did not. The move now commits with the transaction, as the SwitchS move always did: the
new listener is added when the handler runs, so the new behavior's value still reaches the output in that
transaction, and the old listener is removed only when the transaction commits. A failure removes the new listener
instead and leaves the old one in place. The old listener cannot deliver a stale value in the window between the
two, because its update chain ranks below the output node and the engine has already run it by the time the handler
runs; a test pins that ordering too.

Added: the internal TransactionInternal.AfterLast and TransactionInternal.Outcome. AfterLast takes an action that runs
after every last action, with an alternative for a transaction that fails that runs after every discard. Outcome is
a small object a transaction allocates on first request, with three states: unknown until the transaction has run its
last actions or started its failure path, committed once the last actions have run, and failed from the start of the
failure path, before any release or discard runs. Together they let an action that a transaction hands to another
thread learn, when it runs there, whether the transaction committed, and read the cell when it cannot yet know.
SodaFlow.Bindable.ObjectModel.Core 4.3.1 uses them so that a one-way bindable value never shows a value from a
transaction that failed.

Fixed: a step of the graph that threw could run again in a later transaction. The engine runs the queued steps of a
transaction in order of rank, and a step stayed in a list on its node until it had finished, so that a change of the
node's rank could move it in the queue. A step that threw never finished, so it never left the list. The next time the
node's rank changed, in any later transaction, that step went back in the queue and ran there, and it could throw again
and fail a transaction that had nothing to do with it. A node's rank changes when it gets a link from a node of higher
rank, which a switch or the close of a loop can make. A step now leaves the list before it runs.

The same change removes a wasted step. A step can raise the rank of its own node while it runs, which SwitchC and
SwitchB do when they switch to an inner cell of higher rank than their output, and that put the running step back in
the queue, so it ran a second time in the same transaction. A Map on the switched cell called its function twice, with
the same value both times, and a listener on the cell still saw one update. Only a function with side effects, which a
Map must not have, could tell.

Fixed: ListenOnceImpl and ListenOnceStrongImpl, which the language packages give as ListenOnce and ListenOnceStrong,
could call the handler twice when the stream fired on another thread. The listener stops itself through its own handle,
and it got that handle only after the transaction that attached it had closed and released the lock. A firing on
another thread in that gap ran the handler but could leave the listener attached, and the next firing ran the handler
again. A stress test that called ListenOnceStrong over and over on a sink while another thread sent to it saw the
handler run twice 726 times in 163,783 calls. The listener now gets its handle inside the transaction that attaches it,
so nothing can fire in between, and the same test saw no double call in 137,524 calls. It also costs less: Listen
now runs in that one transaction rather than opening a nested one, so each call allocates 112 bytes less on .NET 10
and 176 to 177 bytes less on .NET Framework 4.7.2, and takes the same time or less. ListenOnceBenchmarks in
SodaFlow.Benchmarks measures it.

The handler also ignores a second firing in one transaction now, as OnceImpl does. No stream in the public API fires
twice in one transaction, so that part guards against a defect in this library rather than anything a caller can do.

OnceImpl loses a branch that could never run. What it does is unchanged.

Removed: the internal NoListener type, which nothing uses any more. No released version of any package here calls it.

Added: the internal IRootableListener and ListenOnceRootableImpl, which gives one. It is a one-shot listener that is
weak until Root is called and is held by the stream after that, until its first firing or Unlisten. A Root after either
of those does nothing. SodaFlow.FSharp's listenOnceAsync uses it so that an async that never runs leaves no listener
behind.

Fixed: a long chain of operations could crash the process with a stack overflow. When a link gives a node a higher
rank, every node downstream of it needs a higher rank too, and the engine walked them with a recursive call per node.
Closing a loop onto a stream of higher rank does this for the whole loop, and a loop over a chain of 10,000 maps
overflowed the stack. .NET cannot catch a stack overflow, so the process ended. The walk now keeps its own stack on the
heap. It visits the nodes in the same order and gives each the same rank as before, and a cycle still throws the same
exception. A loop over 50,000 maps now closes, and a value sent through it arrives.

The stack is one array that every walk reuses, kept only while it holds 1,024 levels or fewer, so a chain of that depth
or less allocates nothing for it. RankPropagationBenchmarks in SodaFlow.Benchmarks measures the walk, and a chain of up
to 1,000 maps costs the same time and memory as before, within the noise of the runs.

Added: the internal TransactionInternal.AfterReleaseImpl. It runs an action on the calling thread once the outermost
transaction has released the transaction lock, including when that transaction failed. Outside a transaction it runs
the action at once. Actions that throw do not stop the ones after them, and their exceptions come after the
transaction's own, so no failure is lost. SodaFlow.Core.Async 5.0.2 uses it to start its operations outside the lock.

Every outermost transaction now checks for such actions as it releases the lock. On .NET 10 that has no measurable
cost. On .NET Framework 4.7.2, measured back to back against the code before this change, an empty transaction went
from 92.1 to 100.0 ns and a send to a sink with one listener from 337.2 to 358.6 ns. Allocation is unchanged on both.
TransactionBenchmarks in SodaFlow.Benchmarks measures it.

Added: the internal ChooseImpl and ChooseInternal, which map and filter in one node where a map followed by a
FilterSome built two. The language packages' Choose and choose use the first, and the per-key cells of
SodaFlow.Core.Collections use the second, where it takes about a kilobyte off each cell. FilterSomeInternal, which
takes this library's own optional type, no longer allocates a closure for every firing; the FilterSome of each language
package is unchanged. The internal MaybeInternal.MatchSome, which that closure was the last use of, is removed.

Changed: queuing a step of the graph no longer takes the node rank lock. Every step that an operation queues in a
transaction, which is one per map, filter, or listener that a firing reaches, took and released that lock, and
nothing it guarded could be touched by any thread but the one holding the transaction lock: the queue is read, drained,
and reordered only on that thread, and a rank changes only inside a transaction. Measured back to back against the
code before this change, a send through a chain of 32 maps went from 3,828 to 3,734 ns on .NET 10 and from 5,415 to
5,036 ns on .NET Framework 4.7.2, and a send to a sink with one listener from 210 to 201 ns and from 347 to 322 ns.
Allocation is unchanged. TransactionBenchmarks measures the chain. The lock itself is gone: linking a node to its
target, the one other place that took it, runs under the transaction lock for the same reason.

Fixed: a start hook can now register a start hook. The hooks ran in a foreach over the list they are registered in,
and a registration from inside a hook changed that list and threw "Collection was modified". The hooks now run by
index, up to the count at the start of the run, so a hook registered from inside a hook runs from the next transaction
on.

Fixed: work queued from a last action no longer leaks into the next transaction. A last action runs after the
transaction has drained its queues, so a prioritized step it queued had nothing left to run it. The step stayed in the
engine's queue and ran at the start of the next transaction, whichever thread opened it, and a send or a sample from a
last action was silently dropped. The transaction now fails with an InvalidOperationException that says so, and the
failure discards the step, so the next transaction starts clean. Nothing in this library's own last actions does this;
the check is there to make the mistake loud rather than silent. A last action can still queue a last action, which runs
in the same drain, and a post, which runs after it.

5.0.0

Adds StreamExtensionMethodsInternal.DebounceImpl, which each language surface gives as a debounce of a stream. It fires
the last value after a time with no other value, and each firing moves the alarm out. The method takes the clock, a
function that makes an alarm from a cell, a function that arms a deadline, and the value for no deadline. Thus, it holds
no optional type, as FilterSomeImpl does not.

PostImpl takes one more argument, for the packages that reach these internals and for the public Post that SodaFlow and
SodaFlow.FSharp give: the action to run where the posted action does not run or does not complete. A transaction that
fails while it propagates discards each action that Post holds. Code that gives a value to something which waits, and
gives that value from a posted action, had no way to release it, thus the waiting code waited forever.

The release runs one time, and only where the posted action does not complete. Two conditions give that: the transaction
fails before it runs the action, and the action itself throws. The argument is the exception of the transaction in the
first condition, and the exception of the action in the second. An action that completes runs no release.

A throw from a release does not stop the release of another posted action, and it does not replace the exception that
caused it. The caller gets an AggregateException with that exception first and the throw from the release after it, thus
no code loses a failure.

The release belongs to one posted action, at the call that posts it, and not to a transaction. Thus, no code can ask for
a release with nothing to release, and none must find the transaction that is open to ask.

SodaFlow.Core.Async is the first caller. Its Execute gives a Task to code that waits, and it gives the value of that
Task from a posted action.

Adds SnapshotLatestImpl, for the SnapshotLatest that SodaFlow and SodaFlow.FSharp give. It samples a behavior when a
stream fires, as SnapshotImpl does. SnapshotImpl gives the value from before the transaction. SnapshotLatestImpl gives
the value at the end of the transaction.

It uses the mechanism of LiftImpl. The stream and the updates of the behavior send into one pulse. A coalesce operation
that ranks above both reads the captured update. Thus, the behavior can update before the output fires. A stream that
fires more than once in a transaction gives one output firing for each, as SnapshotImpl does.

SnapshotLatestImpl takes up to eight behaviors or cells. All of its forms use one private method. Each form gives a
capture for each behavior, a function that reads the captured values, and the function that puts them together. The
captures are StreamInternal.Pulse on the updates of each behavior. The reads are Behavior.Take, which LiftImpl also
uses, and which is internal now and not private.

Adds MergeMaybesImpl, for the Merge of streams of different types that SodaFlow and SodaFlow.FSharp give. It takes two
to eight streams and a function that gets one MaybeInternal for each stream. The mechanism is the one of LiftImpl, with
streams in place of the updates of behaviors. Each stream captures its firing and sends into one pulse. A coalesce
operation that ranks above the pulse makes one output firing from the captured values. This is less expensive than a
merge of mapped streams, and no stream has priority.

LiftImpl takes up to eight behaviors or cells, where it took up to six, and SnapshotImpl takes up to eight, where it
took up to four. They are for the wider Lift and Snapshot that SodaFlow gives. Each new form is the form before it with
one more input.

The name is not MergeImpl. With that name, the F# type inference at the call in mergeOptions2 also uses the MergeImpl
for streams of one type. Thus, mergeOptions2 accepts only two streams of one type. The F# package builds without an
error, and a call with two types fails only in the code that calls it.

StreamInternal.Pulse is new. It connects a stream to a pulse, and it takes the place of the private Behavior.Pulse,
which connected only the updates of a behavior. LiftImpl, SnapshotLatestImpl, and MergeMaybesImpl call it, and give it
the updates of a behavior where the input is a behavior.

BREAKING for the packages that reach these internals: AttachListenerImpl is named AttachListenerInternal. The Impl
suffix here marks a method that a public extension forwards to, and the extension that forwarded to this one is gone
from SodaFlow and SodaFlow.FSharp, so the name follows the one HoldInternal and HoldLazyInternal use for a method with
no public counterpart. Only those two packages called it, and both move with this release.

BREAKING for the packages that reach these internals, though this assembly has no public API of its own to move:
ListenOnceImpl returns IWeakListener, where it returned IStrongListener, and ListenOnceStrongImpl is new and returns
what ListenOnceImpl used to. Internal is not private here - this assembly grants InternalsVisibleTo to the shipping
packages around it - so a SodaFlow or a SodaFlow.FSharp built against 4.x names a method whose return type has changed
and cannot bind to it at run time. Both of those packages carry 5.0.0 as their floor. No other package calls either
method.

The two one-shot listeners are one generic method now rather than two copies of the same thirty lines, which is what the
split would otherwise have created. Nothing about the sequence changed: the listener stops itself at the first value, a
value that the call to listen sends again unlistens early, and the caller gets a listener that does nothing in that
case.

This is the last of the rename that 2.0.0 began. That release swapped ListenImpl to the weak listener and removed
ListenWeakImpl; ListenOnceImpl kept the old meaning until now, and was the one name left where the short form rooted the
stream.

4.0.2

Adds the package icon that nuget.org shows beside this package. No source file changed since 4.0.1.

4.0.1

Grants InternalsVisibleTo to the three collections assemblies:
SodaFlow.Core.Collections, SodaFlow.Collections and SodaFlow.FSharp.Collections.

Required rather than cosmetic, for the reason 2.1.0 was. The CLR checks the friend-assembly attribute on the assembly
that declares the internals, so those three throw MethodAccessException at run time against a 4.0.0 core, even though
they build cleanly from source, where the grants are present. Anything depending on SodaFlow.Collections or
SodaFlow.FSharp.Collections needs this version, and both carry it as their floor.

No other change, public or internal.

4.0.0

BREAKING: internal members are gone or changed shape. Internal is not private here - this assembly grants
InternalsVisibleTo to the shipping packages around it, so what it declares internal is API to them, and removing any of
it is a break they have to be rebuilt against.

Gone: TransactionInternal.IsActiveImpl, its parameterless constructor, Close and the Post overload; the Utilities class
and its Yield; MaybeInternal's NoneType, the None field and the implicit conversion from it; the three Lazy overloads of
LazyExtensionMethodsInternal.LiftImpl; LazyBehavior's LazyInitialValue; TransactionInternal's ActionEntry and its
Entry.IsRemoved. TransactionInternal.Split returns void where it returned UnitInternal, which is a different method to a
compiler and to the runtime.

Added: the NoListener type, and an Equals overload on MaybeInternal taking its own type rather than object.

The public surface is unchanged. Every type this package exposes - Stream, Cell, Behavior, their sinks and loops - has
the same members it had in 3.0.0, so code which only uses the published API needs nothing but the recompile a new major
asks for.

A major rather than a 3.x, and the version is doing real work. Published SodaFlow.FSharp 3.1.0 calls
TransactionInternal.IsActiveImpl and depends on this package as [3.0.0, 4.0.0). Shipped as 3.0.1 or 3.1.0 this would
resolve against that package and fail when the call was made; shipped as 4.0.0 it falls outside the range and NuGet
declines the pairing instead. That is the whole reason the ranges carry an upper bound.

Requires System.ValueTuple 4.6.2, where it required 4.4.0. Nothing here uses it differently: this repository named two
versions of it, one in the shipping projects and one in the test projects, and now names a single version in both. A
consumer does nothing about this; NuGet resolves the higher floor.

3.0.0

BREAKING: FilterMaybeImpl and FilterMaybeInternal are renamed to FilterSomeImpl and FilterSomeInternal, following the
rename in SodaFlow and SodaFlow.FSharp.

These are declared internal, but internal is not private here. This assembly grants InternalsVisibleTo to eight shipping
packages - SodaFlow, SodaFlow.FSharp, SodaFlow.Core.Async, SodaFlow.Async, SodaFlow.FSharp.Async and the three bindable
object model packages - so anything internal is API as far as they are concerned, and renaming one breaks them exactly
as renaming a public member would. A wrapper compiled against the old names throws MissingMethodException against this
version.

That is why this is a major version even though nothing changed for a caller outside those eight.

No one has to remember that. Every package here now declares the range of this one it was built against, bounded at the
next major, so NuGet refuses a pairing that would fail rather than resolving it and leaving the failure until someone
runs the code. SodaFlow and SodaFlow.FSharp ship alongside this release; the async and bindable object model packages
follow with compatibility releases, each of which moves a dependency range and nothing else.

2.1.1

Adds the release notes below. 2.1.0 shipped with the repository-wide notes instead, which described the 2.0.0 listen
rename and said nothing about what 2.1.0 actually changed. No code change since 2.1.0.

2.1.0

Grants InternalsVisibleTo to the three bindable object model assemblies:
SodaFlow.Core.Bindable.ObjectModel, SodaFlow.Bindable.ObjectModel and SodaFlow.FSharp.Bindable.ObjectModel.

Required rather than cosmetic. The CLR checks the friend-assembly attribute on the assembly that declares the internals,
so those three throw MethodAccessException at run time against a 2.0.0 core - even though they build cleanly from
source, where the grants are present. Anything depending on SodaFlow.Bindable.ObjectModel needs this version.

No API change otherwise.

2.0.0

Fixed: Cell.updates is volatile, so its lazily created stream is published safely on weak memory models. It was read
outside the transaction lock with nothing ordering the write, letting a reader observe a partly constructed object on
arm64.

Calm is expressed over CarryState rather than duplicating its state protocol, and a throwing transaction now releases
all five of its queues rather than three.

Breaking for the packages that reach these internals, though no public API moved: ListenImpl kept its name and swapped
from the strong listener to the weak one, and ListenWeakImpl is gone.

---

About this package

The engine: Stream<T>, Cell<T>, Behavior<T>, Transaction, the listener interfaces, and the dependency graph that makes
updates atomic. Language neutral, and not installed directly - take SodaFlow for C# or SodaFlow.FSharp for F#, both of
which bring it with them and expose its operations.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
