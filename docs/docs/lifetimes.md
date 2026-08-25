---
title: Listener lifetimes
---

# Listener lifetimes

FRP graphs are held together by references, and getting those references wrong is the most
common way to break a SodaFlow program. Listeners that die early stop firing, silently; listeners
that never die leak, also silently. Neither throws.

## Strong and weak

`Listen` returns an `IStrongListener`. It keeps the graph it observes alive, and it implements
`IDisposable`. Hold it for as long as you want the subscription to live, then `Unlisten()` or
`Dispose()` it — they do the same thing.

`ListenWeak` returns an `IWeakListener`. It does **not** keep the graph alive. Use it when
something else already owns the lifetime and you do not want the subscription extending it.

`ListenOnce` unsubscribes itself after the first firing. `ListenOnceAsync` gives you the same
thing as a `Task<T>`, with an optional `CancellationToken`.

## The common mistake

```csharp
// Wrong: nothing holds the listener, so it can be collected and stop firing.
s.Map(x => x * 2).Listen(Console.WriteLine);
```

This compiles, runs, and works — until a garbage collection happens, after which it silently
stops. Assign it to something whose lifetime matches the subscription you want:

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

## `AttachListener`

`AttachListener` ties a listener's lifetime to a stream, so the listener survives exactly as
long as the stream does and is disposed when the stream is collected:

```csharp
Stream<int> s = source.Map(f).AttachListener(someListener);
```

This is the tool for subscriptions created *inside* a graph that you do not hold a handle to —
particularly inside a [`Switch`](switch.md) branch, where the branch itself comes and goes.

## `MutableListener`

A `MutableListener` is an `IListener` whose target can be swapped while the handle stays
stable:

| Member | Purpose |
| --- | --- |
| `SetListener(l)` | Point it at a listener, disposing whatever it held. |
| `ClearListener()` | Release the current target, keeping the handle. |
| `Unlisten()` | Stop listening entirely. |
| `GetListenerWithWeakReference()` | A weak view of it. |

Use it when a long-lived object subscribes to a succession of short-lived sources — one field,
one lifetime, changing target.

## `Cleanup`

`Cleanup` runs arbitrary code when it is garbage collected:

```csharp
Cleanup c = new Cleanup(() => handle.Close());
```

The action fires when `c` becomes unreachable, which makes it a way to attach native or
unmanaged teardown to the lifetime of a piece of graph. `c.CleanupNow()` forces it immediately
rather than waiting for collection.

Note the shape of the guarantee: this is finalization, not deterministic disposal. It runs
*eventually*. For anything that must be released at a known moment, use `IDisposable` and
`CleanupNow()` explicitly.

## Verifying it

These invariants are enforced by `SodaFlow.Tests.Memory`, which asserts them using weak
references, the node's listener set, and the manager's registry count — no profiler required
for most of them. If you change cleanup behaviour, those tests are the specification.

A separate set in the same project counts live objects with dotMemory. Those are tagged
`[Ignore("Requires dotMemory.")]` so they skip on CI and run locally when you have it
installed.
