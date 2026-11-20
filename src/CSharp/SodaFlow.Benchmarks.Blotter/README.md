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

Intel Core i7-9700, Windows 11, .NET 10.0.15, BenchmarkDotNet 0.15.8, out of process, measured on 2026-11-20. No other
build, test, or benchmark ran on the machine during the two runs.

- **Release** is the last tag of each package: SodaFlow 5.0.1, SodaFlow.Core 5.0.0, SodaFlow.Collections 2.0.0 (core
  2.0.0), and SodaFlow.Bindable.ObjectModel 4.0.4 (core 4.3.0), as `packages.release.lock.json` records.
- **Main** is `origin/main` at `f0de695`, the merge of #251, with no local change.

Times are medians. Allocation is per operation. A ratio is main divided by the release, so a lower ratio is a larger
gain.

At 10,000 orders:

| Benchmark | Release | Main | Time | Release alloc | Main alloc | Alloc |
|---|---|---|---|---|---|---|
| `BuildAndDispose` | 54.1 ms | 26.0 ms | 0.48x | 13.33 MB | 8.94 MB | 0.67x |
| `TickOnPage` | 96.4 μs | 59.8 μs | 0.62x | 100.0 KB | 74.9 KB | 0.75x |
| `TickOffPage` | 1.85 μs | 1.40 μs | 0.76x | 3.3 KB | 2.9 KB | 0.90x |
| `FillOnPage` | 94.4 μs | 42.2 μs | 0.45x | 93.3 KB | 40.7 KB | 0.44x |
| `FillOffPage` | 22.1 μs | 13.8 μs | 0.62x | 20.3 KB | 18.4 KB | 0.91x |
| `AmendRow` | 201.2 μs | 86.9 μs | 0.43x | 188.8 KB | 80.1 KB | 0.42x |
| `SelectRows` | 18.8 μs | 6.80 μs | 0.36x | 16.3 KB | 8.9 KB | 0.55x |
| `SearchAndClear` | 29.6 ms | 9.97 ms | 0.34x | 9.20 MB | 2.05 MB | 0.22x |
| `ToggleWorkingOnly` | 5.47 ms | 3.36 ms | 0.61x | 1.26 MB | 669.5 KB | 0.52x |
| `ReverseSort` | 5.46 ms | 2.14 ms | 0.39x | 3.06 MB | 1.21 MB | 0.40x |
| `ChangeSortColumn` | 15.7 ms | 10.4 ms | 0.66x | 5.59 MB | 3.69 MB | 0.66x |
| `PageForwardAndBack` | 3.44 ms | 2.23 ms | 0.65x | 2.16 MB | 1.21 MB | 0.56x |
| `OrderLifecycle` | 5.22 ms | 1.92 ms | 0.37x | 2.42 MB | 1.07 MB | 0.44x |

At 1,000 orders:

| Benchmark | Release | Main | Time | Release alloc | Main alloc | Alloc |
|---|---|---|---|---|---|---|
| `BuildAndDispose` | 11.6 ms | 4.70 ms | 0.41x | 4.31 MB | 2.33 MB | 0.54x |
| `TickOnPage` | 97.2 μs | 60.5 μs | 0.62x | 99.9 KB | 74.7 KB | 0.75x |
| `TickOffPage` | 1.82 μs | 1.30 μs | 0.72x | 3.1 KB | 2.7 KB | 0.86x |
| `FillOnPage` | 96.1 μs | 41.7 μs | 0.43x | 93.1 KB | 40.0 KB | 0.43x |
| `FillOffPage` | 22.0 μs | 12.7 μs | 0.58x | 20.2 KB | 18.8 KB | 0.93x |
| `AmendRow` | 192.7 μs | 86.3 μs | 0.45x | 187.5 KB | 80.9 KB | 0.43x |
| `SelectRows` | 19.0 μs | 6.75 μs | 0.35x | 16.3 KB | 8.9 KB | 0.55x |
| `SearchAndClear` | 3.80 ms | 2.88 ms | 0.76x | 2.60 MB | 2.00 MB | 0.77x |
| `ToggleWorkingOnly` | 2.28 ms | 1.28 ms | 0.56x | 1.26 MB | 669.0 KB | 0.52x |
| `ReverseSort` | 3.75 ms | 1.96 ms | 0.52x | 2.28 MB | 1.21 MB | 0.53x |
| `ChangeSortColumn` | 6.67 ms | 3.86 ms | 0.58x | 3.56 MB | 2.01 MB | 0.56x |
| `PageForwardAndBack` | 3.56 ms | 1.97 ms | 0.55x | 2.16 MB | 1.21 MB | 0.56x |
| `OrderLifecycle` | 5.28 ms | 1.83 ms | 0.35x | 2.41 MB | 1.07 MB | 0.44x |

Each side ran once at these commits. On every row, the standard deviation is 4% of the mean or less on both sides.
Across the 26 rows, the geometric mean of the time ratios is 0.51, and that of the allocation ratios is 0.56. Thus, main
does each of these operations in about half the time of the release, and with a little more than half the allocation.

### Main since #207

The first run of main was on 2026-11-08, at `e3560ac`, the merge of #207. Against that run, three rows at 10,000 orders
changed the most:

- `SearchAndClear` went from 26.6 ms and 8.61 MB to 9.97 ms and 2.05 MB.
- `BuildAndDispose` went from 40.6 ms and 11.72 MB to 26.0 ms and 8.94 MB.
- `ToggleWorkingOnly` went from 4.85 ms to 3.36 ms, with the same allocation within 3%.

At 1,000 orders, `BuildAndDispose` went from 6.06 ms to 4.70 ms. `ReverseSort` and `PageForwardAndBack` are 39% and
23% faster than in that run. That run had a standard deviation of 16 to 23% on those two rows, so read those two
changes as approximate. The other rows of main agree with that run within 13% on time and 8% on allocation. No run
measured which commit between the two gave each gain.

The release reads as it did on 2026-11-08. Its times agree within 11% on all rows but `ReverseSort` at 10,000 orders,
which was bimodal then and is not now. Its allocation agrees within 2% on all rows but `TickOffPage` at 1,000 orders,
which allocates 8% less.

### Main before #207

At `d546b14`, main failed every benchmark here with a stack overflow in `SwitchKeepListenersAlive`, at the first
strong listener on the blotter. The cause was #174, which made a switch's keep-alive set forward each call to its outer
and its inner. In this view model, a `SwitchS` over the `ForwardReference` selects a stream from the view model's own
output. Thus, the call came back to the switch and never ended. #207 makes the switch stop a call that comes back to it.

Two earlier main runs at `d546b14` used a local guard that does the same as #207. Against them, the times of the #207
run agreed within 10% on most rows. Its allocation was lower on most rows, by up to 23%, and higher on `TickOffPage`.
Main also took #205 between the two commits. It changed the immediate binding scheduler, which this project binds
through, to run a posted action after the transaction releases its lock. That is the probable cause, but no run measured it on its own.
