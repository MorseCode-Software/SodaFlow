4.0.0

BREAKING: requires SodaFlow.Core 4.x, where it required 3.x. That
package removed internal members this one is built against, so the two
have to move together; nothing it removed is in its public surface, and
nothing here changed shape because of it.

BREAKING: requires SodaFlow.Functional 3.x, where it required 2.x. Unit is a
struct there now, and this package's surface is full of Stream<Unit> and its
relatives, so the two move together. Nothing in this package's own API
changed shape.

BREAKING: the JetBrains annotation attributes which were compiled into the
SodaFlow namespace - SodaFlow.PureAttribute, SodaFlow.NotNullAttribute and
fifty more - are gone. They were a vendored copy, public by accident rather
than by intent, and are replaced by a reference to the JetBrains.Annotations
package which is not redistributed with this one. Nothing here was meant to
be consumed through them.

The build is warning-free, which it was not: the unreachable-code warnings
in the priority queue are gone.

Requires System.ValueTuple 4.6.2, where it required 4.4.0. Nothing here
uses it differently: this repository named two versions of it, one in
the shipping projects and one in the test projects, and now names a
single version in both. A consumer does nothing about this; NuGet
resolves the higher floor.

3.0.0

New: ForwardReference constructs a value which can refer to itself while it is
being constructed.

    Node node = ForwardReference<Node>.WithoutCaptures(
        reference => new Node(new Child(reference.AsCell())));

It is the single-valued case of a cell loop. A loop lets a cell be referred to
before it exists and is closed with the cell the reference turned out to mean;
this produces one value rather than a series of them, and closes the loop with
a constant cell, so the reference resolves to that value and never changes.

What it is for is the knot two objects tie when each needs the other at
construction. Without it one of them has to be built half-formed and completed
afterward, through a settable member that has no business being settable once
the graph is up. Here nothing is mutable and no half-built object is reachable,
because the reference cannot be read before the constructing function returns -
doing so throws, as it does for any looped cell.

WithCaptures returns whatever else the construction is worth keeping, as it
does on a loop, and infers its capture type from the function. The value type
is named on ForwardReference<T> rather than on the methods, which is what
leaves the capture type free to be inferred.

C# only for now; there is no F# counterpart yet.

BREAKING: Stream.FilterMaybe is renamed to Stream.FilterSome. It does exactly
what it always did; the old name said "stream of Maybe" where what the method
actually selects is the case that has a value.

This one breaks loudly. There is no overload left under the old name, so every
call site is a compile error naming the method, and the fix is a rename.
Nothing about the behavior, the transaction semantics or the type changed.

New: Stream.Choose(f) maps and filters in one step, firing only the values the
function produced. It is exactly Map(f).FilterSome(), for the common case where
deciding whether an event should pass is the same work as producing the value
to pass on - parsing, looking up, narrowing a type. The spelled-out form is
still there for when the intermediate Stream<Maybe<T>> is wanted for itself.

SodaFlow.FSharp gets the same pair: filterOptionS is renamed to filterSomeS,
and chooseS is added. Some names the case that has a value in both languages,
so both APIs now say so.

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

BREAKING: the two listen methods swapped names. Listen is now the weak listener
and ListenStrong the strong one, on both Stream and Cell.

This one breaks quietly. Assigning the result to an IStrongListener stops
compiling and is easy to find, but code that discards the result, or holds it
in a var or an IListener, keeps compiling and silently becomes a weak
subscription - which fails later, as listeners that quietly stop firing. Rename
every existing Listen call to ListenStrong first, then decide which of them
actually wanted to be weak.

Fixed: alarms were never delivered while the thread pool was saturated. The
timer loop ran on the pool, needing a pool thread on every iteration, so under
pool pressure it was never scheduled and no alarm fired at all. It now waits on
a dedicated background thread.

Every public type and member is documented, and six comments that wrote
<param name="x" /> where <paramref name="x" /> was meant are corrected - each
had declared a second doc entry for the parameter and rendered as nothing.

Requires SodaFlow.Core 2.0.0 or newer.

---

About this package

The C# surface over SodaFlow.Core: extension methods and static factories for
streams, cells, behaviors, transactions and the timer systems. Install this to
use SodaFlow from C#; it brings the core with it.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
