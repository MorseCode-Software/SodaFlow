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

Run either `SodaFlow.Samples.Counter.Wpf` or `SodaFlow.Samples.Counter.Avalonia`.
