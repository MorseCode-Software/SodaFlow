5.0.0

Adds postWithReleaseOnFailure, a second form of post, which takes the action to
run where the posted action does not run or does not complete. A transaction that fails
while it propagates discards each action that post holds. Code that gives a
value to something which waits, and gives that value from a posted action, had
no way to release it, thus the waiting code waited forever.

The release runs one time, and only where the posted action does not complete.
Two conditions give that: the transaction fails before it runs the action, and
the action itself throws. The argument is the exception of the transaction in
the first condition, and the exception of the action in the second. An action
that completes runs no release.

A throw from a release does not stop the release of another posted action, and
it does not replace the exception that caused it. The caller gets an
AggregateException with that exception first and the throw from the release
after it, thus no code loses a failure.

The release belongs to one posted action, at the call that posts it, and not to
a transaction. Thus, no code can ask for a release with nothing to release, and
none must find the transaction that is open to ask.

SodaFlow.Core.Async is the first caller. Its Execute gives a Task to code that
waits, and it gives the value of that Task from a posted action.

F# has no optional parameter on a let-bound function, thus the two forms are two
names here, as mapAsync and mapAsyncWithInputConverter are.

Fixed: the At member of the timer system no longer holds an alarm alive
through the cell it reads. It listened to that cell with a strong listener,
which the keep-alive set of the cell's graph roots, and that listener holds
the alarm sink. So every call to At on a long-lived cell left an alarm and a
listener that nothing could collect, however long ago the caller let go of the
stream. The listener is weak now and the alarm holds it, which is the
arrangement every other derived stream in this library uses, and it gives the
same lifetime from the other side: the alarm keeps its listener while a caller
keeps the alarm, and both go when the caller does.

BREAKING: Stream.attachListener and its alias attachListenerS are gone. They
tied a listener's lifetime to a chosen stream, which is how a combinator keeps
its own wiring alive, and every combinator in this library does that through an
internal method rather than through these. Nothing outside the library called
them, and nothing outside could: building a primitive that way also needs the
weak listen overload taking a node, which is internal, and a handler that sends,
which throws. They named a part of how the graph is assembled that the rest of
this API keeps behind the primitives it gives you.

If a call to either exists, the listener it attached was already tied to the
stream by the function that made it. Keep the listener for as long as the
subscription should live, or use listenStrong, which roots it for you.

BREAKING: Stream.listenOnce answers with a weak listener, where it answered with
a strong one, and Stream.listenOnceStrong is new and answers with what
listenOnce used to. The aliases follow: listenOnceS is the weak one and
listenOnceStrongS is new. The one-shot listeners now divide the way listen and
listenStrong have divided since 4.0.0.

This one breaks quietly. A binding annotated as a strong listener stops
compiling and is easy to find. A call that ignores the listener - which a
one-shot subscription invites - keeps compiling and becomes a subscription that
fires only where no collection happens first. Rename every existing listenOnce
call to listenOnceStrong, then decide which of them want to be weak.

Where a call keeps the weak listener, keep it until the firing arrives: nothing
else holds the handler, because the node reaches it through a weak reference.

Stream.listenOnceAsync is unchanged and stays strong. It answers with an async
rather than a listener, so there is no handle a caller could hold.

BREAKING: requires SodaFlow.Core 5.x, where it required 2.0.0 or newer. That
package changed the return type of an internal method this one is built
against, so the two move together. Nothing it changed is in its public surface.

4.0.1

Adds the package icon that nuget.org shows beside this package. No source file
changed since 4.0.0.

Every package here ships this release together, so the dependency versions
move with it.

4.0.0

No API change of its own. This release moves to SodaFlow.Core 4.x, and
is a major because taking it obliges a consumer to take that.

It could not have stayed on 3.x. Transaction.isActive in 3.1.0 called
TransactionInternal.IsActiveImpl, which SodaFlow.Core 4.0.0 removes; it
calls HasCurrentTransaction now, which is what the C# side already
called and what IsActiveImpl forwarded to. Same answer, one less hop,
and nothing about isActive's own signature or behavior changes.

BREAKING: requires FSharp.Core 11.0.100, where it required 4.5.2. A
consumer still on FSharp.Core 4.x cannot take this release. Nothing in
this package's own code turns on anything that changed between those
versions - it compiles against 11.0.100 unaltered - but the floor is
written into the package, so the requirement is real whether or not the
code exercises it. It moves because the shipping projects and the test
projects now compile against one version of FSharp.Core instead of
disagreeing about it.

3.1.0

New: ForwardReference.create and ForwardReference.createWithNoCaptures build a
value which can refer to itself while it is being constructed, with
forwardReference and forwardReferenceWithNoCaptures as the shorthand aliases.
This is the F# counterpart of the type SodaFlow 3.0.0 added, which shipped
without one.

    let node = forwardReferenceWithNoCaptures (fun reference -> Node (Child reference))

It is the single-valued case of a cell loop. A loop lets a cell be referred to
before it exists and is closed with the cell the reference turned out to mean;
this produces one value rather than a series of them, and closes the loop with
a constant cell, so the reference resolves to that value and never changes.

The naming follows the loops already here rather than the C# type: create and
createWithNoCaptures, as Cell.loop pairs with Cell.loopWithNoCaptures, taking
and returning struct tuples the same way.

Where C# has to be told the value type, because a lambda gives inference
nothing to work from and only some of a method's type arguments cannot be
given, F# infers both the value and the capture types from the function. Neither
is ever written.

Reading the reference before the constructing function returns throws, as it
does for any looped cell.

3.0.0

BREAKING: Stream.filterOption is renamed to Stream.filterSome, and the
filterOptionS shorthand to filterSomeS. Nothing about the behavior changed.

The old name described the type it consumed; the new one describes the case it
keeps. Some names that case in F# as much as it does in C#, so the two APIs now
agree, and the C# counterpart is FilterSome rather than FilterMaybe.

New: Stream.choose, with the chooseS shorthand, transforms firings with a
function returning an option and fires only the values it produced. It is
map f >> filterSome in one step, takes the stream last like everything else
here, and is the counterpart of List.choose.

Requires SodaFlow.Core 3.x. The internal helper this calls was renamed in step,
so a 2.x core resolved against this package would throw
MissingMethodException. This package's dependency on SodaFlow.Core is declared
as a range bounded at the next major, so that pairing is refused at restore
rather than discovered at runtime.

2.0.1

Adds the release notes below. 2.0.0 shipped without any, because the mechanism
that reads them from a file landed after that version was tagged. No code
change since 2.0.0.

2.0.0

BREAKING: the two listen functions swapped names, as in the C# package.
Stream.listen and Cell.listen are now the weak ones, listenStrong the strong
ones, and the listenS and listenC shorthand aliases follow.

Same quiet breakage: a call that ignores the returned handle keeps compiling
and becomes a weak subscription, which fails later as listeners that stop
firing. Rename every existing listen call to listenStrong first, then decide
which of them wanted to be weak.

Fixed: alarms were never delivered while the thread pool was saturated. The
timer loop ran with Async.Start on the pool, so under pool pressure it was
never scheduled and no alarm fired. It now waits on a dedicated background
thread. The per-iteration CancellationTokenSource, allocated on every pass and
never disposed, is gone, and SetTimer no longer cancels while holding the timer
lock.

Every module, type and function is now documented. Previously this assembly
shipped an empty XML documentation file despite having generation enabled, so
IntelliSense had nothing to show for any of it. FS3390 is enabled, so the
comments are checked at build time.

Requires SodaFlow.Core 2.0.0 or newer.

---

About this package

The F# surface over SodaFlow.Core: modules of curried, pipeline-friendly
functions over streams, cells and behaviors, plus an AutoOpen module of
suffixed aliases so open SodaFlow alone gives you mapS, listenC, sendS and the
rest. Install this to use SodaFlow from F#; it brings the core with it.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
