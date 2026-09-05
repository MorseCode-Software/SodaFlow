4.0.0

No API change of its own. This release moves to SodaFlow.Core 4.x, and
is a major because taking it obliges a consumer to take that.

It could not have stayed on 3.x. Transaction.isActive in 3.1.0 called
TransactionInternal.IsActiveImpl, which SodaFlow.Core 4.0.0 removes; it
calls HasCurrentTransaction now, which is what the C# side already
called and what IsActiveImpl forwarded to. Same answer, one less hop,
and nothing about isActive's own signature or behavior changes.

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
