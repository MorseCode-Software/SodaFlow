---
title: Which package do I install?
---

# Which package do I install?

The build publishes a package per language surface, per add-on, and per shared core. Only two of
them are ones most people install directly.

## The short answer

| You write | Install | And that is it |
| --- | --- | --- |
| C# | `SodaFlow` | Pulls in `SodaFlow.Core` and `SodaFlow.Functional` automatically. |
| F# | `SodaFlow.FSharp` | Pulls in `SodaFlow.Core` automatically. |

Add an async package **only** if you need to run `Task`-based work from a stream — see
[Asynchronous work](async.md). Add a bindable package **only** if you are writing a UI
view model and want cells and streams to reach XAML as `INotifyPropertyChanged`
properties and `ICommand`s — see [Data binding](bindable.md). Add a collections package
**only** if you have a large keyed collection and want to observe individual items without
every observer waking for every edit — see [Reactive collections](collections.md).

| You also need | Install |
| --- | --- |
| `MapAsync` in C# | `SodaFlow.Async` |
| `MapAsync` in F# | `SodaFlow.FSharp.Async` |
| Data binding in C# | `SodaFlow.Bindable.ObjectModel` |
| Data binding in F# | `SodaFlow.FSharp.Bindable.ObjectModel` |
| Reactive collections in C# | `SodaFlow.Collections` |
| Reactive collections in F# | `SodaFlow.FSharp.Collections` |

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
| `SodaFlow.Collections` | `SodaFlow.Collections` | Only for reactive collections in C#. |
| `SodaFlow.FSharp.Collections` | `SodaFlow.FSharp.Collections` | Only for reactive collections in F#. |
| `SodaFlow.Collections.Core` | `SodaFlow.Core.Collections` | No — a dependency of both collections packages. |

## Why `SodaFlow` and `SodaFlow.Core` are separate

`SodaFlow.Core` is the engine. It declares the types you actually hold — `Stream<T>`,
`Cell<T>`, `Behavior<T>`, `Transaction`, the listener interfaces — along with the transaction
machinery and dependency graph that make updates atomic. It is language-neutral.

`SodaFlow` and `SodaFlow.FSharp` are thin language surfaces over that engine. C# gets
extension methods and static factory classes; F# gets modules of curried, pipeline-friendly
functions. Both are wrappers around the *same* core types, which is why a `Stream<T>` created
in a C# library can be consumed by F# code and vice versa.

You never reference `SodaFlow.Core` directly, because you would get types with no usable
operations on them. The wrappers supply those operations.

`SodaFlow.Functional` is a separate concern: it holds `Maybe<T>`, `Either<T1,T2>` and `Unit`,
the small functional vocabulary the C# API needs and C# does not ship with. It has no FRP in
it and can be used on its own. F# already has `option`, `Result` and `unit`, which is why
`SodaFlow.FSharp` does not depend on it. The collections packages follow the same rule one layer
down: `SodaFlow.Collections` adds `Maybe<T>` and `SodaFlow.FSharp.Collections` adds `option` over a
shared core that has no optional type, so the F# package brings no `Maybe<T>` with it.

## The bindable object model

The bindable packages are the bridge to XAML. A cell or a sink goes in, and what comes out is
an object that raises `INotifyPropertyChanged` for its `Value` property, or an `ICommand`, so
WPF and Avalonia can bind to a SodaFlow graph without either side knowing about the other.

Every bindable comes from an `IBindableFactory`, which holds the scheduler that all of them use.

| You have | You call (C# / F#) | You bind to |
| --- | --- | --- |
| `Cell<T>` | `CreateOneWay` / `ToOneWay` | `IOneWayBindableValue<T>` — read-only |
| `CellSink<T>`, or a `Cell<T>` plus a `StreamSink<T>` | `CreateTwoWay` / `ToTwoWay` | `ITwoWayBindableValue<T>` — reads and writes |
| `StreamSink<T>` | `CreateBindableAction` / `ToBindableAction` | `IBindableAction` — an `ICommand` |

Bind to `SomeProperty.Value`, not `SomeProperty`: a bindable value is an object whose `Value`
changes, not the value itself. Bindable actions are the exception, being `ICommand`
implementations already.

Updates reach the UI through the `IBindingScheduler` the factory was built with. Build one
factory at startup, on the UI thread, with `SynchronizationContextBindingScheduler.Capture()`,
and hand it to each view model; the thread a view model is built on then changes nothing.
There is no process-wide default and no fallback. `BindingScheduler.Immediate` runs handlers on
the calling thread, for tests and headless hosts: at once with no transaction open, or as soon
as the open transaction ends.

## Versioning

Each package has its own version, and a release of one leaves the others where they were. Do not
expect the version numbers to move together — they are not meant to.

Independent numbering does not mean the packages are independently *installable*, though. Every
wrapper — `SodaFlow` and `SodaFlow.FSharp`, and the async, bindable, and collections packages —
uses the internals of `SodaFlow.Core`, so a change to those internals binds it to one particular
core even when no public API moved. Upgrade a wrapper and the core packages under it together.
When a change binds the wrappers to a new core, the whole group takes a major version together.
A package depends on each package of this repository from the version it was built against up to,
but not including, that package's next major, so a wrapper built against 6.1.0 of a dependency
declares `[6.1.0, 7.0.0)`. NuGet never resolves past that bound on its own. A project that
references a newer major directly still gets it, with warning NU1608, which a build that treats
warnings as errors fails on.

## Supported frameworks

Every package multi-targets `net472`, `net6.0`, `net8.0`, and `netstandard2.0`. A project on .NET 8
or later gets the `net8.0` build, and a project on .NET 6 or .NET 7 gets the `net6.0` build.

### Trimming and native AOT

The `net8.0` builds of the C# packages are marked as compatible with trimming and native AOT. The
other builds aren't marked, because the .NET trim and AOT analyzers don't exist for them.

The F# packages aren't marked. The analyzers don't run on F#, and a native AOT publish that roots
the F# packages reports warnings inside FSharp.Core.

The analyzers can't see everything the native AOT compiler checks. The compiler builds the code of
every generic type before the app runs. Where a type nests inside itself with no limit the compiler
can prove, it stops at a fixed depth and reports IL3054, and code that needed an expansion past
that depth throws at run time.

So the build also publishes a small app with native AOT, treats every warning from the compiler as
an error, and runs the result. The app uses the stream and cell operations and sorts a collection
by more levels than that depth. It covers only what it calls, so read the output of your own
publish for IL3054 as well.

Native AOT also has a cost that no warning reports. An order with two levels makes the compiler
build one type for each pairing of the sort value types an app uses, so the publish slows down as
an app sorts by more value types. [Views](collections.md#views) has the measurements and a way to
keep the number down.

A publish that roots a whole assembly is a different matter. Rooting `SodaFlow.Core` with
`TrimmerRootAssembly` still reports IL3054, because it makes the compiler build every method of
every generic type, including combinations that no app calls.
