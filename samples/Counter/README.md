# Counter

The smallest thing that is still a real SodaFlow application: a number, three buttons, and a Reset
that enables itself.

Read [`CounterViewModel.cs`](SodaFlow.Samples.Counter.ViewModels/CounterViewModel.cs). It is about
twenty lines of graph, and the interesting part is what is missing from it — no `count` field, no
`OnPropertyChanged("Count")`, and nothing that has to remember to re-check whether Reset should be
enabled.

Each button contributes a *function of the current count* rather than a number, which is what lets
Reset join the same stream as the other two:

```csharp
Stream<Func<int, int>> edits = new[]
{
    increment.MapTo<Unit, Func<int, int>>(n => n + 1),
    decrement.MapTo<Unit, Func<int, int>>(n => n - 1),
    reset.MapTo<Unit, Func<int, int>>(_ => 0)
}.OrElse();

Cell<int> count = edits.Accum(0, (edit, n) => edit(n));
```

Everything else is a function of `count`: the label, and whether Reset is enabled. Because they
are derived rather than assigned, they cannot disagree with each other.

Run any of the three heads, which all bind the same view model:

- `SodaFlow.Samples.Counter.Wpf` — WPF on .NET 10
- `SodaFlow.Samples.Counter.Wpf.NetFramework` — the same WPF head on .NET Framework 4.8.1
- `SodaFlow.Samples.Counter.Avalonia` — Avalonia on .NET 10

The view model stays on `netstandard2.0` so that the .NET Framework head can reference it at all.
That head is a copy of the .NET 10 one with a different title, and running the two side by side is
the whole demonstration: the graph, the bindables and the commands do not care which runtime is
underneath them.
