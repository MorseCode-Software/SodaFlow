---
title: Operation reference
---

# Operation reference

Every operation, in both languages, in one place. C# spells these as extension methods and
static factories; F# offers qualified module functions (`Stream.map`) and `[<AutoOpen>]`
suffixed aliases (`mapS`). The alias column below lists the latter, since that is what you get
from `open SodaFlow` alone.

For full signatures and parameter contracts, follow through to the
[generated API reference](../api/index.md).

## Creating

| C# | F# alias | Result |
| --- | --- | --- |
| `Stream.Never<T>()` | `neverS ()` | A stream that never fires. |
| `Stream.CreateSink<T>()` | `sinkS ()` | A stream you can `Send` into. |
| `Stream.CreateSink<T>(coalesce)` | `sinkWithCoalesceS f` | As above; `coalesce` combines multiple sends in one transaction. |
| `Cell.Constant(v)` | `constantC v` | A cell that never changes. |
| `Cell.ConstantLazy(lazy)` | `constantLazyC v` | As above, computed on demand. |
| `Cell.CreateSink(initial)` | `sinkC initial` | A cell you can `Send` into. |
| `Cell.CreateStreamSink<T>()` | `sinkCS ()` | A stream sink whose firings are treated as cell updates. |
| `Behavior.Constant(v)` | `constantB v` | A behavior that never changes. |
| `Behavior.CreateSink(initial)` | `sinkB initial` | A behavior you can `Send` into. |

Sinks are the boundary between imperative code and the FRP graph. Send into them from event
handlers, timers, and network callbacks; everything downstream stays pure.

## Stream operations

| C# | F# alias | Meaning |
| --- | --- | --- |
| `s.Map(f)` | `mapS f s` | Transform each value. |
| `s.MapTo(v)` | `mapToS v s` | Replace each value with a constant. |
| `s.Filter(pred)` | `filterS pred s` | Drop firings failing the predicate. |
| `s.FilterMaybe()` | `filterOptionS s` | Unwrap `Maybe<T>` / `option`, dropping empties. |
| `s.Merge(s2, f)` | `mergeS f (s, s2)` | Combine two streams; `f` resolves simultaneous firings. |
| `s.OrElse(s2)` | `orElseS (s, s2)` | Combine two streams; on simultaneity the left wins. |
| `streams.Merge(f)` | `mergeAllS f streams` | Merge a collection of streams. |
| `streams.OrElse()` | `orElseAllS streams` | Left-biased merge of a collection. |
| `s.Hold(initial)` | `holdS initial s` | Turn into a cell remembering the latest value. |
| `s.HoldLazy(lazy)` | `holdLazyS v s` | As above with a lazy initial value. |
| `s.Snapshot(c)` | `snapshotAndTakeC c s` | On each firing, take the cell's value, discarding the stream's. |
| `s.Snapshot(c, f)` | `snapshotC c f s` | On each firing, combine the stream value with the cell's. |
| `s.Snapshot(c1, c2, f)` | `snapshot2C c1 c2 f s` | Same, over more cells (up to 4). |
| `s.Gate(c)` | `gateC c s` | Drop firings while a `Cell<bool>` is false. |
| `s.Calm()` | `calmS s` | Suppress firings equal to the previous one. |
| `s.Calm(comparer)` | `calmWithEqualityComparerS cmp s` | As above with an explicit comparer. |
| `s.Accum(initial, f)` | `accumS initial f s` | Fold into a **cell** holding the accumulated state. |
| `s.Collect(initial, f)` | `collectS initial f s` | Mealy machine: emit an output and a new state. |
| `s.Once()` | `onceS s` | Only the next firing, then never again. |
| `s.Listen(handler)` | `listenS handler s` | Subscribe. Returns `IWeakListener`; does **not** keep the graph alive. |
| `s.ListenStrong(handler)` | `listenStrongS handler s` | Subscribe and keep the graph alive. Returns `IStrongListener`. |
| `s.ListenOnce(handler)` | `listenOnceS handler s` | Subscribe, then unsubscribe after one firing. |
| `s.ListenOnceAsync()` | `listenOnceAsyncS s` | The next firing as a `Task<T>`. |
| `s.AttachListener(l)` | `attachListenerS l s` | Tie a listener's lifetime to this stream. |

The `Snapshot` family is the workhorse. `Gate` is `Snapshot` plus `Filter`; `MapTo` is `Map`
with a constant. Reach for the specific one — it reads better and does less work.

## Cell operations

| C# | F# alias | Meaning |
| --- | --- | --- |
| `c.Sample()` | `sampleC c` | Read the current value *now*. An imperative escape hatch. |
| `c.SampleLazy()` | `sampleLazyC c` | Read on demand, for use inside loops. |
| `c.Map(f)` | `mapC f c` | Transform the value. |
| `c.Lift(c2, f)` | `lift2C f (c, c2)` | Combine two cells. Overloads to 6 in C#, 8 in F#. |
| `cells.Lift()` | `liftAllC id cells` | Combine a collection into `Cell<IReadOnlyList<T>>`. F#'s `liftAllC` always takes a combining function; C# also has `cells.Lift(f)`. |
| `c.Apply(cf)` | `applyC cf c` | Apply a function held in a cell. The primitive `Lift` is built from. |
| `c.Calm()` | `calmC c` | Suppress updates equal to the previous value. |
| `c.Updates()` | `updatesC c` | The stream of changes. Does **not** fire on subscribe. |
| `c.Values()` | `valuesC c` | Changes, **plus** one firing in the transaction it was obtained in. |
| `c.Listen(handler)` | `listenC handler c` | Subscribe. Fires immediately with the current value. Does **not** keep the graph alive. |
| `c.ListenStrong(handler)` | `listenStrongC handler c` | As above, and keeps the graph alive. |
| `c.AsBehavior()` | `asBehaviorC c` | View this cell as a behavior. |
| `cc.SwitchC()` | `switchC cc` | Flatten `Cell<Cell<T>>`. See [Switch](switch.md). |
| `cs.SwitchS()` | `switchS cs` | Flatten `Cell<Stream<T>>`. |
| `cb.SwitchB()` | `switchB cb` | Flatten `Cell<Behavior<T>>`. |

### `Updates` versus `Values`

These differ in exactly one way, and it catches people out.

`Updates` gives you changes only. `Values` gives you changes *plus* an initial firing — but
only if you obtain and use it inside the same explicit transaction. Outside a transaction that
initial firing has nowhere to happen, and you silently get `Updates` behavior instead.

```csharp
// Correct: the initial firing has a transaction to happen in.
IListener l = Transaction.Run(() => c.Values().Listen(Console.WriteLine));
```

If all you want is "current value, then changes", `Listen` on the cell already does exactly
that and needs none of the ceremony.

## Behavior operations

`Behavior<T>` deliberately carries a much smaller surface than `Cell<T>`:

| C# | F# alias | Meaning |
| --- | --- | --- |
| `b.Sample()` | `sampleB b` | Read the current value. |
| `b.Map(f)` | `mapB f b` | Transform the value. |
| `b.Lift(b2, f)` | `lift2B f (b, b2)` | Combine behaviors. |
| `b.Apply(bf)` | `applyB bf b` | Apply a function held in a behavior. |
| `bb.SwitchB()` | `switchBB bb` | Flatten `Behavior<Behavior<T>>`. |

Note what is **missing**: a behavior has no `Listen`, no `Updates`, and no `Values`. That is
not an oversight. A behavior is defined at every point in time, so "the moments at which it
changes" is not a question the model is willing to answer — answering it would let you detect
steps that are meant to be invisible.

When you genuinely need those steps, `Operational.Updates(b)` and `Operational.Value(b)` will
hand them over, with the caveats below.

Going the other way, `c.AsBehavior()` converts a cell to a behavior freely, because that
direction discards information rather than inventing it.

## Transactions

| C# | F# alias | Meaning |
| --- | --- | --- |
| `Transaction.Run(f)` | `runT f` | Run `f` in one transaction, returning its result. |
| `Transaction.RunVoid(a)` | `runT` | Run an action in one transaction. |
| `Transaction.IsActive()` | `isActiveT ()` | Whether a transaction is currently running. |
| `Transaction.Post(a)` | `postT a` | Run after the current transaction closes. |
| `Transaction.OnStart(a)` | `onStartT a` | Run whenever any transaction starts. |

See [Transactions](transactions.md) for what this buys you and when you need to reach for it.

## Operational primitives

| C# | Meaning |
| --- | --- |
| `Operational.Updates(b)` | The stream of steps of a behavior. |
| `Operational.Value(b)` | Fires once on listening with the current value, then on each step. |
| `Operational.Defer(s)` | Push firings into a subsequent transaction. |
| `Operational.Split(s)` | Turn a stream of collections into a stream firing once per element, each in its own transaction. |

The source is blunt about the first two: they are "not part of the main SodaFlow API" and they
break the non-detectability of behavior steps. The stated rule is that you may use them only
inside functions that do not let the caller detect those steps. In practice — fine in library
internals, a smell in application code.

`Defer` and `Split` are different in kind. They concern transaction scheduling rather than
observability, and `Split` in particular is the normal, unremarkable way to fan a collection
out into individual firings.
