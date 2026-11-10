---
title: Listener lifetimes
---

# Listener lifetimes

FRP graphs are held together by references, and getting those references wrong is the most
common way to break a SodaFlow program. Listeners that die early stop firing, silently; listeners
that never die leak, also silently. Neither throws.

## One rule

`Listen` returns an `IListener`. The handler runs for as long as you hold that listener, and no
longer. The listener keeps the graph that it observes in memory, and nothing but your reference
keeps the listener. `IListener` is an `IDisposable`: `Unlisten()` and `Dispose()` do the same
thing, and each stops the handler immediately.

Hold the listener in a field of the object that owns the subscription:

```csharp
this.listener = s.Map(x => x * 2).Listen(Console.WriteLine);
```

In a scoped context, `using` holds the listener and stops it, and it is what the test suite uses
throughout:

```csharp
using (result.Listen(@out.Add))
{
    s.Send(1);
    s.Send(2);
}
```

`ListenOnce` unsubscribes itself after the first firing, and follows the same rule. Hold its
listener until the firing that you wait for.

> **Upgrading:** `ListenStrong` and `ListenOnceStrong` are gone, and so are `IStrongListener`
> and `IWeakListener`. A strong listener kept firing after you dropped it, for as long as a
> source of its stream lived, and then nothing could stop it. Each call to a strong form stops
> compiling, which marks every place to change. Call `Listen` or `ListenOnce` there, and keep
> the result. Code that calls `Listen` and discards the result compiles, and it is
> [the mistake that fails silently](#the-mistake-that-fails-silently). Rename `IWeakListener`
> to `IListener`, and the weak and strong forms of `Listener.Empty`, `CreateComposite`, and
> `Append` to the plain ones. In F#, the same applies to `listenStrong`, `listenOnceStrong`, and
> their `S` and `C` aliases, and the `WeakListener` and `StrongListener` modules are now
> `Listener`.

## Waiting for one firing

`ListenOnceAsync` gives you the next firing as a `Task<T>`, with an optional
`CancellationToken`. An `await` has no place to keep a listener, so this is the one call that
needs no handle: the sources of the stream hold the wait.

A stream with more than one input, which is a lift, a merge, `Apply`, `MergeMaybes`, or a
switch, is held by every one of its roots, so the wait lives while any of them does. Lift a
cell you own with one you were handed, and the wait stays after the owner of the handed cell
drops it, for as long as yours lives. A switch is held by the outer cell and by the inner
stream or cell that it selects now, so `Cell.Constant(s).SwitchS().ListenOnceAsync()` waits for
as long as `s` lives. The memory tests pin this for a lift, a merge, and a switch.

When nothing can fire the stream anymore, because every one of those sources is unreachable,
the wait goes away with them. The task then never completes, and the code after the `await`
never runs. No exception arrives, and a `finally` block in that method does not run. A token
changes this: while a token that can cancel is reachable, it holds the wait and the stream, and
a cancellation ends the wait with an `OperationCanceledException`.

F#'s `listenOnceAsync` returns an `Async`, which might never run, so it splits the
difference. The listener attaches as soon as you call it, so a firing before you start the
async is not lost, but until the async runs, the async value itself is the only thing
holding the listener. Once it runs, the stream holds the listener until the first firing or
a cancellation. An async that never runs — say, because a cancellation stopped the
workflow before it got there — leaves nothing behind once it's garbage collected.

## The mistake that fails silently

```csharp
// Wrong: nothing holds the listener, so it can be collected and stop firing.
s.Map(x => x * 2).Listen(Console.WriteLine);

// Wrong for the same reason, and easier to write by accident.
s.Map(x => x * 2).ListenOnce(Console.WriteLine);
```

This compiles, runs, and works until a garbage collection happens, after which it silently
stops. The node holds your handler through a `WeakReference`, and the `IListener` from the call
is the only thing that keeps the handler alive. Drop it, and the handler becomes collectable. A
test usually ends before a collection, so it passes. A long session does not.

Hold the listener for exactly as long as you want the subscription to live. `ListenOnce` is the
same method with a shorter lifetime: hold its listener until the firing arrives, or call
`ListenOnceAsync`.

## When the framework gives you no teardown hook

Some UI frameworks discard a view without ever calling anything you can hook. A WPF
`UserControl` is the usual case: it goes out of use and is simply dropped, with no `Dispose`,
and `Unloaded` is unreliable enough that you cannot hang teardown off it.

The rule covers this case with no more work. Hold the listener in a field of the control:

```csharp
public partial class RateView : UserControl
{
    // Held so the subscription lives exactly as long as this control does.
    private readonly IListener listener;

    public RateView(RateViewModel vm)
    {
        this.InitializeComponent();
        this.listener = vm.Rate.Listen(r => this.RateText.Text = r.ToString());
    }
}
```

The stream side reaches the handler only through a `WeakReference`, so the view model does not
root the control. The field keeps delivery running for as long as the control is alive, and the
control becoming unreachable is what unsubscribes it — lifetime coupling without a teardown
hook. The control and its listener form a reference cycle between themselves, which the
collector reclaims as a unit.

Two things to be clear-eyed about. Teardown is **not deterministic**: between the control
falling out of use and the collection that reclaims it, the handler still fires. That is fine
when it only updates controls nobody is looking at, and not fine when the handler has effects
beyond the control — writing to a store, sending a command — which will keep happening for an
unbounded interval. And the pattern is only as good as the control's collectability: a CLR
event subscription to a long-lived object, a static, or a binding whose source outlives the
control will root the control and the listener with it, leaving a leak that now *looks* like it
handles lifetime correctly.

## Do not `Send` from inside a handler

The XML documentation on the listen methods is explicit: neither `StreamSinkExtensionMethods.Send`
nor `CellSinkExtensionMethods.Send` may be called from a handler, and doing so **throws** — the
reason given is that they are not meant to be used to build new primitives.

When you need a handler to feed another sink, defer it past the transaction boundary:

```csharp
IListener l = s.Listen(v => Transaction.Post(() => other.Send(v)));
```

Handlers also carry no thread guarantee — the docs say to make no assumptions about which
thread they run on, and not to block.

## The machinery behind a combinator

For ordinary subscriptions, `Listen` is all you need. It gives you a handle that holds the
stream, so the subscription lives exactly as long as you keep the handle. `ListenOnceAsync` has
no handle to give, so it roots its listener in the keep-alive set of each root the stream has,
until the first firing or a cancellation. The listener holds the stream in turn, so the two form
a cycle that the collector reclaims together when the roots go.
Neither permanently roots a stream by itself.

A combinator needs one more kind of subscription, and none of it is public. It binds *some
other* listener to *a chosen* stream's lifetime, and has it actively unlistened — disconnecting
its node from upstream — when that stream is collected, rather than merely becoming unreachable.
That is how a combinator keeps its own wiring alive. [`Switch`](switch.md) subscribes to the outer
cell to learn when the branch changes, and to whichever inner stream is currently selected; the
caller holds neither, only the stream `Switch` returns. Both subscriptions are therefore attached
to that returned stream, so they live exactly as long as the result does. `Operational`,
`LoopedStream` and `TimerSystem` do the same thing.

To stop those listeners after the stream is gone, the library keeps a weak view of each one, which
can unlisten it without holding the stream. The inner subscription of `Switch` is replaced on every
branch change, but attaching a fresh listener each time would grow the stream's attached-listener
list without bound, so one listener whose target can be re-pointed is attached once.

Application code never gets into that situation, which is why this machinery is internal rather
than merely discouraged: building a primitive this way needs the `Listen` overload taking a
node, which is `internal`, and a handler that sends, which throws. `Switch` has already done it
for you. Earlier versions exposed parts of it — `AttachListener`, the weak view through
`GetListenerWithWeakReference`, `MutableListener`, and `Cleanup` — and 6.0.0 removed the last of
them from the public surface.

## Verifying it

These invariants are enforced by `SodaFlow.Tests.Memory`, which asserts them using weak
references, the node's listener set, and the manager's registry count — no profiler required
for most of them. If you change cleanup behavior, those tests are the specification.

A separate set in the same project counts live objects with dotMemory. Those are tagged
`[Ignore("Requires dotMemory.")]` so they skip on CI and run locally when you have it
installed.
