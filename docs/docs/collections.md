---
title: Reactive collections
---

# Reactive collections

A large keyed collection where only a small fraction of the items are being watched at any
moment — a hundred thousand accounts behind a list showing twenty rows.

The naive shape does not work. A `Cell<IReadOnlyDictionary<K, V>>` fires on every edit, so
every observer wakes for every change; and a `Cell` whose value carries its own inner cells
builds graph nodes inside the fold that produces each new value. This builds neither. One cell
holds the whole snapshot, one stream carries resolved changes, and a per-item observer is a
filter over that stream costing one hash lookup per transaction, independent of collection
size.

It lives in a separate package; see [Which package do I install?](packages.md).

```bash
dotnet add package SodaFlow.Collections
```

## Everything that can change it is declared at construction

There is no `Send`, no sink, no method that mutates a live collection. `Create` takes the
initial contents and every stream that will ever edit it, so what the collection can do is
fully defined before it exists and readable in one place.

```csharp
using SodaFlow;
using SodaFlow.Collections;

// Domain streams, however they are produced.
Stream<Entry<AccountId, AccountState>> opened = ...;
Stream<Guid> closed = ...;
Stream<(Guid Key, Func<AccountState, AccountState> Transform)> deposits = ...;

ReactiveCollection<Guid, AccountId, AccountState> accounts =
    ReactiveCollection<Guid, AccountId, AccountState>.Create(
        static id => id.Value,
        initialAccounts,
        CollectionEdit<Guid, AccountId, AccountState>.FromAdds(opened),
        CollectionEdit<Guid, AccountId, AccountState>.FromRemoves(closed),
        CollectionEdit<Guid, AccountId, AccountState>.FromUpdates(deposits));
```

Sinks still belong at the edge of the program — that is how UI events and I/O enter the graph
at all — but they are the caller's, created at the boundary and passed in here as streams. If
an edit stream depends on something derived from the collection, close the circle with
`Stream.CreateLoop` at the call site. See [Feedback loops](loops.md).

## An item is two halves

`Entry<TId, TState>` splits an item into an immutable `Identity` and a mutable `State`, and
the key is derived from the identity alone. Nothing in the update path can reach the identity
— an update carries `Func<TState, TState>` — so key stability is structural rather than
checked at run time. Re-keying is therefore a remove plus an add, which is a structural edit,
which is where you wanted it.

## Observing

| You want | You read | It fires |
| --- | --- | --- |
| One item's state | `StateCell(key)` | When that key changes |
| One item's identity | `IdentityCell(key)` | Only on structural change |
| The whole store | `SnapshotCell` | On every change |
| Count or key changes | `ShapeCell` | Only on structural change |
| A view's keys, in order | `KeysCell` | When that view's membership or order changes |
| A view's changes as operations | `ChangesStream` | Same, as a delta |
| The collection a view came from | `Root` | Never — it is the root itself |

`StateCell` is the one that matters for a bound row. It returns `Cell<Maybe<TState>>` in C#
and `Cell<'TState option>` in F#, filters the change stream on a single hash lookup, and takes
the new value straight off the change event rather than sampling `SnapshotCell` — a cell
sampled *during* a transaction still holds its pre-transaction value, which is exactly wrong
here.

The key need not exist yet. A removal fires no value and a later add under the same key fires
one again, so a view bound to a key can outlive its item and can be built before it. Feed it
straight into `IOneWayBindableValue<T>` for the bound row; see [Data binding](bindable.md).

`StateCell` caches weakly per key, so N observers of one key share a node and the node goes
away when the last observer does. The cache is keyed by the projected type as well as the key,
so the C# and F# surfaces over one collection cannot be handed each other's cells.

## Views

A collection is two separable things: an item store, and a sequence of keys into it. The root
owns the store; a view differs only in which keys it holds and in what order. So both are
`IReactiveCollection<TKey, TId, TState>`, and `Filter` and `SortBy` take one and return one, the
way `Where` takes and returns an `IEnumerable`.

```csharp
IReactiveCollection<Guid, AccountId, AccountState> topTen = accounts
    .SortByDescending(static (_, state) => state.Balance)
    .Filter(static (_, state) => !state.IsFrozen)
    .Take(10);
```

| Operation | Overloads |
| --- | --- |
| `Filter` | A predicate, a `Cell<Func<TId, TState, bool>>`, or a criteria cell plus a predicate |
| `SortBy` / `SortByDescending` | A selector, or a selector with explicit comparers |
| `SortByKey` | The root's own order, over any stage |
| `Take` | A count, or a `Cell<int>` |
| `Switch` | Follows whichever view a cell holds |

`StateCell` and `IdentityCell` answer for the store, so an item seen through two views is
literally the same cell — sharing is not arranged, it is what falls out of never copying. That
is also the one seam the unification leaves: `SnapshotCell` on a view is the whole store, not
the view's contents, and asking a filtered view about a key it filtered out still gives that
item's state. Membership questions belong to `KeysCell`.

Ordering the root is lazy. A collection nobody sorts or lists never builds a sorted key set,
and `TKey` only has to be comparable if something actually asks for keys in order. The keyed,
unordered deltas remain available as `ItemChangesStream`.

To switch between sorts whose keys are *different* types, as clickable column headers need,
hold the views in a cell and use `Switch` — a chain's type does not grow at each step, but the
sort key stays a real generic parameter down to the comparer, so sort values are never boxed.

## How the chain stays incremental

Each stage keeps its own ordered key set and applies the operations from above.

| Stage | On upstream insert or remove | On upstream update | On upstream move |
| --- | --- | --- | --- |
| `Filter` | Test, add or drop | May enter, leave, or move | Ignored |
| `SortBy` | Add or drop | Re-file, O(log n) | Ignored |
| `Take` | Re-window | Forwarded if inside the window | Re-window |

`ViewUpdate` is what makes chaining work. A stage that saw no positional consequence still has
to tell the stage below that an item changed, because that stage may sort or filter on exactly
the state that just moved. UI consumers can ignore it — the row's own `StateCell` already
reports the value.

`Filter` preserves upstream order without tracking positions in the upstream list: it asks the
upstream's key set for a new set under *the same order*, holding the members it kept.
That keeps it O(log n) per changed key instead of needing rank queries over a subsequence.

Re-filing a key on update stores the sort value it was filed under, so the old entry is
removed with the comparison that placed it — otherwise you are hunting a sorted set for an
item whose sort position has already moved underneath it.

## Three costs worth knowing

- Changing a predicate or a limit rebuilds that stage and everything below it and reports
  `IsReset`. This is more expensive than it sounds, and more expensive than not having a chain at
  all: a rebuild files every surviving key into a fresh ordered set, so at ten thousand items one
  criteria change measures about sixteen times the cost of re-deriving the same view with LINQ.
  Debounce keystroke-driven criteria upstream, and do not drive a chain from something that
  changes per frame.
- `Take` diffs its old and new windows rather than translating operations: O(limit) per
  transaction, and a reorder inside the window reports as removes and inserts from the first
  differing position rather than as moves. For a top-n that is the cheap direction to be wrong
  in.
- `Filter` after `Take` filters the window, so it yields at most `limit` items. That is what
  the chain says; write `Filter` before `Take` if you meant the other thing.

## Measuring it

The claim above — that per-item observation is proportional to the number of bound rows and
independent of the collection size — is what
`src/CSharp/SodaFlow.Benchmarks/KeyedCollectionEditBenchmarks.cs` measures, against the two
shapes people reach for instead: a cell sink per mutable value per object, and the same cells
fed from one shared edit stream.
`KeyedCollectionBuildBenchmarks` measures what each of them costs to stand up, and
`KeyedCollectionViewBenchmarks` measures the view chain — `Filter`, `SortBy` and `Take` — against
re-deriving the same view from a cell holding the whole collection.

```bash
dotnet run -c Release --project src/CSharp/SodaFlow.Benchmarks -- --filter *KeyedCollection*
```

Sinks per field win on the time an individual edit takes, and the benchmark says so. What they
cost is everything the build benchmark shows — `items × fields` graph nodes whether or not
anything reads them.

They also describe a collection most applications do not have, which is the more important
caveat. A sink is how an event from *outside* the graph gets in, and that is enforced rather
than advised: `Send` throws `Send may not be called inside a callback` when it is reached from
within a transaction. So a cell per field fed by sinks holds only while every mutable value in
the collection arrives whole from the outside world, with no logic anywhere between the source
and the value. Derive one field from another — a balance from a running total, a status from two
other fields — and you cannot send it any more. Read that column as the floor a collection would
hit if none of its data were computed, rather than as the alternative you are choosing against.

The alternative you are actually choosing against is the next column. Wire those same cells to a
stream of edits so that they compose, and every edit in the collection evaluates one filter per
item — a cost proportional to the collection rather than to what is on screen.

What the numbers look like is machine-specific and will drift; what they are *shaped* like is
the point. One edit to a key nothing is watching, twenty rows bound, on .NET 10 on one
developer machine:

| Items | Sinks per field | Cells per field from a stream | Reactive collection |
| --- | --- | --- | --- |
| 1,000 | 0.70 µs | 108 µs | 5.9 µs |
| 10,000 | 0.71 µs | 3,010 µs | 6.3 µs |

Ten times the items costs the stream-fed cells twenty-eight times the work and the collection
seven percent. That is the property the design is for: the cost of an edit follows the number of
bound rows, not the size of the collection. Sinks per field are flat too, and faster — they are
also the shape you cannot feed from a stream.

Standing the same collections up, on the same machine:

| Items | Sinks per field | Cells per field from a stream | Reactive collection |
| --- | --- | --- | --- |
| 1,000 | 4.6 ms, 3.8 MB | 15.3 ms, 5.5 MB | 0.44 ms, 0.55 MB |
| 10,000 | 115 ms, 36.9 MB | 217 ms, 54.4 MB | 7.3 ms, 4.5 MB |

A cell per mutable value is `items × fields` graph nodes, and at ten thousand items that is a
hundred and fifteen milliseconds and thirty-seven megabytes spent before anything is on screen.
The collection is sixteen times less of the first and eight times less of the second, because
the only per-item graph nodes it builds are the twenty a view actually asked for.

### What the view chain costs

Keeping "the top twenty unfrozen items by score" current, against re-deriving it from a cell
holding the whole collection with `Where`, `OrderByDescending` and `Take`:

| Operation | Items | Re-derived | Chained |
| --- | --- | --- | --- |
| Edit one item | 1,000 | 67 µs | 15 µs |
| Edit one item | 10,000 | 717 µs | 14 µs |
| Add and remove an item | 1,000 | 135 µs | 27 µs |
| Add and remove an item | 10,000 | 1,404 µs | 30 µs |
| Change the threshold | 1,000 | 67 µs | 706 µs |
| Change the threshold | 10,000 | 713 µs | 11,616 µs |

Read the three rows separately, because they do not agree.

An **edit** is what the chain is for, and it is flat: thirteen microseconds at a thousand items
and fourteen at ten thousand, against a re-derivation that grows with the collection. At ten
thousand that is fifty-two times.

**Adding and removing** is flat too — twenty-eight microseconds and thirty — which is fifty
times a re-derivation at ten thousand items. It was not always: the identity map used to be a
plain dictionary, which can only produce its next version by being copied, so a structural edit
was O(n) however cheaply the stages below it absorbed the change. This benchmark is what found
that, and the map is a trie now.

**Changing the threshold** loses, by eleven times at a thousand items and sixteen at ten
thousand. A rebuild files every surviving key into a fresh ordered set, so the chain builds a
persistent tree where re-deriving sorts an array — and a tree costs an allocation per node where
an array sort costs none. That gap is the data structure rather than a constant waiting to be
shaved: the tree is what makes every *other* row of this table cheap.

Both are Θ(n), so no amount of work on the chain will beat re-deriving here — the honest ceiling
is parity, and this is not at it. If your criteria change as often as your data does, a chain is
the wrong shape and a plain `Lift` is the right one. If they change on a keystroke, debounce
them.

## Simultaneous edits

Edits arriving from different input streams in the same transaction merge into one change
event and one cell update. Merge order is arbitrary in SodaFlow, which is safe here because
the union is commutative and the one order-dependent case — two transforms for the same key in
one transaction — throws rather than composing in an undefined order. Combine them into a
single transform before firing.

An update against a key that is not there throws `KeyNotFoundException`; an add of a key that
is already there throws `InvalidOperationException`; and an edit that resolves to no change at
all fires nothing.

## Optionality

Never null, and each language gets its own optional type. In C# that is `Maybe<T>` —
`StateCell`, `IdentityCell`, `snapshot.Lookup`, `states.Lookup` and `IndexOfMaybe` all answer
with one. See [Maybe, Either and Unit](functional.md). In F# it is `option`.

That works because `SodaFlow.Collections.Core` has no optional type of its own. It answers in
`TryGetEntry`, `TryGetState`, `TryGetNewState` and an `IndexOf` returning `-1`, and each
language surface puts its own optional type back on top. It is the same reason
`SodaFlow.FSharp` does not depend on `SodaFlow.Functional`, applied one layer down: nothing
that installs the F# collections package acquires `Maybe<T>` it has no use for.

`ChangeFor` returns `Maybe<Maybe<TState>>` in C# and `'TState option option` in F#, and the
nesting carries real information. The outer level is whether the key moved at all — no value
meaning no event for this observer — and the inner is whether it is present afterwards, so a
removal arrives as a value containing no value. The core says the same thing as two questions:
`WasChanged(key)` and `TryGetNewState(key, out state)`.

## Storage

`IStateMap<TKey, TState>` is the seam where the storage strategy is chosen.
`ImmutableStateMap` is the default: a hash array mapped trie, about O(log32 n) per update,
allocating only the path from the root — roughly four nodes per edit at 100k items. Measure
before replacing it.

The contract an implementation must honor is that **a value returned by an instance never
changes for the lifetime of that instance**. SodaFlow reads a cell's pre-transaction value
during a transaction, so a snapshot that aliases storage you have since mutated will silently
report the future. An in-place strategy therefore has to version its storage and serve older
instances from a log, rather than handing back the live map.

## F#

The same library, with the arguments ordered for the pipeline — the collection comes last, so
a chain reads in the order it runs — and F# functions rather than `Func` for predicates and
selectors.

```fsharp
open SodaFlow
open SodaFlow.Collections

let accounts = create keyOf initialAccounts [ fromAdds opened; fromRemoves closed ]

let topTen =
    accounts
    |> sortByDescending (fun _ state -> state.Balance)
    |> filter (fun _ state -> not state.IsFrozen)
    |> take 10

let selected = accounts |> stateCell selectedKey
```

Optionality is `option`, not `Maybe`, and it costs nothing to get it: the projection into
`option` happens inside the same map the per-item cell already had, so there is no extra graph
node and no second cache. `SodaFlow.FSharp.Collections` brings no more with it than
`SodaFlow.FSharp` does.
