---
title: Listener lifetimes
---

# Listener lifetimes

FRP graphs are held together by references, and getting those references wrong is the most
common way to break a Sodium program: listeners that die early stop firing, and listeners that
never die leak.

## Strong and weak

`Listen` returns an `IStrongListener`. It keeps the graph it observes alive, and it implements
`IDisposable`. Hold onto it for as long as you want the subscription to live, then either
`Unlisten()` or `Dispose()` it.

`ListenWeak` returns an `IWeakListener` instead. It does not keep the graph alive. Use it when
something else already owns the lifetime and you do not want the subscription to extend it.

`ListenOnce` unsubscribes itself after the first firing. `ListenOnceAsync` gives you the same
thing as a `Task<T>`, with an optional `CancellationToken`.

## The common mistake

```csharp
// Wrong: nothing holds the listener, so it can be collected and stop firing.
s.Map(x => x * 2).Listen(Console.WriteLine);
```

Assign it to a field with a lifetime matching the subscription you want:

```csharp
this.listener = s.Map(x => x * 2).Listen(Console.WriteLine);
```

## Composing cleanup

`AttachListener` ties a listener's lifetime to a stream, so tearing down the stream tears down
the listener with it. `MutableListener` lets you swap out what is being listened to while
keeping one stable handle. `Cleanup` and the `CleanupExtensionMethods` collect several
listeners into one disposable unit.

> [!NOTE]
> The invariants on this page are enforced by the `Sodium.Frp.Tests.Memory` test project, which
> asserts them with weak references, the node's listener set, and the manager's registry count.
> If you change cleanup behaviour, those tests are the specification. This page should grow to
> cover the transaction-boundary cases they exercise.
