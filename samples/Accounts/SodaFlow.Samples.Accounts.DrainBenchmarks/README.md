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

Each benchmark checks, outside what it measures, that its click did what it should: the drain
emptied the frozen accounts, the toggle changed the row list, and every Pay raised the balance by
exactly one deposit. Out of process, a failed check fails that benchmark - it reads `NA` and the
error is listed under Errors. With `--inprocess`, BenchmarkDotNet prints the error but still reports
the timings as if nothing were wrong, so read the log. Neither sets a non-zero exit code.

## Results

Intel Core i7-9700, Windows 11, .NET 10.0.12, BenchmarkDotNet 0.15.8, out of process. Drain and Pay
measured at d2ee700, the rest at dc3eadf; nothing between the two touches the toggles, the tracker or
what is retained. 100,000 accounts in the default state (arrival order, frozen accounts hidden). Pay
pays into the first row; Drain and the toggles run on a fresh view model each iteration.

| | Original | OptimizedDrain |
|---|---|---|
| Drain | 60.3 ms, 11.51 MB | **34.8 ms, 8.71 MB** |
| Pay before a drain | 23.7 μs, 21.9 KB | **22.4 μs, 20.4 KB** |
| Pay after a drain | 24.7 μs, 21.5 KB | **22.2 μs, 20.5 KB** |
| Show frozen | 169.3 ms, 43.11 MB | 169.1 ms, 43.11 MB |
| Reverse the balance sort | 12.3 ms, 5.71 MB | 12.7 ms, 5.71 MB |
| Retained after Create | 56.3 MB | **54.2 MB** |

Retained memory is from `--footprint`, which also shows both view models ending with the same total.
The two differ only in how they track drainable accounts, which neither toggle touches, so the toggles
measure the same work for both.

### The tracker's update step alone

`TrackerBenchmarks` runs just the set update, on inputs shaped like what `ItemChange` provides: a
Pay-shaped change (one active account) and a Drain-shaped change (all 25,335 frozen accounts).

| | Pay-shaped change | Drain-shaped change |
|---|---|---|
| OptimizedDrain's former fold | 233.3 ns, 336 B | 2.44 ms, 702 KB |
| Single pass (`Contains` before every change) | 25.0 ns, 0 B | 3.46 ms, 702 KB |
| **Hybrid** (`Contains` only until the first real change) | **22.7 ns, 0 B** | **2.06 ms**, 702 KB |

The single pass pays for its early exit on bulk changes, looking every key up twice. The hybrid keeps
the early exit only while nothing has moved, then calls the builder directly, whose `Add` and
`Remove` already do nothing for a key that is where it should be.

In context the step is small: about 0.1% of a Pay and about 6% of a Drain.

## Findings

1. **OptimizedDrain is a clear win over Original** on Drain (1.7x faster, 24% less allocation) and on
   retained memory (about 2 MB less), and no worse on Pay.
2. **The hybrid update - applied** in `AccountsViewModelOptimizedDrain`, replacing the fold measured
   above as "OptimizedDrain's former fold". It also removed a redundancy: `ChangedKeys` is already
   `NewStates.Keys.Concat(Removed)`, so `Removed.Concat(ChangedKeys)` walked the removed keys twice,
   and every changed key was removed and then re-added.
3. **Pass `ImmutableHashSet<int>` to `Drain`** rather than `IReadOnlyCollection<int>`, which boxes
   the set's enumerator. Not applied. The comment and ReSharper suppression above it still describe
   an older, concrete parameter.
4. **`Calm()` on `canDrain` - applied** in `AccountsViewModelOptimizedDrain`. The drainable set is
   folded over every item change, so a Pay into an active account hands back the same set and
   `canDrain` recomputes `true`, which woke the Drain command's enablement on every Pay. `--growth`,
   run before and after in one session, puts a Pay once warm at 21,376 B without it and 20,872 B with
   it - 504 B less - and time within noise.
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
6. **A drain reset the whole view chain - fixed in the library.** A stage lists the operations a change
   causes up to a budget of max(1,000, its own count / 10), and past it rebuilds and reports a reset,
   which makes every stage below it rebuild too. The root counted every key an edit named. A drain
   edits 25,335 accounts, past the root's budget of 10,000, so the root reset and the visible filter
   tested all 100,000 accounts again, the sort filed every visible account again, and the page's rows
   were projected again - all for accounts the view does not show.

   Disabling the budget confirmed it: with every stage's disabled, `--allocations` gave exactly the
   figures recorded here at e56d2ff, before the budget existed. With only the root's disabled,
   `OptimizedDrain` did too.

   A budget now counts work rather than keys: an insert, a remove, or a re-file under an order that
   reads the state. The root counts only adds and removes, since an update cannot move a key in
   arrival order. A filter skips a key it does not hold before counting anything, and a filter or sort
   whose order reads no state re-files with a lookup and does not count it. A sort under an order that
   reads the state checks its budget before starting rather than after re-filing as much as the budget
   allows. BenchmarkDotNet, before (at 8957f95) and after:

   | Drain | Original | OptimizedDrain |
   |---|---|---|
   | Before | 213.3 ms, 41.27 MB | 197.6 ms, 41.95 MB |
   | After | 60.3 ms, 11.51 MB | 34.8 ms, 8.71 MB |

   `Original` does not return to its figure from before the budget existed, 38.70 MB, and is better
   for it. Its drainable view is itself a filter holding the 25,335 frozen accounts, a drain removes
   every one, and removes are work, so past that filter's own budget of 2,533 it rebuilds to empty
   rather than removing them one at a time.

   The drain was also timed by hand for `OptimizedDrain` in each state the view can be in, averaged over
   eight fresh view models, with the library before the fix (e3e05ed's parent) and after:

   | Drain, OptimizedDrain | Before | After |
   |---|---|---|
   | Frozen hidden, arrival order | 222.2 ms, 41.95 MB | 36.0 ms, 8.71 MB |
   | Frozen shown, arrival order | 238.7 ms, 48.87 MB | 55.8 ms, 10.87 MB |
   | Frozen hidden, sorted by balance | 255.6 ms, 41.92 MB | 35.1 ms, 8.70 MB |
   | Frozen shown, sorted by balance | 296.6 ms, 48.95 MB | 166.1 ms, 23.65 MB |

   The last is the one a large update still resets: shown, the drained accounts reach the sort by
   balance, which has to re-file every one of them. It resets from there down rather than from the
   root.
