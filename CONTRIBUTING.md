# Contributing to SodaFlow

This file is for people who work on SodaFlow itself: how parts of the libraries work inside, how they are tested, and
how the packages are released. To use SodaFlow, start with the
[documentation](https://morsecode-software.github.io/SodaFlow/) instead. How to build and test the repository is in the
[README](README.md#building).

## Listener lifetimes inside the library

The [Listener lifetimes](docs/docs/lifetimes.md) page gives the rule a user follows. This is the machinery behind it.

### The machinery behind a combinator

`Listen` gives a handle that holds the stream, so the subscription lives exactly as long as the caller keeps the
handle. `ListenOnceAsync` has no handle to give, so it roots its listener in the keep-alive set of each root the stream
has, until the first firing or a cancellation. The listener holds the stream in turn, so the two form a cycle that the
collector reclaims together when the roots go. Neither permanently roots a stream by itself.

A combinator needs one more kind of subscription, and none of it is public. It binds *some other* listener to *a chosen*
stream's lifetime, and has it actively unlistened (its node disconnected from upstream) when that stream is collected,
rather than merely becoming unreachable. That is how a combinator keeps its own wiring alive. `Switch` subscribes to the
outer cell to learn when the branch changes, and to whichever inner stream is currently selected. The caller holds
neither, only the stream `Switch` returns. Both subscriptions are therefore attached to that returned stream, so they
live exactly as long as the result does. `Operational`, `LoopedStream`, and `TimerSystem` do the same thing.

To stop those listeners after the stream is gone, the library keeps a weak view of each one, which can unlisten it
without holding the stream. The inner subscription of `Switch` is replaced on every branch change, but attaching a
fresh listener each time would grow the attached-listener list of the stream without bound, so one listener whose
target can be re-pointed is attached once.

Application code never gets into that situation, which is why this machinery is internal rather than merely discouraged:
building a primitive this way needs the `Listen` overload taking a node, which is `internal`, and a handler that sends,
which throws. Earlier versions exposed parts of it: `AttachListener`, the weak view through
`GetListenerWithWeakReference`, `MutableListener`, and `Cleanup`. SodaFlow.Core 6.0.0 removed the last of them from the
public surface.

### Verifying it

These invariants are enforced by `SodaFlow.Tests.Memory`, which asserts them using weak references, the listener set
of the node, and the registry count of the manager, with no profiler. If you change cleanup behavior, those tests are
the specification.

## Conformance with the denotational semantics

The [Denotational semantics](docs/docs/semantics.md) page says that SodaFlow is tested against the specification.
`DenotationalSemanticsTests` transcribes the Haskell reference cases into both language surfaces, the same cases in
each, case for case, and asserts that SodaFlow produces the specified occurrences:

| | |
| --- | --- |
| C# | `src/CSharp/SodaFlow.Tests/DenotationalSemanticsTests.cs` |
| F# | `src/FSharp/SodaFlow.FSharp.Tests/DenotationalSemanticsTests.fs` |

They cover `Never`, `MapS`, `Snapshot`, `Merge`, `Filter`, `SwitchS`, `Updates`, `Split`, `Constant`, `Hold`, `MapC`,
`Apply`, and `Sample`, the primitives the paper defines, plus the .NET additions `Value`, `ListenC`, `ConstantLazy`,
`SwitchC`, and `SampleLazy`.

Each test drives a simulated timeline: streams are given fixed occurrences at integer times, the transaction at each
time is run, and the observed output list is compared against the value the specification requires.
`Test_Merge_TestCase` in C#, and `Merge: Test Case` in F#, for instance, pins down exactly what simultaneous
occurrences produce, which is the case most libraries leave undefined.

## Reactive collections

What the [Reactive collections](docs/docs/collections.md) page promises a user, and the costs it gives, rest on the
design below. None of it is public.

### What a stage holds

Each stage of a view keeps its own snapshot current with one graph node, and each change makes one small object, which
holds the two maps behind the visible keys of that stage.

Each collection and each view keeps one index from key to cell, and a change looks its keys up there. That is what
makes an edit of one row cost the same whether a hundred rows are bound or ten thousand. A cell that nothing holds any
more is skipped, even before the garbage collector reclaims it. The index is paid for when a cell is made: a few
hundred bytes for each cell.

The weak cache of `StateCell` is the one member where the split between the core and the language surfaces could
have cost something. The cache stays in the core and takes the projection from the wrapper, so neither surface pays
for an extra graph node. It is keyed by the projection as well as the key. Each language surface holds one projection
per cell type, and a cache belongs to the projection that filled it, so the C# and F# surfaces over one collection
cannot be handed each other's cells.

### How the chain stays incremental

Each stage keeps its own ordered key set and applies the operations from above.

| Stage | On upstream insert or remove | On upstream update | On upstream move |
| --- | --- | --- | --- |
| `Filter` | Test, add or drop | May enter, leave, or move | May enter, leave, or move |
| `SortBy` | Add or drop | Re-file, O(log n) | Re-file, O(log n) |
| `Take` | Re-window | Forwarded if inside the window | Re-window |

`ViewUpdate` is what makes chaining work. A stage that saw no positional consequence still has to tell the stage below
that an item changed, because that stage may sort or filter on exactly the state that just moved.

An upstream move says the same thing, which is why it is re-filed rather than ignored. A re-file that moves a key
reports the move alone, so the move is the only operation carrying the new sort value of the key, and a stage below
has to file against it exactly as it would against an update. The `StateCell` of the row fires on one too, for the
same reason: the value is what moved the row.

That is the only thing a move ever means. A move caused by the order itself changing would break a filter below it,
which re-files a moved key under the order it already holds and so would keep the old one. It would also cost everything
else below it: every stage would re-file each moved key, and the `StateCell` of every moved row would fire, for values
that did not change. Thus, a stage whose order changes resets instead, and the stages below re-file what they hold under
the new order.

`Filter` preserves upstream order without tracking positions in the upstream list: it asks the key set of the upstream
for a new set under *the same order*, holding the members it kept. That keeps it O(log n) per changed key instead of
needing rank queries over a subsequence.

Re-filing a key on update stores the sort value it was filed under, so the old entry is removed with the comparison
that placed it. Otherwise, you are hunting a sorted set for an entry whose sort position has already moved underneath
it.

A state edit can never move a key in an order that makes its sort value from the key or from the identity. The order of
the root, the order of arrival, is such an order, and so is each filter directly above it. In such an order, a stage
skips the removal and the add that would find that out, which is worth about a tenth of an edit and a fifth of its
allocation, as `KeyedCollectionViewBenchmarks` measured it.

### Observing through a view

Observing through a view costs nothing over observing the collection directly, and that is only because of how it is
built. The natural implementation, lifting the cell of the collection against the keys of the view, costs about twice
an ordinary edit, because every observer becomes a node the propagation walks whenever the view moves at all, and a
reorder counts as moving. Holding membership per observer and calming it wins back about a quarter of that. The
library does neither. A view keeps its own index from key to cell, exactly as the collection does, so a change of the
view reaches only the cells of the keys it names and goes no further.

An edit of many keys in one transaction is the other case. Each cell that a change reaches asks the change whether an
operation names its key, and a change that names more keys than have cells reaches every cell. A scan of the
operations would make a wide edit against many observers pay for every observer against every operation, with almost
every comparison finding nothing. Thus, a `CollectionViewChange` with at least
`MinimumOperationsForIndex` operations builds a dictionary of its keys once it has answered `LookupsBeforeIndex` lookups
by a scan. Both constants are 8. A wide edit through a view therefore costs close to the same edit on the collection,
where the item change always answers from a dictionary.

### Optionality

`SodaFlow.Collections.Core` has no optional type of its own, and depends on neither `SodaFlow.Functional` nor anything
else beyond `SodaFlow.Core`. It answers in `TryGetItem`, `TryGetState`, `TryGetNewState`, and an internal index lookup
returning `-1`, and each language surface puts its own optional type back on top: `SodaFlow.Collections` adds
`Maybe<T>`, and `SodaFlow.FSharp.Collections` adds `option`. The sentinel does not escape: the only `IndexOf` either
surface exposes is the one answering `Maybe<int>` or `int option`.

It is the same reason `SodaFlow.FSharp` does not depend on `SodaFlow.Functional`, applied one layer down: nothing that
installs the F# collections package acquires `Maybe<T>`, which F# has no use for.

### Storage

`StateMap<TKey, TState>` is the seam where the storage strategy is chosen. `ImmutableStateMap` is the default: an
`ImmutableDictionary`, which is an AVL tree of hash buckets. An update is O(log n) and allocates only the path from
the root, about seventeen nodes per edit at 100k items. Measure before replacing it.

The contract an implementation must honor is that **a value returned by an instance never changes for the lifetime of
that instance**. SodaFlow reads the pre-transaction value of a cell during a transaction, so a snapshot that aliases
storage you have since mutated will silently report the future. An in-place strategy therefore has to version its
storage and serve older instances from a log, rather than handing back the live map.

`Fold` reads `Pairs` for its first value, not `Keys` with a lookup for each, which measured three times slower at every
size.

## Packages and releases

The [Which package do I install?](docs/docs/packages.md) page tells a user which packages to take and how their
versions relate. This is how the repository produces them.

### Why a wrapper and its core move together

`SodaFlow.Core` exposes its real work through `internal` members. Every wrapper, `SodaFlow`, `SodaFlow.FSharp`, and each
async, bindable, and collections assembly, reaches them through `InternalsVisibleTo`, which
`src/SodaFlow.Core/AssemblyInfo.cs` lists. Thus, a change to those internals binds the wrappers to one particular core
even when no public API moved. When that happens, the whole group takes a major version together, and the version
range on each dependency is what keeps an old wrapper off a newer core. The packing gives each dependency on a package
of this repository the range from the version it was built against up to, but not including, the next major of that
package. The `AddUpperBoundToProjectReferenceVersions` target in `src/Directory.Build.props` writes that range, and its
comment gives the reason.

### Versions and tags

Each package versions independently from its own git tag prefix, via [MinVer](https://github.com/adamralph/minver).
Each packable project sets its own `MinVerTagPrefix`. Pushing `sodaflow-async-1.1.0` releases `SodaFlow.Async` at 1.1.0
and leaves every other package exactly where its own last tag put it.

| Package | Tag prefix |
| --- | --- |
| `SodaFlow` | `sodaflow-` |
| `SodaFlow.Core` | `sodaflow-core-` |
| `SodaFlow.Functional` | `sodaflow-functional-` |
| `SodaFlow.FSharp` | `sodaflow-fsharp-` |
| `SodaFlow.Async` | `sodaflow-async-` |
| `SodaFlow.Async.Core` | `sodaflow-async-core-` |
| `SodaFlow.FSharp.Async` | `sodaflow-fsharp-async-` |
| `SodaFlow.Bindable.ObjectModel` | `sodaflow-bindable-objectmodel-` |
| `SodaFlow.Bindable.ObjectModel.Core` | `sodaflow-bindable-objectmodel-core-` |
| `SodaFlow.FSharp.Bindable.ObjectModel` | `sodaflow-fsharp-bindable-objectmodel-` |
| `SodaFlow.Collections` | `sodaflow-collections-` |
| `SodaFlow.Collections.Core` | `sodaflow-collections-core-` |
| `SodaFlow.FSharp.Collections` | `sodaflow-fsharp-collections-` |

A package with no tag on the commit being built keeps the version its last tag gave it, so a release only publishes
what actually moved.

### Trimming and native AOT

The `net8.0` build of each C# package runs the .NET trim, AOT, and single-file analyzers and fails on any warning, so
code that needs reflection can't reach a release unnoticed.
