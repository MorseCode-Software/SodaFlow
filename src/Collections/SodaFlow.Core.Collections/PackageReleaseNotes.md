1.0.0

First release.

Requires SodaFlow.Core 4.x and SodaFlow.Functional 3.x.

SodaFlow.Functional is a dependency here where it is not one of the other core
packages. Optionality in this API is Maybe<T> rather than null throughout, so
the type is in the public surface rather than an implementation detail of a
language wrapper.

---

About this package

The engine behind SodaFlow.Collections and SodaFlow.FSharp.Collections. It
declares the types a consumer holds - Entry, CollectionSnapshot,
CollectionChange, CollectionEdit, IStateMap, IOrderedKeys, CollectionViewChange
and the ViewOperation hierarchy - along with FrpCollection itself and the view
chain the language wrappers expose.

Install one of those two rather than this. On its own this package gives you
types whose operations are internal, reached only through the wrappers.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
