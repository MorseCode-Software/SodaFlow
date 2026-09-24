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

A repeating timer is `At` plus a loop: when it fires, compute the next target and send that
back into the cell. Because the feedback goes through a sink rather than through pure FRP
logic, do the send in a `Transaction.Post` — see [Transactions](transactions.md).

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
