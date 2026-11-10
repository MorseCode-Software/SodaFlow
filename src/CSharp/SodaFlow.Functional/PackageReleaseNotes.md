4.0.0

BREAKING: a default Either holds no case. Before, default(Either<T1, T2>), and with it an Either that was never
assigned, such as a field or an array slot, matched as the first case with the default value of T1, which for a
reference type handed null to onFirst without a warning. The case index is now 1-based, so the zero an unassigned
struct carries means no case, and Match throws InvalidOperationException for it; every other member that reads the
value goes through Match, so it throws too. A default instance is equal to a default instance and to nothing else,
and its hash code is that of no case. Code that read a default on purpose and expected the first case has to make the
value with First instead. Maybe<T> already had a safe default, None, and the two structs now agree. The tests that
pinned the old behavior now pin the throw.

Fixed: Maybe<T>.CompareTo allocated two closures on every call. It was two nested Match calls, and each one captured a
value. A sort calls it about n log n times, so sorting 100,000 Maybe<int> values on .NET 10 took 45.5 ms and allocated
396 MB. CompareTo now reads the two fields directly and allocates nothing. The same sort takes 6.1 ms, a little under
the 6.6 ms that sorting the same values as Nullable<int> takes. On .NET Framework 4.7.2 it went from 46.4 ms and 402 MB
to 9.4 ms and no allocation. The ordering is unchanged. MaybeCompareBenchmarks in SodaFlow.Benchmarks measures it.

Fixed: the other members of Maybe<T> that took a lambda allocated on every call. Map, Bind, MatchVoid, MatchSome,
MatchNone, ValueOr with a value, ValueOrThrow, both OrElse, the three Lift, MapAsync, WhereAsync, and the IMaybe
members each went through Match with a lambda that captured an argument, so each call allocated a closure and a
delegate, and Map, the action members, and Lift allocated two or more. Every member now reads the fields through one
internal TryGetValue, and allocates nothing. Match is unchanged, and the public surface does not change: there is
still no way to read the value without answering both cases. Measured on a Maybe<int> with a value, .NET 10: Map 13.0
ns and 112 bytes to 0.6 ns and nothing, MatchSome 13.2 ns and 112 bytes to 0.1 ns and nothing, MatchVoid 18.2 ns and
176 bytes to 0.4 ns and nothing, Lift of two 38.3 ns and 328 bytes to 1.7 ns and nothing. On .NET Framework 4.7.2, Map
26.2 ns and 177 bytes to 5.2 ns and nothing, and Lift 65.1 ns and 457 bytes to 9.8 ns and nothing. MaybeBenchmarks in
SodaFlow.Benchmarks measures them. Maybe<T> is also a readonly struct now, as Unit already was, so a call on a
readonly field or on None no longer copies the value first. The members read the same fields in the same order, so a
contained null still reaches the function, and a function for the other case still does not run.

Fixed: MaybeMonad.SelectMany and MaybeMonad.Where, the two members a query expression over a Maybe<T> calls, still
allocated. SelectMany went through two nested Bind calls whose lambdas captured the transformation, the collation, and
the first value, so a query with two from clauses allocated two closures and two delegates, and Where captured its
predicate in one. Both now read the value through TryGetValue, as the members above do, and allocate nothing; the
results are unchanged, and the predicate still does not run without a value. Measured on .NET 10: a query from two
maybes 27.1 ns and 256 bytes to 7.7 ns and 64 bytes, Where 10.1 ns and 88 bytes to 1.0 ns and nothing, and a query
with a where clause between two from clauses 39.7 ns and 344 bytes to 9.6 ns and 64 bytes. On .NET Framework 4.7.2,
36.4 ns to 14.2 ns, 17.6 ns to 5.7 ns, and 55.2 ns to 21.2 ns. The 64 bytes that remain on the two queries are the
delegate the query expression itself makes for a lambda that reads an instance field, which no library change can
remove; a query over locals or statics has none.

Fixed: the action and map members of every Either allocated on every call, and the untyped interface members did too.
MatchVoid wrapped each action in a closure, one per case, so a call on an Either of eight allocated eight; each
MapFirst through MapEighth captured its function in a closure; and IEitherOfTwo.Match through IEitherOfEight.MatchVoid
wrapped each function or action again. Each of them is now a switch on the case index, the shape Match already had,
with the no-case throw in its default arm, so a default instance still throws from every member. Match, Swap, IsFirst,
TryGetFirst, and ToString were already free. MatchAsyncVoid and the untyped MatchAsyncVoid keep their ToAsyncFunc
wrappers, because those turn a synchronous throw into a faulted task. Every Either is also a readonly struct now, as
Maybe<T> is, so a call on a readonly field or a static readonly value no longer copies the struct, which for an Either
of eight is nine fields. Measured on an Either holding its first case, .NET 10: MatchVoid of two 22.6 ns and 176 bytes
to 0.5 ns and nothing; MapFirst of two 7.3 ns and 24 bytes to 1.2 ns and nothing; MatchVoid of eight 82.0 ns and 704
bytes to 43.6 ns and 512 bytes, where the bytes that remain are the benchmark's own eight delegates for lambdas that
capture a counter; MapFirst of eight 16.8 ns and 88 bytes to 5.3 ns and nothing. On .NET Framework 4.7.2, MatchVoid of
eight 130.3 ns and 1220 bytes to 41.4 ns and 514 bytes, and MapFirst of eight 32.8 ns and 88 bytes to 15.0 ns and
nothing. EitherBenchmarks in SodaFlow.Benchmarks measures them.

Fixed: the ToAsyncFunc overloads awaited the action without ConfigureAwait(false), so the continuation that gives
Unit.Value went back to the caller's synchronization context. A caller on a single-threaded context, such as a UI
thread, that blocked on the task from MatchAsyncVoid, or from a wrapper of its own, with an action that completed on
another thread, never got that continuation and hung. The wrappers now stay off the caller's context, as the rest of
this package already did. A caller that awaits the task sees no change, because its own await decides where it
continues. The build now fails on an unconfigured await in library code, so the omission cannot come back.

Added: a net8.0 build, beside the net472, net6.0, and netstandard2.0 builds. It compiles the same source. A project on
.NET 8 or later now takes it in place of the net6.0 build, and a project on .NET 6 or .NET 7 still takes the net6.0
build.

3.0.2

The documentation comments are rewritten in Simplified Technical English, and the ones that the rewrite damaged are
repaired. Only the comments changed since 3.0.1: the compiled code is the same, and the XML documentation file that
ships beside the assembly is what differs.

3.0.1

Adds the package icon that nuget.org shows beside this package.

Unit.Equals now carries NotNullWhen on every target framework, where it carried the attribute on .NET only. The
attribute comes from an internal polyfill on net472 and netstandard2.0, which do not declare it themselves. This tells a
caller's compiler what the method already did, and changes nothing at run time.

3.0.0

BREAKING: Unit is a readonly struct rather than a sealed class. It is a type with exactly one value, so a struct is what
it should always have been:
nothing to allocate, nothing to be null, and nothing to reference-compare. It now implements IEquatable<Unit>,
IComparable<Unit>, IStructuralEquatable and IStructuralComparable, and ToString returns "()".

This breaks in both directions. In source, Unit u = null no longer compiles, and neither does anything else which
treated it as a reference. In binary, a generic instantiated over Unit - Stream<Unit>, StreamSink<Unit>, Func<Unit>

- is a different type at runtime once Unit is a struct, so an assembly compiled against the old one will not bind
  against this. Recompile rather than mix.

The == operator no longer asks whether either side is null, because neither side can be. Two Units are equal, and that
is the whole of it.

Everything else here is a cleanup with no effect on behavior: nullable annotations completed, the build made
warning-free, and the vendored JetBrains annotations replaced by a reference to the package they came from, which is not
redistributed.

2.0.0

BREAKING: WhereMaybe and AllMaybeOrNone are gone, renamed to WhereSome and AllSomeOrNone. Both did exactly what the new
names say, under old ones that named the type where every other member here names the case that has a value. Rename the
calls; nothing else about either changed. These are renames and not deprecations because the package is days old and
there is no installed base worth carrying the old names for.

New: a Maybe<T> vocabulary large enough to use without falling back to Match for everything.

Building one: Maybe.SomeIf (condition, value) and its lazy overload turn an if/then which produces a value in one branch
and nothing in the other straight into a Maybe<T>. Maybe.SomeNotNull, and the ToMaybe / ToNullable extension methods,
bridge to and from null references and Nullable<T>. SomeNotNull is deliberately not what Some does: Some (null) still
contains null, which is what lets Maybe<string> tell no value apart from the value null.

Working with one: ValueOr, ValueOrDefault, ValueOrThrow, OrElse, Where, Select, and Lift for two, three and four inputs.
Select and Where complete the set the compiler looks for, so query syntax now works over a Maybe<T> - the SelectMany it
needed was already there.

Across an await: MapAsync, BindAsync and WhereAsync on Maybe<T>, for when the work is asynchronous, and the same
operators on Task<Maybe<T>> - along with Map, Bind, Where, Match, OrElse, ValueOr, ValueOrDefault and ValueOrThrow - so
a chain which starts asynchronously can be continued without awaiting in the middle of it. MatchAsync only ever covered
the consuming side. Nothing runs on the empty path, which returns one cached completed task per type rather than
allocating per miss.

There is deliberately no Maybe<Task<T>> to Task<Maybe<T>> conversion: that shape almost always means Map was used where
MapAsync was meant, and shipping the repair would make the mistake easier to keep.

Sequences: Choose to map and filter in one step, an AllSomeOrNone overload taking the mapping function, ToEnumerable,
and FirstOrNone, LastOrNone, SingleOrNone and ElementAtOrNone for the LINQ operators whose OrDefault forms cannot say
whether they found anything, and MinOrNone, MaxOrNone and AggregateOrNone for the ones which throw on an empty sequence
rather than answer at all. MinOrNone and MaxOrNone skip nulls the way Min and Max do, since Comparer<T>.Default sorts
null before everything; a sequence of nothing but nulls has nothing to compare and gives no value, where LINQ gives
null. SingleOrNone still throws when there is more than one element, exactly as SingleOrDefault does: that is a
contradicted assumption rather than a missing answer.

Parsing and lookup, replacing bool Try... (v, out result) with Maybe<TResult> Try... (v): TryParse for every numeric
type, plus Boolean, Char, Guid, DateTime, DateTimeOffset, TimeSpan and Uri, on string; TryParseEnum and
TryParseDefinedEnum, the second of which rejects the undeclared numbers Enum.TryParse accepts; and TryGetValue on
IReadOnlyDictionary<TKey, TValue>.

Maybe.FromTryGet adapts any other method of that shape, including ones this package has never heard of, and the TryGet
delegate types it takes are public.

Nullable reference types are enabled. The annotations say what the members already documented: a null string parses as
no value, a null sequence counts as empty, a null dictionary has no entry, a null comparer means the default one, and
ValueOrDefault answers with something that may be null.

SomeNotNull and ToMaybe now take T? for a reference type, which is the point of them - whether the reference is null is
the question they are asked.

Nullability is carried by the type argument, as it is for every other generic container. Maybe<string> cannot hold null
and Maybe<string?> can; the same for each position of an Either. Some (null) still stores a null rather than collapsing
to None, so the type argument has to be a nullable one to allow it, and Match hands back exactly what was stored.

This can produce new warnings in code that has nullable enabled and was passing null where the annotations now say not
to. It changes no behavior and no signature: nothing was made non-generic, nothing was renamed, and nothing that
compiled without nullable enabled stops compiling.

New on Either: Swap, on the two-case either only, which exchanges the cases. It is how an operation that only addresses
the second case reaches the first, and swapping twice gives back the original. There is no Swap beyond two cases, where
there is no single exchange to make.

Maybe<T> also implements IComparable<Maybe<T>>, ordering no value before every value, which is how Nullable<T> is
ordered under Comparer<T>.Default. OrderBy and Array.Sort therefore work on it, and do so without boxing. No < and >
operators come with it: Nullable<T> has them and they answer false in both directions when either side is absent, so !
(a < b) stops meaning a >= b.

Fixed: Maybe<T> and all seven Either arities now implement IEquatable<T>. They are structs which did not, so
EqualityComparer<T>.Default could not find a typed comparison and fell back to the one which compares through Equals
(object), boxing both operands on every comparison - in Distinct, Contains, IndexOf, GroupBy and every dictionary
lookup. Being structs is how these types avoid allocating, and this made them allocate anyway, in exactly the
collection-heavy code which would notice. No behavior change: the new Equals is the same comparison the == operator
already made.

Apart from the removal above, everything here is new API, and nothing else that shipped in 1.0.x has changed behavior.

This package depends on nothing, so it declares no ranges of its own. What it gains is a ceiling in everyone else's: the
packages built on it - SodaFlow, SodaFlow.Async and SodaFlow.Bindable.ObjectModel - now declare it as a range bounded at
the next major. A consumer on an older one of those cannot be moved onto this version by a restore that resolves cleanly
and then fails on a method the rename took away.

1.0.2

Carries the release notes below, which 1.0.1 shipped without. No code change since 1.0.1 - the mechanism that reads
these from a file landed after that version was tagged, so there was no way to attach them to it.

1.0.1

Complete XML documentation for every public type and member.

Fixed: Maybe<T>.Map carried a typeparam tag for the containing type's parameter, which is not legal on a method.

No API change. This package does not depend on SodaFlow.Core, so it was unaffected by the breaking change in the rest of
the 2.0.0 release and stays on 1.x.

---

About this package

The small functional vocabulary the C# API needs and C# does not ship with:
Maybe<T>, Either<T1,T2> through Either of eight cases, and Unit.

No FRP in it, and no dependency on anything else here, so it can be used on its own. F# already has option, Result and
unit, which is why SodaFlow.FSharp does not reference it.

Maybe<T> has no property that hands out the value unchecked: reach it with Match, or one of the helpers built on it, so
the case where there is none has to be answered for. Either works the same way.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
