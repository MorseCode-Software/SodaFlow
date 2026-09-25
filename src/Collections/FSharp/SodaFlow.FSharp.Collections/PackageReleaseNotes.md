2.0.0

BREAKING: sortByKey and orderByKey no longer take a comparer. sortByKey sorts
by the default comparer, and orderByKey takes unit, as orderByArrival does.
The comparer and the direction go to the new sortByKeyWith and
orderByKeyWith, as the other With forms take them. A call that passed
Comparer.Default becomes sortByKey or orderByKey (); a call with any other
comparer becomes sortByKeyWith comparer false or orderByKeyWith comparer
false.

Adds sortByKeyDescending and orderByKeyDescending. A cell that moves between
two key orders with the same comparer turns the list that the stage holds and
does not sort each key again.

Adds itemCell, which gives the two parts of one item as one optional value. It
follows the key as stateCell does, and it answers for the collection or the
view that a caller asks, with the same cache for each key.

Use it for a value that reads the identity and the state together. A lift of
identityCell against stateCell does the same work, and it gives two optional
values, thus four combinations, of which the store cannot give two: an
identity with no state, and a state with no identity. One cell gives one
optional value and removes the two branches that no code can reach. It also
costs one cell and not two cells with a lift above them.

It is not the correct selection for each row. An item holds the state, thus
itemCell sends a value at each edit to the state of its key. Each value that
comes from it is made again at that moment, and that includes a part which
reads the identity alone. Where one binding reads the identity and a different
binding reads the state, take the two cells: identityCell sleeps through an
edit to the state, and that is what makes it almost free to hold.

Adds filterByIdentityC, which is filterByIdentity with a predicate in a cell,
as filterC is to filter. A change to the predicate tests each item again and
names what entered and what left. A state edit still does not test the
predicate.

Fixed: the documentation of filterC said that a change to the predicate builds
the stage again and reports a reset. It reports the keys that entered and
left as inserts and removals, and it builds again and resets only when more
keys move than a list of them is worth.

Requires SodaFlow.FSharp 4.x and SodaFlow.Collections.Core 2.x.

1.0.1

Adds the package icon that nuget.org shows beside this package. No source file
changed since 1.0.0.

Every package here ships this release together, so the dependency versions
move with it.

1.0.0

First release.

Requires SodaFlow.FSharp 4.x and SodaFlow.Collections.Core 1.x. Installing this
brings the F# API it extends, so one install gives you the whole surface. This
one reaches the core's internals as well, so the SodaFlow.Core it runs against
has to be 4.0.1 or later; SodaFlow.Collections.Core carries that floor, and
NuGet resolves it from there.

Optionality is option, not Maybe, and it brings no more with it than
SodaFlow.FSharp does. The core carries no optional type at all - its lookups
are TryGets - so the projection into option happens inside the map a per-item
cell already had, costing no extra graph node.

---

About this package

A large keyed collection for cases where the number of items being actively
observed is a small fraction of the total. Per-item observation costs one hash
lookup per observer per transaction, independent of collection size.

The same library as SodaFlow.Collections, with the arguments ordered for the
pipeline - the collection comes last, so a chain reads in the order it runs -
and F# functions rather than Func for predicates and selectors.

  let accounts = create keyOf initial [ fromAdds opened; fromRemoves closed ]

  let topTen =
      accounts
      |> sortByDescending (fun _ state -> state.Balance)
      |> filter (fun _ state -> not state.IsFrozen)
      |> take 10

  let selected = accounts |> stateCell selectedKey

sortByOrderC takes a cell of orders rather than a selector, which is how a
clickable column header is written: an order carries its own sort value type
inside itself, so one cell holds orders sorting by an int and by a string
alike. orderBy, orderByDescending, orderByIdentity, orderByIdentityDescending,
orderByKey and the two With forms build them, mirroring the sorts one for one.

thenBy, thenByDescending, thenByIdentity, thenByIdentityDescending and their
With forms add a level to an order, deciding only between keys it ranks equal,
and sortByOrder sorts by an order that does not change - which is how a
multi-level sort that never changes is written.

A collection keeps its items in the order they arrived rather than by key, and
sortByArrival and orderByArrival take a sorted view back to that order.

slice offset limit is the paging window, and take is the case of it that starts
at zero; sliceC takes cells for either end, so turning the page is one send.
There is no skip - a window with both ends is bounded, which is what keeps the
stage at O(limit) per transaction.

map ends a chain: one object per key, in order, kept so the same key gives back
the same object - which is what a list binds to. Build each one from stateCell
and identityCell and it follows its own item, so one edit moves one row rather
than rebuilding the list. mapWith chooses how much to keep and hears about what
is dropped; disposing the result releases what is still held.

createByIdentity and createByIdentityWith drop the key selector when the identity
implements IIdentity<'TKey> and so carries its own key.

Everything that can change the collection is declared at construction: create
takes the initial contents and every edit stream, lifted from domain streams by
fromAdds, fromRemoves, fromUpdates and fromStates. There is no imperative entry
point.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
