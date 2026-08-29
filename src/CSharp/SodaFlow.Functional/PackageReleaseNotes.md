1.0.1

Complete XML documentation for every public type and member.

Fixed: Maybe<T>.Map carried a typeparam tag for the containing type's
parameter, which is not legal on a method.

No API change. This package does not depend on SodaFlow.Core, so it is
unaffected by the breaking change in the rest of the 2.0.0 release and stays on
1.x.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases/tag/sodaflow-2.0.0
