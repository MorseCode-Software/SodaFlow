# Samples

Two applications, each built twice — once in WPF and once in Avalonia — over one shared view
model that knows about neither.

| Sample | Libraries it uses | What it shows |
| --- | --- | --- |
| [Counter](Counter) | `SodaFlow`, `SodaFlow.Bindable.ObjectModel` | The whole idea on one screen |
| [Search](Search) | those two plus `SodaFlow.Async` | Search-as-you-type against a slow service |

Each sample is a folder with its own solution. Open
`Counter/SodaFlow.Samples.Counter.slnx` or `Search/SodaFlow.Samples.Search.slnx` and run either
head.

## Layout

Each sample has three projects:

```
Counter/
  SodaFlow.Samples.Counter.ViewModels/   netstandard2.0 - the FRP graph. No UI reference.
  SodaFlow.Samples.Counter.Wpf/          net8.0-windows - XAML and about ten lines of C#
  SodaFlow.Samples.Counter.Avalonia/     net8.0         - the same, in Avalonia
```

The split is the point. A SodaFlow view model is built from cells, streams and bindables, none of
which come from a UI framework, so the view model project targets `netstandard2.0` and references
no UI package at all. If it ever needed one to compile, the claim would be empty.

Read the view model first. The two heads are almost entirely XAML, and reading them side by side
shows how little of an application has to know which framework it is running on.

## Two things that trip people up

**Bind to `SomeProperty.Value`, not `SomeProperty`.** A bindable property is an object that raises
`PropertyChanged` for `"Value"`. Binding to the property itself shows a type name. Commands are
the exception: they are `ICommand` implementations, so `Command="{Binding Reset}"` is correct.

**WPF needs `UpdateSourceTrigger=PropertyChanged` on a two-way `TextBox`.** Without it WPF writes
the source only when the box loses focus, so nothing reaches the graph while the user types.
Avalonia writes on every keystroke by default. The Search sample shows both.

## They reference packages, not the source next door

Every sample takes its dependencies from nuget.org, at a pinned version, exactly as an
application outside this repository would:

```xml
<PackageReference Include="SodaFlow" Version="2.0.0" />
<PackageReference Include="SodaFlow.Async" Version="2.1.0" />
<PackageReference Include="SodaFlow.Bindable.ObjectModel" Version="1.0.0" />
```

Project references into `src/` would have been easier to set up and worse to live with: a sample
would then break the moment anyone changed a library API, and it would break in whatever branch
that work was happening in. Pinning to released versions means a sample moves when someone
deliberately bumps the version here, and until then it keeps demonstrating a combination that is
actually installable.

It also means the samples are a real test of the published packages rather than of the working
tree, which is the only place a mistake in the package metadata itself will show up.
