---
title: Listener lifetimes
---

# Listener lifetimes

FRP graphs are held together by references, and getting those references wrong is the most
common way to break a SodaFlow program. Listeners that die early stop firing, silently; listeners
that never die leak, also silently. Neither throws.

## Strong and weak

`Listen` returns an `IStrongListener`. It keeps the graph it observes alive, and it implements
`IDisposable`. Hold it for as long as you need to be able to *end* the subscription, then
`Unlisten()` or `Dispose()` it — they do the same thing. Dropping the handle does not end the
subscription; see [below](#dropping-the-listener-listen-returns).

`ListenWeak` returns an `IWeakListener`. It does **not** keep the graph alive. Use it when
something else already owns the lifetime and you do not want the subscription extending it.

`ListenOnce` unsubscribes itself after the first firing. `ListenOnceAsync` gives you the same
thing as a `Task<T>`, with an optional `CancellationToken`.

## Dropping the listener `Listen` returns

```csharp
// Keeps firing — but nothing can stop it any more.
s.Map(x => x * 2).Listen(Console.WriteLine);
```

`Listen` roots the listener in the stream's keep-alive set, so discarding the return value
does **not** stop delivery. The handler keeps running until the stream itself is disposed or
collected. What you have lost is the handle: there is no longer any way to `Unlisten()`, so
the subscription overstays rather than dying quietly.

That is deliberate, and pinned by `ListenerIsKeptAliveWhileStillListening` in the memory
tests — a listener that silently stopped firing would be the worse failure of the two.

Keep the listener whenever you need to be able to stop it:

```csharp
this.listener = s.Map(x => x * 2).Listen(Console.WriteLine);
```

In a scoped context, `using` is the clearer form and is what the test suite uses throughout:

```csharp
using (result.Listen(@out.Add))
{
    s.Send(1);
    s.Send(2);
}
```

## The mistake that does fail silently

`ListenWeak` is the one where dropping the reference stops delivery:

```csharp
// Wrong: nothing holds the weak listener, so it can be collected and stop firing.
s.Map(x => x * 2).ListenWeak(Console.WriteLine);
```

This compiles, runs, and works — until a garbage collection happens, after which it silently
stops. The node holds your handler through a `WeakReference`, and the returned
`IWeakListener` is the only thing keeping it alive — `ListenWeak` deliberately does not root
that listener anywhere. Drop it and the handler becomes collectable.

Use `ListenWeak` only when something else owns the lifetime, and hold that reference for
exactly as long as you want the subscription to live.

## When the framework gives you no teardown hook

Some UI frameworks discard a view without ever calling anything you can hook. A WPF
`UserControl` is the usual case: it goes out of use and is simply dropped, with no `Dispose`,
and `Unloaded` is unreliable enough that you cannot hang teardown off it.

Here `Listen` is not merely awkward, it is the bug. It roots the listener in the source
stream's keep-alive set, so a view model that outlives the control ends up holding it:

```
ViewModel → stream → keep-alive set → listener → handler → captured `this` → UserControl
```

Nothing ever breaks that chain, and the control leaks for the lifetime of the view model.

`ListenWeak`, with the listener held in a field, inverts it:

```csharp
public partial class RateView : UserControl
{
    // Held so the subscription lives exactly as long as this control does.
    private readonly IWeakListener listener;

    public RateView(RateViewModel vm)
    {
        this.InitializeComponent();
        this.listener = vm.Rate.ListenWeak(r => this.RateText.Text = r.ToString());
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

The XML documentation on `Listen` is explicit: neither `StreamSinkExtensionMethods.Send` nor
`CellSinkExtensionMethods.Send` may be called from a handler, and doing so **throws** — the
reason given is that `Listen` is not meant to be used to build new primitives.

When you need a handler to feed another sink, defer it past the transaction boundary:

```csharp
IListener l = s.Listen(v => Transaction.Post(() => other.Send(v)));
```

Handlers also carry no thread guarantee — the docs say to make no assumptions about which
thread they run on, and not to block.

## Machinery you probably do not need

`AttachListener`, `MutableListener` and `Cleanup` are public, but they exist mainly so the
library can build its own primitives. `Operational`, `LoopedStream`, `TimerSystem` and the
`Switch` implementations all use them; nothing in the test suite uses them the way an
application would.

For ordinary subscriptions you do not need any of them. `Listen` already ties the listener's
lifetime to the stream's: it roots the listener in the stream's keep-alive set, and the
listener holds the stream in turn, so the two form a cycle that the collector reclaims together
once you drop your handle. A listener never permanently roots a stream by itself.

What `AttachListener` adds is the ability to bind *some other* listener to *a chosen* stream's
lifetime, and to have it actively unlistened — disconnecting its node from upstream — when that
stream is collected, rather than merely becoming unreachable. That matters when a subscription
is created inside a graph you hold no handle to, typically a [`Switch`](switch.md) branch that
comes and goes: tying it to the source stream would outlive the branch, so it is tied to the
branch instead. The XML documentation on `Listen` points here for that case.

`MutableListener` is an `IListener` whose target can be swapped (`SetListener`, `ClearListener`,
`Unlisten`) while the handle stays stable — for a long-lived object subscribing to a succession
of short-lived sources. `Cleanup` runs an action when it is collected, or immediately via
`CleanupNow()`, which is finalization rather than deterministic disposal: it runs *eventually*.

## Verifying it

These invariants are enforced by `SodaFlow.Tests.Memory`, which asserts them using weak
references, the node's listener set, and the manager's registry count — no profiler required
for most of them. If you change cleanup behaviour, those tests are the specification.

A separate set in the same project counts live objects with dotMemory. Those are tagged
`[Ignore("Requires dotMemory.")]` so they skip on CI and run locally when you have it
installed.
