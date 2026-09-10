# Accounts

A list of twenty accounts, filtered, sorted, and shown a page at a time, with a running total over
all of them. It exists to show what `SodaFlow.Collections` is for: **an edit reaches the rows bound
to it, not the rows next to them.**

Run either head — they bind the same view model:

```bash
dotnet run --project samples/Accounts/SodaFlow.Samples.Accounts.Avalonia
```

```bash
dotnet run --project samples/Accounts/SodaFlow.Samples.Accounts.Wpf
```

## What to watch

**Pay £100 into the top account.** One balance changes. The other rows do not flicker and the list
does not rebuild, even though the sort could have moved that account. Bind a list to one cell
holding the whole list — which is right for the [Search](../Search) sample, whose results really are
one answer — and every row would be rebuilt instead.

**Turn the page.** A slice moves a window over an ordering that did not move, so this costs
essentially nothing however large the collection.

**Show or hide frozen accounts.** Also a criteria change, but this one rebuilds the filter, which is
the expensive kind. Both are one line in the view model and the difference between them is invisible
in the code, which is why the [reference page](../../docs/docs/collections.md) spells the costs out.

**The total.** Over every account rather than the page, and folded from what changed rather than
recomputed — the change carries the store on both sides, so a delta needs nothing kept alongside.

## What to read

Everything is in `SodaFlow.Samples.Accounts.ViewModels`; the two heads only draw it.

- `Accounts.cs` — the two halves of an account. `AccountIdentity` implements `IIdentity<int>`, so
  `Create` takes the key from the identity rather than asking for a selector.
- `AccountsViewModel.cs` — the graph. The chain is
  `Filter` → `SortByDescending` → `Slice` → `Map`, and `Map` is what turns it into rows.

Two things in there are worth a second look.

The deposit button pays into whichever account is at the top of the current page, so the edit
depends on the view and the view depends on the edit. That is a real cycle, closed with
`Stream.CreateLoop`.

`Map` hands back a `MappedItems`, which holds the rows and is disposable. Rows are released when
their key leaves and the bound is exceeded, and the ones that never leave are released when the
projection is — so the view model puts it in the same list as everything else it owns.

## A note on how this sample is built

Unlike the others here, this one references the projects under `src/` rather than published
packages, and it is not in the samples workflow. The collections packages are not published yet, and
this sample exists partly to find out whether their API needs changing before they are — which it
already has, twice.

Both of those go away when the packages ship: this becomes a `PackageReference` with a pinned
version like the rest, and joins the matrix.
