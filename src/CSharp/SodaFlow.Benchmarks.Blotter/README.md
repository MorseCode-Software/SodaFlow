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
