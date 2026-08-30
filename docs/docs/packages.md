---
title: Which package do I install?
---

# Which package do I install?

The .NET build publishes ten NuGet packages. Only two of them are things most people
install directly.

## The short answer

| You write | Install | And that is it |
| --- | --- | --- |
| C# | `SodaFlow` | Pulls in `SodaFlow.Core` and `SodaFlow.Functional` automatically. |
| F# | `SodaFlow.FSharp` | Pulls in `SodaFlow.Core` automatically. |

Add an async package **only** if you need to run `Task`-based work from a stream — see
[Asynchronous work](async.md). Add a bindable package **only** if you are writing a UI
view model and want cells and streams to reach XAML as `INotifyPropertyChanged`
properties and `ICommand`s — see [the samples](samples.md).

| You also need | Install |
| --- | --- |
| `MapAsync` in C# | `SodaFlow.Async` |
| `MapAsync` in F# | `SodaFlow.FSharp.Async` |
| Data binding in C# | `SodaFlow.Bindable.ObjectModel` |
| Data binding in F# | `SodaFlow.FSharp.Bindable.ObjectModel` |

## The full list

| Package | Assembly | Install directly? |
| --- | --- | --- |
| `SodaFlow` | `SodaFlow` | **Yes**, for C#. |
| `SodaFlow.FSharp` | `SodaFlow.FSharp` | **Yes**, for F#. |
| `SodaFlow.Core` | `SodaFlow.Core` | No — a dependency of both of the above. |
| `SodaFlow.Functional` | `SodaFlow.Functional` | Rarely — a dependency of `SodaFlow`. |
| `SodaFlow.Async` | `SodaFlow.Async` | Only if you need `MapAsync` in C#. |
| `SodaFlow.FSharp.Async` | `SodaFlow.FSharp.Async` | Only if you need `MapAsync` in F#. |
| `SodaFlow.Async.Core` | `SodaFlow.Core.Async` | No — a dependency of both async packages. |
| `SodaFlow.Bindable.ObjectModel` | `SodaFlow.Bindable.ObjectModel` | Only for data binding in C#. |
| `SodaFlow.FSharp.Bindable.ObjectModel` | `SodaFlow.FSharp.Bindable.ObjectModel` | Only for data binding in F#. |
| `SodaFlow.Bindable.ObjectModel.Core` | `SodaFlow.Core.Bindable.ObjectModel` | No — a dependency of both bindable packages. |

## Why `SodaFlow` and `SodaFlow.Core` are separate

`SodaFlow.Core` is the engine. It declares the types you actually hold — `Stream<T>`,
`Cell<T>`, `Behavior<T>`, `Transaction`, the listener interfaces — along with the transaction
machinery and dependency graph that make updates atomic. It is language-neutral.

`SodaFlow` and `SodaFlow.FSharp` are thin language surfaces over that engine. C# gets
extension methods and static factory classes; F# gets modules of curried, pipeline-friendly
functions. Both are wrappers around the *same* core types, which is why a `Stream<T>` created
in a C# library can be consumed by F# code and vice versa.

You never reference `SodaFlow.Core` directly, because you would get types with no usable
operations on them — the core exposes its real work through `internal` members that the
wrappers reach via `InternalsVisibleTo`.

`SodaFlow.Functional` is a separate concern: it holds `Maybe<T>`, `Either<T1,T2>` and `Unit`,
the small functional vocabulary the C# API needs and C# does not ship with. It has no FRP in
it and can be used on its own. F# already has `option`, `Result` and `unit`, which is why
`SodaFlow.FSharp` does not depend on it.

## The bindable object model

The bindable packages are the bridge to XAML. A cell or a sink goes in, and what comes out is
an object that raises `INotifyPropertyChanged` for its `Value` property, or an `ICommand`, so
WPF and Avalonia can bind to a SodaFlow graph without either side knowing about the other.

| You have | You call | You bind to |
| --- | --- | --- |
| `Cell<T>` | `ToOneWay()` / `oneWay` | `IOneWayBindableValue<T>` — read-only |
| `Cell<T>` plus a `StreamSink<T>` | `ToTwoWay()` / `twoWay` | `ITwoWayBindableValue<T>` — reads and writes |
| `StreamSink<T>` | `ToOneWayToSource()` / `oneWayToSource` | `IOneWayToSourceBindableValue<T>` — write-only |
| `StreamSink<T>` | `ToBindableAction()` / `toBindableAction` | `IBindableAction` — an `ICommand` |

Bind to `SomeProperty.Value`, not `SomeProperty`: a bindable value is an object whose `Value`
changes, not the value itself. Bindable actions are the exception, being `ICommand`
implementations already.

Updates reach the UI through an `IBindingScheduler`, resolved when a bindable is constructed:
an explicitly passed scheduler wins, then a process-wide `BindingScheduler.Default`, then the
`SynchronizationContext` of the constructing thread, and failing all of those handlers run
inline. A view model built on the UI thread therefore needs no configuration at all.

Bindables may be constructed on any thread, so a view model never has to know which thread the
binding engine uses — that is what lets it stay ignorant of the UI entirely. What it does need
is for one of those to be resolvable: if construction happens somewhere with no
`SynchronizationContext` to capture, set `BindingScheduler.Default` during startup.
`BindingScheduler.Immediate` runs handlers inline, for tests and headless hosts.

## Versioning

Each package versions independently from its own git tag prefix, via
[MinVer](https://github.com/adamralph/minver). Pushing `sodaflow-async-1.1.0` releases
`SodaFlow.Async` at 1.1.0 and leaves every other package exactly where its own last tag put
it. Do not expect the ten version numbers to move together — they are not meant to.

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

A package with no tag on the commit being built keeps the version its last tag gave it, so a
release only publishes what actually moved.

Independent numbering does not mean the packages are independently *installable*, though.
Every wrapper — `SodaFlow` and `SodaFlow.FSharp`, all three async assemblies and all three
bindable ones — reaches `SodaFlow.Core` through `InternalsVisibleTo`, so a change to those
internals binds them to one particular core even when no public API moved. When that happens
the whole group takes a major version together — the emitted NuGet dependency is a minimum
version rather than a range, so nothing but the version number stops an old wrapper being
resolved against a newer core.

## Supported frameworks

Every package multi-targets `net472`, `net6.0` and `netstandard2.0`.
