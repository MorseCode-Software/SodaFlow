# Blotter benchmarks

An order blotter of the kind a trading desk keeps open all day, written the way this repository writes view models, and
measured two ways: what it costs to build, and what each thing a user or a feed does to it costs once it is open. The
same source builds against main's projects or against the last released packages, so one run of each says what the
unreleased changes give a real screen.

## The view model

`BlotterViewModel` is one `Transaction.Run` and one `ForwardReference`, with a private constructor that takes the
finished graph. It holds:

- Two collections: the orders, keyed by identifier with an immutable identity (symbol, side, trader) and a state that
  edits change (quantity, filled, limit, status), and the quotes, keyed by symbol.
- A list that goes through a search on the identity, a filter on the status, a sort on one of four columns in either
  direction, and pages of 25 rows.
- A row view model for each order on the page, with a one-way value for each column and commands to cancel, amend,
  and select. Each row follows the quote of its symbol through a `SwitchC`, so its last price, notional, and edge move
  with the market.
- Totals folded over every change of the orders: the working count and the exposure on each side.
- A selected order, a `SwitchC` on the key the user selected last, with a cancel command.
- An entry form with validation that submits a new order, with its identifier from a block-form `Cell.Loop`.
- A command that removes every inactive order.

The market data feed and the exchange are streams the caller passes in, as a real application would get them from its
services.

## Running

Release is required. `SodaFlowSource` chooses the copy of the libraries: `Projects`, the default, builds against
`src/`, and `Release` against the published packages at the versions pinned in the project file. Set it in the
environment rather than with `-p`: BenchmarkDotNet builds the project again out of process, and only the environment
reaches that build.

```bash
dotnet run -c Release --project src/CSharp/SodaFlow.Benchmarks.Blotter -- --filter '*'
```

```bash
SodaFlowSource=Release dotnet run -c Release --project src/CSharp/SodaFlow.Benchmarks.Blotter -- --filter '*'
```

Each benchmark process writes a line that names the copy it loaded, such as
`// SodaFlowSource: Release, SodaFlow.Core 5.0.0+a369c146…`, so the log shows that the inner build used the copy the
run asked for. Each copy has its own lock file: `packages.lock.json` for the projects and `packages.release.lock.json`
for the packages.

Before measuring, each benchmark process does every operation once and checks what it changed: a tick on the page's
symbol changes the first row and a tick on another symbol does not, a fill changes the remaining quantity, two reversals
give the same first row, and so on. A failed check stops that benchmark before it measures the wrong work, and it reads
`NA`.

On Windows without long paths enabled, BenchmarkDotNet's copy of the project under `bin` can pass 260 characters from a
deep clone and fail to build; every result then reads `NA`. Clone somewhere shorter, enable long paths, or add
`--inprocess`.

## What is measured

`BlotterBuildBenchmarks.BuildAndDispose` builds the blotter and disposes it, at 1,000 and at 10,000 orders.

`BlotterSendBenchmarks` builds one blotter and measures each interaction against it. The setup clears the inactive
orders, submits two more large orders than a page holds, all on one symbol, through the form, and sorts by identifier with the newest first, so the
first page shows only those orders. Each benchmark leaves the blotter as it found it, so every invocation does the same
work:

| Benchmark            | What happens                                                                    |
|----------------------|---------------------------------------------------------------------------------|
| `TickOnPage`         | A trade on the symbol of every row on the page                                  |
| `TickOffPage`        | A trade on a symbol that no row on the page shows                               |
| `FillOnPage`         | A fill of the order in the first row                                            |
| `FillOffPage`        | A fill of a large order off the page                                            |
| `AmendRow`           | One lot more on the first row, then one lot less                                |
| `SelectRows`         | Select the first row, then the second                                           |
| `SearchAndClear`     | Type "A" into the search, then clear it                                         |
| `ToggleWorkingOnly`  | Show working orders only, then all orders                                       |
| `ReverseSort`        | Two clicks on the header the list sorts on                                      |
| `ChangeSortColumn`   | Sort by remaining quantity, then two clicks back to identifier, newest first     |
| `PageForwardAndBack` | Next page, then previous page                                                   |
| `OrderLifecycle`     | Type a quantity and submit, cancel the selected new order, clear inactive orders |

## Results

Intel Core i7-9700, Windows 11, .NET 10.0.14, BenchmarkDotNet 0.15.8, out of process, measured on 2026-11-08.

- **Release** is the last tag of each package: SodaFlow 5.0.1, SodaFlow.Core 5.0.0, SodaFlow.Collections 2.0.0 (core
  2.0.0), and SodaFlow.Bindable.ObjectModel 4.0.4 (core 4.3.0), as `packages.release.lock.json` records.
- **Main** is `origin/main` at `e3560ac`, the merge of #207, with no local change.

Times are medians. Allocation is per operation. A ratio is main divided by the release, so a lower ratio is a larger
gain.

At 10,000 orders:

| Benchmark | Release | Main | Time | Release alloc | Main alloc | Alloc |
|---|---|---|---|---|---|---|
| `BuildAndDispose` | 49.1 ms | 40.6 ms | 0.83x | 13.33 MB | 11.72 MB | 0.88x |
| `TickOnPage` | 97.6 μs | 59.6 μs | 0.61x | 99.9 KB | 74.9 KB | 0.75x |
| `TickOffPage` | 2.0 μs | 1.5 μs | 0.75x | 3.2 KB | 3.0 KB | 0.92x |
| `FillOnPage` | 96.1 μs | 43.7 μs | 0.45x | 93.3 KB | 41.0 KB | 0.44x |
| `FillOffPage` | 22.7 μs | 13.7 μs | 0.60x | 20.3 KB | 18.5 KB | 0.91x |
| `AmendRow` | 194.9 μs | 88.6 μs | 0.45x | 187.8 KB | 80.9 KB | 0.43x |
| `SelectRows` | 18.8 μs | 7.1 μs | 0.38x | 16.3 KB | 9.2 KB | 0.56x |
| `SearchAndClear` | 29.4 ms | 26.6 ms | 0.90x | 9.20 MB | 8.61 MB | 0.94x |
| `ToggleWorkingOnly` | 5.17 ms | 4.85 ms | 0.94x | 1.26 MB | 689.1 KB | 0.54x |
| `ReverseSort` | 7.87 ms | 2.07 ms | 0.26x | 3.06 MB | 1.25 MB | 0.41x |
| `ChangeSortColumn` | 15.3 ms | 10.7 ms | 0.70x | 5.59 MB | 3.75 MB | 0.67x |
| `PageForwardAndBack` | 3.43 ms | 2.20 ms | 0.64x | 2.16 MB | 1.24 MB | 0.58x |
| `OrderLifecycle` | 5.23 ms | 1.95 ms | 0.37x | 2.42 MB | 1.10 MB | 0.45x |

At 1,000 orders:

| Benchmark | Release | Main | Time | Release alloc | Main alloc | Alloc |
|---|---|---|---|---|---|---|
| `BuildAndDispose` | 11.2 ms | 6.06 ms | 0.54x | 4.31 MB | 2.67 MB | 0.62x |
| `TickOnPage` | 99.7 μs | 58.3 μs | 0.58x | 100.2 KB | 75.0 KB | 0.75x |
| `TickOffPage` | 2.0 μs | 1.4 μs | 0.74x | 3.4 KB | 2.9 KB | 0.85x |
| `FillOnPage` | 92.7 μs | 41.3 μs | 0.45x | 93.1 KB | 40.4 KB | 0.43x |
| `FillOffPage` | 21.5 μs | 12.8 μs | 0.60x | 20.2 KB | 18.4 KB | 0.91x |
| `AmendRow` | 194.8 μs | 84.4 μs | 0.43x | 188.4 KB | 80.7 KB | 0.43x |
| `SelectRows` | 19.2 μs | 7.3 μs | 0.38x | 16.3 KB | 9.2 KB | 0.56x |
| `SearchAndClear` | 3.59 ms | 3.19 ms | 0.89x | 2.60 MB | 2.02 MB | 0.78x |
| `ToggleWorkingOnly` | 2.27 ms | 1.45 ms | 0.64x | 1.26 MB | 688.8 KB | 0.54x |
| `ReverseSort` | 3.73 ms | 3.23 ms | 0.87x | 2.26 MB | 1.25 MB | 0.55x |
| `ChangeSortColumn` | 6.60 ms | 4.38 ms | 0.66x | 3.56 MB | 2.07 MB | 0.58x |
| `PageForwardAndBack` | 3.51 ms | 2.55 ms | 0.73x | 2.16 MB | 1.24 MB | 0.57x |
| `OrderLifecycle` | 5.21 ms | 2.00 ms | 0.38x | 2.41 MB | 1.09 MB | 0.45x |

Each side ran once at these commits, so the run-to-run spread is not known for every row. At 1,000 orders, the main
runs of `ReverseSort`, `ChangeSortColumn`, and `PageForwardAndBack` have a standard deviation of 16 to 23% of the mean.
Thus, read those three ratios as approximate. The release's `ReverseSort` at 10,000 orders was bimodal (mean 7.50 ms,
standard deviation 1.40 ms), and its median is the figure above.

### Main before #207

At `d546b14`, main failed every benchmark here with a stack overflow in `SwitchKeepListenersAlive`, at the first
strong listener on the blotter. The cause was #174, which made a switch's keep-alive set forward each call to its outer
and its inner. In this view model, a `SwitchS` over the `ForwardReference` selects a stream from the view model's own
output. Thus, the call came back to the switch and never ended. #207 makes the switch stop a call that comes back to it.

Two earlier main runs at `d546b14` used a local guard that does the same as #207. Against them, the times above agree
within 10% on most rows. Allocation is lower here on most rows, by up to 23%, and higher on `TickOffPage`. Main also
took #205 between the two commits. It changed the immediate binding scheduler, which this project binds through, to run
a posted action after the transaction releases its lock. That is the probable cause, but no run measured it on its own.
