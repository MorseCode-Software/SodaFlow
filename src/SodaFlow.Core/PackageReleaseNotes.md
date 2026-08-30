3.0.0

No public API change. The internal helpers FilterMaybeImpl and
FilterMaybeInternal are renamed to FilterSomeImpl and FilterSomeInternal,
following the public rename in SodaFlow and SodaFlow.FSharp.

This is a major version despite the public surface being untouched, because
the internal surface is the contract between this package and its wrappers.
A wrapper compiled against the old names resolves them at runtime and throws
MissingMethodException against this version. Leaving this on 2.x would have
made SodaFlow 2.0.1 and SodaFlow.Core 2.2.0 an allowed pairing under the "do
not mix major versions" rule while still failing that way, which is the exact
trap the 2.0.0 notes warned about.

Upgrade SodaFlow, SodaFlow.FSharp and SodaFlow.Core together.

2.1.1

Adds the release notes below. 2.1.0 shipped with the repository-wide notes
instead, which described the 2.0.0 listen rename and said nothing about what
2.1.0 actually changed. No code change since 2.1.0.

2.1.0

Grants InternalsVisibleTo to the three bindable object model assemblies:
SodaFlow.Core.Bindable.ObjectModel, SodaFlow.Bindable.ObjectModel and
SodaFlow.FSharp.Bindable.ObjectModel.

Required rather than cosmetic. The CLR checks the friend-assembly attribute on
the assembly that declares the internals, so those three throw
MethodAccessException at run time against a 2.0.0 core - even though they
build cleanly from source, where the grants are present. Anything depending on
SodaFlow.Bindable.ObjectModel needs this version.

No API change otherwise.

2.0.0

Fixed: Cell.updates is volatile, so its lazily created stream is published
safely on weak memory models. It was read outside the transaction lock with
nothing ordering the write, letting a reader observe a partly constructed
object on arm64.

Calm is expressed over CarryState rather than duplicating its state protocol,
and a throwing transaction now releases all five of its queues rather than
three.

Breaking for the packages that reach these internals, though no public API
moved: ListenImpl kept its name and swapped from the strong listener to the
weak one, and ListenWeakImpl is gone.

---

About this package

The engine: Stream<T>, Cell<T>, Behavior<T>, Transaction, the listener
interfaces, and the dependency graph that makes updates atomic. Language
neutral, and not installed directly - take SodaFlow for C# or SodaFlow.FSharp
for F#, both of which bring it with them and expose its operations.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
