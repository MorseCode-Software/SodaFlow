3.1.0

New: ForwardReference constructs a value which can refer to itself while it is
being constructed.

    Node node = ForwardReference.WithoutCaptures<Node>(
        reference => new Node(new Child(reference.AsCell())));

It is the single-valued case of a cell loop. A loop lets a cell be referred to
before it exists and is closed with the cell the reference turned out to mean;
this produces one value rather than a series of them, and closes the loop with
a constant cell, so the reference resolves to that value and never changes.

What it is for is the knot two objects tie when each needs the other at
construction. Without it one of them has to be built half-formed and completed
afterwards, through a settable member that has no business being settable once
the graph is up. Here nothing is mutable and no half-built object is reachable,
because the reference cannot be read before the constructing function returns -
doing so throws, as it does for any looped cell.

WithCaptures returns whatever else the construction is worth keeping, as it
does on a loop. Both of its type arguments have to be written out, since T
cannot be inferred from a lambda and C# does not allow only some of them to be
given.

C# only for now; there is no F# counterpart yet.

3.0.0

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

Requires SodaFlow.Core 3.0.0 or newer. The internal helper this calls was
renamed in step, so a 2.x core resolved against this package throws
MissingMethodException. Upgrade SodaFlow, SodaFlow.FSharp and SodaFlow.Core
together.

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

Requires SodaFlow.Core 2.0.0 or newer. Do not mix 1.x and 2.x packages.

---

About this package

The C# surface over SodaFlow.Core: extension methods and static factories for
streams, cells, behaviors, transactions and the timer systems. Install this to
use SodaFlow from C#; it brings the core with it.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
