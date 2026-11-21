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

using (total.Listen(Console.WriteLine))
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

use _ = total |> listenS (printfn "%d")
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

These samples hold the listener in `using` and `use`. A handler runs only while something holds
its listener, and the scope does that here and stops the listener at its end. See
[Listener lifetimes](lifetimes.md).

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
itself covers every value made from it. So does a mark in a local that gets no other value, such as
`var marked = placeholder.AsCell();`.

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

The same analyzer also has `SF2001`, a rule for a listener that nothing holds. [Listener
lifetimes](lifetimes.md#the-mistake-that-fails-silently) describes it.

The analyzer reads the code that runs while a loop is open: the function that you give a loop
block or a forward reference. That function can be a lambda, an anonymous method, or a method of
your project given by name, also in parentheses, in a cast, or in `new Func<...>(...)`. It can also
be a local that holds such a function, from its declaration or from the last assignment before the
block, where both are statements of one block. A block whose function the analyzer cannot find
this way, such as a delegate from a field or a parameter, or a local that gets its function under a
condition, gets `SF1003`, because none of its code is checked. When that code calls your own
methods, constructors, local functions, or property getters, the analyzer reads them too.

### The rules

Each rule is a warning, except `SF1003`, which is an info: you see it in the editor, and the build
is quiet.

| Rule | Reports | What to do |
| --- | --- | --- |
| `SF1001` | `Sample()`, the `Value` of a bindable, a write to the `Value` of a two-way bindable, or `CanExecute`, `Execute`, or `Dispose` of a bindable action, also through `ICommand`, `IDisposable`, or `IBindable`, on a value made from the placeholder, in the block. | Use `SampleLazy` or a lazy form, or move the read into a callback. |
| `SF1002` | A loop whose definition links to its own placeholder, with no hold that it samples, `Operational.Defer`, or timer between them. This throws `DependencyCycleException`, at the close or when a switch selects the loop. With `ForwardReference`, the same holds for a member of the object that is defined by reading that member back through the reference, for example with `SwitchC`. | Put a hold that the definition samples, `Operational.Defer`, or a timer in the cycle. |
| `SF1003` | A value made from the placeholder that goes to code the analyzer cannot read, and that can read the value. Also a block whose function the analyzer cannot read. | If that code does not read the value before the close, [mark the argument](#code-the-analyzer-cannot-read). |
| `SF1004` | A method, constructor, local function, or lambda of your project that reads a value it gets from the block. Reported at the argument. | As for `SF1001`, in that code. |
| `SF1005` | A call that reads a pending member of an object built in the block. Also a helper that reads a field the block set, or a lambda that your code kept and the block runs. Reported at the call. | Make the call after the block, or in a callback. |
| `SF1006` | A loop whose initial value reads the loop, through a lazy sample of it or a `Map` whose function samples it. With `ForwardReference`, a member that is read back while it is still pending, so that the first read of the member needs its own value. | Give the loop an initial value that does not read the loop. |

### What is not reported

- **A read in a callback.** The function of a `Snapshot`, a `Listen`, or a `Map` of a stream runs
  after the loop closes.
- **A read after the block**, through a capture or a property of a row.
- **A lambda or a local function that nothing in the block calls.** The analyzer reads a function
  where it runs, not where you write it.
- **The function of a `Map` or `Lift` of a cell**, until something reads the cell. The function
  runs at the first read of the cell, not when you build it. A function that samples a looped value
  is thus harmless in a cell that you only keep. If you sample that cell in the block, or close the
  loop with it, the analyzer reports the sample or the close. It reads the function at that first
  read, with the values that the function reads then, so a capture or a member that becomes pending
  after you build the cell counts too, and what the function writes there counts for the code after
  the read. A second read of the cell does not run the function again, when the analyzer knows that
  the first read ran (see the limits below). Collection views work the
  same way. Their predicates, selectors, sort orders, and comparers run when something reads the
  view. The eviction handler of a `Map` on a collection runs when a key leaves the view, and in a
  call of `Dispose` for each object the map built. The analyzer reads the handler at a `Dispose` in
  the block, also one through `IDisposable`, unless the code uses the map for nothing but that
  `Dispose`.
- **A call that the compiler removes.** A call of a method with `[Conditional]`, such as
  `Debug.WriteLine` in a release build, does not run, and neither do its arguments, when the build
  defines none of its symbols.
- **A sample of a `Hold` with a plain initial value.** A read in the block gives the initial
  value, because a firing of the stream comes after the close. That is also true for a hold that
  the block makes, after a send into its sink in the block: the hold takes the sent value only at
  the end of the transaction. The same goes for an `Accum` and for a cell sink. It also goes for a
  read through a `Map`, a `Lift`, a `Calm`, or an `AsCell` of such a hold, through a bindable of
  it, and through a `Cell.ConstantLazy` of its `SampleLazy`: each of these takes its first value
  from the hold at the read. A switch of the hold is different: it takes the sent value at the end
  of the transaction.

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
  block sets. A write to a static field of a class that is not generic replaces what the field
  held, where the write always runs: in the block, in a method that the block always calls, or in
  the function of a `Map` at a first read that always runs. The variable of a `catch` clause gets each object that a `throw` in its `try` block,
  or in a method or a lambda that the block calls, can send to that clause. Its filter gets none,
  because an exception in a filter is lost and makes the filter false.
- Collections, arrays, and dictionaries. A list, an array, or a span that you write as a literal
  keeps each element apart, also where the literal spreads a second such local, so `cells[0]`,
  `cells[^1]`, `cells.ElementAt(1)`, and each element of a list pattern in an `is` or a `switch` on
  `cells` are each one element. A positional pattern on a tuple gives each part its own item. A
  `Dictionary` that you write with constant keys and no comparer, and that the code then only
  reads, keeps the value of each key apart, so `cells["total"]` is that one value. A key of the type
  `object` has the type of its constant too, so the `int` 1 and the `long` 1 are two keys.
  `Array.Fill` puts its value into each element that it fills, as an assignment to that element
  would, and `Array.Reverse` of a whole array moves each element to the other end. A
  `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`, `ReadOnlyMemory<T>`, or `ArraySegment<T>` that a method
  makes from an array in one of its locals or parameters, with an implicit conversion, `AsSpan`,
  `AsMemory`, a constructor, `Slice`, or a range, is another name for the elements of that array,
  as long as neither name gets a second value. `Fill`, a write to an element or through a `ref`
  local of an element, and `CopyTo` into it put their values into the array, and so do `CopyTo`
  into the array itself and `Array.Copy`. Where each step from the array to the view starts at a
  constant, as `AsSpan(1)` or `[1..]` does, a write to an element at a constant index and a `Fill`
  reach only the elements of the array that they write. Otherwise, they reach every element. A span
  that `new Span<T>(ref local)` makes is another name for that local. A write through a `ref` that
  an indexer, a method, or a local function of yours returns goes where that `ref` points: a
  parameter, a local of the code around a local function, a static field, or a member of the object
  of the call or an element of that member. A write to a member through an element of a local array,
  `List<T>`, or `Dictionary` created with no comparer, at a constant index or key or at an index in
  a local, replaces what an earlier write gave that member. So does a write through a local that got
  that element, and a write through any other name of the object in the element, such as `d` after
  `a[0] = d`, when the analyzer knows which object that is. A later read through the element or the
  local sees only the last value. An index in a local that gets only a constant is that constant
  index. A local that gets the array or the list in a field sees each element that code puts in
  through the field later, also in a method, until the local or the field gets a different value.
  The other way also holds: a read through the field sees an element that code puts in at an index
  through such a local.
- Names of one object. Where one name gets its object from another, as in `a[0] = d`,
  `box.Slot = Shared`, or `var slots = SharedSlots`, the two are names of one object from there. A
  name is a local, a parameter, a static field, a field of the object in a local, or an element of a
  local collection or of an array in a static field, at a constant index or at an index in a local.
  A write to a field or a property with no body through any of the names replaces what earlier
  writes through all of them gave that member, and a read through any of them sees the last value,
  while each name keeps the object. A write in a method that the block calls counts as well, where
  the block always calls the method one time and the method writes through a static field, an
  element of an array in one, or a parameter whose argument is such a name. A new object that goes
  into a name with an object initializer, as in `slots[0] = new Slot { Cell = value }`, gives the
  name only the values that the initializer sets, also where such a method puts it and keeps it
  there to its end.
- Your helpers, constructors, property getters, records, and object initializers, and a call
  through an interface to an object that the block built, or to one that a `readonly` field or a
  get-only property creates in its initializer. That includes a property that uses `field`, a
  member of an extension block, a static member of an interface called through a type parameter,
  and the constructor that `new T()` runs, where the call says the type of `T`.
- Events and delegates: the handlers added with `+=` run where the event is raised, and a delegate
  made with `+` or `Delegate.Combine` runs each of its functions. Each element of the
  `GetInvocationList()` of a delegate is one of those functions.
- Loops: each pass of a `for`, `foreach`, `while`, or `do`, of a `goto` back to a label, or of a
  `goto case` or `goto default` back to an earlier section of a `switch`, is read until a pass adds
  nothing new.
- Sinks: a value that the block sends into a sink reaches each value made from that sink, also one
  made before the send, such as a `Hold` of the sink or a switch of that hold. So does a value sent
  in a function that `Transaction.Post` runs after the transaction: a lambda, a local function, a
  method that you give to `Post` by its name, or a post inside a posted function. The same holds for
  an action that you post to any `IBindingScheduler`, and for a write to the `Value` of a
  two-way bindable of a sink, which sends the value into that sink after the transaction.
  `Execute` of a bindable action sends its parameter into the sink of its firings after the
  transaction in the same way. A handler that you add to an event of a bindable runs after the
  transaction too, because the bindable raises its events through its scheduler. So does each
  result that the operation of `MapAsync` gives to `FromValue` or `Construct`, which the pipeline
  sends into the sink of results. The analyzer finds those calls only in the code of the
  operation itself, and not in a method that the operation calls.
- Members that C# calls with no call syntax: an indexer, an operator, a conversion, also of each
  element of a spread and of each element of a tuple that the code converts to a different tuple
  type or deconstructs, `operator true` or `operator false` before a user-defined `||` or `&&`, a
  setter, an event accessor, `Deconstruct`, `ToString` in an interpolated string that the code
  formats at once, which is `TryFormat` of `ISpanFormattable` for a value that implements it, and
  otherwise the `ToString` of `IFormattable` for a value that implements that, the `AppendFormatted`
  of an interpolated string handler of your own, which gets each value in place of that `ToString`,
  `Dispose` at the end of a `using` or a `foreach`, `DisposeAsync` at the end of an `await using`,
  `GetAwaiter` at an `await`, with `IsCompleted` of its awaiter and `GetResult` where the analyzer
  knows that the awaiter is complete, the enumerator of a `foreach`, the `Add` of a
  collection initializer or a collection expression, the `Create` method of a `CollectionBuilder`,
  `Count` and `Slice` for an index from the end, a range, or a list pattern, the `ToString` and the
  copy constructor of a record, the `ToString` of each value that a record or a tuple prints, the
  `Equals` or `GetHashCode` of each member that the `Equals`, `GetHashCode`, `==`, and `!=` of a
  record or an anonymous type call, the `ToString` of each value that an anonymous type prints, and
  the `==` of each element of a tuple comparison. The explicit static constructor of a class runs,
  with the static initializers of that class, once in the block, at the first use that runs it: the
  first object of the class or of a class derived from it, or the first read, write, or call of a
  static member, through the name of the class or through a simple name in the code of another
  class, such as a name that `using static` gives. Each type argument of a generic class, as in
  `Cache<int>` and `Cache<string>`, has a run of its own. A member of a nested class runs only the
  static constructor of the nested class, and a delegate of a static method runs it where something
  calls the delegate, not where the code makes the delegate. A first use that the code can skip,
  such as one in a branch or in an arm of `?:`, counts only for itself, so the analyzer reads the
  constructor again at the next use. A use in a loop counts for the rest of the block, also when
  the loop has no turn.
- A value of the type `dynamic` whose object the analyzer saw made. It reads the method, the getter
  or the setter, the indexer, the operator, the implicit conversion, or the `operator true` or
  `operator false` that the runtime binder finds on the type of that object.
- Members that the platform calls for you: `CompareTo` in a sort, `Equals` and `GetHashCode` in a
  search, a set, or a dictionary, a comparer that you give a collection, `ToString` in `string.Join`
  or `string.Format`, and the `ToString` of each part that a `KeyValuePair`, a `DictionaryEntry`, or
  a `Tuple` object prints. A method with a format string, such as `string.Format`, calls `TryFormat`
  of a value that implements `ISpanFormattable`, and the `ToString` of `IFormattable` where
  `TryFormat` fails or a width pads the text.
- Members that SodaFlow calls for you: `CompareTo` of each sort value of a collection view that
  sorts with no comparer, at the first read of the view, and `Equals` of the comparer of a two-way
  bindable, when you write its `Value`. `RunTimersTo` of a clock built on
  `TimerSystemImplementationBase` runs, in the call, each callback that `SetTimer` gave the same
  local before it, unless both times are integer constants and the time of the callback is later.
  A `TimerSystem` calls `Start` and `Now` of its implementation when you construct it, and `Stop`
  when you dispose it. The construction of a `SystemClockTimerSystem` calls its `now` function.
- Code that waits. The analyzer reads a LINQ query or an iterator where something enumerates it,
  the function of a `Lazy<T>` or a `ThreadLocal<T>` where something reads its `Value`, and the
  function of a `Task` that you create with `new` where `RunSynchronously` runs it. The `Result` of
  such a task, and `GetResult` of its awaiter, have the value that the function returns, however
  the task runs. A query expression over a type whose methods run their function in the call, such
  as `from x in maybe select ...` over a `Maybe<T>`, runs where it is written. Where the code takes
  only one item from the call of an iterator, with `First`, `FirstOrDefault`, `Any()` with no
  predicate, `Take(1)`, `ElementAt(0)`, or `ElementAtOrDefault(0)`, also after a `Select`, or with
  a `foreach` that leaves the loop in its first pass on every path, by a `break`, a `return`, a
  `throw`, or a `goto`, the iterator runs only up to its first `yield return` and the `finally`
  blocks around it. The analyzer still reads the whole iterator where the code moves its enumerator
  by hand, where a method of yours gets the call and takes one item, and where `Zip` stops at the
  end of a shorter sequence. An `async` method or
  lambda runs in the block up to its first `await` of a task that may not be complete, and the rest
  of it runs later. That holds for an `await` in a `switch` section, in a `finally` block, in the
  condition of a `do`, or in the header of a `for`, `foreach`, `switch`, `while`, or `using` too.
  The `Dispose` at the end of a `using` or a
  `foreach` runs later as well when such an `await` comes before it in its scope. An `await using`
  awaits the task of each `DisposeAsync` that it calls, so the code after its scope runs later
  unless each of them is a method of yours that returns only complete tasks. An
  `await foreach` runs its body in the block only for an item that the sequence gives at once, and
  the code after it only when every item comes at once. An async iterator runs nothing until
  something enumerates it. A method of `SodaFlow.Functional` that gets a task and returns one, such
  as the `Map` of a `Task<Maybe<T>>`, awaits that task first, so its function runs in the block
  only when the analyzer knows that the task is complete.
- The serializers of `System.Text.Json`, Json.NET (also `FromObject` of `JToken`, `JObject`, and
  `JArray`), `XmlSerializer`, and the data contract serializers, and LINQ to XML, which call your
  getters and enumerate your sequences. `System.Text.Json` writes the properties of the type that
  the call, a `Type` argument written as `typeof`, or the property names, unless that type is
  `object` or has `[JsonDerivedType]`, and the others write those of the object. The analyzer also
  reads a non-public member with `[JsonInclude]` or with the `[JsonProperty]` of Json.NET, the
  public fields where the serializer writes them, and a converter that a `[JsonConverter]` or the
  `Converters` of the options names. `XmlSerializer` writes a property that it can set, and also a
  get-only property whose type is a collection other than an array. Json.NET writes only the
  members with `[JsonProperty]` of a type whose `[JsonObject]` says `MemberSerialization.OptIn`.
  Json.NET and `XmlSerializer` call a public `ShouldSerialize` method of a member before they write
  it. The data contract serializers write the members with `[DataMember]` of a type with
  `[DataContract]`, and the public members that they can set, without `[IgnoreDataMember]`, of
  each other type.
  Each serializer skips a member that has the ignore attribute of its own library, `[JsonIgnore]`
  or `[XmlIgnore]`, and reads one that has only the attribute of another library. A `[JsonIgnore]`
  with a `Condition` other than `Always` still reads the value to decide, so the analyzer reads that
  member.
  It reads `IncludeFields`, `IgnoreReadOnlyProperties`, and `IgnoreReadOnlyFields` where you create
  the options in the call. It reads the object that you give the serializer and the objects up to
  three levels inside it.

### Its limits

The analyzer does not replace the check at run time. Where it cannot be sure, it reports on the
careful side, so it can report a read that does not throw:

- A collection that it cannot follow item by item has the state of all its items. A list that
  holds one pending cell and one plain cell then reports a read of either.
- A `foreach` that sets a member of each item does not clear a pending value when the `foreach`
  runs inside another loop. It can also leave the value for a list that the code changes in a way
  other than `Add`, `Insert`, or `AddRange`, or that gets an item under a condition with no loop
  that adds on every pass, and for an item that an `AddRange` of anything but a literal put in.
- The `Updates` of a cell count as firing the first value of the cell too, unless the cell is a
  sink that the analyzer sees made. So a hold of the updates of a hold, switched as the close, can
  get `SF1002` although it returns.
- Serializer options that you do not create in the call, such as options in a local or a field,
  count as options that write public fields and read-only properties.
- A sample in the function of a task that `RunSynchronously` runs gets `SF1001`, although the task
  keeps the exception and the code after the call goes on. Only a later `Wait` or `Result` throws.
  The same holds for a sample before the first `await` of an `async` local function or lambda whose
  task nothing waits for.
- The function of a `Map`, a `Lift`, a collection view, or a `LazySample.Defer` also counts with
  the values that it read where you built the cell. If it read a pending value there, the first
  read of the cell gets a report, even when the code gave that capture or member a plain value
  before the read.
- A cell or a `Lazy` that a constructor or a method of an object builds into a member, when nothing
  made from the placeholder goes into that call. The analyzer does not read such a call, so it does
  not see the function of the value. A later read of the value gets no report, also after another
  method gave a member that the function reads a pending value.
- The `Result` of a task that you create with `new` has the value that its function returns even
  where no code runs the task. Such a `Result` waits forever, and the code after it does not run.
- A method that writes a member through an object that the analyzer does not know, such as an
  element of an array in a static field, can have written that member of any such object. So a
  read of the member through an element whose object the analyzer does not know, or through a
  local that got such an element, after a call that wrote that member, also gets what the call
  wrote. That is so even when the call wrote a
  different object.
- It reports a read under a condition, whether or not that path runs. A comparer that only a tie
  calls, and an `Equals` that only two equal hash codes call, are reads of this kind. So is the
  function of a `Merge`, which runs only when both streams fire in one transaction: what it returns
  counts as a value of the merge even where only one stream fires. Only a
  condition that is a constant is read for what it says: the analyzer skips the branch of an `if`
  and the arm of a `?:` that the constant rules out, the body of a `while` or a `for` whose
  condition is the constant `false`, and the second turn of a `do` whose condition is the constant
  `false`. It still reads a `switch` section that a constant rules out, and the code after an `if`
  on a constant whose branch always returns.
- A first read of a cell that the analyzer cannot be sure runs leaves the cell for the next read,
  which then runs the function again. That is a read in a branch of an `if` or an arm of a `?:`
  whose condition is not a constant, and a read in a `catch` or `finally` block. A read in a `try`
  block counts only for the code after the `try`, and only when the `try` has no `catch` clause. A
  read in a loop counts for the code after the loop only when the loop always runs its first turn:
  a `do`, a `while (true)`, a `for` with no condition or from a constant up to a larger one, and a
  `foreach` over an array literal with an item. Inside any loop, a read that each turn runs counts
  for the turns after it.
- Where a value can be one of several objects, a plain value set on it does not clear a pending
  one. An element counts as one object from a write through it to a read through it only in a
  straight line: in one function, with no loop, `goto` label, or `switch` section around the read
  alone, and with nothing between them but reads, operators on numbers, writes to locals, fields,
  and properties with no body, and calls of the platform or SodaFlow that get neither the collection
  nor a function other than a lambda written there. When the collection is a local that got the
  array or the list in a field, a call of one of your methods can come between too, if its body
  holds only such code. That body can call one more method of yours of the same kind. A new value
  for the collection, the index, or the element ends it, and so does a read that runs a function you
  wrote: a sample, a `Value`, or a `CanExecute` of a cell whose function the analyzer saw you give
  it, or the `Value` of a `Lazy<T>` whose function it knows. Names of one object hold the same way,
  from the assignment that makes them one, and a call of one of your methods with such a body can
  come between them if it gives none of the names a new object. A conversion of your own makes a new
  object, so it gives no second name.
- The objects that calls of your methods return share one state for each member, where the
  analyzer did not see the object made. A write to a member through the result of one call adds to
  what writes through other calls gave that member, and does not replace it, because the analyzer
  cannot tell whether two calls give the same object, even two calls with the same text. So a plain
  value written into `Find(id).Cell` after a pending one still reports a read of `Find(id).Cell`.
  A local that holds the result is one object: writes through the local replace each other, and a
  read through it sees only them. Such a write still goes into the shared state, so a read through a
  second call, or through a local of a second call with no value of its own, sees it, even where the
  two calls give different objects. The state is shared across the names of one property as well: a
  property of a class and the interface properties that it implements, or two interface properties
  that one property of a class in your project implements. A pending value written through one name
  then reaches a read through another name on the result of any call. The exception is a call of a
  method that is not virtual and whose body only returns an expression of a sealed type: the
  analyzer then knows the type of the result, and a name that this type does not have does not
  reach it.
- A handler removed with `-=` still counts as a handler of the event, unless the code adds and
  removes that one handler in a straight line. The handler must be a local that holds one lambda,
  and the code must add it to and remove it from a local delegate, or an event of a local that
  holds an object made with `new`. A local delegate can have any code between the add and the
  remove. An event of such an object can too, as long as no code before the remove uses the
  object other than to add or remove handlers, and no constructor that `new` runs passes `this`
  on. Otherwise, nothing can come between the add and the remove. An event of `this`, of a field,
  or of a parameter never qualifies.
- A sample of a hold gives only the initial value where the hold is made and sampled in the
  transaction of the block: in the block's own code, in a function that `Transaction.Run` runs, in
  the function of a `Map` of a `Cell.Constant`, or in a local function, lambda, or method that such
  code calls or passes to a call that runs it there. A read through a `Map`, a `Lift`, a `Calm`, an
  `AsCell`, a bindable, or a `Cell.ConstantLazy` of a `SampleLazy` counts where the same read of the
  hold counts. A lambda written at a call counts only where the call is one of those two, a method
  of your own, a method of an array or a `List<T>` that runs it, such as `ForEach`, `Find`, or
  `Sort`, or a LINQ method that runs it in the call. A method that only keeps the lambda, such as
  `Add` or `Array.Fill`, does not count. A LINQ query such as `Select` counts only where the same
  code enumerates it in the transaction: a `First` or a `ToList` on it, a `foreach` over it, or a
  local that holds it and that only such code reads. A hold that a method reads from its own object
  counts where the code made that object with `new` and gave it the hold in the constructor of a
  positional record, or in a readonly field or a get-only property that a primary constructor sets.
  A hold that the code reads from a list that you write as a literal, at a constant index, counts as
  the hold itself. So does a sample in the function of a `Lazy<T>` whose `Value` the code of the
  transaction reads. A sample in a lambda written at any other call, such as a `Map` of a stream, or
  of a hold kept in a member that code can set again, in a list that code can change, or in a list
  at an index that is not a constant, gets each value sent into the sink as well. A close with such
  a sample can then get `SF1002`, although the sample runs before the end of the transaction.

Some reads that throw it does not see:

- Reflection, such as `Activator.CreateInstance` or `MethodInfo.Invoke`. A value made from the
  placeholder that you pass to one of these gets `SF1003`. The analyzer follows two literal forms:
  `typeof(T).GetMethod("M").Invoke(obj, [a])`, where `T` and its base types have one public method
  named `M`, which is not generic and not virtual and takes those arguments, and
  `Activator.CreateInstance(typeof(T), a)`, where one public constructor of `T` takes that many
  arguments. The name can be a string or a `nameof`, and the arguments must be written in the call,
  one by one or as an array or a collection expression. Arguments in an array held in a local are
  not followed, and a value there gets `SF1003` when the local's own block puts it in: in the
  declaration, by an assignment to the local or to an element, or by `Array.Fill`. It reads those
  puts in order: a statement of the block that runs before the call and sets the local again, or
  sets an element at a constant index, replaces what was there, so a plain cell set over the value
  clears it. A put in a branch or a loop, or after other code used the local, only adds to what was
  there. The same holds for a local of type `object`. A `ToArray` or a `ToList` of such a local, or
  of a local `List<T>`, counts as the local, and an `Add`, an `Insert`, or an `AddRange` of that
  list counts as a put. A parameter that gets such an array counts as what each call of its method
  gives it, and the analyzer then reads what that method puts into it. A method of yours that gets
  such an array, and sets one of its elements to one of its own parameters, counts as a put of what
  the call gives that parameter. A value that reaches the array another way, such as a value that
  the method makes itself or that a call inside it puts there, gets no report at all.
- A method of a library other than the platform and SodaFlow that keeps a function and runs it
  later.
- A static initializer of a class with no static constructor. The runtime can run such initializers
  at any time before the first read of a field, so the analyzer does not read them.
- A function that the block sends into a sink, where an `Apply` of that sink, or a
  `CollectionEdit.FromUpdates` of it, runs it. The analyzer does not read what such a function
  returns, so a cycle through its result gets no `SF1002`.
- A new element that code adds to a list with `Add` through a local that got the list from a
  field, read through the field.
- A write into an element of an array in a field that a method you call makes through a `ref`
  local, through `Array.Copy`, through an array that a call returns, or through a second field that
  holds the same array, read through a local that got the array before the call.
- A write through a span, a memory, or a segment that a method gets as a parameter, that
  `CollectionsMarshal` or `MemoryMarshal` makes, or that a lambda or a local function uses. The
  write does not reach the array.
- A handler that the code adds to an event of an object through one name, when code raises the event
  through another name of that object. That happens when a constructor passes `this` on, as into a
  static list, and code later raises the event through an element of the list.
- A write to a member through `this` in a constructor, when the constructor also gave `this` to a
  name that the caller reads, such as an element of an array that it gets. A read through that name
  after the constructor returns still sees the value from before the write. Inside a method or a
  constructor, a local, a field, or an element at a constant index that gets `this`, also as an
  element of a collection expression such as `[this]`, is the same object as `this` until code gives
  it a different value.
- A write in the function of a `Map`, a `Lift`, or a collection view that the analyzer did not see
  you build, such as a cell in a field that a read in the block runs for the first time. A function
  that you give to `Map` or `Lift` should have no side effects in any case.
- A member that the platform calls from a method that is not in its table of common methods.
- A converter of serializer options that you do not create in the call, a converter that a
  `JsonConverterFactory` makes, the settings of Json.NET, and an object more than three levels
  inside the one that you give a serializer.
- An interpolated string that becomes a `FormattableString` or an `IFormattable` and that code
  formats later. It calls no `ToString` where you write it, and the analyzer reads none there. The
  analyzer does read the `ToString` calls where `FormattableString.Invariant` gets the string.
- A function given to a `Map`, a `Lift`, or a collection view that reads a value only through an
  implicit conversion of your own, with no cast and no call in the function, or only through a
  compound assignment operator, such as `operator +=`, that a type of yours declares.
- A function that a constructor from outside your project keeps, other than a `Lazy<T>`, a
  `ThreadLocal<T>`, the comparer of a platform collection, or a `Task` that `RunSynchronously` runs.
  The analyzer does not read what the function of a task does where `Start` runs it on another
  thread, although it sees the value that the function gives to `Result`. It does not see that
  value for a task from `Task.Run` or `Task.Factory.StartNew`.
- Code after an `await` of a task that is already complete when the analyzer cannot tell that it
  is. That code runs in the block, and the analyzer reads it as code that runs later. The same
  goes for the body of an `await foreach` over a sequence other than a call of your own async
  iterator, also through `WithCancellation` or `ConfigureAwait`, or an enumerator object of yours
  whose `MoveNextAsync` returns only complete tasks, and over an iterator whose first
  `yield return` is not at the top level of its body. The analyzer reads the `DisposeAsync` of the
  enumerator at the end of such a loop as code that runs later too. The analyzer knows the complete
  tasks that the platform makes, such as `Task.CompletedTask` and `Task.FromResult`, a local, a
  `readonly` field, or a get-only property whose initializer is such a task, and a method or a
  getter of your own that returns only such tasks. An awaiter of your own whose `IsCompleted` always
  returns `true`, and is not virtual, is complete too.
- Two calls of your methods that give the same object, in two cases. A read through a local that
  has a value of its own, written through that local, does not see a later write through a second
  call. And when a method returns a sealed class, the result of a call that comes before the first
  write through a result of a call counts as a new object, so a read through it does not see that
  write.
- A delegate that one of your methods returns as an element of a tuple. The analyzer loses the
  function of the delegate there, whether the code deconstructs the tuple or keeps it in a local.
  A call of it then counts as code that the analyzer cannot read: a value made from the placeholder
  that you pass to it gets `SF1003`, and what the function writes, such as a capture that another
  function of the method reads, is not seen. The same delegate returned alone, or in a tuple that
  the block itself makes, is read.
- A chain of your own calls more than six calls deep. There, the analyzer reports `SF1003` at the
  call where it stops. The same happens past a budget of 512 bodies of methods, constructors,
  getters, and lambdas that it reads for one block, at the call that would go over it.
- A method that calls itself, directly or through other methods. The analyzer reads such a call,
  and stops at a later one that gives the method the same kind of values in its arguments and in
  the members of its object as a call that it is still reading. There, it reports `SF1003` for a
  value made from the placeholder, as it does at the depth limit.
- A virtual method called on an object that the analyzer did not see built, where your project
  overrides the method in a class that the object can be. The analyzer cannot tell which body
  runs, so a value made from the placeholder that you pass to it gets `SF1003`. A call with no
  receiver or on `this`, inside a method of an object, runs the override of the class that the
  analyzer saw the object made with. Where it does not know that class, it reads the method of each
  class that the object can be, so it can report a read that only one of them makes.
