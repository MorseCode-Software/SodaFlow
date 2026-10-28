2.0.1

Fixed: making a cell for each key of a large collection took time that grew with the square of the number of keys.
StateCellImpl, IdentityCellImpl, and ItemCellImpl keep each cell they make in a weak cache, and once that cache held 64
entries, every add first swept all of it for cells the garbage collector had removed. With every cell still in use,
the sweep found nothing and ran again on the next add. The cache now sweeps again only after it has doubled in size
since its last sweep, so on average the sweeps cost the same for each add however large the cache gets. A cell the
garbage collector removed still leaves the cache at the next sweep.

In a benchmark that makes a state cell for each key of a collection and keeps every cell, the cells for 10,000 keys
took 127 ms where they took 449 ms on .NET 10, and 149 ms where they took 726 ms on .NET Framework 4.7.2.
PerKeyCellBenchmarks in SodaFlow.Benchmarks measures it.

Changed: each per-key cell, from StateCell, IdentityCell, and ItemCell on a collection or a view, builds one node
fewer. The cell was a map that projects the change for its key, a filter that drops the changes for other keys, and a
hold; the map and the filter are now one node. A cell allocates about 930 bytes less, which is 23 percent of what it
allocated, and in the benchmark above the cells for 10,000 keys take 79 ms where they took 128 ms on .NET 10, and
104 ms where they took 146 ms on .NET Framework 4.7.2. What a cell fires is unchanged.

The cost per cell in that benchmark looked as if it grew with the number of keys, from about 8 µs at 1,000 keys to
about 13 µs at 10,000 on .NET 10, and the earlier entry said the reason was not found. It is the garbage collector: the
benchmark keeps every cell, so every object a cell is made of survives every collection and is copied forward, and
that copying starts once the cells outgrow the budget of the youngest generation. With collections suppressed, the
cost is flat at about 4 µs per cell from 2,000 to 20,000 keys, and what a cell allocates is the same at every size.
Fewer objects per cell is the lever, which is what the change above pulls.

2.0.0

Adds the fold of a collection, which CollectionFoldUtility.FoldImpl gives to each language surface. A total, a count of
a view, or an average had no operation here, thus each caller wrote the delta of a change by hand. The fold takes a
group: a function that reads the identity and the state of one item, a zero, an add, and a subtract. Each change carries
the states before it and the states after it, thus the fold removes the previous value of each key that the change names
and adds the new one. The cost of an edit is the count of the keys in that edit.

CollectionFoldUtility.FoldByIdentityImpl gives the fold of the identities. It reads only the item changes that add or
remove a key, and each reset. A reset puts a key in Added only when the view did not have it before, thus it does not
name a key that one edit replaced and that stays in the view. The fold reads each key of a reset for that cause.
ItemChange has an internal IsReset for it, and its constructor takes that value.

A group, and not one combine function, because only an invertible operation can answer an edit with no read of each
item. A sum and a count are groups. A maximum is not one, and this operation cannot give one.

The first value is a read of the store at the construction, thus a caller gives no seed. A view folds its own items,
because the States of a scoped snapshot hold the keys of that view alone. A change from a reset names each key of the
view, thus the fold stays correct at a cost of the size of the view for that one change.

BREAKING: KeyOrder.ByKey (keyComparer) is now ByKey (keyComparer, isDescending), as each other factory that takes a
comparer also names its direction. Add isDescending: false to keep an existing order, or call ByKey () where the
comparer was Comparer<TKey>.Default. Adds ByKey () and ByKeyDescending (), with the default comparer.

BREAKING: MappedItems<TResult> is a sealed class, where it was a readonly struct, as the status of MapAsync is. A struct
with a Dispose is a handle that each copy duplicates, and its default value threw at a disposal. Source that reads Items
and calls Dispose needs no edit. Anything compiled against 1.x does, because a struct and a class are not the same type
to the runtime. default (MappedItems<T>) is null now. A second Dispose never releases an object that the first one
released.

BREAKING for the two language surfaces: SortByKeyImpl takes the direction, and FilterByIdentityImpl takes a cell of the
predicate in place of the predicate. A SodaFlow.Collections or SodaFlow.FSharp.Collections built against 1.x does not
run against this; take the 2.x of each with it.

Adds the internal members that the two language surfaces need for ItemCell:
ItemCellImpl and CreateItemCell on ReactiveCollection, and the projection for one item on an item change and on a view
change.

Fixed: the stage of a slice lost an edit to a key that kept its position in the window. The stage compares the window
before a change with the window after it, and it gave only a ViewUpdate to a key that the comparison found in both. A
ViewMove from a sort above carries a new state, and a replacement arrives as a ViewRemove and a ViewInsert. The stage
now gives a ViewUpdate for the first, and a ViewRemove and a ViewInsert at the same index for the second.

Requires SodaFlow.Core 5.x, which ships in the same release.

1.0.1

Adds the package icon that nuget.org shows beside this package. No source file changed since 1.0.0.

Every package here ships this release together, so the dependency versions move with it.

1.0.0

First release.

Requires SodaFlow.Core 4.0.1 or later, and nothing else from this repository. 4.0.1 is the release that grants this
assembly access to the core's internals, which it reaches throughout; against 4.0.0 it builds from source but throws
MethodAccessException at run time, so the dependency floor is 4.0.1 rather than 4.0.0.

StateMap exposes Pairs as well as Keys, for anything that reads the whole collection - a total, an average, a count.
Iterating Keys and looking up each one answers the same question and costs an O (log32 n) search per item; on a hundred
thousand items that measured three times slower.

OrderedKeys and StateMap are abstract classes with internal constructors rather than interfaces, so what a snapshot
answers with is closed: one storage strategy and one filtered face of it, and two kinds of key set. What builds the next
version of either is not on them - that is the protocol between stages, and a version built by anyone else would be one
the collection had never heard of. The Create overloads that took a state map are gone with it, since nothing outside
could supply one.

IIdentity is the one interface left, and it is the one meant to be implemented:
say an identity carries its own key and Create will take it from there.

There is no optional type in this API. Lookups are TryGetItem, TryGetState and TryGetNewState, and IndexOf answers -1,
so that each language surface can put its own optional type on top - Maybe in SodaFlow.Collections, option in
SodaFlow.FSharp.Collections - and neither pays for the other's.

---

About this package

The engine behind SodaFlow.Collections and SodaFlow.FSharp.Collections. It declares the types a consumer holds - Item,
CollectionSnapshot, ItemChange, CollectionEdit, StateMap, OrderedKeys, KeyOrder, CollectionViewChange and the
ViewOperation hierarchy - along with ReactiveCollection itself and the view chain the language wrappers expose.

Install one of those two rather than this. On its own this package gives you types whose operations are internal,
reached only through the wrappers.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
