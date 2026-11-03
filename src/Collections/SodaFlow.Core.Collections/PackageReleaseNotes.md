3.0.0

Requires SodaFlow.Core 6.x, which ships in the same release. The major version is for that move: a consumer
that moves to this version moves to that SodaFlow.Core, whose SampleLazy and HoldLazy changed shape. In that
release, a per-key cell made during a transaction that then fails no longer keeps an item from the snapshot the
failure discarded, which the cache of per-key cells made visible: the cell stayed cached, and each later reader saw
the phantom item until that key changed again. The seed of the root snapshot now builds its state inside the function
that SodaFlow can run again after such a failure.

Fixed: the cache of per-key cells compares keys as the collection does. It used the default comparer, so on a
collection keyed with a comparer such as StringComparer.OrdinalIgnoreCase, a key asked for in two spellings got two
cells, each with its own node chain and subscription; both were correct, since the changes they read use the
collection's comparer, but the sharing the cache exists to give was lost between the spellings. One key now gives one
cell, on the collection and on each view of it.

Added: ReactiveCollection.Create, the form that takes the key from an identity that implements IIdentity<TKey>, has an
overload with an IEqualityComparer<TKey> before the initial entries. The selector forms already had one; the identity
form had the default comparer only, so an identity that carries its own key forced strict equality on it.

Fixed: a request for a per-key cell from outside a transaction could deadlock with a request from inside one.
StateCell, IdentityCell, and ItemCell took the gate of their cache first and, on a miss, the transaction lock inside
the creation of the cell. A projection that runs in a transaction, such as every row that MappedItems builds, holds
the transaction lock and then waits for the gate. A UI thread asking for a row's cell while an edit propagated on
another thread could stop both threads for good. A miss now takes the transaction lock first and the gate inside it,
which is the order a request from a transaction already used, and looks the cache up again under both locks, so one
key still gets one cell. A hit takes only the gate, as before, and opens no transaction. A test runs the two threads
against each other and stopped in its first round on the previous code.

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

Changed: the per-key cell caches are keyed by the projection that fills them, not by the projected type alone. The
internal StateCellImpl, IdentityCellImpl, and ItemCellImpl take a CellProjection, which holds the two functions that
make a cell's value and is the key of its cache, instead of the two functions as separate parameters. Before, two
callers projecting to one type with different functions would have shared a cache, and the second would have got cells
giving the first's values; no caller did that, and nothing stopped one. The language packages each hold one
projection per cell type, so what they return is unchanged. Only the internal surface moved, so SodaFlow.Collections
and SodaFlow.FSharp.Collections need this version of the core, which their dependency range gives them.

Changed: an edit of many keys in one transaction, with many per-key cells on a view, no longer costs every cell a scan
of every operation. A per-key cell on a view asks the change at each transaction whether an operation names its key,
and the change answered with a linear scan of its operations, so n updated keys against m cells cost n × m key
comparisons, almost all of which found nothing. A single-key edit is one comparison per cell, and a change too wide
for a list of operations is a reset, which answers from the store; the cost lived between the two. A change with eight
or more operations now builds a dictionary from each key to whether an insert or a removal names it, at its first
lookup after eight scans, and the three projections answer from it after that. One observer on a wide change still
pays a scan, and nothing pays more than a constant multiple of the cheaper path. The lookups run in the transaction
that sends the change, so the index needs no lock. Nothing public changes.

In a new benchmark, ViewChangeProjectionBenchmarks, an edit of 1,000 keys in one transaction through a filter with 100
state cells on it takes 731 µs where it took 868 µs on .NET 10, against 690 µs for the same cells on the collection;
on .NET Framework 4.7.2 it takes 1,402 µs where it took 1,903 µs, against 1,361 µs on the collection. At 100 keys and
100 cells it takes 80 µs where it took 97 µs on .NET 10, equal to the collection; and at 10 keys the two arms measure
the same as before. The index costs about 22 KB for a change of 1,000 operations.

The edit streams of a collection now merge through the one node that a list merge gets in SodaFlow.Core 6.0.0. Before,
they were folded into a chain of pairwise merges that started from a never-firing stream, so an edit crossed one node
for each stream before it, and a collection with one edit stream, the usual shape, paid a merge with that never-firing
stream on every edit. Edits from several streams in one transaction still combine in the order of the streams.
Measured on one edit of one key, on a collection of a thousand items with one observed key, .NET 10: through the first
of 1, 2, 8, and 32 edit streams, 1.71, 1.95, 3.12, and 7.92 µs before and 1.51, 1.65, 1.70, and 1.67 µs after; through
the last stream, which the chain served in one hop, 1.76, 1.74, 1.69, and 1.72 µs before and 1.52, 1.65, 1.71, and
1.69 µs after.

A change of direction on a sort no longer copies the sorted set. A sort stage that receives the same sort value in the
opposite direction did not sort every key again, but it did copy the set it held into a new sorted set, which sorted
the entries once more and allocated a node for each. The stage now reads the set it holds from its end through a view
that costs nothing to make, and a change back to the original direction drops the view. The view keeps the documented
rule for ties: the key still breaks a tie in ascending order in both directions, so a run of keys with equal sort
values keeps its sequence, as a fresh build in the new order would have it. A read of one position through the view
costs O(log² n) rather than O(log n), because it has to find the ends of the run that holds the position; a read of
all the keys stays O(n), and an edit after the change costs what it cost before plus one small object. Measured in a
new benchmark, SortReverseBenchmarks, on a collection of 100,000 items sorted by an order cell with a listener on the
view, .NET 10: the toggle took 13.3 ms and allocated 6.7 MB before, of which the copy alone was 11.3 ms, and takes
2.1 µs and 4.2 KB after. On .NET Framework 4.7.2 it took 25.7 ms before and takes 4.0 µs after. The toggle followed by
a read of every key, which is what a bound list does at a reset, took 19.5 ms before and takes 4.5 ms after on .NET 10,
and 34.3 ms before and 11.0 ms after on .NET Framework, so the walk through the runs costs less than the copy it
replaced even for a consumer that reads the whole list.

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
