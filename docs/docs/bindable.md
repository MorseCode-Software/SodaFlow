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

// The view model's properties, built by the factory it was given.
IOneWayBindableValue<int> countValue = factory.CreateOneWay(count);
IBindableAction incrementCommand = factory.CreateBindableAction(increment);
IBindableAction resetCommand = factory.CreateBindableAction(reset, count.Map(n => n != 0));
```

Notice what is absent: no `count` field, no `OnPropertyChanged("Count")`, and nothing that
re-checks whether Reset should be enabled. Enablement is just another cell, so the command
follows the cell and the cell follows the count. They cannot disagree.

## What you can turn into a bindable

Every bindable comes from an `IBindableFactory`; there is no other way to build one.

| You have | You call | You get | Direction |
| --- | --- | --- | --- |
| `Cell<T>` | `CreateOneWay(cell)` | `IOneWayBindableValue<T>` | Graph to view |
| `CellSink<T>` | `CreateTwoWay(sink)` | `ITwoWayBindableValue<T>` | Both |
| `Cell<T>` + `StreamSink<T>` | `CreateTwoWay(cell, editsStreamSink)` | `ITwoWayBindableValue<T>` | Both |
| `StreamSink<T>` + initial value | `CreateOneWayToSource(editsStreamSink, initialValue)` | `IOneWayToSourceBindableValue<T>` | View to graph |
| `CellSink<T>` | `CreateOneWayToSource(sink)` | `IOneWayToSourceBindableValue<T>` | View to graph |
| `StreamSink<T>` | `CreateBindableAction(sink)` | `IBindableAction<T>` | An `ICommand` |

Two `CreateTwoWay` overloads exist because there are two situations. A `CellSink<T>` is the simple
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

`CreateBindableAction` turns a `StreamSink<T>` into an `ICommand`. The `CommandParameter` is
carried through to the stream, and enablement comes from an optional `Cell<bool>` — omit it
and the command is always enabled. `CanExecuteChanged` is raised for you when that cell
changes; nothing raises it by hand.

A disposed action reports `CanExecute` as false, so a torn-down view model cannot be invoked
through a stale binding.

For a `StreamSink<Unit>` the parameterless overload wins overload resolution and yields the
non-generic `IBindableAction`. Write `CreateBindableAction<Unit>(...)` explicitly if you want the
parameterized form for a unit sink. A `StreamSink<Maybe<T>>` gives a command whose
`CommandParameter` can be null, a `T`, or a `Maybe<T>`.

## The binding scheduler

Notifications have to reach the UI on the UI thread. An `IBindingScheduler` does that, and the
factory holds the one that every bindable it builds uses. `BindableFactory` requires one:

```csharp
// At startup, on the UI thread.
IBindableFactory factory = new BindableFactory(SynchronizationContextBindingScheduler.Capture());
```

Hand that factory to each view model, through its constructor. A bindable never picks a
scheduler for itself, so the thread a view model happens to be built on changes nothing: build
it on any thread, and its notifications still reach the thread the factory was given.

There is no process-wide default and no fallback. An earlier version captured the constructing
thread's `SynchronizationContext` and, when there was none, ran handlers inline without a word,
so a view model built on a background thread raised `PropertyChanged` off the UI thread and the
binding engine failed somewhere far from the cause. Requiring the scheduler up front moves that
failure to the one line that builds the factory.

`BindingScheduler.Immediate` runs handlers inline on purpose, which is what tests and headless
hosts want.

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

Because a view model takes its `IBindableFactory` through its constructor, a test builds it with
`new BindableFactory(BindingScheduler.Immediate)` in place of the real one. Without that
substitution, assertions would race the UI thread's message pump. `IBindableFactory` is an
interface, so a test can also supply its own.

## F#

The F# package builds bindables the same way, through an `IBindableFactory`:

```fsharp
open SodaFlow
open SodaFlow.Bindable.ObjectModel

let factory =
    BindableFactory(SynchronizationContextBindingScheduler.Capture()) :> IBindableFactory

let incrementSink = sinkS<unit> ()
let count = incrementSink |> accumS 0 (fun _ n -> n + 1)

let countBindable = factory.ToOneWay count
let increment = factory.ToBindableAction incrementSink
```

`ToOneWay`, `ToTwoWay`, `ToOneWayToSource` and `ToBindableAction` mirror the C# members and take
an optional comparer or enablement cell. `ToBindableOptionAction` is the command whose
`CommandParameter` can be null, a `'T`, or a `'T option`.

> [!NOTE]
> The generated [API reference](../api/index.md) carries the full parameter contract for every
> C# overload. It does not cover `SodaFlow.FSharp.Bindable.ObjectModel`: DocFX renders an F#
> assembly in its compiled shape, which misleads more than it helps, so the F# packages are
> deliberately absent from it. See [F# and the API reference](fsharp-api.md). The F# functions
> are documented in their own source.
