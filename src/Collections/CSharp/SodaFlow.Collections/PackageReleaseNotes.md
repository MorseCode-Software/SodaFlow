1.0.0

First release.

Requires SodaFlow 4.x, SodaFlow.Collections.Core 1.x and SodaFlow.Functional
3.x. Installing this brings the C# API it extends, so one install gives you the
whole surface.

---

About this package

A large keyed collection for cases where the number of items being actively
observed is a small fraction of the total - a hundred thousand accounts behind
a list showing twenty rows.

One cell holds the whole snapshot, one stream carries resolved changes, and a
per-item observer is a filter over that stream costing one hash lookup per
transaction, independent of collection size. No cell is nested inside another
cell's value, so nothing builds graph nodes inside a fold.

Everything that can change the collection is declared at construction. Create
takes the initial contents and every edit stream; there is no Send, no sink, no
method that mutates a live collection.

  StateCell(key)      one item, no value while the collection you asked
                      does not hold it - so a filtered view answers for
                      itself, at the same cost as asking the collection
  IdentityCell(key)   its immutable half, moving only when that key enters
                      or leaves the collection you asked - so a view answers
                      for itself here too, and a state edit never wakes one
  ShapeCell           fires on count or key change only
  SnapshotCell        the whole store, on every change
  KeyChangesStream    how the keys moved: positions, no states
  ItemChangesStream   how the items changed: states, no positions

The last two are a pair rather than one thing in two shapes. Bind a list to the
first, because a list has to know where a row went; fold the second for a total
or an average, because it names what changed and so costs what changed rather
than what the collection holds.

A view is a collection, not a handle on one. Filter, SortBy and the rest take a
ReactiveCollection and return one, and what comes back answers for itself: its
keys, its changes, its snapshot, its items and its per-item cells all hold what
it holds and nothing else. Nothing on it leads back to what it came from.

There is no interface, and that is deliberate: one implementation, not meant to
be substituted or mocked, and concrete so the language surfaces can reach what
they need without a cast.

Map ends a chain: one object per key, in order, kept so the same key gives back
the same object. It is what a list binds to - build each row from StateCell and
IdentityCell inside the projection and every row follows its own item, so one
edit moves one row rather than rebuilding the list. What it keeps is bounded,
counting keys that have left rather than keys in the view, and it hands back a
MappedItems to dispose the way MapAsync hands back a status.

Views chain and stay incremental. Filter, FilterByIdentity, SortBy, SortByDescending,
SortByIdentity, SortByIdentityDescending, SortByKey, Take and Slice each take a
ReactiveCollection and return one, the way Where takes and returns an
IEnumerable, and an item seen through two views is literally the same cell.

SortBy also takes a Cell<KeyOrder<TKey, TIdentity, TState>>, which is how a
clickable column header is written. An order carries its own sort value type
inside itself, so one cell holds orders sorting by an int and by a string
alike, while the sort key stays a real generic parameter down to the comparer.
Build them with the factories on KeyOrder - By, ByDescending, ByIdentity,
ByIdentityDescending and ByKey - which mirror those sorts one for one.

Slice(offset, limit) is the paging window, and Take is the case of it that
starts at zero. There is no Skip: a window with both ends is bounded, which is
what keeps the stage at O(limit) per transaction.

An identity that implements IIdentity<TKey> carries its own key, and
ReactiveCollection.Create - on the non-generic companion - takes it from there
rather than asking for a selector. Optional; the selector overloads remain.

  collection.SortByDescending((_, s) => s.Balance)
            .Filter((_, s) => !s.IsFrozen)
            .Take(10)

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
