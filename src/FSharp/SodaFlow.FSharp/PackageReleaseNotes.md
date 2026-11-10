6.0.0

BREAKING: sampleLazy returns a LazySample<'T>, and holdLazy, accumLazy, collectLazy, and constantLazy take one, where
each used a Lazy<'T>, so a lazy expression no longer fits. Only SodaFlow reads a LazySample, which is what lets a
transaction that fails forget its value; the core notes say why. New: the LazySample module, with defer for one from a
function, map, and lift2 to lift8, and the lazySample alias for defer in Shorthand. A function given to any of them
can run again after a failed transaction, so it must be pure and must not send.

BREAKING: getListenerWithWeakReference in the Listener, WeakListener, StrongListener, and MutableListener modules, and
Listener.unlistenWeak, are removed, and IListenerWithWeakReference is internal. The MutableListener module is removed
with the type, which is internal, and the Cleanup module with Cleanup, which is removed; the core notes say why. F#
never had the explicit loop types that the C# package removes in this release, so loops in F# do not change.

BREAKING: Cell<'T> derives from Behavior<'T>, so the functions of the Behavior module take a cell without asBehavior,
and lift2 to lift8 take cells and behaviors together and give a behavior. asBehavior stays, as an upcast.

BREAKING: ITimerSystem<'T> inherits IDisposable, ITimerSystemImplementation<'T> has a Stop, and Transaction.onStart
and onStartT return the registration of the action as an IDisposable, so a call that ignored the result now needs an
ignore. A timer system's constructor installs a hook that runs at the start of every transaction in the process, and a
real clock starts a thread; before this release nothing could remove either, so each timer system made, for example
one per test, taxed every later transaction for the life of the process. Disposing the timer system removes the hook
and ends the thread, and after that Time does not move and no alarm fires. A custom ITimerSystemImplementation<'T>
needs a Stop, which can be empty for a clock a test moves by hand.

BREAKING: Time updates when the clock moves and holds its value otherwise. The hook that runs at the start of each
transaction sent the clock's reading to Time every time, so a listener on Operational.updates on Time ran once per
transaction in the process, with the value Time already held whenever the clock had not moved, which a clock a test
moves by hand never does between transactions. The hook now sends only a reading that differs from the last one, and an
alarm's event at the time Time holds sends nothing either. That is what a behavior's updates mean everywhere else in the
library. A listener on the updates of Time fires fewer times, which is the breaking part; a sample of Time, which is how
a behavior is meant to be read, sees no change, and neither does At. The C# package makes the same change in its 6.0.0,
and its notes have the figures.

Added: a constructor on SecondsTimerSystem, SystemClockTimerSystem, and UtcClockTimerSystem that takes a TimeSpan
resolution, as in the C# package. The clock rounds every reading down to a multiple of it, so a reading inside the step
of the last one leaves Time as it is and the transaction skips the update and its listeners. An alarm fires in the first
transaction whose rounded reading reaches it, up to one resolution late, and At still fires with the alarm's own time.
TimeSpan.Zero, which the constructors without a resolution use, reads the clock as it is; a negative resolution raises
ArgumentOutOfRangeException. The C# notes have the figures.

BREAKING: a stopped TimerSystemImplementationBase<'T> throws ObjectDisposedException from SetTimer, and so At on a
disposed timer system throws it from the transaction that gives the alarm a time, whether that is the construction
or a later send. Before, the timer was accepted and never fired, and the caller held a handle for an alarm that
could not come. A cell with no time sets nothing and passes as before. A custom ITimerSystemImplementation<'T>
should do the same after its Stop.

The timer system's hook also allocates nothing in a transaction with no alarm due, where before it allocated a list
and an enumerator on every transaction in the process, and it no longer takes the implementation's lock when no timer
is set. The C# notes have the measurement; the F# hook has the same shape.

BREAKING: SimpleTimer<'T> is no longer public. It is the handle that TimerSystemImplementationBase<'T> gives from
SetTimer, and every caller already reached it as an ITimer. Its documentation said not to make one, and nothing outside
the package could make one that fires. The C# package's equivalent was always private.

The timer system is now the one in SodaFlow.Core, which the C# package uses too: TimerSystem, the scheduling in
TimerSystemImplementationBase, and the three clocks. Before, this package had its own copy of each, and the two copies
drifted; a change to the C# hook earlier in this release reached F# only in a second change, after a test caught it.
Every other public type keeps its signature and its behavior. The one difference is a clock that reads NaN: compare
treats NaN as equal to itself where = does not, so Time no longer updates on every transaction for such a clock. No
clock in the package gives NaN.

Transaction.run no longer allocates a closure and a delegate of its own. With SodaFlow.Core 6.0.0, a run that returns
a value from outside a transaction measures 42 ns and 200 bytes to 35 ns and 112 bytes on .NET 10; the core notes have
the rest.

Operational.defer and Operational.split allocate less with SodaFlow.Core 6.0.0, which keeps the splits of a transaction
in a list where it kept a dictionary. On .NET 10 a defer measures 1,376 bytes to 1,208, and a defer from each of 8 split
elements 8,720 to 7,208; the core notes have the rest.

Added: UtcClockTimerSystem, a timer system over DateTime.UtcNow, and a constructor on SystemClockTimerSystem that
takes the clock as a unit -> DateTime; the existing constructor still reads DateTime.Now. The C# notes say why: local
time moves with the machine's clock at a daylight saving change, and a wait across that change waits for the wrong
time, so a wait or a deadline belongs on the UTC one and the local one is for a time a screen shows. The clock
constructor is also the shortest way to put a clock a test moves by hand under a real TimerSystem<DateTime>.

This release also raises the dependency floor, so that a new install gets SodaFlow.Core 6.0.0. That release has fixes
that reach this package, and its notes have the details of each:

- A cell with a lazy initial value, a looped cell, the state of a lazy fold, and the first value of a bindable no
  longer keep a value from a transaction that failed after its sample phase.

- A transaction that fails no longer leaves state behind that breaks the graph after it. Before, a cell could stop
  updating, a sink could throw on every later send, and a stream could replay old firings to each new listener.
- lift, apply, snapshotLatest, and the merge of two streams of different types no longer use a value that an input
  sent in a transaction that failed. Before, the next update of another input combined it with the uncommitted value,
  and the result was wrong until the first input updated again.
- switchB and switchC no longer keep a switch that a failed transaction made. Before, the output went on following the
  behavior the transaction had switched to, although the outer cell still held the one from before the failure.
- listenOnce and listenOnceStrong no longer call the handler twice when the stream fires on another thread at the
  moment the listener is attached.
- A step of the graph that threw in a failed transaction no longer runs again in a later one. The same fix stops a
  map of a cell from switchC or switchB from running its function a wasted second time when the switch moves to an inner
  cell of higher rank than its output.
- A long chain of operations no longer crashes the process with a stack overflow when a link raises the rank of every
  node in it, as closing a loop over a chain of 10,000 maps did.
- A posted action, or an element of Operational.split, whose transaction fails no longer leaves behind what it deferred.
  The actions it posted do not run, the release of each one does, and the values it sent through defer or split do not
  arrive.

NuGet resolves the lowest version a range allows, so a fresh install of 5.0.1 would go on resolving SodaFlow.Core
5.0.0. This release also calls a member that SodaFlow.Core 5.0.1 adds, so it cannot run against 5.0.0, and the
floor is what keeps a consumer from pairing them.

Changed: listenOnceAsync is now built on listenOnceStrong, so it gets the same fix. It used to carry its own copy of
that logic, with the same gap. The result still arrived only once, but the listener could stay attached until the
stream fired again.

Fixed: listenOnceAsync no longer keeps the stream alive through its cancellation token. It registered with the token
of the async and never removed the registration, which held the listener, and through it the stream. With a token
that outlived the stream, the stream could not be collected even after the async had completed. An async started
without a token of its own gets Async.DefaultCancellationToken, which lives as long as the process, so in that common
case every stream that listenOnceAsync was used on stayed in memory for good. The registration now goes when the async
completes, and a check with that default token found the stream collected on the first collection after the result
arrived. The result is unchanged.

Changed: when its token is canceled before the stream fires, listenOnceAsync now cancels, as its documentation has
always said. Before, it failed with a TaskCanceledException instead, because Async.AwaitTask turns a canceled task
into an exception. Async.StartAsTask gave a faulted task rather than a canceled one, and a try ... with around the
call caught the exception. Now the async ends the way any canceled async does: Async.StartAsTask gives a canceled
task, a try ... with does not see it, and a try ... finally or a use still runs. Code that caught TaskCanceledException
from listenOnceAsync to notice the cancellation no longer gets it, and the cancellation reaches whatever started the
async, as it does for any other async.

Fixed: a listenOnceAsync that never runs no longer leaves its listener on the stream. The listener attaches when you
call listenOnceAsync, and only the running async removed it. An async can be stopped before it runs: started with a
token that was already canceled, or reached by a let! after a cancellation, which the let! checks before it starts the
next async. In those cases the listener stayed on the stream until the stream fired, which for a stream that seldom
fires was a leak with no end. Now the listener is weak, and held only by the async, until the async runs; when it
runs, the stream holds it until the first firing or a cancellation, as before. A firing between the call and the start
of the async still reaches the result, as long as you hold the async.

Changed: choose builds one node instead of a map followed by a filter. It fires the same values in the same transactions
and costs about half as much to build.

Fixed: TimerSystem could send its Time behavior backward. Timers fire on more than one thread, the implementation's
own and any thread whose transaction start runs the timers that are due, and each one queues its event after it lets
go of the implementation's lock. The events then reached a first-in, first-out queue out of order, so alarms at 10 and
11 could arrive as 11 then 10, with Time sent 11 then 10. And a transaction that started between a timer's removal and
its event sent the current time first, so the event, for an earlier time, then sent Time back. Events are now taken in
order of time, and an event that arrives after a later Time was sent still fires its alarm, with its own time and in
its own transaction, but leaves Time where it is. This holds for any implementation, including one you write.

Not changed: Time still follows the clock. If the clock itself goes back, as DateTime.Now does when daylight saving
time ends, Time goes back with it, as before.

Fixed: a transaction that fails no longer moves an alarm. When the cell given to the At member of a timer system
changes, At cancels the timer of the old deadline and sets one for the new deadline, and it did both while the
transaction was still open. If something later in that transaction threw, the cell kept its old deadline, but the timers
had already changed: the alarm fired at a deadline the cell never held, and never at the one it did. debounce is built
on At, so a failed transaction could make it fire early, late, or not at all. The old timer is now canceled only when
the transaction commits, and a transaction that fails takes back the timer it set, even one that had already fired on
the timer thread. A disposed timer system still refuses a new deadline by failing the transaction that sends it, as
before. The fix is in SodaFlow.Core, so it reaches an implementation you write as well.

Fixed: SecondsTimerSystem counted its seconds as DateTime.Now minus the DateTime.Now at construction, so the count
jumped by an hour when daylight saving time began or ended, and moved with any correction of the system clock. It now
counts with a Stopwatch, which only measures time going by.

Changed: the timer thread reads the next timer due with the set's own Min rather than LINQ's First, which allocated an
enumerator every time the thread woke to see what was due. Which timer it finds, and when it fires, are unchanged.

5.0.1

lift7, lift8, and snapshot5 to snapshot8, with their B forms, now call the Core form for their count, as lift6 and
snapshot4 do. Before, each one put together narrower lifts or snapshots through a struct tuple, which made more streams
and a tuple each time one of them fired. The values that they give do not change.

No public signature changes, and no dependency version changes.

5.0.0

Adds Time.debounce, and debounceS in the shorthand module, which fires the last value of a stream after a time with no
other value. Each firing moves the alarm out, thus a sequence of firings with no space between them gives one value. A
search box is the usual position for it: the text of each keystroke goes in, and one search comes out.

let searches = query |> updatesC |> Time.debounce timers (fun now -> now + 0.3)

It is in the Time module and not in the Stream module, because it reads an ITimerSystem, and F# compiles Stream.fs
before Time.fs. The second argument reads the time of a firing and gives the time to fire at. It must give a time after
the time that it reads.

The result is in a transaction of the alarm and never in the transaction of a firing. It cancels no work that started:
give a strategy to mapAsync that cancels the operation it replaces for that, and debounce the input also.

Adds postWithFailureHandler, a second form of post, which takes the action to run where the posted action does not run
or does not complete. A transaction that fails while it propagates discards each action that post holds. Code that gives
a value to something which waits, and gives that value from a posted action, had no way to release it, thus the waiting
code waited forever.

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

F# has no optional parameter on a let-bound function, thus the two forms are two names here, as mapAsync and
mapAsyncWithInputConverter are.

Adds snapshotLatest, snapshotLatestB, snapshotLatestAndTake, and snapshotLatestAndTakeB. The shorthand is
snapshotLatestC, snapshotLatestB, snapshotLatestAndTakeC, and snapshotLatestAndTakeB. snapshot gives the value from
before the transaction. Thus, a stream that fires in the transaction that updates the cell sees the previous value. A
loop must read its own last state, thus that is correct there. It is wrong where a change of one cell must read the
value that another cell has after the same change.

snapshotLatest gives the new value where the transaction updates the cell, and the current value where it does not. Only
the stream causes a firing, as with snapshot. Thus, c1 |> Cell.values |> Stream.snapshotLatest c2 f fires when c1
changes, and not when only c2 changes.

It cannot close a loop. Do not give it a cell that the result updates in the same transaction.

snapshotLatest2 to snapshotLatest8 and snapshotLatest2B to snapshotLatest8B take two to eight cells or behaviors, with
the shorthand snapshotLatest2C to snapshotLatest8C and snapshotLatest2B to snapshotLatest8B. Each one gives the new
value of each cell that the transaction updates, and the current value of each cell that it does not update. For more
cells, give it a cell from lift2 or from one of the other lift functions. In a transaction, a lifted cell has the new
value.

Adds mergeOptions2 to mergeOptions8, with the shorthand mergeOptions2S to mergeOptions8S. Each one merges two to eight
streams of different types. The result is a stream of struct tuples with one option for each stream, in the sequence of
the arguments. It fires in each transaction in which one or more of the streams fire. Each element is Some with the
value of its stream if that stream fired, and None if it did not. Thus, one or more elements are Some. merge must have
streams of one type, and it must put simultaneous values into one value of that type. mergeOptions keeps each value, and
no stream has priority.

The result is a struct tuple, as the state and the output of collect are. A merged stream can fire in a large number of
transactions, and a struct tuple does not allocate a tuple for each firing.

Fixed: the At member of the timer system no longer holds an alarm alive through the cell it reads. It listened to that
cell with a strong listener, which the keep-alive set of the cell's graph roots, and that listener holds the alarm sink.
So every call to At on a long-lived cell left an alarm and a listener that nothing could collect, however long ago the
caller let go of the stream. The listener is weak now and the alarm holds it, which is the arrangement every other
derived stream in this library uses, and it gives the same lifetime from the other side: the alarm keeps its listener
while a caller keeps the alarm, and both go when the caller does.

BREAKING: Stream.attachListener and its alias attachListenerS are gone. They tied a listener's lifetime to a chosen
stream, which is how a combinator keeps its own wiring alive, and every combinator in this library does that through an
internal method rather than through these. Nothing outside the library called them, and nothing outside could: building
a primitive that way also needs the weak listen overload taking a node, which is internal, and a handler that sends,
which throws. They named a part of how the graph is assembled that the rest of this API keeps behind the primitives it
gives you.

If a call to either exists, the listener it attached was already tied to the stream by the function that made it. Keep
the listener for as long as the subscription should live, or use listenStrong, which roots it for you.

BREAKING: Stream.listenOnce answers with a weak listener, where it answered with a strong one, and
Stream.listenOnceStrong is new and answers with what listenOnce used to. The aliases follow: listenOnceS is the weak one
and listenOnceStrongS is new. The one-shot listeners now divide the way listen and listenStrong have divided since
4.0.0.

This one breaks quietly. A binding annotated as a strong listener stops compiling and is easy to find. A call that
ignores the listener - which a one-shot subscription invites - keeps compiling and becomes a subscription that fires
only where no collection happens first. Rename every existing listenOnce call to listenOnceStrong, then decide which of
them want to be weak.

Where a call keeps the weak listener, keep it until the firing arrives: nothing else holds the handler, because the node
reaches it through a weak reference.

Stream.listenOnceAsync is unchanged and stays strong. It answers with an async rather than a listener, so there is no
handle a caller could hold.

BREAKING: requires SodaFlow.Core 5.x, where it required 2.0.0 or newer. That package changed the return type of an
internal method this one is built against, so the two move together. Nothing it changed is in its public surface.

4.0.1

Adds the package icon that nuget.org shows beside this package. No source file changed since 4.0.0.

Every package here ships this release together, so the dependency versions move with it.

4.0.0

No API change of its own. This release moves to SodaFlow.Core 4.x, and is a major because taking it obliges a consumer
to take that.

It could not have stayed on 3.x. Transaction.isActive in 3.1.0 called TransactionInternal.IsActiveImpl, which
SodaFlow.Core 4.0.0 removes; it calls HasCurrentTransaction now, which is what the C# side already called and what
IsActiveImpl forwarded to. Same answer, one less hop, and nothing about isActive's own signature or behavior changes.

BREAKING: requires FSharp.Core 11.0.100, where it required 4.5.2. A consumer still on FSharp.Core 4.x cannot take this
release. Nothing in this package's own code turns on anything that changed between those versions - it compiles against
11.0.100 unaltered - but the floor is written into the package, so the requirement is real whether or not the code
exercises it. It moves because the shipping projects and the test projects now compile against one version of
FSharp.Core instead of disagreeing about it.

3.1.0

New: ForwardReference.create and ForwardReference.createWithNoCaptures build a value which can refer to itself while it
is being constructed, with forwardReference and forwardReferenceWithNoCaptures as the shorthand aliases. This is the F#
counterpart of the type SodaFlow 3.0.0 added, which shipped without one.

    let node = forwardReferenceWithNoCaptures (fun reference -> Node (Child reference))

It is the single-valued case of a cell loop. A loop lets a cell be referred to before it exists and is closed with the
cell the reference turned out to mean; this produces one value rather than a series of them, and closes the loop with a
constant cell, so the reference resolves to that value and never changes.

The naming follows the loops already here rather than the C# type: create and createWithNoCaptures, as Cell.loop pairs
with Cell.loopWithNoCaptures, taking and returning struct tuples the same way.

Where C# has to be told the value type, because a lambda gives inference nothing to work from and only some of a
method's type arguments cannot be given, F# infers both the value and the capture types from the function. Neither is
ever written.

Reading the reference before the constructing function returns throws, as it does for any looped cell.

3.0.0

BREAKING: Stream.filterOption is renamed to Stream.filterSome, and the filterOptionS shorthand to filterSomeS. Nothing
about the behavior changed.

The old name described the type it consumed; the new one describes the case it keeps. Some names that case in F# as much
as it does in C#, so the two APIs now agree, and the C# counterpart is FilterSome rather than FilterMaybe.

New: Stream.choose, with the chooseS shorthand, transforms firings with a function returning an option and fires only
the values it produced. It is map f >> filterSome in one step, takes the stream last like everything else here, and is
the counterpart of List.choose.

Requires SodaFlow.Core 3.x. The internal helper this calls was renamed in step, so a 2.x core resolved against this
package would throw MissingMethodException. This package's dependency on SodaFlow.Core is declared as a range bounded at
the next major, so that pairing is refused at restore rather than discovered at runtime.

2.0.1

Adds the release notes below. 2.0.0 shipped without any, because the mechanism that reads them from a file landed after
that version was tagged. No code change since 2.0.0.

2.0.0

BREAKING: the two listen functions swapped names, as in the C# package. Stream.listen and Cell.listen are now the weak
ones, listenStrong the strong ones, and the listenS and listenC shorthand aliases follow.

Same quiet breakage: a call that ignores the returned handle keeps compiling and becomes a weak subscription, which
fails later as listeners that stop firing. Rename every existing listen call to listenStrong first, then decide which of
them wanted to be weak.

Fixed: alarms were never delivered while the thread pool was saturated. The timer loop ran with Async.Start on the pool,
so under pool pressure it was never scheduled and no alarm fired. It now waits on a dedicated background thread. The
per-iteration CancellationTokenSource, allocated on every pass and never disposed, is gone, and SetTimer no longer
cancels while holding the timer lock.

Every module, type and function is now documented. Previously this assembly shipped an empty XML documentation file
despite having generation enabled, so IntelliSense had nothing to show for any of it. FS3390 is enabled, so the comments
are checked at build time.

Requires SodaFlow.Core 2.0.0 or newer.

---

About this package

The F# surface over SodaFlow.Core: modules of curried, pipeline-friendly functions over streams, cells and behaviors,
plus an AutoOpen module of suffixed aliases so open SodaFlow alone gives you mapS, listenC, sendS and the rest. Install
this to use SodaFlow from F#; it brings the core with it.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
