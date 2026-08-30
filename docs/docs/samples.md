---
title: Sample applications
---

# Sample applications

Two applications live in [`samples/`](https://github.com/MorseCode-Software/SodaFlow/tree/main/samples) in the repository, each built twice — once in
WPF and once in Avalonia — over one shared view model that knows about neither.

| Sample | Uses | What it shows |
| --- | --- | --- |
| [Counter](https://github.com/MorseCode-Software/SodaFlow/tree/main/samples/Counter) | `SodaFlow`, `SodaFlow.Bindable.ObjectModel` | The whole idea on one screen |
| [Search](https://github.com/MorseCode-Software/SodaFlow/tree/main/samples/Search) | those two plus `SodaFlow.Async` | Search-as-you-type against a slow service |

Each is a folder with its own solution: open `Counter/SodaFlow.Samples.Counter.slnx` or
`Search/SodaFlow.Samples.Search.slnx` and run either head.

## Why the view model is its own project

Each sample has three projects: a `netstandard2.0` view model holding the FRP graph, and two
UI heads that are almost entirely XAML.

```
Counter/
  SodaFlow.Samples.Counter.ViewModels/   netstandard2.0 - the graph. No UI reference.
  SodaFlow.Samples.Counter.Wpf/          net8.0-windows - XAML and about ten lines of C#
  SodaFlow.Samples.Counter.Avalonia/     net8.0         - the same, in Avalonia
```

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

## Two things that trip people up

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
<PackageReference Include="SodaFlow" Version="2.0.0" />
<PackageReference Include="SodaFlow.Async" Version="2.1.0" />
<PackageReference Include="SodaFlow.Bindable.ObjectModel" Version="1.0.0" />
```

Project references into `src/` would have been easier to set up and worse to live with: a sample
would then break the moment anyone changed a library API. Pinning to released versions means a
sample moves when someone deliberately bumps a version, and until then it keeps demonstrating a
combination that is actually installable — which also makes the samples a real test of the
published packages rather than of the working tree.
