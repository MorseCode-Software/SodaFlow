6.0.0

BREAKING: SampleLazy returns a LazySample<T>, and HoldLazy, AccumLazy, CollectLazy, and ConstantLazy take one, where
each used a Lazy<T>. Only SodaFlow reads a LazySample, at the end of the transaction that asked for it, so a
transaction that fails can forget the value the sample took. A Lazy<T> keeps the first value it gives, and that was
the defect below. Map transforms a LazySample, Lift combines two to eight, and LazySample.Defer makes one from a
function, for an initial value that is not a sample of the graph. A function given to any of them runs once for
each value, however many cells start from the sample, and runs again after a transaction that failed, so it must be
pure and must not send. A sample that forces itself, which a loop that is not closed can cause, now throws an
InvalidOperationException whose message says so; before, Lazy<T> reported a ValueFactory that accessed its own Value.

BREAKING: IListener has one member, Unlisten. GetListenerWithWeakReference is gone from it, and the
IListenerWithWeakReference that it returned is internal. The weak view lets the library stop the listeners attached to a
stream once the stream is collected, without keeping the stream alive to do so. That is the library's own work, and a
caller that implemented IListener, as the composite factories invite, had to write a method whose contract was the
library's. Every listener the library makes still has its weak view, now behind an internal interface, and a stream
attaches only that interface, so the compiler checks that each attached listener has one.

BREAKING: MutableListener is internal, and Cleanup is removed. The switches hold their inner listener in a
MutableListener, and nothing outside the library needed one; its ClearListener, which only callers outside the library
used, is removed with it. Nothing in the library used Cleanup. It ran an action when the garbage collector removed it,
at a time the collector chose, and its own documentation advised IDisposable for a release at a known time.

BREAKING: Cell<T> derives from Behavior<T>. A cell was a second object that held a behavior; now it is the behavior, so
a cell goes wherever a behavior is expected, and each cell is one object fewer. A behavior that an operation derives is
still not a cell, even when a cell feeds it, so a cast cannot recover the steps of a value whose type says it is
continuous. LoopedCell<T> is a Cell<T> and no longer holds a LoopedBehavior<T>. Behavior<T> loses three protected
members, ValueProperty, UsingInitialValue, and UseInitialValueAgain, which nothing outside the library could reach,
because its constructors are internal.

Changed: a looped behavior sampled before its loop closes throws an InvalidOperationException whose message names
LoopedBehavior, where it named BehaviorLoop, which the C# package removes in this release.

Changed: a looped cell sampled before its loop closes throws an InvalidOperationException whose message names
LoopedCell, where it named LoopedBehavior.

A map or a lift of a cell or a behavior is its own node, where it was a hold of an output stream: the operator sent into
a stream, and the behavior listened to it. The value now arrives at the behavior directly, the initial value comes from
the operator without a lazy sample, and the stream of the updates is made only when something asks for it: a listener, a
lift or other operator on top, or Updates and Values. What a caller sees does not change, including the value a listener
gets when it asks for the updates in the transaction that changed them. Measured on building a thousand, .NET 10: a map
of one cell 900 to 625 KB and 1.20 to 0.79 ms, a lift of two cells 1,469 to 1,163 KB and 2.13 to 1.51 ms, and a lift of
six cells 3,184 to 2,824 KB and 6.54 to 5.00 ms. A send through a lift allocates what it did.

A firing no longer takes a lock that the whole process shares to read the listeners of each stream it reaches. Each node
keeps a snapshot of its listeners, which changes only when a listener is added or removed. A send now reads the snapshot
without the lock, and takes the lock only to build a new one after a change. Measured on .NET 10: a send to a sink with
one listener 171 to 162 ns, through a chain of eight maps 699 to 627 ns, to a fan-out of eight maps 1,045 to 964 ns, and
to one input of a lift of two cells 443 to 422 ns. On .NET Framework 4.7.2 the chain goes from 1,867 to 1,776 ns. A send
allocates what it did.

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
memory tests pin a lift, a merge, and a switch, each with its first root dropped and its other root kept. A switch over a
loop can reach its own keep-alive set again through its inner or its outer. A view model that a ForwardReference builds,
and that switches onto its own output, is an example. A walk with a visited set finds the roots of a strong listener, so
the walk ends where the loop comes back. A switch that selects a new inner walks again for each strong listener that
passes it, and changes only the roots that changed. Thus, Unlisten releases the listener from every root, also after a
switch in its loop moved out of the loop. All keep-alive sets share one lock, so two threads in one loop cannot wait for
each other. The memory tests pin both loops.

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

A link is two objects and a stream is three. The target that a node holds for each link is also the weak view of the
listener that made the link: it holds its source node and removes itself from it, where before a third object held the
node and the target for that. The node of a stream holds the registry entry of the stream, the weak handle and the views
of the attached listeners, where before an object of its own held them and the registry held that; the registry now
holds the nodes. A node registers when the first listener is attached to its stream, because the registry exists to stop
the attached listeners of a dead stream, and a stream with none has nothing to stop: a sink takes no weak handle and no
registry slot at all. Measured in the scratch harness on .NET 10, a map in a chain is 465 to 417 bytes, and a full
garbage collection with 100,000 live map nodes takes 32.7 ms to 26.6 ms, because each map is two objects fewer. In
GraphBuildBenchmarks, per thousand, .NET 10: a chain of maps 1.04 ms and 481 KB to 0.95 ms and 435 KB, ListenStrong on
one stream 371 µs and 579 KB to 347 µs and 555 KB, and a sink 395 µs and 437 KB to 52 µs and 367 KB.

The registry sweep after a young collection no longer walks the live graph. The sweep ran after every collection and
read every entry under the registry lock, whether or not anything had died, which was 1.1 ms at 75,000 live streams and
4.2 ms at 300,000 on .NET 10, about a hundred times a second under load, and every stream that registered waited on that
lock. The registry now keeps its entries by garbage collector generation. A stream in generation N can die only in a
collection of generation N or higher, so the sweep after a collection of generation N examines generations 0 to N, and a
sweep after a young collection takes about a microsecond at any size. A full sweep costs what it did. A loop that builds
a map and sends, with 300,000 live streams in the registry, runs 24,000 pairs a second before and 52,000 after.

Transaction.Run and Transaction.RunVoid allocate nothing of their own. Run wrapped its function in a closure and a
delegate for the state-passing Apply, and RunVoid wrapped its action in a second closure and delegate before that, so
an empty RunVoid inside an open transaction cost 176 bytes for nothing. Both now pass the caller's delegate as the
state. Measured in the scratch harness on .NET 10: an empty RunVoid from outside a transaction 55 ns and 288 bytes to
36 ns and 112 bytes, a Run that returns a value 42 ns and 200 bytes to 35 ns and 112 bytes, and an empty RunVoid
inside an open transaction 27 ns and 176 bytes to 6 ns and nothing. The 112 bytes that remain are the transaction.
The F# Transaction.run goes through the same path.

A list lift copies its inputs once. The enumerable overload copied them to an array and then the overload it called
copied that array again, and the cell overloads went through the same path with a Select in front, so a lift of
cells paid two copies and a lift of behaviors from a collection one. Every overload now hands one array to the lift,
which is 8 bytes per input less: a lift of 1,000 cells from an array is 532 KB to 524 KB on .NET 10, and one of 4,000
is 2,120 KB to 2,088 KB.

A merge of two streams, and so OrElse, no longer goes through the list merge. The list merge is one node for n inputs,
which was right for n, but it allocates the input array, a slots array, a closure and a delegate for each input
because the index is captured, and a closure for the fold, and a merge of two is the common case. A new MergeTwo holds
the two slots in fields and gives its methods as the delegates, with the same semantics: the left value goes to the
left of the function when both fire, an input that fires more than once in a transaction folds into its own slot in
order, and a failed transaction clears the slots. The list merge routes two inputs to it as well. Measured in the
scratch harness on .NET 10: a merge of two with a function 1,562 bytes to 1,362, OrElse 1,642 to 1,442, and a list
merge of two 1,726 to 1,638, against 425 for a map; a list merge of three or four is unchanged. The second link, the
gate, and their delegates are what remains, so a merge stays about three maps.

A merge of two streams of different types, the Merge overload that gives a tuple of two Maybe values, takes the same
step. It went through the merge of maybes for n inputs, which captures each input in an object of its own and adds an
array of them, a closure that makes the array, and a closure that reads them. A new MergeTwoMaybes holds the two slots
in fields and gives its methods as the delegates, with the same semantics: each slot keeps the last value its input
sent in the transaction, the gate reads the two once, and a slot a failed transaction filled gives no value. Measured
in the scratch harness on .NET 10: 1,559 bytes to 1,287. A merge of three or four streams of different types is
unchanged.

Cell.Calm opens one transaction when called outside one, where it opened four: one each for the two samples, the
updates, the calm, and the hold, so four lock acquisitions, four runs of every start hook, and four transaction
objects for one calm. The steps now share one transaction, and Accum's hold and a stream's Calm no longer open a
nested Apply that found the transaction open and allocated a closure for nothing. Nothing observable changes, since
the samples are captured at the end of whichever transaction closes them either way, and a calm built from outside is
now atomic with respect to a send from another thread. Measured in the scratch harness on .NET 10, built from outside
a transaction: a cell calm 4 transactions to 1, 3,085 bytes to 1,733, and 3,320 ns to 2,030; an accum 1,673 bytes to
1,577; a stream calm 1,209 bytes to 1,121. A cell's Listen and ListenStrong go through one transaction as before and
allocate the same.

Transaction.Post allocates nothing of its own when no transaction is open, where it allocated four objects: two to
wrap the action in the delegate type of the post queue, and two to capture it for the transaction it opened. The
action runs in that transaction as before. When a transaction is open, the post still queues one closure, where it
queued the same two wrappers and the closure. Measured in the scratch harness on .NET 10: a post from outside a
transaction 296 bytes to 152, which is the transaction itself; a post from inside one 536 bytes to 456. The bindable
values build on this, and SodaFlow.Bindable.ObjectModel.Core 5.0.0 says what it saves there.

Sample on a cell or a behavior from outside a transaction allocates no delegate, where it allocated one of 64 bytes
for the lambda that captured the behavior; the transaction it opens is what remains. A two-way bindable value's
refresh is such a sample on every change. Measured in the scratch harness on .NET 10: 176 bytes to 112 and about 118
ns to 105 per sample.

Every operation that opens a transaction when none is open now does so through a static lambda with its values as the
state, where it captured them in a closure: a display class and a delegate per call, also when a transaction was
already open and the lambda ran at once. That covers each lift, snapshot, merge, calm, accum, collect, switch, hold
with a lazy sample, and listen on a cell, the loop builders, the collection root, and the async map. Nothing
observable changes. Measured in the scratch harness on .NET 10, bytes per construction inside one transaction, before
and after: a hold 369 to 305, an accum 1,294 to 1,142, a collect 835 to 747, a merge of two with a function 1,362 to
1,258, a cell map 1,310 to 1,150, a cell lift of two 2,355 to 2,195 and of six 4,030 to 3,869, a cell calm 2,235 to
2,075, a switch 3,757 to 3,541; from outside a transaction, a lift 2,321 to 2,161 and an accum 1,577 to 1,425.

A link to a node with no listeners no longer takes the listeners lock to walk its descendants, which it has none of.
Every link in a chain had that lock, because the target of a link is the fresh output node. The node keeps a flag
that the lock sets. A chain of maps measures the same within the noise of the harness, so this removes a lock, not a
measured cost.

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

A link is one object where it was three, and an operator's output stream takes no weak handle. The action of a link was
a lambda, which is a closure and a delegate, and a listener object held the delegate alive because the node holds the
action through a weak reference. An operator's action is now one object that holds what the lambda captured, is the
action, and is the listener that the output attaches; a handler given to Listen or ListenStrong goes behind such an
object too, and a behavior is the action of its own link. The registry, which stopped the links of a collected stream
through a weak handle to that stream, asks the weak reference that the first attached link already holds, because that
link's action holds the stream, so no operator stream allocates a handle. Nothing observable changes. Measured in
GraphBuildBenchmarks, per thousand, before and after, .NET 10 then .NET Framework 4.7.2: a chain of maps 1.14 ms and 442
KB to 0.68 ms and 362 KB, and 1.12 ms to 0.83 ms; a fan-out of maps 1.02 to 0.89 ms and 586 to 495 KB, and 1.64 to 0.99
ms; a map with a hold 1.62 to 1.78 ms and 919 to 726 KB, and 2.25 to 1.94 ms, where the .NET 10 time is inside the noise
of an arm that spends two thirds of its time in collections: runs of it with memory randomization swing by forty percent
on the same code, and the scratch harness, which separates the pauses with GC.GetTotalPauseDuration, puts the work
outside the pauses at 640 to 790 ns per node on the branch against 720 to 890 ns on main; a map of one cell 1.49 to 1.22
ms and 1,127 to 922 KB, and 2.01 to 1.65 ms; an accum 1.55 to 1.31 ms and 1,288 to 1,094 KB, and 2.04 to 1.67 ms; a
merge of two streams 2.59 to 1.73 ms and 1,522 to 1,420 KB, and 2.55 to 2.26 ms; a lift of two cells 3.20 to 2.39 ms and
2,199 to 1,919 KB, and 3.80 to 3.29 ms; a lift of six 8.33 to 7.35 ms and 4,268 to 3,634 KB, and 10.57 to 8.75 ms; a
switch of a constant cell 13.97 to 8.87 ms and 3,455 to 3,171 KB, and 12.49 to 9.41 ms; ListenStrong on one stream 388
to 330 µs and 555 to 469 KB, and 463 to 441 µs; ListenStrong on one cell 3.79 to 2.88 ms and 1,875 to 1,693 KB, and 4.17
to 3.56 ms; a filter view of a collection of ten 49.6 to 37.7 ms and 10,133 to 9,200 KB, and 41.2 to 34.0 ms. In the
scratch harness on .NET 10, a full collection over 100,000 live maps takes 25.6 ms to 19.2 ms, because each map is two
objects fewer.

A value listener's initial value and a switch's first selection queue on one shared rank-zero node. Each queued through
an entry on a new node of its own, so that the entry sat at rank zero, and an entry registers with its node, which
allocates the node's entry list at the first registration: a node, a list, and the list's array, about 150 bytes, for a
rank that nothing can raise because nothing links into a node that nothing holds. The one shared node takes those
entries now, and an entry on it goes into no list. A switch makes two value listeners, and Cell.Listen,
Cell.ListenStrong, Cell.Values, and every bindable value make one. Nothing observable changes. Measured in
GraphBuildBenchmarks, per thousand, before and after, .NET 10 then .NET Framework 4.7.2: a switch of a constant cell
8.87 to 9.14 ms and 3,171 to 2,877 KB, and 9.41 to 9.09 ms; ListenStrong on one cell 2.88 to 2.67 ms and 1,693 to 1,546
KB, and 3.56 to 3.46 ms. The switch's .NET 10 time moves inside its error of 0.18 ms either way. In the scratch probe on
.NET 10, a switch of a constant cell is 3,106 to 2,799 bytes, and ListenStrong on a cell 1,648 to 1,496.

An operator that reads all of its inputs before it sends is its own gate, and a lift, a SnapshotLatest, and a merge of
maybes are one object for each arity. The gate that queues such an operator once in a transaction was an object that
held the operator's step and discard as delegates, wrapped each in a closure, and held none of the operator's state:
about 280 bytes in every lift, merge of two or more streams, merge of maybes, list lift, and SnapshotLatest. The
operator now derives from the gate. A lift also kept its recombine step and its initial value as two delegates on a
closure, and SnapshotLatest and a merge of maybes for three or more streams built their captures through closures and an
array; the object for each arity holds the inputs and the function as fields and gives those steps as methods, and a
lift's object is the source of the lazy sample its hold starts from. Nothing observable changes. Measured in
GraphBuildBenchmarks, per thousand, before and after, .NET 10 then .NET Framework 4.7.2: a lift of two cells 2.38 to
2.13 ms and 1,920 to 1,492 KB, and 3.30 to 2.84 ms; a lift of six 7.38 to 6.40 ms and 3,639 to 3,205 KB, and 8.75 to
7.99 ms; a merge of two streams 1.70 to 1.57 ms and 1,420 to 1,086 KB, and 2.25 to 2.00 ms. In the scratch probe on .NET
10, per item: a SnapshotLatest of one cell is 1,380 to 929 bytes, a merge of maybes of three streams 1,886 to 1,394, and
a list merge of three 1,943 to 1,553.

SodaFlow.Core now holds the timer system that the C# and F# packages give: the hook that sends Time and the alarms at
the start of each transaction, the scheduling with its timer thread, and the clocks. All of it is internal. Each package
keeps its public types and passes their members to it, with its own optional type for At, as Debounce already did.
Before, each package had a copy, and the two drifted.

Fixed: a transaction that fails no longer moves an alarm. The timer system's At listens to the cell that holds the
deadline, and its listener canceled the timer of the old deadline and set one for the new deadline while the transaction
was still open. When something later in that transaction threw, the cell kept its old deadline, but the timers had
already changed: the alarm fired at a deadline that never committed, and never at the one the cell held. Which of two
listeners at the same rank runs first is not something a caller can see, so the failure showed for some graphs and not
for others. DebounceImpl is built on At, so a failed transaction could make a debounce fire early, late, or not at all.
The listener still sets the new timer in the transaction, so a stopped timer system still fails that transaction with
nothing committed, but the old timer is now canceled only when the transaction commits. A transaction that fails revokes
the timer it set instead. A revoked timer sends no alarm, even when the timer thread fired it while the transaction was
open, or its callback runs after the failure.

Fixed: a posted action that throws delivers none of its sends. A posted action runs in a transaction of its own after
the transaction that posted it closes, and a throw from it now fails that transaction, as a throw from the body of a
transaction does: the sends it made before the throw do not reach the graph, and a cell it sent into keeps its value.
Before, the throw closed the child transaction instead of discarding it, so the sends propagated and the cells changed,
while the same action run with no transaction open, which goes through the same path as a transaction body, discarded
them. The exception continues to the code that ran the transaction that posted the action, as before, and a release
given with the action still runs.

Fixed: a posted action, or an element of a split, whose transaction fails no longer leaves behind the work it deferred.
Each of them runs in a child transaction of the transaction that deferred it, and a Post or a Split from inside a child
went straight into the queues of that first transaction. When the child then failed, by a throw from the action or from
a listener while it propagated, nothing took that work out again: the actions it had posted ran, their releases did not,
and the values it had sent through Defer or Split arrived in later transactions, although the transaction that produced
them was discarded. The first transaction always dropped what it had deferred when it failed, so the two paths
disagreed. What a child defers is at the end of each queue, after whatever was there when the child started, so a child
that fails now has the queues put back to where they were, and the release of each post that goes runs with the child's
exception as its cause. A release that throws comes out after that exception, in one AggregateException, as it does for
the first transaction.

Changed: the queue behind Defer and Split is a list in order of arrival, sorted by position when a round of it runs,
where it was a dictionary from a position to one delegate that joined every split at that position. The splits at one
position still run in one transaction, in the order they were made, and the positions still run in order. That is what
lets a failed child's splits be cut off the end, and it allocates less: no dictionary and key array for each round, and
no delegate that grows when several splits share a position. A transaction also no longer keeps a reference to the one
it defers into, which makes each transaction 8 bytes smaller. DeferredWorkBenchmarks in SodaFlow.Benchmarks measures the
paths where nothing fails. Allocated on .NET 10: a post from a transaction 456 B to 440 B, a post from a posted action
672 to 648, a defer 1,376 to 1,208, a defer after a defer 2,208 to 1,880, and a defer from each of 8 split elements
8,720 to 7,208. The empty transaction that an entry above measured at 112 bytes is 104. Time on .NET Framework 4.7.2,
where the runs were steady: the three split cases 1,002 ns to 950 ns, 1,599 to 1,468, and 7,381 to 6,365, and the two
posts within 3% of what they were. On .NET 10 every case is the same or faster within the noise of the run.

Changed: a chain of deferred work that has no end throws DeferredChainLimitException, an InvalidOperationException,
where before it ran forever. A posted action and each element of a split run in a transaction of their own after the
one that deferred them, and that transaction can defer work in turn. A value that finds its way back to its own source
through Defer, Split, or a posted action that posts again, with nothing on the way to stop it, made a new transaction
for every lap: the call that started it never returned, on a full core, with flat memory and no exception. The drain
of a transaction now counts the rounds of splits that it runs, and the generations of posts that it runs after the
last round, where the posts that one generation makes are the next. It stops when either count is at 100,000. The
exception comes out of the call that started the first transaction, after any other deferred failure. Every
transaction before the limit has committed and its values stay; the work that was still waiting does not run, and a
post that was waiting gets its release with the exception as the cause. A chain that does end, but only after more
than 100,000 laps, throws as well, and there is no setting for the limit. The count is of depth and not of volume, so
one split of a million elements is one round. The count of generations starts again after each round, so a chain of
posts inside each lap of a chain of splits is held to the limit once for each kind and not for the product of the two
depths. The deepest chain in the tests of the whole solution is 11 laps. The counts are locals in the drain and
nothing on a queued entry, so a post and a split are the size they were. DeferredWorkBenchmarks allocates the same
with the limit and without it: to a hundredth of a byte in each of its five cases on .NET Framework 4.7.2, and 440,
648, 1,208, 1,880, and 7,208 B on .NET 10. The .NET 10 JIT removes a 32-byte object from some of these paths when its
profile lets it, so a run can read 32 or 64 B more, with the limit or without it; with dynamic PGO off, the defer case
reads 1,240 B both ways. The times do not show the limit: on a machine that was busy, the runs with it are inside the
spread of the runs without it on both runtimes.

Fixed: no transaction gets the id zero. Each transaction takes the next value of a 64-bit count as its id, and zero
already means two things: a root transaction that has no id yet, and an input slot of a merge, lift, apply, or snapshot
that holds no value for the current transaction. The count wraps without an exception, so after 2^64 transactions one of
them took zero. A root transaction or an element of a split with that id read every empty slot as filled, so a merge of
two types reported a default value for the input that did not fire. A posted action with that id took another one at its
first use, and was not affected. The count now skips zero. Nobody was going to meet this: ids are given one at a time
under the transaction lock, and at one id per nanosecond the count takes about 585 years to get there. The point is that
"the id of a transaction is never zero", which the capture slot depends on, now holds without a condition. The cost is
one comparison per id.

Added, for the two language packages: a one-shot listener that gives the outcome of the transaction of its firing, which
ListenOnceAsync and listenOnceAsync now use. It gives the value once that transaction has committed, and the exception
of a transaction that fails after the stream fired, also where the transaction fails before the listener runs. For that,
a transaction that fails now tells each queued step that it discards which exception failed it, and a step that carried
a firing passes that on to a listener that asks for it. Nothing changes for any other listener, and the path of a
transaction that commits is the same, with one allocation fewer where a listener attaches to a stream that already fired
in the same transaction.

Changed: a posted action that throws no longer stops the posted actions behind it, and the transaction that posted them
no longer reads as failed. A transaction commits, runs its last actions, and only then runs what was posted during it,
each action in a transaction of its own. A throw from one used to discard the transaction that posted it, although
everything that transaction sent had already reached the graph: the actions still queued were dropped and their releases
ran with the other action's exception, and the transaction's recorded outcome flipped from committed to failed, so code
that asked it how the transaction ended was told the wrong thing. Each posted action now runs whatever the others do,
and the elements of Operational.Split, which go through the same queue, follow the same rule. When the queue is done,
one exception comes out as it is, and several come out together in an AggregateException, in the order the actions were
posted, each followed by any throw from its own release. The code that ran the transaction gets the exception as before.
A bindable value whose refresh was queued behind such an action was the visible casualty: with
BindingScheduler.Immediate its refresh was dropped and it never refreshed again, which
SodaFlow.Core.Bindable.ObjectModel 5.0.0 fixes on its side as well.

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

Added: a net8.0 build, beside the net472, net6.0, and netstandard2.0 builds. It compiles the same source. A project on
.NET 8 or later now takes it in place of the net6.0 build, and a project on .NET 6 or .NET 7 still takes the net6.0
build.

Added: the net8.0 build is marked as compatible with trimming and native AOT. The build of the package runs the trim,
AOT, and single-file analyzers on it and fails on a report. The assembly carries the metadata IsTrimmable and
IsAotCompatible, so an app that trims only the assemblies so marked, with TrimMode partial, now trims this one too. The
net472, net6.0, and netstandard2.0 builds are not marked, because the SDK has no such analyzers for them. The analyzers
cannot see one thing that the native AOT compiler reports. A native AOT publish that roots the assembly reports IL3054
on generic expansions that the compiler stopped, because MaybeInternal<T> nests inside itself with no limit that the
compiler can prove. The code of an expansion that stopped throws if it runs.

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
