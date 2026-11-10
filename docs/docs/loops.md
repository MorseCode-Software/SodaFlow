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

This is how every loop is written. `Stream.Loop<T>()`, `Cell.Loop<T>()` and `Behavior.Loop<T>()` hand
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
`AsBehavior` say so without a cast. The same three names also exist on the plain types, where they
return their receiver unchanged. That lets you write the mark at the argument it is about, say
`new Row(amount.Lift(total, …).AsCell())`, rather than on the placeholder upstream, where it would
cover every value derived from it. The compiler sees nothing in it; a static analyzer that checks
loops reads it as your promise that the callee does not sample the value before the loop closes.

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

## Why a block

Earlier versions also had an explicit form. `Stream.CreateLoop<T>()` and its cell and behavior
counterparts returned a placeholder with a `Loop` method, and a later statement had to call it.
Four mistakes were possible with it, and each one surfaced only as an exception at run time:
creating the placeholder outside a transaction, never closing it, closing it twice, and closing it
in a different transaction. Version 6.0.0 removed it.

The block form makes all four impossible, and it is the type system that makes them so rather
than a convention. What the block is handed is a `LoopedStream<T>`, which has no `Loop` on it at
all: there is nothing to call twice, nothing to leave uncalled, and nothing to call in the wrong
transaction. The definition is what the block returns rather than something a later statement has
to remember to do.

A loop whose parts seem to need separating still fits. Blocks nest, so several loops can be open
at once, and when the number of loops is known only at run time, a recursive function can open
one block per call and build whatever uses them in the innermost call. If what is being built is
an object rather than a single value, use a forward reference; see below.

## `Sample` inside a loop

Calling `Sample` on a looped cell while still constructing the loop asks for a value that does
not exist yet. Use `SampleLazy` (`sampleLazyC`) instead — it defers the read until the loop has
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

## The loop analyzer

Reading a looped value before its loop closes throws at run time, and the shapes above tell you
how to avoid it. From this release the `SodaFlow` package also carries a Roslyn analyzer that finds
the common shapes at build time, in C# projects. Nothing has to be added: a project that references
`SodaFlow` runs it, and `ExcludeAssets="analyzers"` on that reference removes it. F# is not
covered, because a Roslyn analyzer runs in the C# compiler only; an F# project keeps the exception.

The analyzer looks at the code that runs while a loop is open, which is the function you give a
loop block, and follows the placeholder through the library's operators. That function can be a
lambda, an anonymous method, or a method of your own project given by name. Where that code calls
into your own project, the analyzer reads the body it calls in the same way: a method, a local
function, a constructor, or a property getter. These rules come out of that, all of them warnings
except `SF1003`:

| Rule | Reports |
| --- | --- |
| `SF1001` | `Sample()`, a bindable's `Value`, or `CanExecute` on a value derived from the placeholder, in code that runs before the close. |
| `SF1002` | A loop whose definition is linked to its own placeholder, with no sampled hold, `Operational.Defer`, or timer between them. That is the shape that throws `DependencyCycleException`, at the close or later when a switch selects the loop. |
| `SF1003` | A value derived from the placeholder handed to code the analyzer cannot read and that could read the value. This one is an info, visible in the editor and silent in the build. |
| `SF1004` | A method, constructor, local function, or lambda in your own project that reads a value it was handed, reported at the argument. |
| `SF1005` | A method or property of an object you built in the block that reads a member the construction made pending, a helper that reads a field the block set, or a lambda that a constructor or helper of yours kept and the block then runs. Reported at the call. |
| `SF1006` | A loop whose definition's initial value reads the loop itself, through a lazy sample of it or through a `Map` whose function samples it. That forces itself at the end of the transaction. |

A read inside a callback, a `Snapshot` or `Listen` lambda or a `Map` of a stream for instance, is
not reported, because it runs after the close. Neither is a read after the block, through a capture
or a row's property, so nothing needs a mark there. A lambda or a local function is read where it
runs and not where it is written: one that only a callback calls is as quiet as the callback, and
one that nothing calls is not read at all.

A `Map` or `Lift` of a cell is the case to know about. Its function does not run when you build
the cell. It runs the first time something reads the value. So a function that samples a looped
value is harmless in a cell that is only kept, and the analyzer says nothing about it. Sample that
cell in the block, or close the loop with it, and the function runs before the loop has a value:
the analyzer reports the sample, or the close. The views of a collection behave the same way with
their predicates and selectors, and so does a sort order built with `KeyOrder.By` or `ThenBy`: its
function runs when a view sorts by that order and something reads the view, whether the order
reaches the view directly or in a cell. A comparer is a function here too: one that samples a
looped value, whether it is an object of yours or comes from `Comparer<T>.Create`, makes the view
that sorts with it pending, and is quiet until something reads that view.

A `Hold` is the opposite case. A read in the block always gives its initial value, because the
value of a firing arrives after the close. So a hold that starts plain is fine to sample, whatever
its stream will carry later.

`SF1003` is for code that could read the value and that the analyzer cannot read: a delegate that
came from somewhere else, a method in another assembly that itself uses SodaFlow, or a chain of
your own calls deeper than it follows. Code that knows nothing about SodaFlow, a `List<T>` or a
LINQ query for instance, cannot sample a cell, so the analyzer does not report a value given to
it and instead keeps track of where the value ends up. Where a value does go into code the
analyzer cannot follow, and you know that code does not read it before the close, say so with
`AsCell()`, `AsBehavior()`, or `AsStream()` at the argument. The methods return their receiver and
change nothing at run time; the analyzer reads them as your promise and stays quiet, unless it can
see the callee break the promise, which it still reports. The tests that deliberately read a
placeholder early do it with a pragma and a reason, as in
`#pragma warning disable SF1002 // The cycle is the subject of this test.`, and a project that
wants the escape as a warning sets `dotnet_diagnostic.SF1003.severity = warning` in its
`.editorconfig`.

The analyzer follows a value through locals, the elements of a tuple, collections, fields the
block sets, `foreach`, deconstructions, patterns, and `out` arguments. It follows it into your own
helpers, constructors, getters, records, and object initializers, and through an interface to the
object you built behind it. It knows which library methods run a function at once: a transaction,
a loop block, and the `Fold` of a collection. Code outside the library that takes a function, such
as LINQ or `Maybe.Match`, is taken to run it in the call, and a constructor is taken to keep it.
A `Lazy<T>` keeps its function until something reads `Value`, so the analyzer reads the function
at that read, including through the `Map` and `Lift` of `SodaFlow.Functional`. A lambda that one
of your own constructors or helpers keeps is read where it finally runs, and reported there. A
property pattern reads a property just as a member access does, so `row is { Total: > 0 }` is
checked like `row.Total > 0`.

It also reads the members of yours that C# calls without call syntax: an indexer, an operator, a
conversion, a property setter or an event accessor, a `Deconstruct`, `ToString` in an interpolated
string or a concatenation, `Dispose` at the end of a `using`, the enumerator behind a `foreach`,
and the `Add` behind a collection initializer.

It does not see everything, and in a few places it guesses on the careful side:

- A collection has the state of everything put into it. A list that holds one pending cell and one
  plain one reports a read of either.
- A field set on an object the analyzer did not see built counts for every object of that type,
  to the end of the block.
- A read under a condition is reported whether or not that path runs. A comparer of keys is such a
  read: a sort calls it only when two items tie, and the analyzer does not know your data.
- A LINQ query built in the block is read as if it were enumerated there. If you enumerate it only
  after the block, a read in its lambda is reported although it is fine. An iterator method of
  yours is read the same way, at the call.
- A chain of your own calls is followed three calls deep. Past that, the value gets `SF1003`.
- A function kept by a constructor outside your project is not read when the block then uses that
  object. That covers a comparer kept by a `SortedSet<T>`. `Lazy<T>` is the exception, and a
  `Lazy<T>` handed to code outside your project is taken as kept, not forced.
- Code outside your project can call back into a type of yours through an interface or a virtual
  method it knows: `CompareTo` from a sort, `Equals` and `GetHashCode` from `Contains` or a set,
  `ToString` from `string.Join`, `GetEnumerator` from a LINQ query. A read in such a member is not
  seen. A comparer is the exception.
- A virtual method is read as the method the code names, unless the analyzer saw the object built.

The runtime check stays behind all of these.

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
