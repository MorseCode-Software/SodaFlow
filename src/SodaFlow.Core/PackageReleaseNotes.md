5.0.0

PostImpl takes one more argument, for the packages that reach these internals
and for the public Post that SodaFlow and SodaFlow.FSharp give: the action to
run where the posted action does not run or does not complete. A transaction
that fails while it propagates discards each action that Post holds. Code that
gives a value to something which waits, and gives that value from a posted
action, had no way to release it, thus the waiting code waited forever.

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

BREAKING for the packages that reach these internals: AttachListenerImpl is
named AttachListenerInternal. The Impl suffix here marks a method that a public
extension forwards to, and the extension that forwarded to this one is gone from
SodaFlow and SodaFlow.FSharp, so the name follows the one HoldInternal and
HoldLazyInternal use for a method with no public counterpart. Only those two
packages called it, and both move with this release.

BREAKING for the packages that reach these internals, though this assembly has
no public API of its own to move: ListenOnceImpl returns IWeakListener, where it
returned IStrongListener, and ListenOnceStrongImpl is new and returns what
ListenOnceImpl used to. Internal is not private here - this assembly grants
InternalsVisibleTo to the shipping packages around it - so a SodaFlow or a
SodaFlow.FSharp built against 4.x names a method whose return type has changed
and cannot bind to it at run time. Both of those packages carry 5.0.0 as their
floor. No other package calls either method.

The two one-shot listeners are one generic method now rather than two copies of
the same thirty lines, which is what the split would otherwise have created.
Nothing about the sequence changed: the listener stops itself at the first
value, a value that the call to listen sends again unlistens early, and the
caller gets a listener that does nothing in that case.

This is the last of the rename that 2.0.0 began. That release swapped ListenImpl
to the weak listener and removed ListenWeakImpl; ListenOnceImpl kept the old
meaning until now, and was the one name left where the short form rooted the
stream.

4.0.2

Adds the package icon that nuget.org shows beside this package. No source file
changed since 4.0.1.

4.0.1

Grants InternalsVisibleTo to the three collections assemblies:
SodaFlow.Core.Collections, SodaFlow.Collections and
SodaFlow.FSharp.Collections.

Required rather than cosmetic, for the reason 2.1.0 was. The CLR checks
the friend-assembly attribute on the assembly that declares the
internals, so those three throw MethodAccessException at run time
against a 4.0.0 core, even though they build cleanly from source, where
the grants are present. Anything depending on SodaFlow.Collections or
SodaFlow.FSharp.Collections needs this version, and both carry it as
their floor.

No other change, public or internal.

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
