---
title: Feedback loops
---

# Feedback loops

Some values depend on their own past. A running total, a state machine, a position updated by
velocity — each needs its previous value to compute its next one, which is a circular reference
that ordinary construction cannot express: you would have to pass the value to itself before it
exists.

Loops resolve this with a forward declaration. Declare a placeholder, build the graph that
refers to it, then say what it was a reference to.

## The functional form

This is the idiom to use. `Stream.Loop<T>()`, `Cell.Loop<T>()` and `Behavior.Loop<T>()` hand
your block the placeholder — a `LoopedStream<T>`, `LoopedCell<T>` or `LoopedBehavior<T>` — take
back the definition it returns, and close the loop for you, including the explicit transaction it
needs.

# [C#](#tab/csharp)

```csharp
StreamSink<int> s = Stream.CreateSink<int>();

// A running total: each firing adds to the total so far.
Stream<int> total = Stream.Loop<int>()
    .WithoutCaptures(l => s.Snapshot(l.Hold(0), (n, o) => n + o));

using (total.ListenStrong(Console.WriteLine))
{
    s.Send(1);   // 1
    s.Send(2);   // 3
    s.Send(3);   // 6
}
```

# [F#](#tab/fsharp)

```fsharp
let s = sinkS<int> ()

// A running total: each firing adds to the total so far.
let total =
    loopWithNoCapturesS (fun l -> s |> snapshotC (l |> holdS 0) (+))

use _ = total |> listenStrongS (printfn "%d")
s |> sendS 1   // 1
s |> sendS 2   // 3
s |> sendS 3   // 6
```

---

These samples use `ListenStrong` rather than `Listen` for one reason: `using` and `use` need
`IDisposable`, and only `IStrongListener` implements it. Everywhere the handle is simply held in
a variable, [`Listen`](lifetimes.md) is the one to reach for.

Read the C# version inside out: `l` is the placeholder for the very stream being defined — a
`LoopedStream<int>`, which is a `Stream<int>` and usable as one anywhere — `l.Hold(0)` turns it
into a cell of the running total starting at zero, and each firing of `s` snapshots that cell and
adds to it. The result *is* `l`, which is what `WithoutCaptures` resolves.

Where a placeholder has to be passed somewhere that wants the plain type, `AsStream`, `AsCell` and
`AsBehavior` say so without a cast.

## Capturing extra values

Sometimes the loop body produces something else you want alongside the looped value.
`WithCaptures` returns both:

```csharp
StreamSink<int> s = Stream.CreateSink<int>();

(Stream<int> total, Stream<int> doubled) = Stream.Loop<int>()
    .WithCaptures(l => (
        Stream: s.Snapshot(l.Hold(0), (n, o) => n + o),
        Captures: s.Map(v => 2 * v)));
```

`Stream` is what the loop resolves to; `Captures` is anything else you want out. In F# the
equivalent is `loopS`, which returns a struct tuple of the looped value and the captures, while
`loopWithNoCapturesS` is the shorthand when there is nothing extra.

## The explicit form

`StreamLoop<T>`, `CellLoop<T>` and `BehaviorLoop<T>` are the underlying mechanism, reached
through `Stream.CreateLoop<T>`, `Cell.CreateLoop<T>` and `Behavior.CreateLoop<T>`. Each derives
from the placeholder the block form hands out — `StreamLoop<T>` is a `LoopedStream<T>` — and adds
the one member that closes it, `Loop`. You will meet them in older code and in the book's
examples:

```csharp
Transaction.RunVoid(() =>
{
    StreamLoop<int> l = Stream.CreateLoop<int>();
    Stream<int> total = s.Snapshot(l.Hold(0), (n, o) => n + o);
    l.Loop(total);
});
```

Four rules apply, and the library enforces all four with exceptions:

| Mistake | Message |
| --- | --- |
| Creating a loop outside a transaction | `Loop must be created within an explicit transaction.` |
| Never calling `Loop` | `Loop was not looped.` |
| Calling `Loop` twice | `Loop was looped more than once.` |
| Closing it in a different transaction | `Loop must be looped in the same transaction that it was created in.` |

The functional form exists because it makes all four impossible, and it is the type system that
makes them so rather than a convention. What the block is handed is a `LoopedStream<T>`, which has
no `Loop` on it at all: there is nothing to call twice, nothing to leave uncalled, and nothing to
call in the wrong transaction. The placeholder cannot outlive the block, and the definition is what
that block returns rather than something a later statement has to remember to do.

So reach for `Loop` and write the explicit form only in the rare case where the loop cannot be
scoped in a block at all — where the placeholder and its definition are separated by something a
lambda cannot enclose. If what is being built is an object rather than a single value, that is not
one of those cases; see below.

## `Sample` inside a loop

Calling `Sample` on a looped cell while still constructing the loop asks for a value that does
not exist yet. Use `SampleLazy` (`sampleLazyC`) instead — it defers the read until the loop has
closed and there is something to read.

The same reasoning gives every operation that takes a starting value a deferred counterpart, for
when that starting value is itself read out of the loop: `Hold` has `HoldLazy`, `Accum` has
`AccumLazy`, `Collect` has `CollectLazy`, and `Cell.Constant` has `Cell.ConstantLazy`. Each takes
a `Lazy<T>` where the eager one takes a value, and the F# aliases follow the same rule
(`holdLazyS`, `accumLazyS`, `collectLazyS`).

The two go together more often than not, because a fold seeded from the collection it folds over
needs both:

```csharp
Cell<ImmutableHashSet<int>> drainable =
    accounts.ItemChangesStream.AccumLazy(
        // The keys already in the collection, read lazily because this is that collection.
        initialState: accounts.SnapshotCell.SampleLazy().Map(/* ... */),
        // The keys each change adds or removes.
        f: static (changes, keys) => /* ... */);
```

That is from the Accounts sample: the set it accumulates has to start from the accounts already
there, which is a read of the very collection whose changes it is folding.

## Forward references to a single value

A loop lets a cell be referred to before it exists. `ForwardReference` is the same idea with the
cell taken back out — for when what has to refer to itself is one value, not a series of them:

# [C#](#tab/csharp)

```csharp
Node node = ForwardReference<Node>.WithoutCaptures(
    reference => new Node(new Child(reference.AsCell())));
```

# [F#](#tab/fsharp)

```fsharp
let node = forwardReferenceWithNoCaptures (fun reference -> Node (Child reference))
```

---

The block is handed a `LoopedCell<Node>`, and `AsCell` passes it on as the `Cell<Node>` the
child wants — a cell that means nothing until the call returns and holds the finished node from
then on. It unties the knot two objects make when each needs the other at
construction, which otherwise forces one of them to be built half-formed and completed
afterward — with a settable member that has no business being settable once the graph is up.

`WithCaptures`, and `forwardReference` in F#, return anything else worth keeping from the
construction, the same way a loop does.

### An object under construction

This is the form to use whenever a factory builds an object whose own graph feeds it — a view
model whose buttons edit the collection its rows come from, say. Rather than declaring a loop per
stream, build the whole object inside one `ForwardReference<T>`, keep each stream or cell that has
to be looped in a private field of the object — or use the property it is already exposed through,
where it has one — and read it back off the looped object with `SwitchS` or `SwitchC`:

```csharp
public static IAccountsViewModel Create() =>
    Transaction.Run(static () =>
        ForwardReference<AccountsViewModel>.WithoutCaptures(static viewModelLoop =>
        {
            // The collection is fed from edits that do not exist until the rows do, and the rows
            // come from the collection. Edit is the collection's edit type, shortened here.
            Stream<Edit> depositsLoop = viewModelLoop.Map(static vm => vm.deposits).SwitchS();

            ReactiveCollection<int, AccountIdentity, AccountState> accounts =
                ReactiveCollection.Create(initialEntries: AccountSeed.Items, depositsLoop);

            // ... the rest of the graph, ending with the deposits the rows on the page send ...

            return new AccountsViewModel(rows: rows, deposits: deposits);
        }));
```

The cell the forward reference hands out holds the finished object, so `SwitchS` on a field of it
is the same stream the object ends up with. One reference stands in for every looped value the
object carries, which keeps the count of loops at one however many streams and cells feed back.
In F# the same shape is `forwardReferenceWithNoCaptures` with `switchS` or `switchC`.

The C# value type sits on `ForwardReference<T>` rather than on the methods, which is what leaves
the capture type free to be inferred — a lambda gives inference nothing to work from, and C#
does not allow only some of a method's type arguments to be given. F# needs none of that: both
types are inferred from the function, so neither is ever written.

Internally this is a cell loop closed with a constant cell, so the rule above applies unchanged:
reading the reference during construction asks a question that has no answer yet, and throws.

## Where this shows up

Accumulators are the common case, and `Accum` and `Collect` are loops with the plumbing already
done:

```csharp
Cell<int> total     = s.Accum(0, (v, acc) => v + acc);
Stream<string> outp = s.Collect(0, (v, st) => (ReturnValue: $"#{st}", State: st + 1));
```

Reach for those first. Write the loop by hand when the feedback path runs through logic that
`Accum` and `Collect` cannot express — typically when it passes through other cells, or through
a `Switch`.
