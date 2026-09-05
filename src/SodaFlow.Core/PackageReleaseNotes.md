4.0.0

BREAKING: internal members are gone or changed shape. Internal is not
private here - this assembly grants InternalsVisibleTo to the shipping
packages around it, so what it declares internal is API to them, and
removing any of it is a break they have to be rebuilt against.

Gone: TransactionInternal.IsActiveImpl, its parameterless constructor,
Close and the Post overload; the Utilities class and its Yield;
MaybeInternal's NoneType, the None field and the implicit conversion
from it; the three Lazy overloads of
LazyExtensionMethodsInternal.LiftImpl; LazyBehavior's LazyInitialValue;
TransactionInternal's ActionEntry and its Entry.IsRemoved.
TransactionInternal.Split returns void where it returned UnitInternal,
which is a different method to a compiler and to the runtime.

Added: the NoListener type, and an Equals overload on MaybeInternal
taking its own type rather than object.

The public surface is unchanged. Every type this package exposes -
Stream, Cell, Behavior, their sinks and loops - has the same members it
had in 3.0.0, so code which only uses the published API needs nothing
but the recompile a new major asks for.

A major rather than a 3.x, and the version is doing real work. Published
SodaFlow.FSharp 3.1.0 calls TransactionInternal.IsActiveImpl and depends
on this package as [3.0.0, 4.0.0). Shipped as 3.0.1 or 3.1.0 this would
resolve against that package and fail when the call was made; shipped as
4.0.0 it falls outside the range and NuGet declines the pairing instead.
That is the whole reason the ranges carry an upper bound.

Requires System.ValueTuple 4.6.2, where it required 4.4.0. Nothing here
uses it differently: this repository named two versions of it, one in
the shipping projects and one in the test projects, and now names a
single version in both. A consumer does nothing about this; NuGet
resolves the higher floor.

3.0.0

BREAKING: FilterMaybeImpl and FilterMaybeInternal are renamed to FilterSomeImpl
and FilterSomeInternal, following the rename in SodaFlow and SodaFlow.FSharp.

These are declared internal, but internal is not private here. This assembly
grants InternalsVisibleTo to eight shipping packages - SodaFlow,
SodaFlow.FSharp, SodaFlow.Core.Async, SodaFlow.Async, SodaFlow.FSharp.Async and
the three bindable object model packages - so anything internal is API as far
as they are concerned, and renaming one breaks them exactly as renaming a
public member would. A wrapper compiled against the old names throws
MissingMethodException against this version.

That is why this is a major version even though nothing changed for a caller
outside those eight.

No one has to remember that. Every package here now declares the range of this
one it was built against, bounded at the next major, so NuGet refuses a pairing
that would fail rather than resolving it and leaving the failure until someone
runs the code. SodaFlow and SodaFlow.FSharp ship alongside this release; the
async and bindable object model packages follow with compatibility releases,
each of which moves a dependency range and nothing else.

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
