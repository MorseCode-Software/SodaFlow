---
title: Data binding
---

# Data binding

A XAML binding engine wants `INotifyPropertyChanged` properties and `ICommand`s. A SodaFlow
graph has cells and streams. The bindable object model is the bridge: a cell or a sink goes
in, and what comes out is an object WPF or Avalonia can bind to, with neither side knowing
about the other.

That is what lets a view model be a plain `netstandard2.0` library with no UI reference at
all — see [the samples](samples.md), where exactly one view model drives both a WPF and an
Avalonia head.

It lives in a separate package; see [Which package do I install?](packages.md).

```bash
dotnet add package SodaFlow.Bindable.ObjectModel
```

## The shape of it

```csharp
using SodaFlow;
using SodaFlow.Bindable.ObjectModel;

StreamSink<Unit> increment = Stream.CreateSink<Unit>();
StreamSink<Unit> reset = Stream.CreateSink<Unit>();

Stream<Func<int, int>> edits = new[]
{
    increment.MapTo<Unit, Func<int, int>>(n => n + 1),
    reset.MapTo<Unit, Func<int, int>>(_ => 0)
}.OrElse();

Cell<int> count = edits.Accum(0, (edit, n) => edit(n));

// The view model's properties.
IOneWayBindableValue<int> countValue = count.ToOneWay();
IBindableAction incrementCommand = increment.ToBindableAction();
IBindableAction resetCommand = reset.ToBindableAction(count.Map(n => n != 0));
```

Notice what is absent: no `count` field, no `OnPropertyChanged("Count")`, and nothing that
re-checks whether Reset should be enabled. Enablement is just another cell, so the command
follows the cell and the cell follows the count. They cannot disagree.

## The four bindables

| You have | You call | You get | Direction |
| --- | --- | --- | --- |
| `Cell<T>` | `ToOneWay()` | `IOneWayBindableValue<T>` | Graph to view |
| `CellSink<T>` | `ToTwoWay()` | `ITwoWayBindableValue<T>` | Both |
| `Cell<T>` + `StreamSink<T>` | `ToTwoWay(editsStreamSink)` | `ITwoWayBindableValue<T>` | Both |
| `StreamSink<T>` + initial value | `ToOneWayToSource(initialValue)` | `IOneWayToSourceBindableValue<T>` | View to graph |
| `StreamSink<T>` | `ToBindableAction()` | `IBindableAction<T>` | An `ICommand` |

Two `ToTwoWay` overloads exist because there are two situations. A `CellSink<T>` is the simple
one: the view is the only writer, and the sink is authoritative. Passing a `Cell<T>` and a
separate `StreamSink<T>` is for when the graph computes the value but view edits have to enter
as their own stream — validation, normalization, or any other rule between the edit and the
value.

Each also takes an optional `IEqualityComparer<T>`, used to suppress no-op writes.

Every bindable implements `IBindable`, which is just `IDisposable`. Reading bindables expose
their backing `Cell<T>`, and bindable actions expose `IsEnabledCell` and `FiringsStream`, so a
bindable can be composed back into the graph rather than being a dead end.

## Bind to `.Value`, not to the property

A bindable value is an *object whose `Value` changes*, not the value itself. The property name
raised on `PropertyChanged` is always `"Value"`, so the binding path carries it:

```xml
<TextBlock Text="{Binding Count.Value}" />
<Button Command="{Binding Increment}" />
```

Binding to `{Binding Count}` shows a type name instead. Commands are the exception — they are
`ICommand` implementations already, so they bind directly.

> [!NOTE]
> WPF needs `UpdateSourceTrigger=PropertyChanged` on a two-way `TextBox`, or it writes the
> source only when the box loses focus and nothing reaches the graph while the user types.
> Avalonia writes on every keystroke by default.

## Two-way writes are optimistic

The graph stays authoritative, but a setter that waited for a round trip would fight the
user's caret. So the setter updates its cached value immediately — the binding engine reads
back exactly what it wrote — and then pushes the value into the graph. Once the graph settles,
a reconciliation pass samples the cell and corrects the cached value if the graph changed or
rejected the write.

The visible consequence is that an input mask which upper-cases text, or a validation rule
which discards a value, corrects the view a moment after the keystroke rather than blocking
it. Setting a value the comparer considers unchanged does nothing at all.

## Commands

`ToBindableAction` turns a `StreamSink<T>` into an `ICommand`. The `CommandParameter` is
carried through to the stream, and enablement comes from an optional `Cell<bool>` — omit it
and the command is always enabled. `CanExecuteChanged` is raised for you when that cell
changes; nothing raises it by hand.

A disposed action reports `CanExecute` as false, so a torn-down view model cannot be invoked
through a stale binding.

For a `StreamSink<Unit>` the parameterless overload wins overload resolution and yields the
non-generic `IBindableAction`. Write `ToBindableAction<Unit>(...)` explicitly if you want the
parameterized form for a unit sink.

## The binding scheduler

Notifications have to reach the UI on the UI thread. An `IBindingScheduler` does that, and one
is resolved when each bindable is constructed, in this order:

1. a scheduler passed explicitly to the call;
2. the process-wide `BindingScheduler.Default`, if it has been set;
3. the `SynchronizationContext` of the thread doing the constructing;
4. failing all of those, handlers run inline.

A view model built on the UI thread therefore needs no configuration at all: step 3 finds the
context and everything works.

Bindables may be constructed on **any** thread — a view model never has to know which thread
the binding engine uses, which is the whole point of keeping it ignorant of the UI. What it
does need is for one of those four steps to produce the right answer. If your view models are
built somewhere with no `SynchronizationContext` to capture — a background thread, a custom UI
framework, a headless host — set `BindingScheduler.Default` during startup, or pass a
scheduler in. Otherwise step 4 silently dispatches inline, and updates reach the view on
whichever thread happened to send them.

`BindingScheduler.Immediate` runs handlers inline on purpose, which is what tests want.

## Disposal

Every bindable owns a subscription into the graph, which is why `IBindable` is `IDisposable`.
A view model typically keeps them in one list and disposes them together:

```csharp
private readonly IReadOnlyList<IBindable> bindables;

// ...

public void Dispose()
{
    foreach (IBindable b in this.bindables)
    {
        b.Dispose();
    }
}
```

That heterogeneous list is exactly what `IBindable` exists for. See
[Listener lifetimes](lifetimes.md) for why the subscription needs holding onto in the first
place.

## Testing, and injecting the scheduler

`BindableFactory` holds one scheduler and hands it to everything it creates, so a view model
can take an `IBindableFactory` through its constructor and a test can substitute
`BindingScheduler.Immediate` for the real one. Without that substitution, assertions would
race the UI thread's message pump.

## F#

The F# package exposes the same operations as a `Bindable` module, with the scheduler and
comparer as named variants rather than optional arguments:

```fsharp
open SodaFlow
open SodaFlow.Bindable.ObjectModel

let incrementSink = sinkS<unit> ()
let count = incrementSink |> accumS 0 (fun _ n -> n + 1)

let countBindable = count |> Bindable.oneWay
let increment = incrementSink |> Bindable.toBindableAction
```

`oneWay`, `twoWay`, `twoWayCS` (for a `CellSink`), `oneWayToSource`, `oneWayToSourceCS` and
`toBindableAction` each have `WithComparer`, `WithScheduler` and
`WithComparerAndScheduler` forms. `BindableFactory` is available too, taking an optional
scheduler.

> [!NOTE]
> The generated [API reference](../api/index.md) carries the full parameter contract for every
> C# overload. It does not cover `SodaFlow.FSharp.Bindable.ObjectModel`: DocFX renders an F#
> assembly in its compiled shape, which misleads more than it helps, so the F# packages are
> deliberately absent from it. See [F# and the API reference](fsharp-api.md). The F# functions
> are documented in their own source.
