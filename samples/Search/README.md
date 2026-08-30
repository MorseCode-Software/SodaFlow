# Search

Search-as-you-type against a deliberately slow service. This is the case that is genuinely awkward
to write by hand, which is why it is the sample that uses all three libraries.

Type a city. Type `fail` to see the error path. Type quickly to see one search supersede another.

## The bugs that are not here

Typing faster than the service responds means several searches are outstanding at once. The
familiar failures are:

- an older reply arriving last and overwriting a newer one
- a spinner that never stops, because a cancelled request never decremented a counter
- a stale error left on screen after a later search succeeded

None of them are reachable in
[`SearchViewModel.cs`](SodaFlow.Samples.Search.ViewModels/SearchViewModel.cs):

- `AsyncConcurrencyStrategy.SwitchLatest()` means a superseded search can never publish
- `IsRunning` is derived from the pipeline, not counted, so there is no `+= 1` to get out of step
- the error is cleared by the same stream that starts a search, so it cannot outlive its request

```csharp
Cell<string> error = failed
    .Map(e => e.Message)
    .OrElse(searches.MapTo(string.Empty))
    .Hold(string.Empty);
```

## Cancellation

`Catalogue.SearchAsync` honours its `CancellationToken`. That is what makes Cancel — and
supersession — stop work rather than merely discard its result. An operation that ignores the
token still runs to completion; cancelling then only means nothing is published.

Run either `SodaFlow.Samples.Search.Wpf` or `SodaFlow.Samples.Search.Avalonia`.
