# Drain benchmarks

Measures the Accounts sample's ways of knowing which frozen accounts still hold money, and of
draining them: speed and allocation for a Pay (before and after a drain) and a Drain, plus retained
memory.

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

Intel Core i7-9700, Windows 11, .NET 10.0.12, BenchmarkDotNet 0.15.8, 100,000 accounts in the
default state (arrival order, frozen accounts hidden). Pay pays into the first row; Drain runs on a
fresh view model each iteration.

| | Original | OptimizedDrain | TunedSet | CountAndScan |
|---|---|---|---|---|
| Drain | 75.6 ms, 38.7 MB | **35.7 ms**, 8.7 MB | 36.6 ms, 8.7 MB | 39.4 ms, **8.0 MB** |
| Pay before a drain | 162 μs | 162 μs | 162 μs | 167 μs |
| Pay after a drain | 169 μs | 165 μs | 153 μs | 165 μs |
| Retained after Create | 56.3 MB | 54.2 MB | 54.2 MB | **52.8 MB** |

Every view model ends with the same total. Pay times, and Pay allocations of 70-110 KB, are within
noise of each other; see the row list finding below for why.

### The tracker's update step alone

`TrackerBenchmarks` runs just the set update, on inputs shaped like what `ItemChange` provides: a
Pay-shaped change (one active account) and a Drain-shaped change (all 25,335 frozen accounts).

| | Pay-shaped change | Drain-shaped change |
|---|---|---|
| OptimizedDrain's fold | 229.6 ns, 336 B | 2.37 ms, 702 KB |
| Single pass (`Contains` before every change) | 21.8 ns, 0 B | 3.54 ms, 702 KB |
| **Hybrid** (`Contains` only until the first real change) | **23.7 ns, 0 B** | **2.11 ms**, 702 KB |

The single pass pays for its early exit on bulk changes, looking every key up twice. The hybrid keeps
the early exit only while nothing has moved, then calls the builder directly, whose `Add` and
`Remove` already do nothing for a key that is where it should be.

In context the step is small: under 0.2% of a Pay and about 7% of a Drain.

## Findings

1. **OptimizedDrain is a clear win over Original** on Drain (2.1x faster, 78% less allocation) and on
   retained memory (about 2 MB less), and no worse on Pay.
2. **Use the hybrid update** for the set. It also removes a redundancy in the committed fold:
   `ChangedKeys` is already `NewStates.Keys.Concat(Removed)`, so `Removed.Concat(ChangedKeys)` walks
   the removed keys twice, and every changed key is removed and then re-added.
3. **Pass `ImmutableHashSet<int>` to `Drain`** rather than `IReadOnlyCollection<int>`, which boxes
   the set's enumerator. The comment and ReSharper suppression above it describe the old concrete
   parameter.
4. `Calm()` on `canDrain` made no measurable difference, and `CountAndScan` trades 1.35 MB of
   retained memory for 3.7 ms per drain - neither is clearly worth it.
5. **The row list is rebuilt on every Pay** in every view model, which is where most of a Pay's cost
   and noise comes from. `--growth` shows `Rows` changing on 5,000 of 5,000 Pays that move no row.
   Each view stage's `KeysCell` is held from its unfiltered results stream in
   `CollectionViewUtility`, so it emits on every transaction - the same `OrderedKeys` instance when
   nothing moved - and `MappedItems` projects a new list each time. `KeyChangesStream` already
   filters to `IsReset || Operations.Count > 0`; filtering `KeysCell`'s source the same way should
   stop it. It is not a leak: allocation and retained memory vary but do not climb over 50,000 Pays.

Drain iterations (35-75 ms) are under BenchmarkDotNet's recommended 100 ms, though the reported
error is under 2%.
