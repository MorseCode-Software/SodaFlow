# Drain benchmarks

Measures the Accounts sample's ways of knowing which frozen accounts still hold money, and of
draining them: speed and allocation for a Pay (before and after a drain) and a Drain, plus retained
memory, and for two clicks that reshape the view - Show frozen and reversing a sort.

This branch is `origin/collections-next` with `samples/Accounts` from `samples/next` (b1398a3). It is
based there because the sample uses `KeyOrder.ByArrival`, which only `collections-next` has.

## The view models compared

| Name | Where | How drainable accounts are tracked |
|---|---|---|
| `Original` | `AccountsViewModel` | A filtered `ReactiveCollection` of frozen accounts with a balance |
| `OptimizedDrain` | `AccountsViewModelOptimizedDrain` | An `ImmutableHashSet<int>` folded over item changes |
| `TunedSet` | `AccountsViewModelTunedSet` (review variant) | The same set, updated in one pass, with `Calm()` on `canDrain` |
| `CountAndScan` | `AccountsViewModelCountAndScan` (review variant) | A folded count, and the keys found by scanning the snapshot at drain time |

The two review variants are generated from `AccountsViewModelOptimizedDrain` and differ from it only
in the tracker and in `Drain`.

## Running

Release is required.

```bash
dotnet run -c Release --project samples/Accounts/SodaFlow.Samples.Accounts.DrainBenchmarks -- --filter '*'
```

```bash
dotnet run -c Release --project samples/Accounts/SodaFlow.Samples.Accounts.DrainBenchmarks -- --filter '*TrackerBenchmarks*'
```

The BenchmarkDotNet benchmarks are `PayBenchmarks`, `DrainBenchmarks`, `ToggleFrozenBenchmarks`
(one click of Show frozen), `ToggleBalanceSortBenchmarks` (reversing a sort by balance) and
`TrackerBenchmarks`.

Other modes, each run once per view model name:

- `--footprint <name>` - retained memory after Create, after a drain and after 1,000 Pays, plus the
  total, which checks every view model drains the same accounts.
- `--allocations <name>` - allocations counted on the calling thread only.
- `--growth <name>` - allocation, time and retained memory per block of 5,000 Pays, and how often
  `Rows` reported a change.
- `--inprocess` - run the BenchmarkDotNet benchmarks in-process, if the generated project will not
  build.

Out of process, BenchmarkDotNet builds a copy of the view models and the libraries under this
project's `bin` folder, several directories deep. On Windows without long paths enabled, a clone
that already sits at a long path can push those past 260 characters: the build fails with `MSB4102
... exceeds the OS max path limit` and every result reads `NA`. Clone somewhere shorter, enable long
paths, or add `--inprocess`.

## Results

Intel Core i7-9700, Windows 11, .NET 10.0.12, BenchmarkDotNet 0.15.8, out of process, measured at
8957f95. 100,000 accounts in the default state (arrival order, frozen accounts hidden). Pay pays into
the first row; Drain and the toggles run on a fresh view model each iteration.

| | Original | OptimizedDrain | TunedSet | CountAndScan |
|---|---|---|---|---|
| Drain | 213.3 ms, 41.27 MB | **197.6 ms**, 41.95 MB | 200.0 ms, 41.95 MB | 202.6 ms, 41.27 MB |
| Pay before a drain | 23.5 μs, 21.5 KB | 23.1 μs, 20.9 KB | **22.0 μs, 20.4 KB** | 22.1 μs, 20.8 KB |
| Pay after a drain | 22.7 μs, 21.5 KB | 22.2 μs, 20.9 KB | **21.8 μs**, 20.7 KB | 21.9 μs, **20.4 KB** |
| Show frozen | - | 169.0 ms, 43.11 MB | 167.9 ms, 43.11 MB | - |
| Reverse the balance sort | - | 12.5 ms, 5.71 MB | 12.6 ms, 5.71 MB | - |
| Retained after Create | 56.3 MB | 54.2 MB | 54.2 MB | **52.8 MB** |

Retained memory is from `--footprint`, which also shows every view model ending with the same total.
The four view models differ only in how they track drainable accounts, which neither toggle touches, so
the toggles run for two of them.

A drain now takes about 200 ms for every view model, and that is almost all one cost the view models
do not control: the drain resets the whole view chain. See finding 6. The reported error on every
drain is under 1%.

### The tracker's update step alone

`TrackerBenchmarks` runs just the set update, on inputs shaped like what `ItemChange` provides: a
Pay-shaped change (one active account) and a Drain-shaped change (all 25,335 frozen accounts).

| | Pay-shaped change | Drain-shaped change |
|---|---|---|
| OptimizedDrain's former fold | 250.0 ns, 336 B | 2.45 ms, 702 KB |
| Single pass (`Contains` before every change) | 21.8 ns, 0 B | 3.55 ms, 702 KB |
| **Hybrid** (`Contains` only until the first real change) | **22.6 ns, 0 B** | **2.06 ms**, 702 KB |

The single pass pays for its early exit on bulk changes, looking every key up twice. The hybrid keeps
the early exit only while nothing has moved, then calls the builder directly, whose `Add` and
`Remove` already do nothing for a key that is where it should be.

In context the step is small: about 0.1% of a Pay, and about 1% of a drain as it stands - about 6% of
one that did not reset the chain.

## Findings

1. **OptimizedDrain's advantage over Original on Drain is hidden while the chain resets.** Both now
   spend about 200 ms and 41 MB, mostly rebuilding the chain (finding 6), so OptimizedDrain is only
   7% faster and allocates slightly more. With the root's budget disabled it is 1.8x faster (34.5 ms
   against 61.0 ms) and allocates 24% less (8.71 MB against 11.51 MB). It retains about 2 MB less
   after Create either way, and is no worse on Pay.
2. **The hybrid update - applied** in `AccountsViewModelOptimizedDrain`, replacing the fold measured
   above as "OptimizedDrain's former fold". It also removed a redundancy: `ChangedKeys` is already
   `NewStates.Keys.Concat(Removed)`, so `Removed.Concat(ChangedKeys)` walked the removed keys twice,
   and every changed key was removed and then re-added.
3. **Pass `ImmutableHashSet<int>` to `Drain`** rather than `IReadOnlyCollection<int>`, which boxes
   the set's enumerator. Not applied. The comment and ReSharper suppression above it still describe
   an older, concrete parameter.
4. **`Calm()` on `canDrain` saves about 450-500 B per Pay**: `--growth` puts `TunedSet` and
   `CountAndScan`, which have it, at 20,928 and 20,984 B per Pay once warm, against 21,432 B for
   `OptimizedDrain`, which does not. Not applied. `CountAndScan` trades 1.4 MB of retained memory for
   about 3 ms per drain, which is not clearly worth it.
5. **The row list was rebuilt on every Pay - fixed in the library.** `--growth` showed `Rows`
   changing on 5,000 of 5,000 Pays that moved no row, which was most of a Pay's cost and all of its
   noise. Each view stage's `KeysCell` was held from its unfiltered results stream in
   `CollectionViewUtility`, so it emitted on every transaction and `MappedItems` projected a new list
   each time. It was not a leak: allocation and retained memory varied but did not climb.

   Filtering `KeysCell` the way `KeyChangesStream` is filtered, as first suggested here, would not
   have helped: that stream keeps updates, and a Pay reaches every stage as one. `KeysCell` now moves
   only on a reset, insert, remove or move, which is what the documentation already said of it. The
   cell a stage loops its own state through still takes every result, because a re-file that moves
   nothing carries the key's new sort value and the next edit is filed against it.

   `--growth` now reports `Rows` changing on none of 50,000 Pays, 22-24 μs and about 21 KB per Pay
   once warm for every view model, and retained memory flat.
6. **A drain resets the whole view chain - not addressed.** A stage lists the operations a change
   causes only up to a budget of max(1,000, its own count / 10), and past it rebuilds and reports a
   reset. A drain edits 25,335 accounts, past the root's budget of 10,000, so the root resets, and a
   reset forces every stage below it to rebuild too: the visible filter tests all 100,000 accounts
   again, the sort files every visible account again, and the page's rows are projected again - all
   for accounts the view does not show. Listing the changes instead would have cost the visible
   filter a membership check and a predicate test for each, and nothing further down at all.

   Disabling the budget confirms it. Allocation per drain on the calling thread (`--allocations`), and
   BenchmarkDotNet's time, with the budget as it is, with only the root's disabled, and with every
   stage's disabled:

   | | Original | OptimizedDrain | TunedSet | CountAndScan |
   |---|---|---|---|---|
   | As it is | 213.3 ms, 41.27 MB | 197.6 ms, 41.95 MB | 200.0 ms, 41.95 MB | 202.6 ms, 41.27 MB |
   | Root's budget disabled | 61.0 ms, 11.51 MB | 34.5 ms, 8.71 MB | 35.3 ms, 8.71 MB | 38.1 ms, 8.03 MB |
   | Every budget disabled (allocation only) | 38.70 MB | 8.71 MB | 8.71 MB | 8.03 MB |

   With every budget disabled the allocations are exactly those recorded here at e56d2ff, before the
   budget existed, so the budget accounts for all of the change. For three view models the root's
   budget alone does. `Original` is the exception, and there the budget helps: its drainable view is
   itself a filter holding the 25,335 frozen accounts, a drain removes every one, and past that
   filter's own budget of 2,533 it rebuilds to empty rather than removing them one at a time - 11.51 MB
   against 38.70 MB.

   The budget weighs a change against the size of the stage it reaches, and not against what a reset
   costs every stage below. At the root, where everything is below, a reset is the dearest answer.
