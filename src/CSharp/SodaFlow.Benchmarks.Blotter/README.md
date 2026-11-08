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
- **Main** is `origin/main` at `d546b14`, with one local change described below. Main ran twice. The table shows the
  second run, whose source is identical to the release run's, and the first run's time where the two differ by more than
  10%.

Times are medians. Allocation is per operation and differed between the two main runs by 5% at most.

At 10,000 orders:

| Benchmark          | Release  | Main     | Time  | Release alloc | Main alloc | Alloc |
|--------------------|----------|----------|-------|---------------|------------|-------|
| `BuildAndDispose`  | 49.1 ms  | 41.5 ms  | 0.84x | 13.33 MB      | 11.73 MB   | 0.88x |
| `TickOnPage`       | 97.6 μs  | 64.8 μs  | 0.66x | 99.9 KB       | 92.5 KB    | 0.93x |
| `TickOffPage`      | 2.0 μs   | 1.2 μs   | 0.61x | 3.2 KB        | 2.6 KB     | 0.79x |
| `FillOnPage`       | 96.1 μs  | 44.3 μs  | 0.46x | 93.3 KB       | 43.9 KB    | 0.47x |
| `FillOffPage`      | 22.7 μs  | 14.4 μs  | 0.63x | 20.3 KB       | 19.9 KB    | 0.98x |
| `AmendRow`         | 194.9 μs | 90.2 μs  | 0.46x | 187.8 KB      | 87.3 KB    | 0.46x |
| `SelectRows`       | 18.8 μs  | 7.3 μs   | 0.39x | 16.3 KB       | 10.1 KB    | 0.62x |
| `SearchAndClear`   | 29.4 ms  | 27.2 ms  | 0.92x | 9.20 MB       | 8.80 MB    | 0.96x |
| `ToggleWorkingOnly`| 5.17 ms  | 4.57 ms  | 0.88x | 1.26 MB       | 875 KB     | 0.68x |
| `ReverseSort`      | 7.87 ms  | 2.29 ms  | 0.29x | 3.06 MB       | 1.61 MB    | 0.53x |
| `ChangeSortColumn` | 15.3 ms  | 11.2 ms  | 0.73x | 5.59 MB       | 4.30 MB    | 0.77x |
| `PageForwardAndBack` | 3.43 ms | 3.30 ms (first run 2.41 ms) | 0.70–0.96x | 2.16 MB | 1.61 MB | 0.74x |
| `OrderLifecycle`   | 5.23 ms  | 2.42 ms (first run 2.07 ms) | 0.40–0.46x | 2.42 MB | 1.29 MB | 0.53x |

At 1,000 orders:

| Benchmark          | Release  | Main     | Time  | Release alloc | Main alloc | Alloc |
|--------------------|----------|----------|-------|---------------|------------|-------|
| `BuildAndDispose`  | 11.2 ms  | 6.32 ms  | 0.57x | 4.31 MB       | 2.69 MB    | 0.62x |
| `TickOnPage`       | 99.7 μs  | 64.4 μs  | 0.65x | 100.2 KB      | 92.6 KB    | 0.92x |
| `TickOffPage`      | 2.0 μs   | 1.3 μs   | 0.68x | 3.4 KB        | 2.8 KB     | 0.82x |
| `FillOnPage`       | 92.7 μs  | 42.5 μs  | 0.46x | 93.1 KB       | 43.8 KB    | 0.47x |
| `FillOffPage`      | 21.5 μs  | 13.5 μs  | 0.63x | 20.2 KB       | 19.3 KB    | 0.96x |
| `AmendRow`         | 194.8 μs | 86.3 μs  | 0.44x | 188.4 KB      | 88.0 KB    | 0.47x |
| `SelectRows`       | 19.2 μs  | 7.5 μs (first run 6.6 μs) | 0.34–0.39x | 16.3 KB | 10.1 KB | 0.62x |
| `SearchAndClear`   | 3.59 ms  | 3.00 ms  | 0.83x | 2.60 MB       | 2.20 MB    | 0.85x |
| `ToggleWorkingOnly`| 2.27 ms  | 1.50 ms  | 0.66x | 1.26 MB       | 875 KB     | 0.68x |
| `ReverseSort`      | 3.73 ms  | 2.29 ms  | 0.61x | 2.26 MB       | 1.61 MB    | 0.71x |
| `ChangeSortColumn` | 6.60 ms  | 4.75 ms (first run 4.07 ms) | 0.62–0.72x | 3.56 MB | 2.61 MB | 0.73x |
| `PageForwardAndBack` | 3.51 ms | 2.58 ms | 0.73x | 2.16 MB      | 1.61 MB    | 0.74x |
| `OrderLifecycle`   | 5.21 ms  | 1.98 ms  | 0.38x | 2.41 MB       | 1.26 MB    | 0.52x |

The release ran once, so its own run-to-run spread is not known. The two main runs agree within 3% on most rows, and
differ by up to 27% on a few: the build at 1,000 orders and the page, sort, select, and lifecycle rows marked above. The release's `ReverseSort` at 10,000 orders was bimodal
(mean 7.50 ms, standard deviation 1.40 ms), and its median is the figure above.

### Main needed a local change to run

At `d546b14`, main fails every benchmark here with a stack overflow in `SwitchKeepListenersAlive.Use`, at the first
strong listener on the blotter. The released packages run the same source correctly. It started with #174 ("Root a
strong listener on a multi-input stream in every root's keep-alive set, and move a switch's with its inner"): a 20-line
program with the same shape as this view model overflows at that merge and runs at the merge before it.

The shape is the one this repository recommends for a view model whose own parts feed it: a `SwitchS` over the
`ForwardReference`, whose inner stream comes from rows built out of the collection that the switch feeds. A switch's
keep-alive set forwards each call to its outer set and to its current inner set. Here the inner set reaches the switch
again, so the call never ends. The Accounts sample has the same shape and will fail the same way when its pinned
packages move past #174.

The main figures above were taken with a re-entrance guard on `SwitchKeepListenersAlive`: a flag that stops a call that
comes back to the set while it forwards a call, and that also covers `SetInner`. With it the repository's whole test
suite passes. The guard is not part of this project, and the main figures are only as good as that fix is a fair one.
