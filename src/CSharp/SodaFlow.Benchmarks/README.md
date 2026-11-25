# Benchmarks

Microbenchmarks of the libraries under `src/`, written with BenchmarkDotNet. The project references the projects, not
the published packages, so a run measures the working tree. Each class asks one question, and its XMLDoc says what it
compares against and how to read the result.

## Running

Release is required: BenchmarkDotNet refuses to measure a debug build. `Program` hands its arguments to a
`BenchmarkSwitcher`, so a new class needs no registration, and `--filter` chooses what runs. Everything, which takes a
while:

```bash
dotnet run -c Release --project src/CSharp/SodaFlow.Benchmarks -- --filter '*'
```

One class:

```bash
dotnet run -c Release --project src/CSharp/SodaFlow.Benchmarks -- --filter '*BindableValue*'
```

The project targets `net472` and `net10.0`, and each benchmark class declares a job on each runtime, so one run reports
both.

## The reactive collections page

The tables on the [Reactive collections](../../../docs/docs/collections.md) page come from these classes. Each one
measures a `ReactiveCollection` against the shapes people reach for instead.

| Class | What it measures |
| --- | --- |
| `KeyedCollectionEditBenchmarks` | One edit to a key nothing is watching, with twenty rows bound, against a cell sink per mutable value per object and against the same cells fed from one shared edit stream. This is the claim that per-item observation follows the number of bound rows and not the size of the collection. |
| `KeyedCollectionBuildBenchmarks` | What each of those shapes costs to stand up. |
| `KeyedCollectionViewBenchmarks` | The view chain, `Filter`, `SortBy`, and `Take`, against re-deriving the same view from a cell that holds the whole collection. |
| `KeyedCollectionPagingBenchmarks` | Turning the page of a `Slice`, and editing within one, against re-deriving those with `OrderByDescending`, `Skip`, and `Take`. |
| `KeyedCollectionAggregateBenchmarks` | A total over the whole collection folded from the change stream, against one read again from the store. |
| `KeyedCollectionScaleBenchmarks` | One question, what a selective filter costs per edit, up to a million items. |

Run them all with:

```bash
dotnet run -c Release --project src/CSharp/SodaFlow.Benchmarks -- --filter '*KeyedCollection*'
```

### Reading the scale benchmark

`KeyedCollectionScaleBenchmarks` compares a filter over the state with `FilterByIdentity`, for a key the view does not
hold. Most of what an edit costs is spent before any stage is consulted, so a saving inside one stage disappears into
it. The benchmark therefore has a third arm with no view stages at all, the floor, and the page reports the cost of the
chain as each arm minus that floor.

On that measure the identity filter costs three to six percent less than the filter over the state, consistently. It
was ahead in every paired measurement, on both runtimes, and the gap does not grow with the collection. It is largest
at ten thousand items and flat above that. Both things it skips are lookups in an `ImmutableDictionary`, which is a
balanced binary tree, so each costs O(log n): a depth of about fourteen at ten thousand items and twenty at a million.
Six more nodes to read on each lookup are small beside an edit of seven microseconds, so there was never much room for
the gap to open.

Allocation is identical to the byte at every size. What the identity filter skips is reading, and reading allocates
nothing.
