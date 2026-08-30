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
