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

## What a looped type lets you do

A looped stream is a `Stream<T>`, because a stream has no current value and so nothing can read it
too early. `AsStream` is the no-cast spelling where a `Stream<T>` is wanted.

A looped cell or behavior is the other way around. `LoopedCell<T>` and `LoopedBehavior<T>` are the
*base* types of `Cell<T>` and `Behavior<T>`: they carry every operation except `Sample`, because
the value does not exist until the loop closes, and the type is how the library says so. The
operators keep that fact: `l.Map(f)`, `c.Lift(l, f)`, `l.Calm()` and `l.SwitchC()` all give a
`LoopedCell`, while anything that gives a stream, or a cell whose initial value you supply such as
`l.Updates().Hold(0)`, gives the plain type. A plain cell converts to the looped type implicitly,
so a looped cell goes anywhere the library only listens or snapshots — `Snapshot`, `Gate`,
`Listen`, `Values`, `timers.At`, the bindable factory. Reading a looped value before the close is a
compile error, where it used to be an exception at run time. In F# the same operators live in the
`LoopedCell` and `LoopedBehavior` modules, with the `LC` and `LB` shorthand suffixes: `mapLC`,
`lift2LC`, `switchCLB`.

Two conversions go back. The first is the closer: a loop hands out a `LoopCloser` when it closes,
and `closer.Close(l)` (`closeLC closer l`, `closeLB closer b`) gives the plain type. The block form
passes the closer to a second function that converts the captures, as the next section shows, and
`Loop` in the explicit form returns it. The second is `AsCell()` and `AsBehavior()` (`asCellLC`,
`asBehaviorLB`), which assert the same thing with no closer in hand, at a read site the types
cannot check, such as a test that samples a cell a row exposes. They are not for a constructor: an
object built inside the block takes the looped type, as the forward reference section shows, and
a read before the close still throws.

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

A capture that is derived from the placeholder is a `LoopedCell` inside the block, and it has no
reason to stay one once the loop has closed. `WithCaptures` on `Cell.Loop`, `Behavior.Loop` and
`ForwardReference` takes a second function for that. It runs after the loop has closed, and it is
handed the captures and a `LoopCloser`, the one object that converts a looped value back:

# [C#](#tab/csharp)

```csharp
(Cell<int> total, (Cell<int> doubled, string label)) = Cell.Loop<int>()
    .WithCaptures(
        f: l => (
            Cell: s.Snapshot(l, (n, o) => n + o).Hold(0),
            Captures: (Doubled: l.Map(v => 2 * v), Label: "doubled")),
        close: (captures, closer) => (closer.Close(captures.Doubled), captures.Label));
```

# [F#](#tab/fsharp)

```fsharp
let struct (total, struct (doubled, label)) =
    loopWithLoopedCapturesC
        (fun l -> struct (s |> snapshotC l (+) |> holdS 0, struct (l |> mapLC ((*) 2), "doubled")))
        (fun (struct (d, label)) closer -> struct (d |> closeLC closer, label))
```

---

The closer exists only once the loop has closed, which is what makes the conversion sound: there is
no call that can come too early. It also reads the value it converts, so a capture that depends on
an enclosing loop that is still open throws at the call, with the message a read would give, rather
than at some later read. Convert that capture with the closer of the outer loop instead.

### A cell or stream of looped values

The closer converts one value. It has no overload for a `Cell<LoopedCell<T>>` or a
`Stream<LoopedCell<T>>`, because there is no sound way to retype a container without visiting its
elements: `Cell<T>` is a class, so it has no variance; the conversion goes from the base type to the
derived one, which is a downcast of every value the container will ever hold; and a bare placeholder
is not a `Cell<T>` at run time, so a reinterpreted reference to one would be wrong rather than
late. Visiting the elements is a `Map`, with a node of its own.

Such a container is almost always a sign that the placeholder was reached for from inside a lambda,
which puts the pending type on the inside:

```csharp
Cell<LoopedCell<int>> cells = plain.Map(v => l.Map(x => f(v, x)));   // inner cells read l
```

Build it the other way round and the pending type is on the outside, where the close function
converts it as the same object:

```csharp
LoopedCell<int> cell = l.Lift(plain, (x, v) => f(v, x));             // one cell, no container
LoopedCell<Cell<int>> cells = l.Map(x => plain.Map(v => f(v, x)));   // inner cells built from x
```

Where the inner cells are there to be followed, `SwitchC` on a `LoopedCell<LoopedCell<T>>`
collapses them to one `LoopedCell<T>` inside the block. An object that stores a cell, such as a
row view model built inside a `ForwardReference` block, takes and keeps the looped type, as
[Rows built inside the block](#rows-built-inside-the-block) shows.

## The explicit form

`StreamLoop<T>`, `CellLoop<T>` and `BehaviorLoop<T>` are the underlying mechanism, reached
through `Stream.CreateLoop<T>`, `Cell.CreateLoop<T>` and `Behavior.CreateLoop<T>`. Each is the
placeholder with the one member that closes it, `Loop`: `StreamLoop<T>` derives from
`LoopedStream<T>`, and `CellLoop<T>` and `BehaviorLoop<T>` are the objects the block form makes
and hands to you as a `LoopedCell<T>` or `LoopedBehavior<T>`, the type without `Loop`. On those
two, `Loop` returns the `LoopCloser`, because closing the loop is what makes the conversion back
possible. You will meet them in older code and in the book's examples:

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

A looped cell has no `Sample`: its value does not exist yet, and the type says so, so the call
does not compile. Use `SampleLazy` (`sampleLazyC`) instead — it defers the read until the loop has
closed and there is something to read.

The same reasoning gives every operation that takes a starting value a deferred counterpart, for
when that starting value is itself read out of the loop: `Hold` has `HoldLazy`, `Accum` has
`AccumLazy`, `Collect` has `CollectLazy`, and `Cell.Constant` has `Cell.ConstantLazy`. Each takes
a `LazySample<T>` where the eager one takes a value, and the F# aliases follow the same rule
(`holdLazyS`, `accumLazyS`, `collectLazyS`).

A `LazySample<T>` is not a `Lazy<T>`: your code cannot force it. Only the library reads it, at the
end of the transaction that asked for it, which is what lets a transaction that fails forget the
value it took, so a cell built in that transaction starts from the committed value instead.
`SampleLazy` gives one, `Map` transforms one, `Lift` combines up to eight, and `LazySample.Defer`
(`lazySample`) makes one from a function when the starting value is not a sample of the graph. A
function given to any of these can run again after a failed transaction, so keep it pure and do
not send from it.

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
    reference => new Node(new Child(reference)));
```

# [F#](#tab/fsharp)

```fsharp
let node = forwardReferenceWithNoCaptures (fun reference -> Node (Child reference))
```

---

The block is handed a `LoopedCell<Node>`, and the child takes it as that: a cell that means
nothing until the call returns and holds the finished node from then on. The type is what keeps
the child honest. It can snapshot the cell, map it, listen to it, or bind it, and it cannot read it
in its constructor, which is the one read that would have no answer. It unties the knot two
objects make when each needs the other at construction, which otherwise forces one of them to be
built half-formed and completed afterward — with a settable member that has no business being
settable once the graph is up.

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
the reference has no `Sample`, and a read through `AsCell` during construction asks a question
that has no answer yet, and throws.

### Rows built inside the block

The object under construction usually makes smaller objects as it goes — the rows of a list,
say — and a row often needs a cell that comes from the object itself: its share of a total that
is the sum of the rows, or whether it is the selected one. The row is constructed while the block
runs, so that cell is pending when the constructor sees it. Give the row the looped type, and let
it keep that type:

# [C#](#tab/csharp)

```csharp
internal sealed class Row
{
    internal Row(IBindableFactory factory, string name, CellSink<decimal> amount, LoopedCell<decimal> share)
    {
        this.Name = name;
        this.Amount = amount;
        this.Share = share;

        // The factory takes the looped type and gives the looped interface. The view binds it
        // the same way; see the bindable page.
        this.ShareText = factory.CreateOneWay(share.Map(s => $"{s:P0}"));
    }

    public string Name { get; }

    public CellSink<decimal> Amount { get; }

    public LoopedCell<decimal> Share { get; }

    public ILoopedOneWayBindableValue<string> ShareText { get; }
}

// Inside the ForwardReference block, where viewModelLoop is the LoopedCell<ViewModel>:
LoopedCell<decimal> totalLoop = viewModelLoop.Map(vm => vm.Total).SwitchC();
Row row = new(factory, "a", amount, amount.Lift(totalLoop, (a, t) => t == 0 ? 0 : a / t));
```

# [F#](#tab/fsharp)

```fsharp
type Row(factory: IBindableFactory, name: string, amount: CellSink<decimal>, share: LoopedCell<decimal>) =
    member _.Name = name
    member _.Amount = amount
    member _.Share = share
    member _.ShareText = factory.CreateOneWay(share |> mapLC (fun s -> $"{s:P0}"))

// Inside the forwardReferenceWithNoCaptures block:
let totalLoop = viewModelLoop |> mapLC (fun vm -> vm.Total) |> switchCLC
let row = Row(factory, "a", amount, lift2LC (fun a t -> if t = 0m then 0m else a / t) (amount, totalLoop))
```

---

The parameter type is the whole pattern. The constructor cannot sample the cell or store it as a
`Cell<T>`, because neither compiles, and those were the only two things it could do wrong.
Everything it legitimately wants to do, it can: hand the cell to the bindable factory, which takes
the looped type, map it, lift it with the row's own cells, snapshot it from a command, listen to
it.

Keep the looped type on the property too, rather than converting it with `AsCell()` in a getter. A
`Cell<T>` property is an escape that the code building the row can take while the loop is still
open — `row.Share.Sample()` from inside the block compiles and throws — and the row has no way to
know who is reading. On a `LoopedCell<T>` property every consumer composes freely, and a read is an
`AsCell()` at the read site, written by the one who knows the loop has closed: a test, or an
imperative read after construction. That is where the assertion belongs.

A consumer that genuinely needs the value *now* cannot take the looped type, and the compiler says
so. A collection view's criteria cell is one: `Filter(criteriaCell: totalLoop, …)` does not
compile, because the view's keys would start from a value that does not exist yet. That is a true
cycle through the collection, and it is broken the way it always was, with a sink or a snapshot.

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
