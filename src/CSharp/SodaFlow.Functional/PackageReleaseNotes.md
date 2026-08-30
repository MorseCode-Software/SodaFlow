2.0.0

BREAKING: WhereMaybe and AllMaybeOrNone are gone, renamed to WhereSome and
AllSomeOrNone. Both did exactly what the new names say, under old ones that
named the type where every other member here names the case that has a value.
Rename the calls; nothing else about either changed. These are renames and not
deprecations because the package is days old and there is no installed base
worth carrying the old names for.

New: a Maybe<T> vocabulary large enough to use without falling back to Match
for everything.

Building one: Maybe.SomeIf(condition, value) and its lazy overload turn an
if/then which produces a value in one branch and nothing in the other straight
into a Maybe<T>. Maybe.SomeNotNull, and the ToMaybe / ToNullable extension
methods, bridge to and from null references and Nullable<T>. SomeNotNull is
deliberately not what Some does: Some(null) still contains null, which is what
lets Maybe<string> tell no value apart from the value null.

Working with one: ValueOr, ValueOrDefault, ValueOrThrow, OrElse, Where,
Select, and Lift for two, three and four inputs. Select and Where complete the
set the compiler looks for, so query syntax now works over a Maybe<T> - the
SelectMany it needed was already there.

Across an await: MapAsync, BindAsync and WhereAsync on Maybe<T>, for when the
work is asynchronous, and the same operators on Task<Maybe<T>> - along with
Map, Bind, Where, Match, OrElse, ValueOr, ValueOrDefault and ValueOrThrow - so
a chain which starts asynchronously can be continued without awaiting in the
middle of it. MatchAsync only ever covered the consuming side. Nothing runs on
the empty path, which returns one cached completed task per type rather than
allocating per miss.

There is deliberately no Maybe<Task<T>> to Task<Maybe<T>> conversion: that
shape almost always means Map was used where MapAsync was meant, and shipping
the repair would make the mistake easier to keep.

Sequences: Choose to map and filter in one step, an AllSomeOrNone overload
taking the mapping function, ToEnumerable, and FirstOrNone, LastOrNone,
SingleOrNone and ElementAtOrNone for the LINQ operators whose OrDefault forms
cannot say whether they found anything. SingleOrNone still throws when there is
more than one element, exactly as SingleOrDefault does: that is a contradicted
assumption rather than a missing answer.

Parsing and lookup, replacing bool Try...(v, out result) with
Maybe<TResult> Try...(v): TryParse for every numeric type, plus Boolean, Char,
Guid, DateTime, DateTimeOffset, TimeSpan and Uri, on string; TryParseEnum and
TryParseDefinedEnum, the second of which rejects the undeclared numbers
Enum.TryParse accepts; and TryGetValue on IReadOnlyDictionary<TKey, TValue>.

Maybe.FromTryGet adapts any other method of that shape, including ones this
package has never heard of, and the TryGet delegate types it takes are public.

Fixed: Maybe<T> and all seven Either arities now implement IEquatable<T>.
They are structs which did not, so EqualityComparer<T>.Default could not find a
typed comparison and fell back to the one which compares through
Equals(object), boxing both operands on every comparison - in Distinct,
Contains, IndexOf, GroupBy and every dictionary lookup. Being structs is how
these types avoid allocating, and this made them allocate anyway, in exactly
the collection-heavy code which would notice. No behavior change: the new
Equals is the same comparison the == operator already made.

Apart from the removal above, everything here is new API, and nothing else that
shipped in 1.0.x has changed behavior.

1.0.2

Carries the release notes below, which 1.0.1 shipped without. No code change
since 1.0.1 - the mechanism that reads these from a file landed after that
version was tagged, so there was no way to attach them to it.

1.0.1

Complete XML documentation for every public type and member.

Fixed: Maybe<T>.Map carried a typeparam tag for the containing type's
parameter, which is not legal on a method.

No API change. This package does not depend on SodaFlow.Core, so it was
unaffected by the breaking change in the rest of the 2.0.0 release and stays
on 1.x.

---

About this package

The small functional vocabulary the C# API needs and C# does not ship with:
Maybe<T>, Either<T1,T2> through Either of eight cases, and Unit.

No FRP in it, and no dependency on anything else here, so it can be used on its
own. F# already has option, Result and unit, which is why SodaFlow.FSharp does
not reference it.

Maybe<T> has no property that hands out the value unchecked: reach it with
Match, or one of the helpers built on it, so the case where there is none has
to be answered for. Either works the same way.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
