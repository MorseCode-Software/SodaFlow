# Working in this repository

SodaFlow is an FRP library — cells, streams and behaviors — plus add-ons and four sample
applications. `src/` holds the libraries and their tests; `samples/` holds the samples, each with
its own solution; `docs/` holds the documentation site that docfx publishes.

## How work reaches me

**Never commit to `main`.** Branch, commit, push, open a pull request, and let me review it. This
holds even for a one-line change, and even when I have just told you what to do — the review is the
point, not a formality.

- Branch off an up-to-date `origin/main`. Check first: the local `main` is often behind.
- Name branches the way the existing ones are: `samples/...`, `ci/...`, `release/...`, or a plain
  descriptive slug.
- Split the work into commits that can be reviewed on their own. A language-level change and a
  packaging change belong in separate commits even when they ship in one PR.
- Open the PR with `gh pr create`, then read CI through the PR tools rather than polling `gh`.

## Say what you checked, not what you assume

This is the part I care about most. A claim that something works means you ran it.

- Build what you changed: `dotnet build <solution> --configuration Release -warnaserror`. Warnings
  are errors everywhere here.
- Prefer a check that would actually fail. When a change is meant to preserve behaviour, capture the
  output before and after and diff them; when a function is replaced, test the replacement against
  the thing it replaced over the interesting inputs, not one happy path.
- Read numbers back out of the code rather than trusting prose — including prose you just wrote.
- If something is unverified, say so plainly. "Builds clean; I did not run it" is useful. Silence
  that implies more is not.
- Commit messages say what was checked and how. Look at the existing log for the register: prose,
  full sentences, and the reasoning behind the change rather than a restatement of the diff.

## Inspection is part of done

Every solution is held to one rule set, `src/SodaFlow.sln.DotSettings`, mirrored beside each sample
solution. Run it before pushing — CI will, and it fails on findings at any severity, hints included:

```bash
dotnet cake --target=Inspect-Code
```

```bash
dotnet cake --target=Inspect-Sample --sample=Accounts
```

- Fix findings rather than suppressing them. A `ReSharper disable` needs a reason next to it, and
  the reason has to be about the code and not about the inspection.
- The mirrored `.DotSettings` files must stay byte-identical to the canonical one —
  `Verify-Inspection-Settings` enforces it. A rule change carries every copy with it.
- Watch for findings your own change created: a `<see cref>` to an extension member does not
  resolve, and a `using` alias can go dead when an extraction absorbs its last use.

## FRP conventions

### Let the graph hold the state

Reach for SodaFlow before reaching for a field. In view model logic especially, mutable state that
something has to remember to update is the thing this library exists to remove. Derive values rather
than recomputing them on notification; fold over what changed rather than keeping a counter; gate in
the graph rather than trusting a caller.

- A value that changes is a `Cell`; something that happens is a `Stream`; a value defined at every
  instant is a `Behavior`. Pick by which of those it is, not by which is convenient.
- `Accum` and `Collect` are loops with the plumbing done. Use them before writing a loop by hand.
- Gate rules in the graph as well as on the command. A command's enablement is a copy posted to the
  binding thread and can trail the graph; a `Gate` is sampled in the transaction the edit lands in.

### Loops: prefer `ForwardReference`, then `Loop`, never `CreateLoop`

See `docs/docs/loops.md`, which is the authority.

1. **`ForwardReference<T>`** is the first choice whenever a factory builds an object whose own graph
   feeds it. Build the whole object inside one `ForwardReference<T>`, keep each looped stream or cell
   in a private field, and read it back off the looped object with `SwitchS` or `SwitchC`. One
   reference stands in for every looped value the object carries, however many there are.

2. **`Stream.Loop<T>()` / `Cell.Loop<T>()` / `Behavior.Loop<T>()`** — the scoped block form — for a
   single looped value. The block is handed a `LoopedStream<T>`, `LoopedCell<T>` or
   `LoopedBehavior<T>` and returns the definition; `WithoutCaptures` when that is all, `WithCaptures`
   when the block also produces something to keep.

3. **`Stream.CreateLoop<T>()` and friends**, returning `StreamLoop<T>` / `CellLoop<T>` /
   `BehaviorLoop<T>`, are the old explicit mechanism. Write one only where the placeholder and its
   definition cannot be enclosed in a single block. An object being built is never such a case.

The block forms exist because they make all four loop errors impossible. Do not reintroduce the form
that makes them possible again.

Inside a loop, `Sample` on a looped cell asks for a value that does not exist yet — use `SampleLazy`,
and `HoldLazy` where an initial value itself depends on the loop.

### View models: a static `Create`, a private constructor

Every view model here is built the same way, and new ones should be:

```csharp
public static IAccountsViewModel Create() =>
    Transaction.Run(static () =>
        ForwardReference<AccountsViewModel>.WithoutCaptures(static viewModelLoop =>
        {
            // ... build the whole graph ...
            return new AccountsViewModel(rows: rows, total: total, /* ... */);
        }));

private AccountsViewModel(
    IOneWayBindableValue<IReadOnlyList<IAccountRowViewModel>> rows,
    IOneWayBindableValue<string> total,
    /* ... */)
```

- `Create` returns the **interface**, not the class.
- The constructor is **private** and takes the finished graph. It assigns and does nothing else — no
  construction, no subscription, nothing that could observe a half-built object.
- One `Transaction.Run` for the whole construction, so everything starts from one instant.
- The view model owns what it built: collect the disposables in a list and dispose them all.
  `IDisposable` belongs on the interface when the thing has subscriptions to release.

## Design conventions

### Constructor injection and interfaces, so it can be tested

Depend on interfaces and take them in the constructor. Nothing should reach for a static, a service
locator, or a clock of its own.

- Views bind against the interface (`IAccountsViewModel`), never the class. That is what makes the
  bound surface discoverable and keeps `Create` off it — building a view model is not something a
  view does with one.
- Take the clock as `ITimerSystem<T>` rather than reading the machine's.
- Keep the surface honest: if a view has no business calling it, it does not go on the interface.

### Type it as precisely as it can be typed

Prefer more type parameters over fewer when the extra ones carry real information, even where it
costs verbosity.

- `ReactiveCollection<TKey, TIdentity, TState>` splits identity from state so the collection can know
  that a state edit cannot move an item in a view keyed on identity. That distinction only exists
  because it is in the type.
- A `KeyOrder` keeps its sort value's type to itself, which is what lets one cell hold orders that
  project an `int`, a `string` and a `long`.
- Use a `using` alias when a constructed generic is written repeatedly — and delete the alias when
  its last use goes.
- Reach for `Maybe<T>` rather than null, and for a record where a type is an immutable value.
- Name a domain predicate on the type it is about rather than repeating the expression.

## Code style

- C# 14 where the target allows it. The samples' view models and heads are `net10.0` /
  `LangVersion 14`; Counter's view model stays `netstandard2.0` / `LangVersion 10` so a .NET
  Framework head can reference it. Libraries under `src/` target `net472;net60;netstandard2.0`.
- Use C# 14 extension members where a type from another assembly wants members it cannot have. Do
  not use an extension where a plain member on our own type would do.
- Comments explain **why**, at length where the reasoning is not obvious, and are written as prose.
  This is the house style — match the density of the file you are in. A comment that restates the
  code is worse than none.
- Named arguments are used heavily. Follow the surrounding file.
- `this.` qualification throughout.
- Formatting lives in the `.DotSettings` files, not in `.editorconfig`, which carries whitespace
  hygiene only. Lines run to about 110 characters.
- **Preserve file encoding.** Several files carry a UTF-8 BOM. Scripted edits strip it silently —
  compare the first bytes against `HEAD` after any bulk rewrite. `.gitattributes` normalises to LF.

## Packages, lock files and samples

- Every project restores through a committed `packages.lock.json`. **A `PackageReference` change is
  not done until the regenerated lock file is committed with it.** `RestoreLockedMode` is on for CI
  only, so a missing lock file is a red build there and silence locally. Check with
  `CI=true dotnet restore <solution>`.
- Samples reference **published packages at a pinned version**, never the projects under `src/`.
  That is what keeps work in progress from breaking them and makes them a real test of what was
  published. Bumping a pinned version means restoring and committing the lock file it rewrites.
- Versions come from git tags via MinVer, one prefix per package, so packages release independently.
- Tests are TUnit under Microsoft.Testing.Platform; `global.json` pins the SDK.

## Documentation

Sample and docs pages are kept accurate, and they are the thing most likely to drift.

- **Do not bake a count into prose.** "The other two samples", "it has three scenes" and "three
  applications live in samples/" were all true once and all had to be fixed. Write the sentence
  without the number where the number is not the point.
- When you change code, check the pages that describe it — the sample's own README, `samples/README.md`
  and `docs/docs/`. A documented version, count or snippet is a claim; verify it against the source.
- `dotnet docfx docs/docfx.json --warningsAsErrors` is what CI runs; run it after touching
  `docs/`.
