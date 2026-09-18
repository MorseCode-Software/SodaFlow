# Accounts

A list of a hundred thousand accounts, filtered, sortable by any column, and shown six at a time,
with a running total over all of them. It exists to show what `SodaFlow.Collections` is for: **an
edit reaches the rows bound to it, not the rows next to them** — and so costs what the rows on
screen cost, not what the collection holds.

Run either head — they bind the same view model:

```bash
dotnet run --project samples/Accounts/SodaFlow.Samples.Accounts.Avalonia
```

```bash
dotnet run --project samples/Accounts/SodaFlow.Samples.Accounts.Wpf
```

## What to watch

**Pay $100 into an account.** Every row has its own button. One balance changes. The other rows do
not flicker and the list does not rebuild, even though the sort could have moved that account. Bind a list to one cell
holding the whole list — which is right for the [Search](../Search) sample, whose results really are
one answer — and every row would be rebuilt instead.

**Click a column header.** The sort takes its order from a cell, so a click re-files the stage
that is already there rather than building a second chain and choosing between the two. The three
orders sort by an `int`, a `string` and a `long`, and one cell holds whichever is in force —
a `KeyOrder` keeps its sort value's type to itself, which is what makes them one type.

Two of the three sort on the *identity* half of an account, which no edit can change. Sort by
holder or by number and then deposit: the balance moves and the row does not, and the collection
knows that rather than being told it — the selector is handed the identity and never the state, so
a state edit is not offered to the sort at all. Sort by balance and the same deposit can carry the
row up the list, because that order reads the half that moves.

**Turn the page.** A slice moves a window over an ordering that did not move, so this costs
essentially nothing however large the collection. Sorting while paged leaves the page number where
it was, because a sort reorders the members rather than choosing different ones — unlike the
filter, which can shorten the list out from under an offset and so sends it back to the first
page.

**Flip the frozen accounts switch.** A criteria change too, but this one rebuilds the filter,
which is the expensive kind — where a page turn is the cheap kind and a re-sort is in between. All
three are one line in the view model and the differences between them are invisible in the code,
which is why the [reference page](../../docs/docs/collections.md) spells the costs out. The switch
binds two-way to a cell sink rather than firing a command, because a switch holds its own position.

**Look at a frozen account.** It is greyed out and says *Frozen* where its button would be. That is
only what the row looks like: the view model gates the deposit on the account not being frozen, so
nothing that reaches the command — a stale binding, or code calling `Execute` — can pay into one.

**Drain the frozen accounts.** Every frozen account's balance goes to zero, about twenty-five
thousand of them, whether or not they are showing. It is the other end from a deposit: one edit
carrying twenty-five thousand updates, applied in one transaction, and the total folds one delta.
With the frozen accounts hidden, the filter skips each drained account after a lookup and nothing
below it moves; shown and sorted by balance, the sort has more accounts to re-file than are worth
listing, so it rebuilds. That is the costly one — tens of milliseconds, over a hundred in that last
case — where a deposit is a fraction of one. The button is enabled only while some frozen account
still has money in it, and the view model keeps those accounts' keys as a set, folded over the
collection's item changes.

**The total.** Over every account rather than the page, and folded from what changed rather than
recomputed — the change carries the store on both sides, so a delta needs nothing kept alongside.

## What to read

Everything is in `SodaFlow.Samples.Accounts.ViewModels`; the two heads only draw it.

- `Accounts.cs` — the two halves of an account. `AccountIdentity` implements `IIdentity<int>`, so
  `Create` takes the key from the identity rather than asking for a selector.
- `AccountsViewModelOptimizedDrain.cs` — the graph both heads bind. The chain is `Filter` →
  `SortBy` → `Slice` → `Map`, and `Map` is what turns it into rows.
- `AccountsViewModel.cs` — the same graph, with the accounts to drain kept as a second filter over
  the collection rather than as a set of keys. It also holds the row and sort types both view models
  use. The [drain benchmarks](SodaFlow.Samples.Accounts.DrainBenchmarks/README.md) compare the two.

Four things in there are worth a second look.

Each row's button pays into that row's account, so the edits come from the rows and the rows come
from the collection the edits are for. That is a real cycle, and `Create` closes it with
`ForwardReference<AccountsViewModel>`: the whole graph is built inside a block that is handed the
view model before it exists, and the view model it returns closes the loop. The deposits and the
drains are kept in private fields, and the collection is fed from the looped view model by reading
those fields and flattening with `SwitchS` — which is the shape to reach for whenever the thing
being looped is an object under construction, rather than one stream or cell on its own.

Which rows are on the page moves as the page does, so the deposits are themselves a merge of the
current rows' deposits, rebuilt from each version of the list and switched to with `SwitchS` as
well.

The deposit is gated twice, and only one of those is the rule. The command is disabled for a frozen
account, which is what the view shows; the stream is gated on the same cell with `Gate`, which is
what the collection sees. A command's enablement is a copy on the binding thread, so it can trail
the graph — the gate is sampled in the transaction the deposit lands in.

The header row is one piece of state — a column and a direction — and everything else follows
from it: the order the sort holds, and the caption each header shows with its marker. Three
buttons merge into one stream of columns, and a fold over that stream is the whole of it.

`Map` hands back a `MappedItems`, which holds the rows and is disposable. Rows are released when
their key leaves and the bound is exceeded, and the ones that never leave are released when the
projection is — so the view model puts it in the same list as everything else it owns.

## A note on how this sample is built

Like the others here, this one takes its libraries from nuget.org at a pinned version, exactly as
an application outside this repository would:

```xml
<PackageReference Include="SodaFlow" Version="4.0.0" />
<PackageReference Include="SodaFlow.Collections" Version="1.0.0" />
<PackageReference Include="SodaFlow.Bindable.ObjectModel" Version="3.0.1" />
```

It referenced the projects under `src/` until recently, and the reason is worth recording because it
was spent rather than abandoned: the collections packages were unpublished, and this sample existed
partly to find out whether their API needed changing before they shipped — which it did, twice.
`SodaFlow.Collections` 1.0.0 is released, so the sample now tracks a released version like the rest,
and the samples workflow inspects it as well as building it.

`SodaFlow.Collections.Core` arrives underneath `SodaFlow.Collections` and is not named above. It is
what holds `SodaFlow.Core` to 4.0.1 or later, which the collections assemblies need: they reach its
internals through `InternalsVisibleTo`, and 4.0.0 would throw `MethodAccessException` at run time
rather than failing to build.
