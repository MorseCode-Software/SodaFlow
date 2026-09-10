1.0.0

First release.

Requires SodaFlow.Core 4.x, and nothing else from this repository.

IStateMap exposes Pairs as well as Keys, for anything that reads the whole
collection - a total, an average, a count. Iterating Keys and looking up each
one answers the same question and costs an O(log32 n) search per item; on a
hundred thousand items that measured three times slower.

There is no optional type in this API. Lookups are TryGetItem, TryGetState and
TryGetNewState, and IndexOf answers -1, so that each language surface can put
its own optional type on top - Maybe in SodaFlow.Collections, option in
SodaFlow.FSharp.Collections - and neither pays for the other's.

---

About this package

The engine behind SodaFlow.Collections and SodaFlow.FSharp.Collections. It
declares the types a consumer holds - Item, CollectionSnapshot,
ItemChange, CollectionEdit, IStateMap, IOrderedKeys, CollectionViewChange
and the ViewOperation hierarchy - along with ReactiveCollection itself and the view
chain the language wrappers expose.

Install one of those two rather than this. On its own this package gives you
types whose operations are internal, reached only through the wrappers.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
