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

  StateCell(key)     one item, no value while the key is absent
  IdentityCell(key)  its immutable half, moving only on structural change
  ShapeCell          fires on count or key change only
  SnapshotCell       the whole store, on every change

Views chain and stay incremental. Filter, FilterById, SortBy, SortByDescending,
SortById, SortByIdDescending, SortByKey, Take, Slice and Switch each take an
IReactiveCollection and return one, the way Where takes and returns an
IEnumerable, and an item seen through two views is literally the same cell.

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
