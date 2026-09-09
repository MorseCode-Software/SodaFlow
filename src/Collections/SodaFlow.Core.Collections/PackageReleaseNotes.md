1.0.0

First release.

Requires SodaFlow.Core 4.x, and nothing else from this repository.

There is no optional type in this API. Lookups are TryGetEntry, TryGetState and
TryGetNewState, and IndexOf answers -1, so that each language surface can put
its own optional type on top - Maybe in SodaFlow.Collections, option in
SodaFlow.FSharp.Collections - and neither pays for the other's.

---

About this package

The engine behind SodaFlow.Collections and SodaFlow.FSharp.Collections. It
declares the types a consumer holds - Entry, CollectionSnapshot,
CollectionChange, CollectionEdit, IStateMap, IOrderedKeys, CollectionViewChange
and the ViewOperation hierarchy - along with ReactiveCollection itself and the view
chain the language wrappers expose.

Install one of those two rather than this. On its own this package gives you
types whose operations are internal, reached only through the wrappers.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
