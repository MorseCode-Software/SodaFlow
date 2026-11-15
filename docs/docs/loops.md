---
title: Feedback loops
---

# Feedback loops

Some values depend on their own past. A running total, a state machine, a position updated by
velocity — each needs its previous value to compute its next one. That is a circular reference,
and ordinary construction cannot express it: you must pass the value to itself before it exists.

A loop resolves this with a forward declaration. You get a placeholder, build the graph that
refers to it, and then say what it was a reference to.

## Try `Accum` and `Collect` first

The common case is an accumulator, and `Accum` and `Collect` are loops that are already written:

```csharp
Cell<int> total     = s.Accum(0, (v, acc) => v + acc);
Stream<string> outp = s.Collect(0, (v, st) => (ReturnValue: $"#{st}", State: st + 1));
```

Write a loop yourself when the feedback path has logic that `Accum` and `Collect` cannot express,
typically when it goes through other cells or through a `Switch`.

## Writing a loop

`Stream.Loop<T>()`, `Cell.Loop<T>()`, and `Behavior.Loop<T>()` give your block the placeholder, a
`LoopedStream<T>`, `LoopedCell<T>`, or `LoopedBehavior<T>`. They take back the definition that the
block returns, and they close the loop for you, in the explicit transaction that it needs.

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

Read the C# version from the inside out. `l` is the placeholder for the stream that the block
defines. It is a `LoopedStream<int>`, which is a `Stream<int>` that you can use anywhere a stream
goes. `l.Hold(0)` makes a cell of the running total that starts at zero. Each firing of `s` takes
a snapshot of that cell and adds to it. The result is what `l` stands for, and `WithoutCaptures`
closes the loop with it.

These samples use `ListenStrong` rather than `Listen` for one reason: `using` and `use` need
`IDisposable`, and only `IStrongListener` implements it. Where you keep the handle in a variable,
use [`Listen`](lifetimes.md).

The block cannot get a loop wrong. The placeholder has no method that closes it, so you cannot
close it twice, forget to close it, or close it in a different transaction. The block's return
value is the definition. Versions before 6.0.0 also had `Stream.CreateLoop`, `Cell.CreateLoop`,
and `Behavior.CreateLoop`, which a later statement closed. Version 6.0.0 removes them, so write
each such loop as a block.

### Capturing extra values

Sometimes the block makes something else that you want next to the looped value. `WithCaptures`
returns both:

```csharp
StreamSink<int> s = Stream.CreateSink<int>();

(Stream<int> total, Stream<int> doubled) = Stream.Loop<int>()
    .WithCaptures(l => (
        Stream: s.Snapshot(l.Hold(0), (n, o) => n + o),
        Captures: s.Map(v => 2 * v)));
```

`Stream` is what the loop resolves to, and `Captures` is anything else you want out. In F#,
`loopS` returns a struct tuple of the looped value and the captures, and `loopWithNoCapturesS` is
the shorter form when there is nothing extra.

### Several loops

Blocks nest, so several loops can be open at once. If you know the number of loops only at run
time, a recursive function can open one block for each call. The innermost call builds what uses
them. When you build an object rather than a single value, use a [forward
reference](#forward-references).

### Passing a placeholder on

Where a placeholder goes to code that wants the plain type, `AsStream`, `AsCell`, and `AsBehavior`
convert it without a cast. The plain types have the same three methods, which return their
receiver. The [loop analyzer](#the-loop-analyzer) reads them as a mark: put the mark at the
argument it is about, as in `new Row(amount.Lift(total, …).AsCell())`. A mark on the placeholder
itself covers every value made from it.

## Reading a looped value

A looped value has no value until its loop closes. Thus, `Sample` on a looped cell in the block
asks for a value that does not exist yet, and throws. Use `SampleLazy` (`sampleLazyC`), which
waits until the loop closes and there is a value to read.

Each operation that takes a starting value has a lazy form, for a starting value that comes out of
the loop. The lazy forms are `HoldLazy`, `AccumLazy`, `CollectLazy`, and `Cell.ConstantLazy`. Each
takes a `LazySample<T>` where the plain form takes a value. The F# names follow the same rule:
`holdLazyS`, `accumLazyS`, and `collectLazyS`.

A `LazySample<T>` is not a `Lazy<T>`, and your code cannot force it. The library reads it at the
end of the transaction that asked for it. Thus, if that transaction fails, the library forgets the
value, and a cell built in the transaction starts from the committed value. These make one:

- `SampleLazy`, from a cell.
- `Map`, from another lazy sample.
- `Lift`, from up to eight lazy samples.
- `LazySample.Defer` (`lazySample`), from a function, when the starting value is not a sample of
  the graph.

A function that you give to any of these can run again after a failed transaction. Keep it pure,
and do not send from it.

The two often go together. A fold whose start comes from the collection that it folds over needs
both:

```csharp
Cell<ImmutableHashSet<int>> drainable =
    accounts.ItemChangesStream.AccumLazy(
        // The keys already in the collection, read lazily because this is that collection.
        initialState: accounts.SnapshotCell.SampleLazy().Map(/* ... */),
        // The keys each change adds or removes.
        f: static (changes, keys) => /* ... */);
```

That is from the Accounts sample. The set that it accumulates starts from the accounts already
there, which is a read of the collection whose changes it folds.

## Forward references

A loop lets you refer to a cell before it exists. `ForwardReference` is the same idea for one
value rather than a series of values:

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

The block gets a `LoopedCell<Node>`, and `AsCell` passes it on as the `Cell<Node>` that the child
wants. The cell has no value until the call returns, and holds the finished node from then on.
Thus, two objects that each need the other at construction can both be complete. Without it, you
build one of them half-formed and complete it later, through a member that must then stay
settable.

`WithCaptures`, and `forwardReference` in F#, return anything else that the construction makes, as
for a loop. In C#, you give the value type on `ForwardReference<T>` and the compiler infers the
capture type. In F#, the compiler infers both.

A read of the reference in the block throws, as a read of any looped value does.

### An object under construction

Use this form when a factory builds an object whose own graph feeds it. An example is a view model
whose buttons edit the collection of its rows. Do not make a loop for each stream. Build the whole
object in one `ForwardReference<T>`. Keep each looped stream or cell in a private field of the
object, or use the property that already exposes it. Then read it back from the looped object with
`SwitchS` or `SwitchC`:

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

The forward reference holds the finished object, so `SwitchS` on a field of it is the stream that
the object has at the end. One reference stands for each looped value of the object, however many
streams and cells feed back. In F#, the same shape is `forwardReferenceWithNoCaptures` with
`switchS` or `switchC`.

## The loop analyzer

A read of a looped value before its loop closes throws at run time. The `SodaFlow` package also
has a Roslyn analyzer that finds the common forms of this mistake when a C# project builds. A
project that references `SodaFlow` runs it, with no step of its own. To remove it, put
`ExcludeAssets="analyzers"` on that reference. The analyzer does not check F#, because a Roslyn
analyzer runs in the C# compiler only. In F#, the exception at run time is the check.

The analyzer reads the code that runs while a loop is open: the function that you give a loop
block or a forward reference. That function can be a lambda, an anonymous method, or a method of
your project given by name. When that code calls your own methods, constructors, local functions,
or property getters, the analyzer reads them too.

### The rules

Each rule is a warning, except `SF1003`, which is an info: you see it in the editor, and the build
is quiet.

| Rule | Reports | What to do |
| --- | --- | --- |
| `SF1001` | `Sample()`, the `Value` of a bindable, or `CanExecute` on a value made from the placeholder, in the block. | Use `SampleLazy` or a lazy form, or move the read into a callback. |
| `SF1002` | A loop whose definition links to its own placeholder, with no hold that it samples, `Operational.Defer`, or timer between them. This throws `DependencyCycleException`, at the close or when a switch selects the loop. | Put a hold that the definition samples, `Operational.Defer`, or a timer in the cycle. |
| `SF1003` | A value made from the placeholder that goes to code the analyzer cannot read, and that can read the value. | If that code does not read the value before the close, [mark the argument](#code-the-analyzer-cannot-read). |
| `SF1004` | A method, constructor, local function, or lambda of your project that reads a value it gets from the block. Reported at the argument. | As for `SF1001`, in that code. |
| `SF1005` | A call that reads a pending member of an object built in the block. Also a helper that reads a field the block set, or a lambda that your code kept and the block runs. Reported at the call. | Make the call after the block, or in a callback. |
| `SF1006` | A loop whose initial value reads the loop, through a lazy sample of it or a `Map` whose function samples it. | Give the loop an initial value that does not read the loop. |

### What is not reported

- **A read in a callback.** The function of a `Snapshot`, a `Listen`, or a `Map` of a stream runs
  after the loop closes.
- **A read after the block**, through a capture or a property of a row.
- **A lambda or a local function that nothing in the block calls.** The analyzer reads a function
  where it runs, not where you write it.
- **The function of a `Map` or `Lift` of a cell**, until something reads the cell. The function
  runs at the first read of the cell, not when you build it. A function that samples a looped
  value is thus harmless in a cell that you only keep. If you sample that cell in the block, or
  close the loop with it, the analyzer reports the sample or the close. Collection views work the
  same way. Their predicates, selectors, sort orders, and comparers run when something reads the
  view.
- **A sample of a `Hold` with a plain initial value.** A read in the block gives the initial
  value, because a firing of the stream comes after the close.

### Code the analyzer cannot read

`SF1003` is for code that can read the value and that the analyzer cannot see into. Examples are a
delegate from elsewhere, a method in another assembly that uses SodaFlow, and a chain of your
calls deeper than the analyzer follows. Code that does not know SodaFlow, such as `List<T>` or
LINQ, cannot sample a cell, so a value given to it is not reported.

If you know that the code does not read the value before the close, put `AsCell()`,
`AsBehavior()`, or `AsStream()` on the argument. These methods change nothing at run time. The
analyzer reads them as your promise and is quiet, unless it can see the callee break the promise.

To silence a report for a reason of your own, use a pragma with the reason:

```csharp
#pragma warning disable SF1002 // The cycle is the subject of this test.
```

To make `SF1003` a warning, set its severity in your `.editorconfig`:

```ini
dotnet_diagnostic.SF1003.severity = warning
```

### How far it follows a value

The analyzer follows a value made from the placeholder through the code of your project:

- Locals, tuples, deconstructions, patterns, `out` and `ref` arguments, and the fields that the
  block sets.
- Collections, arrays, and dictionaries. A list or an array that you write as a literal keeps each
  element apart, so `cells[0]` is that one element.
- Your helpers, constructors, property getters, records, and object initializers, and a call
  through an interface to an object that the block built.
- Loops: each pass of a `for`, `foreach`, `while`, or `do` is read until a pass adds nothing new.
- Members that C# calls with no call syntax: an indexer, an operator, a conversion, a setter, an
  event accessor, `Deconstruct`, `ToString` in an interpolated string, `Dispose` at the end of a
  `using`, the enumerator of a `foreach`, and the `Add` of a collection initializer.
- Members that the platform calls for you: `CompareTo` in a sort, `Equals` and `GetHashCode` in a
  search, a set, or a dictionary, a comparer that you give a collection, and `ToString` in
  `string.Join` or `string.Format`.
- Code that waits. The analyzer reads a LINQ query or an iterator where something enumerates it,
  and the function of a `Lazy<T>` where something reads its `Value`.
- The serializers of `System.Text.Json`, Json.NET, and `XmlSerializer`, and LINQ to XML, which
  call your getters and enumerate your sequences.

### Its limits

The analyzer does not replace the check at run time. Where it cannot be sure, it reports on the
careful side, so it can report a read that does not throw:

- A collection that it cannot follow item by item has the state of all its items. A list that
  holds one pending cell and one plain cell then reports a read of either.
- It reports a read under a condition, whether or not that path runs. A comparer that only a tie
  calls, and an `Equals` that only two equal hash codes call, are reads of this kind.
- Where a value can be one of several objects, a plain value set on it does not clear a pending
  one.

Some reads that throw it does not see:

- Reflection, such as `Activator.CreateInstance` or `MethodInfo.Invoke`.
- A method of a library other than the platform and SodaFlow that keeps a function and runs it
  later.
- A member that the platform calls from a method that is not in its table of common methods.
- A serializer with options or a converter of its own.
- A function that a constructor from outside your project keeps, other than a `Lazy<T>` or the
  comparer of a platform collection.
- A chain of your own calls more than four calls deep. There, the analyzer reports `SF1003` at the
  call where it stops.
- A virtual method, if the analyzer did not see the object built: it reads the method that the
  code names, not an override.
