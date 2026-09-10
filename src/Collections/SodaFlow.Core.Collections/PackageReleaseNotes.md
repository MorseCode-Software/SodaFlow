1.0.0

First release.

Requires SodaFlow.Core 4.x, and nothing else from this repository.

StateMap exposes Pairs as well as Keys, for anything that reads the whole
collection - a total, an average, a count. Iterating Keys and looking up each
one answers the same question and costs an O(log32 n) search per item; on a
hundred thousand items that measured three times slower.

OrderedKeys and StateMap are abstract classes with internal constructors rather
than interfaces, so what a snapshot answers with is closed: one storage strategy and
one filtered face of it, and two kinds of key set. What builds the next version
of either is not on them - that is the protocol between stages, and a version
built by anyone else would be one the collection had never heard of. The Create
overloads that took a state map are gone with it, since nothing outside could
supply one.

IIdentity is the one interface left, and it is the one meant to be implemented:
say an identity carries its own key and Create will take it from there.

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
