2.0.0

BREAKING: the two listen methods swapped names. Listen is now the weak listener
and ListenStrong the strong one, on both Stream and Cell, in C# and F# alike
(listen/listenStrong, listenS/listenStrongS, listenC/listenStrongC).

This one breaks quietly. Assigning the result to an IStrongListener stops
compiling and is easy to find, but code that discards the result, or holds it
in a var or an IListener, keeps compiling and silently becomes a weak
subscription - which fails later, as listeners that quietly stop firing. Rename
every existing Listen call to ListenStrong first, then decide which of them
actually wanted to be weak.

Fixed: alarms were never delivered while the thread pool was saturated. The
timer loop ran on the pool, so under pool pressure it was never scheduled and
no alarm fired at all. It now waits on a dedicated background thread. Affects
both the C# and the F# timer systems.

Fixed: Cell.updates is volatile, so its lazily created stream is published
safely on weak memory models.

Every public type and member is now documented, in C# and F# alike.
SodaFlow.FSharp previously shipped an empty XML documentation file despite
having generation enabled.

SodaFlow.Core changed an internal method in place, so a wrapper built against
1.x either throws MissingMethodException or silently changes behavior once it
resolves against a 2.x core. Upgrade a wrapper together with the core it calls;
each package's own notes say which core version it needs.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases/tag/sodaflow-2.0.0
