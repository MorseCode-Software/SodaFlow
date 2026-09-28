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

Two implementations ship in `SodaFlow`:

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
open SodaFlow
open SodaFlow.Time

let timers = SecondsTimerSystem (fun ex -> Log.Error ex) :> ITimerSystem<float>

// Fire five seconds from now.
let target = (timers.Time |> sampleB) + 5.0
let alarm = sinkC (Some target)

let l = timers.At alarm |> listenS (printfn "fired at %f")

// Cancel it before it fires.
alarm |> sendC None
```

Note that the F# side takes an `option`, not a `Maybe` — `Some target` to arm, `None` to
disarm — and that `SodaFlow.Time` needs its own `open`.

---

## A repeating timer

`At` plus a cell loop, and nothing else — no sink, and no `Transaction.Post`. The alarm computes the
next target, and the cell holding that target is what the alarm reads:

# [C#](#tab/csharp)

```csharp
Stream<double> ticks =
    Transaction.Run(() =>
        Cell.Loop<Maybe<double>>()
            .WithCaptures(deadline =>
            {
                Stream<double> alarm = timers.At(deadline.AsCell());

                return (
                    Cell: alarm.Map(static t => Maybe.Some(t + 5.0)).Hold(Maybe.Some(5.0)),
                    Captures: alarm);
            })
            .Captures);
```

# [F#](#tab/fsharp)

```fsharp
let ticks =
    Transaction.run (fun () ->
        let struct (_, alarm) =
            Cell.loop (fun deadline ->
                let alarm = timers.At deadline
                struct (alarm |> Stream.map (fun t -> Some(t + 5.0)) |> Stream.hold (Some 5.0), alarm))

        alarm)
```

---

The loop is the whole trick, and it is why this is ordinary FRP rather than a callback that sends
into a sink: the deadline cell is defined in terms of the alarm that reads it. See
[Loops](loops.md).

**Missed ticks are made up, one per transaction.** The next target is the alarm's own time plus the
period, so the period stays exact; if the process is suspended across a minute of them, the stream
fires once per missed interval, each in its own transaction, as fast as transactions open.

Reading the clock rather than the alarm's value changes nothing. Inside an alarm's transaction
`timers.Time` *is* that alarm's time — that is what a behavior means, the value at that instant — so
snapshotting it gives the same sequence. A repeating timer built on `At` is fixed-rate with
catch-up, and dropping stale ticks is a decision outside the graph: compare a firing's time against
a clock read in a later transaction.

There is no `Periodic` in the library, and the loop above is why: it is short, it is ordinary FRP,
and written out like this the catch-up behavior is visible instead of hidden behind an interval
argument.

Consider whether you need a tick stream at all. Bounce animates without one: a ball's position is a
`Behavior` — a function of time — and the view samples it when it paints, at whatever frequency it
likes. A stream of ticks invites code that adds a delta per tick, which is the state the graph is
there to remove.

## `Debounce`

`Debounce` fires the last value of a stream once a quiet time has passed with no other value. Each
firing pushes the alarm out, so a burst of firings produces one value:

# [C#](#tab/csharp)

```csharp
// One search per 300ms of quiet, rather than one per keystroke.
Stream<string> searches = query.Updates().Debounce(timers, static now => now.AddMilliseconds(300));
```

# [F#](#tab/fsharp)

```fsharp
// One search per 300ms of quiet, rather than one per keystroke.
let searches = query |> updatesC |> Time.debounce timers (fun now -> now.AddMilliseconds 300.0)
```

---

The second argument is the deadline: given the time of a firing, return the time to fire at. It is
a function rather than a duration because the time type is yours — `DateTime` for
`SystemClockTimerSystem`, `double` seconds for `SecondsTimerSystem`, whatever your own
implementation uses. It must return a time *after* the one it is given; an earlier time is a time
the clock has already passed, and the alarm fires at the next transaction.

Three things are worth knowing:

- **The result is in the alarm's transaction**, never in the transaction of a firing. A graph that
  reads both sees two instants, which is correct — they happen at two times.
- **It does not cancel work already started.** Debouncing and `SwitchLatest` solve different halves
  of the same problem: debouncing stops requests that should never have been made, and a cancelling
  `MapAsync` strategy stops the ones already in flight whose results nobody wants. A search box
  usually wants both. See [Asynchronous work](async.md).
- **A firing that lands in the alarm's own transaction re-arms it** rather than being swallowed,
  which is what makes a debounce safe to put in a loop.

Writing this by hand takes a cell loop, a deadline cell, a `Hold` for the last value, and care about
which side of an `OrElse` wins when a firing and the alarm share a transaction. That is the reason
it is in the library.

## Testing

`TimerSystem<T>` is built on `ITimerSystemImplementation<T>`, whose entire contract is `Now`,
`Start`, `SetTimer`, and `RunTimersTo`. That is the reason to prefer a timer system over
`DateTime.Now` scattered through your logic: supply your own implementation and time becomes a
value the test controls.

Implement the interface directly for a test. `TimerSystemImplementationBase<T>` is the base for
a *real* clock — its `Start` spins a background thread that waits out the interval to the next
alarm — which is the one thing a deterministic test must not do. Four members is the whole job:

```csharp
internal sealed class ManualClock : ITimerSystemImplementation<double>
{
    private readonly List<ManualTimer> timers = [];

    public double Now { get; private set; }

    // Nothing to start: this clock only moves when the test moves it.
    public void Start(Action<Exception> handleException)
    {
    }

    public ITimer SetTimer(double time, Action callback)
    {
        ManualTimer timer = new ManualTimer(time, callback);
        this.timers.Add(timer);
        return timer;
    }

    public void RunTimersTo(double now)
    {
        // A copy, because a callback that runs here can set a timer of its own.
        ManualTimer[] due = [.. this.timers.Where(t => !t.Canceled && t.Time <= now)];

        foreach (ManualTimer timer in due)
        {
            this.timers.Remove(timer);
            timer.Fire();
        }
    }

    public void AdvanceTo(double now)
    {
        this.Now = now;
        this.RunTimersTo(now);
    }

    // ITimer is an IDisposable, and disposing one cancels it.
    private sealed class ManualTimer(double time, Action callback) : ITimer
    {
        public double Time { get; } = time;

        public bool Canceled { get; private set; }

        public void Cancel() => this.Canceled = true;

        public void Dispose() => this.Cancel();

        public void Fire() => callback();
    }
}
```

The test then reads as a sequence of instants rather than a sequence of sleeps:

```csharp
ManualClock clock = new ManualClock();
ITimerSystem<double> timers = new TimerSystem<double>(clock, static ex => throw ex);

CellSink<Maybe<double>> alarm = Cell.CreateSink(Maybe.Some(5.0));
List<double> fired = [];

using (timers.At(alarm).ListenStrong(fired.Add))
{
    clock.AdvanceTo(4.9);
    // fired is still empty.

    clock.AdvanceTo(5.0);
    // fired now holds one value, and nothing slept to get it there.
}
```

Nothing in that test depends on wall-clock timing, so it cannot be flaky and it runs as fast as
the processor can walk the graph. `TimerGarbageCollectionTests` in `SodaFlow.Tests.Memory` uses
exactly this shape.

The F# implementation mirrors all of this in `SodaFlow.Time` — `TimerSystem<'T>`,
`SecondsTimerSystem`, `SystemClockTimerSystem`, and the same interfaces.
