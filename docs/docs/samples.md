---
title: Sample applications
---

# Sample applications

Three applications live in [`samples/`](https://github.com/MorseCode-Software/SodaFlow/tree/main/samples) in the repository, each built twice — once in
WPF and once in Avalonia — over one shared view model that knows about neither.

| Sample | Uses | What it shows |
| --- | --- | --- |
| [Counter](https://github.com/MorseCode-Software/SodaFlow/tree/main/samples/Counter) | `SodaFlow`, `SodaFlow.Bindable.ObjectModel` | The whole idea on one screen |
| [Search](https://github.com/MorseCode-Software/SodaFlow/tree/main/samples/Search) | those two plus `SodaFlow.Async` | Search-as-you-type against a slow service |
| [Bounce](https://github.com/MorseCode-Software/SodaFlow/tree/main/samples/Bounce) | `SodaFlow`, `SodaFlow.Bindable.ObjectModel` | Continuous time: motion as a function of `Time` |

Each is a folder with its own solution: open `Counter/SodaFlow.Samples.Counter.slnx`,
`Search/SodaFlow.Samples.Search.slnx` or `Bounce/SodaFlow.Samples.Bounce.slnx` and run either
head.

## Why the view model is its own project

Each sample has three projects: a view model holding the FRP graph, targeting a framework with no
platform in it, and two UI heads that are almost entirely XAML.

```
Counter/
  SodaFlow.Samples.Counter.ViewModels/   netstandard2.0 - the graph. No UI reference.
  SodaFlow.Samples.Counter.Wpf/          net8.0-windows - XAML and about ten lines of C#
  SodaFlow.Samples.Counter.Avalonia/     net8.0         - the same, in Avalonia
```

Search and Bounce have the same shape on .NET 10: a `net10.0` view model, a `net10.0-windows` WPF
head and a `net10.0` Avalonia head, all at C# 14.

The split is the point rather than an accident of layout. A SodaFlow view model is built from
cells, streams and bindables, none of which come from a UI framework, so the view model project
can reference no UI package at all. If it needed one in order to compile, the claim would be
empty. Read the view model first; reading the two heads side by side shows how little of an
application has to know which framework it is running on.

## What each one is for

**Counter** is the smallest thing that is still a real application: a number, three buttons, and
a Reset that enables itself. The interesting part is what is absent — no `count` field, no
`OnPropertyChanged("Count")`, and nothing that has to remember to re-check whether Reset should
be enabled. Each button contributes a *function of the current count* rather than a number,
which is what lets Reset join the same stream as the other two.

**Search** is search-as-you-type against a deliberately slow service — the case that is
genuinely awkward to write by hand, and the one that uses all three libraries. It is built
around the bugs that are *not* reachable in it: an older reply overwriting a newer one, a
spinner that never stops because a canceled request never decremented a counter, and a stale
error left on screen after a later search succeeded. See
[Asynchronous work](async.md) for the mechanism.

**Bounce** is balls bouncing in a box, and the only one of the three about
[`Behavior<T>`](time.md) rather than about cells and streams. A ball's position is a function of
time rather than a number something keeps updating, which has two consequences worth seeing: the
instant it reaches a wall is a root of the quadratic, solved in advance and scheduled with `At`
rather than noticed by a frame; and the views hold no positions at all, asking where things are
once per frame and drawing them there. It has three scenes, from one ball on one axis up to balls
that can be picked up and thrown, where `SwitchB` chooses between following the pointer and
following physics.

It is worth seeing which half of it binds and which does not. The balls bind to nothing: they are
behaviors, read by sampling, and a behavior has no changes to raise `PropertyChanged` about. Which
scene is selected is an ordinary changing value, so it is an ordinary cell exposed as an ordinary
two-way bindable property - which is what lets the summary above the tabs be a function of the
selection, and lets the view model change the tab rather than only learn about it.

## Two things that trip people up

See [Data binding](bindable.md) for the full model.

**Bind to `SomeProperty.Value`, not `SomeProperty`.** A bindable property is an object that
raises `PropertyChanged` for `"Value"`; binding to the property itself shows a type name.
Commands are the exception — they are `ICommand` implementations, so `Command="{Binding Reset}"`
is correct.

**WPF needs `UpdateSourceTrigger=PropertyChanged` on a two-way `TextBox`.** Without it WPF
writes the source only when the box loses focus, so nothing reaches the graph while the user
types. Avalonia writes on every keystroke by default. The Search sample shows both.

## They reference packages, not the source next door

Every sample takes its dependencies from nuget.org at a pinned version, exactly as an
application outside this repository would:

```xml
<PackageReference Include="SodaFlow" Version="4.0.0" />
<PackageReference Include="SodaFlow.Async" Version="4.0.0" />
<PackageReference Include="SodaFlow.Bindable.ObjectModel" Version="3.0.0" />
```

That is Search, which uses all three. Counter and Bounce take the first and the third.

Project references into `src/` would have been easier to set up and worse to live with: a sample
would then break the moment anyone changed a library API. Pinning to released versions means a
sample moves when someone deliberately bumps a version, and until then it keeps demonstrating a
combination that is actually installable — which also makes the samples a real test of the
published packages rather than of the working tree.
