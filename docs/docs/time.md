---
title: Time and timers
---

# Time and timers

Wall-clock time is the classic case where `Behavior<T>` earns its keep over `Cell<T>`. Time is
not a sequence of discrete steps — it is defined at every instant — so a behavior is the honest
model for it.

A timer system provides that behavior, plus a way to get a stream that fires at a chosen
moment.

## Choosing a timer system

Two implementations ship in `Sodium.Frp`:

| Type | Time is | Use when |
| --- | --- | --- |
| `SecondsTimerSystem` | `double`, seconds since the system was created | Simulations, animation, anything relative. |
| `SystemClockTimerSystem` | `DateTime`, from `DateTime.Now` | Scheduling against real calendar times. |

Both take an exception handler, because timer callbacks run outside your call stack and an
exception there has nowhere else to go:

```csharp
SecondsTimerSystem timers = new SecondsTimerSystem(ex => Log.Error(ex));
```

## `Time`

`Time` is a `Behavior<T>` giving the current time:

```csharp
Behavior<double> now = timers.Time;
```

Being a behavior, it has no `Listen` and no `Updates` — you cannot subscribe to "every moment",
which is exactly right, since there is no such discrete sequence. You use it by sampling it
when something else happens, and `Snapshot` has behavior overloads for precisely this:

```csharp
// Timestamp each click.
Stream<double> clickTimes = clicks.Snapshot(now, (_, t) => t);
```

This is the normal way to work with time in FRP: time does not push events at you, it is a
value you read at the moments that matter.

## `At`

`At` turns a cell of *target times* into a stream that fires when each target is reached:

```csharp
Stream<T> At(Cell<Maybe<T>> t)
```

The `Maybe` is what makes it useful. `Maybe.Some(t)` arms the alarm for time `t`;
`Maybe.None` disarms it. Because the argument is a cell, the target can change over time, and
rescheduling is just sending a new value.

# [C#](#tab/csharp)

```csharp
SecondsTimerSystem timers = new SecondsTimerSystem(ex => Log.Error(ex));

// Fire five seconds from now.
double target = timers.Time.Sample() + 5.0;
CellSink<Maybe<double>> alarm = Cell.CreateSink(Maybe.Some(target));

IListener l = timers.At(alarm).Listen(t => Console.WriteLine($"fired at {t}"));

// Cancel it before it fires.
alarm.Send(Maybe.None);
```

# [F#](#tab/fsharp)

```fsharp
open Sodium.Frp
open Sodium.Frp.Time

let timers = SecondsTimerSystem (fun ex -> Log.Error ex) :> ITimerSystem<float>

// Fire five seconds from now.
let target = (timers.Time |> sampleB) + 5.0
let alarm = sinkC (Some target)

let l = timers.At alarm |> listenS (printfn "fired at %f")

// Cancel it before it fires.
alarm |> sendC None
```

Note that the F# side takes an `option`, not a `Maybe` — `Some target` to arm, `None` to
disarm — and that `Sodium.Frp.Time` needs its own `open`.

---

A repeating timer is `At` plus a loop: when it fires, compute the next target and send that
back into the cell. Because the feedback goes through a sink rather than through pure FRP
logic, do the send in a `Transaction.Post` — see [Transactions](transactions.md).

## Testing

`TimerSystem<T>` is built on `ITimerSystemImplementation<T>`, whose entire contract is `Now`,
`SetTimer`, `Start`, and `RunTimersTo`. `TimerSystemImplementationBase<T>` supplies everything
except `Now`.

That is deliberate, and it is the reason to prefer a timer system over `DateTime.Now` scattered
through your logic: supply your own implementation and time becomes a value you control. Tests
advance the clock with `RunTimersTo` and run deterministically, with no sleeping and no
flakiness.

The F# implementation mirrors all of this in `Sodium.Frp.Time` — `TimerSystem<'T>`,
`SecondsTimerSystem`, `SystemClockTimerSystem`, and the same interfaces.
