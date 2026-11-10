3.0.0

Requires SodaFlow.Core 6.x, which ships in the same release. The major version is for that move: a consumer
that moves to this version moves to that SodaFlow.Core, whose SampleLazy and HoldLazy changed shape. In that
release, a per-key cell made during a transaction that then fails no longer keeps an item from the snapshot the
failure discarded, which the cache of per-key cells made visible: the cell stayed cached, and each later reader saw
the phantom item until that key changed again. The seed of the root snapshot now builds its state inside the function
that SodaFlow can run again after such a failure.

BREAKING: Take(int) and Slice(int, int) throw ArgumentOutOfRangeException for a negative count or offset, where
before they clamped it to zero and gave an empty or shifted view with nothing to show for the error. A negative
constant is almost always an arithmetic slip in the caller, and the view is built where the slip is. The Cell<int>
overloads still clamp, because a throw there would fail whichever transaction moved the cell; the documentation now
says so. An offset past the end still gives an empty page on both.

Fixed: the cache of per-key cells compares keys as the collection does. It used the default comparer, so on a
collection keyed with a comparer such as StringComparer.OrdinalIgnoreCase, a key asked for in two spellings got two
cells, each with its own node chain and subscription; both were correct, since the changes they read use the
collection's comparer, but the sharing the cache exists to give was lost between the spellings. One key now gives one
cell, on the collection and on each view of it.

Added: ReactiveCollection.Create, the form that takes the key from an identity that implements IIdentity<TKey>, has an
overload with an IEqualityComparer<TKey> before the initial entries. The selector forms already had one; the identity
form had the default comparer only, so an identity that carries its own key forced strict equality on it.

Added: the same identity form has two overloads that take the initial entries as a Cell<IEnumerable<Item<TIdentity,
TState>>>, with and without a comparer, as the selector form already did. The transaction that builds the collection
samples the cell at its close.

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

An order with more than one level now takes the same path when every level changes direction, and setting such an
order again is now a no-op. Before, two orders built with `ThenBy` were never recognized as the same order or as each
other's reverse, because the combined comparer was a new instance at each construction, so a toggle of every level
sorted every key again and a repeat of an equal order rebuilt the stage. An order now keeps the selector, the comparer,
and the direction of each level and compares them level by level: all the same is the same order, all opposite is the
reverse, and anything else, such as one level of two flipped, is a different order and builds again, as it must,
because keys tied on the first level keep their second-level sequence. Measured in the same benchmark on a two-level
order over 100,000 items: the toggle took 84.5 ms and allocated 13.7 MB before and takes 2.3 µs and 4.2 KB after on
.NET 10, and took 182.3 ms before and takes 4.0 µs after on .NET Framework 4.7.2.

Changed: a view stage, which is what Filter, SortBy, Take, and the other view operations build, is made of two nodes
fewer and allocates about 3.5 KB less, 16 percent of what it allocated. The stage lifted the criteria, the keys above
it, and the store into one cell and read that cell in its snapshot; the snapshot now reads the three cells itself. The
stage also mapped a change from above and a new criteria into one shape each and merged the two; one merge now takes the
two as they are, and that merge is the one-node merge of two types that SodaFlow.Core 6.0.0 adds. What a stage fires is
unchanged. Measured on .NET 10 on a ten-item collection, bytes and time to build inside one transaction: a filter 22,064
to 18,607 bytes and 129 to 113 µs, a sort 23,790 to 20,633 bytes and 140 to 112 µs, a take 23,235 to 20,072 bytes and
115 to 104 µs, and a filter, a sort, and a take chained 54,411 to 43,363 bytes and 251 to 205 µs. The first view on a
collection also pays, once, for the collection's own order of arrival, which is built at the first read of its keys: 8.6
KB and about 35 µs of the figures above.

A cell for one key, from StateCell, IdentityCell, or ItemCell, allocates about 310 bytes less to make, and a request
that finds the cell in the cache now allocates nothing. The builder of each cell runs in the transaction that the cache
opens, where it opened one of its own, and takes its first value through one lazy sample, where it took a sample and a
map on it. A request that found its cell still paid for the closure of a transaction it never opened, about 56 bytes,
because that closure captured the arguments as the method began. The folds and the lazy parts of a view stage now hand
their values to the transaction rather than capture them. Measured in PerKeyCellBenchmarks, a collection and a state
cell for each of its keys, .NET 10: at 1,000 keys 2,124 to 1,809 KB and 2.58 to 2.41 ms, at 10,000 keys 21.3 to 18.1 MB
and 52.0 to 45.0 ms, and through a filter view at 1,000 keys 2,426 to 2,114 KB. On .NET Framework 4.7.2 the 10,000 keys
take 70.8 ms before and 61.7 ms after.

A view stage is two nodes smaller, and a change crosses two fewer nodes on its way through it. The keys that a stage
gives to other code, and the changes that it fires, each came from a filter and a map: two streams, each with its own
node and link. Each is now one node that tests and maps in one step, and the change stream tests a result before it
builds the change, so a result that is dropped builds nothing. The shape cell of a view, built at its first read, and
the shape cell of the collection itself take the same single node. Measured on .NET 10, one run on each side: a filter
view of a ten-item collection 8.8 to 8.1 KB and 35.0 to 31.4 µs to build; through a filter, a sort, and a take chained
on 1,000 items, an edit 7.25 to 6.90 µs and 13.3 to 12.7 KB, an edit that the filter excludes 3.14 to 2.99 µs, and an
add and a remove 23.1 to 20.7 µs and 38.6 to 37.2 KB.

Changed: a filter at a new criteria finds the keys that change sides before it edits its key set. Filter and
FilterByIdentity over a criteria cell test each key when the criteria change. The limit of a stage is 1,000 keys, or a
tenth of the keys that it holds. Up to the limit, the stage moves each key that changed sides and reports one operation
for each. Over the limit, it builds its set again and reports a reset. Before, the stage edited its set key by key as it
tested. For each key up to the limit, a change over the limit made a version of an immutable set and of an immutable
dictionary. Then the stage discarded each version and built the set again. The stage also looked up the position of each
key that it tested, where it needed only to know if it held the key. The stage now tests first, and edits after that
only when the count is in the limit. A stage reports what it reported before: the same keys and the same operations,
with the same positions in the same sequence. A reset comes at the same count. New tests hold each of those at the limit
and one key over it, which no test did before.

Measured in a new benchmark, FilterCriteriaBenchmarks, which sends a criteria and then the first criteria again. The
figures are for 10,000 items on .NET 10, with one run on each side. Through Filter, a change that moves a quarter of the
keys took 14.4 ms and 5,505 KB, and takes 10.3 ms and 2,484 KB. Through FilterByIdentity, it took 10.6 ms and takes 7.8
ms, with the same bytes. A change that moves no key took 5.7 ms through Filter and takes 4.5 ms, and through
FilterByIdentity 4.9 ms and 2.5 ms. A change that moves 100 keys took 6.3 ms and takes 5.1 ms, and 4.5 ms and 2.6 ms.
Those two allocate what they did. At 100,000 items the quarter allocates 24.2 MB where it allocated 55.1 MB. On .NET
Framework 4.7.2, at 10,000 items, the quarter allocates 2,496 KB where it allocated 5,529 KB. Through FilterByIdentity
there, it took 28.1 ms and takes 23.7 ms. Through Filter there, 33.4 ms and 31.4 ms are inside the noise of the two
runs. BlotterSendBenchmarks has a search and its clear over about 7,000 orders, through two filters, a sort, and a page.
That took 28.8 ms and 8,819 KB, and takes 22.2 ms and 5,687 KB.

Changed: a filter that builds its set again takes the entries of the stage above, and makes no entry of its own. A
filter keeps the order of the stage above. Thus, each entry that it keeps is the same entry as in the set above, with
the same key and sort value. When no key fails, the set of the filter is the set above. Then a filter that is off costs
one test for each key at a reset. When at most 40 percent of the keys fail, the filter removes them from the set above,
with one builder for each tree. When more keys fail, the filter builds a set of the entries that pass, with no new entry
and no lookup of a sort value. A filter below a slice, or below a sort that reads its set from the end, builds as
before. A comparison of the two forms set the line at 40 percent. It used 7,000 and 100,000 entries, with the failed
keys spread through the order. Up to a third of the keys, the removal cost less time and less memory, and from a half it
cost more. A filter reports what it reported before. New tests check the keys of each form, and check that a filter that
keeps each key holds the set above.

Measured against the change above, on 10,000 items on .NET 10, with one run on each side. The keys that a search drops
are spread through the order. Thus, FilterCriteriaBenchmarks now also has arms that drop each fourth key and each second
key. Through Filter, a change that moves a quarter of the keys took 10.6 ms and 2,484 KB, and takes 4.0 ms and 225 KB. A
change that moves each fourth key took 10.3 ms and 2,484 KB, and takes 5.1 ms and 634 KB. A change that moves each
second key took 8.7 ms and 2,133 KB, and takes 5.1 ms and 698 KB. FilterByIdentity allocates the same bytes. It takes
3.1 ms where it took 8.0, 5.2 ms where it took 8.4, and 5.1 ms where it took 6.9. At 100,000 items, each fourth key
through Filter took 176.6 ms and 24.2 MB, and takes 66.5 ms and 6.0 MB. On .NET Framework 4.7.2 at 10,000 items, each
fourth key through Filter took 26.4 ms and takes 12.5 ms. Its bytes are within 4 KB of those on .NET 10. The changes
that move no key or 100 keys do not build, and allocate what they did. Their times moved by up to a third in each
direction between the two runs, which is the noise of those runs. BlotterSendBenchmarks has a search and its clear over
about 7,000 orders. That took 27.9 ms and 5,687 KB, and takes 11.2 ms and 2,716 KB.

Changed: a sort below a filter keeps its list at a reset from the filter, in a transaction that edited no item. A change
of criteria that moves more keys than the limit of the filter is a reset. Before, the sort below the filter built its
set again from nothing at that reset. No item changed in that transaction, thus each key that stays has its sort value.
The sort now removes the keys that left and adds the keys that came, with one builder for each tree. It does this when
the keys that leave, and two for each key that comes, are at most the number of keys above. Over that count, it builds
as before. A comparison on 7,000 and on 100,000 entries set that count, because a key that comes cost about two keys
that leave. The test for an edited item compares the identity map and the state map before and after the transaction, as
objects. A sort that reads its set from the end edits the set in its first direction, which the view reads. A sort
reports what it reported before. New tests take a sort on the state and a sort on the identity through each kind of
change. A test does the same with a sort that reads its set from the end. One more test edits a score in the transaction
that changes the criteria, where the sort must build.

Measured against the change above, in BlotterSendBenchmarks on .NET 10, with one run on each side. A search and its
clear over about 7,000 orders took 11.2 ms and 2,715 KB, and takes 10.4 ms and 2,123 KB. A run of the two builds in turn
gave 11.3 ms and 10.8 ms in the fastest runs. In KeyedCollectionViewBenchmarks, a change of the threshold moves one key,
which is no reset, and allocates what it did. Its times in the two runs were up to a third apart. The times of the arm
with no view were also that far apart. A run of the two builds in turn gave the same times.

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
