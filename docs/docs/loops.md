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
`var marked = placeholder.AsCell();`, and a mark that a local with no initializer gets in its one
assignment.

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
block, where both are statements of one block or of one `switch` section. A local with no
initializer counts the same way, from the last assignment before the block. That assignment, or the
declaration, can be a deconstruction of a tuple literal, such as `(block, n) = (Build, 1)` or
`var (block, n) = (Build, 1)`, also of a tuple literal in a tuple literal, or of a local that holds
a tuple literal, which gives the local the element at its place. A block whose function the
analyzer cannot find this way, such as a delegate from a field or a parameter, a local that gets
its function under a condition, a local that a different deconstruction gives a value, such as one
through a `Deconstruct` method, or a local that a lambda or a local function assigns anywhere in
that block, gets `SF1003`, because none of its code is checked. So does
a virtual or override method given by name, unless you give it through `base` or on a value of a
sealed type, or it is a sealed override: an object can override it with a body that the analyzer
does not see. When that code calls your own methods, constructors, local functions, or property
getters, the analyzer reads them too.

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
  after you build the cell counts too. That includes a static member that the function reaches only
  through code of yours that it runs, such as a method, a getter, an indexer, an operator, a
  conversion, or a constructor, also one called on its parameter. What the function writes there
  counts for the code after the read. A second read of the cell does not run the function again, when the analyzer knows that
  the first read ran (see the limits below). Where one call builds two or more cells, as in a loop,
  in a lambda such as the selector of a `Select`, or in a helper that the analyzer reads one time
  for two or more calls, the first read counts only for the name that the code reads through: a
  local, a parameter, a field of the object, or an element of a local at a constant index, as in
  `cells[0]`. A read of a second cell of the same call, through a different name, runs the function
  then. Collection views work the
  same way. Their predicates, selectors, sort orders, and comparers run when something reads the
  view. The eviction handler of a `Map` on a collection runs when a key leaves the view, and in a
  call of `Dispose` for each object the map built. The analyzer reads the handler at a `Dispose` in
  the block, also one through `IDisposable`, through a list that holds the map, or through a field,
  unless the code uses the map for nothing but that `Dispose`. A map that goes straight into a field
  counts as one that built its objects, because code that the analyzer does not read can use it
  there. A lambda that a method of a collection runs for its items, such as `List<T>.ForEach`, gets
  each item, so a `Dispose` there reads the handler of each map that the list holds.
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
  block sets. A write to a static field of a class that is not generic replaces what the field held,
  where the write always runs: in the block, in a method that the block always calls, or in the
  function of a `Map` at a first read that always runs. The variable of a `catch` clause gets each
  object that a `throw` in its `try` block, or in a method or a lambda that the block calls, can
  send to that clause. That includes a lambda that a method of the platform, such as
  `List<T>.ForEach`, runs in the call. An object that the code of a LINQ query or of an iterator
  throws goes to the clauses around the code that enumerates the query, and not to those around the
  code that makes it. `ExceptionDispatchInfo.Throw` of an object, and `Throw` of
  `ExceptionDispatchInfo.Capture` of an object, count as a `throw` of that object. An `async`
  method, local function, or lambda puts the object into its task instead, so the clause gets it
  only where the code waits for that task: around an `await` of the task, also of a local that holds
  it, in parentheses, or through `ConfigureAwait`, `AsTask`, `WaitAsync`, `WhenAll`, or the task
  that an `await` of `WhenAny` gives, around `GetResult` of its awaiter, around an `await foreach`
  over the sequence that an async iterator gives, or around an `await using` whose `DisposeAsync`
  throws it. An `await` of `WhenAny` itself gives the task and throws nothing. A task that
  `RunSynchronously` runs keeps the object as well, and so does the task of a `ContinueWith` that
  runs its function in the call, as described under code that waits. The runtime throws a
  `TypeInitializationException` in place of an object that a static constructor throws, and a
  `TargetInvocationException` in place of one that a method throws through `MethodInfo.Invoke` or a
  constructor through `Activator.CreateInstance` or `new T()` of a type parameter. The
  `InnerException` of such an exception has the object, and so does the `InnerException` of an
  exception of the platform that you create with an inner exception, and each of the
  `InnerExceptions` of an `AggregateException`, also through `First` or another LINQ method that
  gives one element. `Flatten` and `GetBaseException` of such an exception have the object too.
  `GetResult` of the awaiter of a task throws the object that a function of the task threw. An
  `await` of the task of `Task.FromException` or `ValueTask.FromException`, and of the task of a
  `TaskCompletionSource` after `SetException` or `TrySetException`, throws that object too, and so
  does `GetResult` of its awaiter. `Wait` and `Result` of such a task throw an `AggregateException`
  whose `InnerException` and `InnerExceptions` have the object, and so do `Wait` of a task from
  `Task.Run` whose function throws it and `Parallel.Invoke` of such a function. The `Exception` of
  the task has the object the same way. The filter of the `catch` clause gets the object too, and a
  local that the filter sets keeps it. The analyzer reports no read in a filter, because an
  exception there is lost and makes the filter false.
- Collections, arrays, and dictionaries. A list, an array, or a span that you write as a literal
  keeps each element apart, also where the literal spreads a second such local, so `cells[0]`,
  `cells?[0]`, `cells[^1]`, `cells.ElementAt(1)`, and each element of a list pattern in an `is` or a
  `switch` on `cells`, also after `and` or in parentheses, are each one element. A list pattern
  inside a list pattern knows the positions of the inner collection wherever a read such as
  `cells[0][1]` knows them, also after a write through two indexes or through a second name of the
  inner collection. Otherwise, it gives each of its variables every element of the inner collection.
  An element of a range with constant bounds, as `cells[1..][0]`, is one element too. The arguments
  that a call that runs one time gives one by one to a `params` parameter keep their positions in
  the same way. A positional pattern on a tuple gives each part its own item. A `Dictionary` that
  you write with constant keys and no comparer, and that the code then only reads, keeps the value
  of each key apart, so `cells["total"]` is that one value. A key of the type `object` has the type
  of its constant too, so the `int` 1 and the `long` 1 are two keys. `Array.Fill` puts its value
  into each element that it fills, as an assignment to that element would, and `Array.Reverse` of a
  whole array moves each element to the other end. A `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`,
  `ReadOnlyMemory<T>`, or `ArraySegment<T>` that a method makes from an array in one of its locals
  or parameters, with an implicit conversion, `AsSpan`, `AsMemory`, a constructor, `Slice`, or a
  range, is another name for the elements of that array, as long as neither name gets a second
  value. That holds as well where the code makes the view and writes through it in one expression,
  as in `cells.AsSpan(1)[0] = value`. `Fill`, a write to an element, through a `ref` local of an
  element, or through the `ref` variable of a `foreach` over the view, and `CopyTo` into it put
  their values into the array, and so do `CopyTo` into the array itself and `Array.Copy`. Where each
  step from the array to the view starts at a constant, as `AsSpan(1)` or `[1..]` does, a write to
  an element at a constant index and a `Fill` reach only the elements of the array that they write.
  Otherwise, they reach every element. A write through the `ref` variable of a `foreach` adds its
  value to every element of the array and does not replace what each one held. The span that
  `CollectionsMarshal.AsSpan` makes of a local `List<T>` is another name for the elements of that
  list in the same way, and a write through it reaches every element. A span that
  `new Span<T>(ref local)` makes is another name for that local. A `ReadOnlyCollection<T>` that a
  method makes of a `List<T>` in one of its locals or parameters, with `AsReadOnly` or with `new`,
  is another name for that list, so a read through it sees each later put into the list, until the
  local gets a different list. A write through a `ref` that an indexer, a property, a method, or a
  local function of yours returns goes where that `ref` points: a parameter, a local of the code
  around a local function, a static field, or a member of the object of the call or an element of
  that member. So does a write through a `ref` local of such an indexer, and a write through a
  conditional `ref` goes to both of its locations. A write through a `ref` local of a conditional
  `ref`, or through a conditional `ref` that a method or a local function returns, goes to the
  location that a constant condition selects, and to both locations where the condition is not a
  constant. A write through a `ref` that a method returns, to a
  static field or to an element of an array parameter, adds its value to what the location held and
  does not replace it, so a plain cell written that way over a pending one still counts as pending.
  A write through a `ref` local of a static field replaces what the field held, as a write to the
  field itself does. Two `ref` or `in` arguments of one call that are one location, such as `ref c`
  twice or `ref a[0]` and `in a[0]`, are one location in the method too, so a write through one
  parameter is a write through the other. A write to a member through an element of a local array,
  `List<T>`, or `Dictionary` created with no comparer, at a constant index or key or at an index in
  a local, replaces what an earlier write gave that member. So does a write through a local that got
  that element, and a write through any other name of the object in the element, such as `d` after
  `a[0] = d`, when the analyzer knows which object that is. A later read through the element or the
  local sees only the last value. An index in a local that gets only a constant is that constant
  index. A local that gets the array or the list in a field, or in a static property with no body,
  sees each element that code puts in through the field later, also in a method and through a `ref`
  local of a field of such an element at a constant index or at an index in a local, until the local
  or the field gets a different value. The other way also holds: a read through the field sees an
  element that code puts in through such a local, at an index or with `Add`, `Insert`, or `AddRange`
  of a `List<T>`.
- Names of one object. Where one name gets its object from another, as in `a[0] = d`,
  `box.Slot = Shared`, `var slots = SharedSlots`, or `list.Add(d)`, the two are names of one object
  from there. A name is a local, a parameter, a static field or a static property with no body, a
  field or such a property of the object in a local, a property whose get accessor only returns one
  field and whose set accessor, where it has one, only writes it, the result of a method of yours
  with no parameters that only returns a field, or an element of a local collection or of an array
  in a static field, at a constant index or key or at an index in a local. A static property whose
  get accessor only returns such an element of a static array is that element. A local that a method
  of yours gives a field of its object through an `out` parameter is a name of that field, where
  each statement of the method only gives an `out` parameter a field or a literal. A `Deconstruct`
  method of yours of that form gives each part in the same way. An element of a second local that
  got the collection from the first is the same element, as long as neither local gets another
  value. A local that a lambda uses is a name as long as no lambda assigns it, and a `ref` local
  that code gives no other location, and assigns only with `=`, is a name of the location that it
  refers to. So is a `ref` field of a `ref struct` in a local, where a constructor of yours sets the
  field to a `ref` parameter and the code passes it `ref` of a local: the field is a name of that
  local, as long as the code gives the struct local no other value and the field no other `ref`. A
  local that gets a field of `this`, as in `var slot = this.slot;`, is another name of that field
  until code gives the local or the field a different object. A call can do that to a field that is
  not `readonly`, or to any field of a struct, so for such a field a call between the two ends this.
  After that, and after a local that gets the field only in a branch, a write through either name
  still reaches a read through the other. A write to a field or a property with no body through any
  of the names replaces what earlier writes through all of them gave that member, and a read through
  any of them sees the last value, while each name keeps the object. A virtual or abstract property
  counts as having no body only where no override in your project gives it one. A local that code
  takes a `ref` to anywhere in its function, as in `ref d` or `new Span<T>(ref d)`, is no such name,
  because the `ref` can give it a new object at any time. A write through an expression that is no
  such name, such as a conditional expression, reaches a later read through each name whose object
  the analyzer cannot tell apart from the object of the write. It tells two objects apart where the
  write goes to an object that the code creates after the name got its object, through the creation
  itself or through a local that only that creation gives a value, and where it goes through a
  private `static readonly` field that a creation fills and whose object your code never hands on:
  each use of the field reads or writes a field or a property with no body. A write in a method that
  the block calls counts as well, where the block always calls the method one time and the method
  writes through a static field, an element of an array in one, or a parameter whose argument is
  such a name. So does a write through one of the names in a lambda or a local function that the
  block itself calls: it reaches the other names, but it does not replace a write through a second
  name, and a write through a second name does not replace it. So does a write through `this` in a
  method that the block calls on one of the names, also an assignment to `this` in a method of a
  struct, and a read through `this` in such a method sees the last value too. Where such a method
  writes a member through `this` and then through a second name of the same object, such as a static
  field, an element of an array in one, or a parameter, the later write is the one that counts.
  Where the analyzer cannot tell that the second name holds the same object, both writes count. A
  new object that goes into a name with an object initializer, as in
  `slots[0] = new Slot { Cell = value }`, gives the name only the values that the initializer sets,
  also where such a method puts it and keeps it there to its end. A method or a local function that
  always gives its `ref` or `out` parameter a value replaces what a local, or a field of the object
  in a local, held before, where the block always calls it and passes no second `ref` or `out`
  argument. A constructor that puts `this` into a name makes that name a name of the new object, as
  a registry does with a static field or a static property. So does a constructor that does this
  through a method of the object that it calls on `this`, also by its name alone or through a second
  such method, to the depth that the analyzer reads calls. The name can also be an element of an
  array or a list that the constructor gets, a value of a dictionary that it gets, or a field of an
  object that it gets. It can be an element of an array or a list in a static field too, at a
  constant index, by the name of the field or as an argument, where the constructor always puts
  `this` there and the calls up to the code that reads the element always run. Where it puts `this`
  there only on one path, at an index that is not a constant, or in a call that does not always run,
  each element of the collection can be the new object. A list in a static field that the
  constructor adds `this` to, by its name or as an argument, holds the new object from then on, also
  for a method that reads the list later: an event that code raises through an element of that list
  runs each handler that the code added through the new object. Inside a method or a constructor,
  each name of one object that the list above gives, when it gets `this`, also through `Add` or as
  an element of a collection expression such as `[this]`, is the same object as `this` until code
  gives it a different value.
- Your helpers, constructors, property getters, records, and object initializers, and a call
  through an interface to an object that the block built, or to one that a `readonly` field or a
  get-only property creates in its initializer. That includes the `Equals`, `GetHashCode`,
  `ToString`, and `Deconstruct` that the compiler writes for a record, also where you call one by
  name, a partial method or a partial property, read from the part that has its code, a property
  that uses `field`, a member of an extension block, also in its static form, in a property
  pattern, given by name as a function, and where it writes through a `ref` receiver, a static
  member of an interface called through a type parameter, also an operator such as `a + b`, and the
  constructor that `new T()` runs, where the call says the type of `T`. A call of a partial method with no part that has code reads
  nothing, not even its arguments, because the compiler removes it.
- Events and delegates: the handlers added with `+=` run where the event is raised, and a delegate
  made with `+` or `Delegate.Combine` runs each of its functions. Each element of the
  `GetInvocationList()` of a delegate is one of those functions. An element at a constant index is
  the one function at that place where a local holds the list and the analyzer knows the functions
  in order: the delegate is a lambda, a method given by name, or a `+` or a `Delegate.Combine` of
  two such delegates, directly or through locals that get no other value. `Sample` given by name,
  as in `Transaction.Run(cell.Sample)` or `Select(CellExtensionMethods.Sample)`, reads its cell
  where the delegate runs, as a lambda that samples the cell does.
- Loops: each pass of a `for`, `foreach`, `while`, or `do`, of a `goto` back to a label, or of a
  `goto case` or `goto default` back to an earlier section of a `switch`, is read until a pass adds
  nothing new.
- Sinks: a value that the block sends into a sink reaches each value made from that sink, also one
  made before the send, such as a `Hold` of the sink or a switch of that hold. A function that the
  block sends counts too, where an `Apply` of the sink or a `CollectionEdit.FromUpdates` of it runs
  the function: the analyzer reads what it returns, with the code around it as that code is at the
  end of the block, so a cycle through its result gets `SF1002`. So does a value sent
  in a function that `Transaction.Post` runs after the transaction: a lambda, a local function, a
  method that you give to `Post` by its name, or a post inside a posted function. The same holds for
  an action that you post to any `IBindingScheduler`, and for a write to the `Value` of a
  two-way bindable of a sink, which sends the value into that sink after the transaction.
  `Execute` of a bindable action sends its parameter into the sink of its firings after the
  transaction in the same way, unless its enablement is `Cell.Constant(false)`, written in the
  creation call or in a local that no code writes, because the action then sends nothing. Both
  hold wherever the code keeps the bindable or the action: in a local, a field, a property of an
  object, a parameter, or the result of a method. Outside a
  local that the creation call made, the sink must be one that the block made. The analyzer also
  reads the `Post` of a scheduler of your own where it knows the class of the scheduler, because
  such a `Post` can run the action at once.
  A handler that you add to an event of a bindable runs after the
  transaction too, because the bindable raises its events through its scheduler. So does each
  result that the operation of `MapAsync` gives to `FromValue` or `Construct`, which the pipeline
  sends into the sink of results. The analyzer reads what the operation returns, also through a
  method that it calls, with each value that the source fires as its input. A value that `Execute`
  of the status gives the operation is such an input too, where a local holds that status.
- Members that C# calls with no call syntax: an indexer, also its setter in an object initializer
  such as `new Slots { [0] = value }`, an operator, a conversion, also of each
  element of a spread, of each element of a tuple that the code converts to a different tuple type,
  also with a cast or to a nullable tuple type, or deconstructs, and of each part that a
  `Deconstruct` method gives to a variable of a different type, `operator true` or `operator false`
  before a user-defined `||` or `&&`, a setter, an event accessor, `Deconstruct`, `ToString` in an
  interpolated string that the code formats at once, which is `TryFormat` of `ISpanFormattable` for
  a value that implements it, and otherwise the `ToString` of `IFormattable` for a value that
  implements that, the constructor, the `AppendLiteral`, and the `AppendFormatted` of an
  interpolated string handler of your own, which gets each value in place of that `ToString`, and no
  append where that constructor sets `shouldAppend` only to the constant `false`, `Dispose` at the
  end of a `using` statement or a `foreach`, and at the end of the block of a `using` declaration,
  from the last declaration to the first, `DisposeAsync` at the end of an `await using`, also of a
  resource
  in `ConfigureAwait`, `GetAwaiter` at an `await`, with `IsCompleted` of its awaiter and `GetResult`
  where the analyzer knows that the awaiter is complete, the enumerator of a `foreach`, the `Add` of
  a collection initializer or a collection expression, the `Create` method of a `CollectionBuilder`,
  `Count` and `Slice` for an index from the end, a range, or a list pattern, the `ToString` and the
  copy constructor of a record, the `ToString` of each value that a record or a tuple prints, the
  `Equals` or `GetHashCode` of each member that the `Equals`, `GetHashCode`, `==`, and `!=` of a
  record or an anonymous type call, but not where the `==` or `!=` of a record gets `null` on the
  right or two names that hold one object, which return at once, the `ToString` of each value that
  an anonymous type prints, and the `==` of each element of a tuple comparison. The explicit static
  constructor of a class runs, with the static initializers of that class, once in the block, at the
  first use that runs it: the first object of the class or of a class derived from it, also one that
  `new T()` or `Activator.CreateInstance` makes, or the first read, write, or call of a static
  member, through the name of the class or through a simple name in the code of another class, such
  as a name that `using static` gives. A struct runs its static constructor the same way, except
  that of its objects only a call of a constructor that the struct declares runs it: `default`, and
  `new S()` or `Activator.CreateInstance<S>()` where `S` declares no constructor without
  parameters, do not. Each type argument of a generic class, as in
  `Cache<int>` and `Cache<string>`, has a run of its own. A member of a nested
  class runs only the static constructor of the nested class, and a delegate of a static method runs
  it where something calls the delegate, not where the code makes the delegate. A first use that the
  code can skip, such as one in a branch or in an arm of `?:`, counts only for itself, so the
  analyzer reads the constructor again at the next use. A use in each turn of a loop counts for the
  rest of the block only when the loop always runs its first turn, by the rule for an `await` below,
  and the code around the loop always runs it: each loop around it has a first turn too, and no
  branch, no `try` without a `catch`, and no call in a branch holds it. A use in any other loop
  counts only for itself, because the loop can run no turn.
- A value of the type `dynamic` whose object the analyzer saw made. It reads the method, the getter
  or the setter, the indexer, the event accessor of a `+=` or a `-=`, the operator, also of `++`,
  `--`, and a compound assignment, the conversion, with or without a cast, the `operator true` or
  `operator false`, the enumerator of a `foreach`, and the `GetAwaiter` and `IsCompleted` of an
  `await`, that the runtime binder finds on the type of that object. It picks a method as the binder
  does: by the names and the number of the arguments, with the optional parameters and a `params`
  parameter that takes each argument from its place on as one element, and by the type
  of each argument whose type the code knows, and it reads none where two methods remain. A
  conversion can have a standard conversion before or after it, as in the compiler. The analyzer
  does not read `GetResult` after an `await` of a dynamic value.
- Members that the platform calls for you: `CompareTo` in a sort, `Equals` and `GetHashCode` in a
  search, a set, or a dictionary, a comparer that you give a collection, `ToString` in `string.Join`
  or `string.Format`, the `ToString` of each part that a `KeyValuePair`, a `DictionaryEntry`, or a
  `Tuple` object prints, and the `ToString` of the value of a `Lazy<T>`, which its own `ToString`
  calls. A method with a format string, such as `string.Format`, calls `TryFormat` of a value that
  implements `ISpanFormattable`. It calls the `ToString` of `IFormattable` where
  `TryFormat` returns false, and to pad the text for a positive width, such as `{0,5}`. The analyzer
  reads that `ToString` unless the format is a constant with no positive width and the `TryFormat`
  of the object returns only `true`.
- Members that SodaFlow calls for you: `CompareTo` of each sort value of a collection view that
  sorts with no comparer, at the first read of the view, and `Equals` of the comparer of a two-way
  bindable, when you write its `Value`. `RunTimersTo` of a clock built on
  `TimerSystemImplementationBase` runs, in the call, each callback that `SetTimer` gave the same
  clock before it, unless both times are integer constants and the time of the callback is later.
  The clock can be in a local, a field, a property, or a parameter, where the analyzer saw it made,
  and the two calls can be in different methods. A timer that a method of yours sets runs, in the
  code that called that method, as a timer that this code set does. A method of yours runs the
  timers that its caller set only where its call of `RunTimersTo` runs on every path of that method,
  and where each call of a method between the code that set the timers and that method runs on every
  path too. The same holds for a method of yours that disposes a map or a timer system that its
  caller made. A call in a `finally` block or in a `try` block with no `catch` runs on every path,
  and so does a call in the `else` of an `if` on a constant, in the `switch` section that a constant
  or a lone `default` selects, after a `goto` to a label before it, and in a loop or a
  `List<T>.ForEach` that always runs at least once. A condition on a parameter whose argument is a
  constant counts as that constant, and so does a parameter with a default value that the call
  leaves out, unless the method writes the parameter. A test for `null` of a value that the analyzer
  saw made counts as not `null`, so `if (clock == null) return;` and `clock?.RunTimersTo(0)` run the
  timers. A condition that the arguments do not decide, such as one on a field, keeps the call from
  counting.
  A `Cancel` or a `Dispose` of the timer before the run, or the end of a `using` statement of it,
  takes the timer away when it runs on every path, by the same rule, also in a loop that always
  runs at least once. In a `try` block it takes the timer away at the end of the `try`, so a run of
  the timers in the `finally` block still runs it. A `Cancel` under a condition that is not a
  constant keeps the timer. A `Cancel` in a lambda, a local function, or a method of yours takes the
  timer away only where each call of that code, up to the block, runs on every path too, and not in
  a `try` block. The timer can be a parameter, an element of a list, or a local that holds one,
  where the analyzer knows that the value is that one timer.
  A `TimerSystem` calls `Start` and `Now` of its implementation when you construct it. Disposing it
  inside a transaction calls `Stop` only after the transaction releases its lock, so a `Dispose` in
  the block runs no `Stop` there, and the analyzer does not read it. The construction of a
  `SystemClockTimerSystem` calls its `now` function.
  `Debounce` reads the `Time` of its timer system, and calls its `At`, when you call it.
- Code that waits. The analyzer reads a LINQ query or an iterator where something enumerates it, the
  function of a `Lazy<T>` or a `ThreadLocal<T>` where something reads its `Value` (a `Lazy<T>` runs
  it at the first read only, as a cell does: see the limits below), and the function
  of a `Task` that you create with `new` where `RunSynchronously` runs it. The `Result` of such a
  task, and `GetResult` of its awaiter, have the value that the function returns, however the task
  runs. A `ContinueWith` with the option `TaskContinuationOptions.ExecuteSynchronously`, written as
  a constant, on a task that the analyzer knows is complete, runs its function in the call, so the
  analyzer reads the function there. The task of the continuation keeps the exception of a read in
  that function, so the analyzer reports such a read only where the code waits for that task: with
  `Wait`, `Task.WaitAll`, `Result`, or `GetResult` of its awaiter. An `await` of the task, also
  through `ConfigureAwait` or `WhenAll`, puts the exception into the task of its `async` function,
  and the report comes where the code waits for that task. A continuation that nothing waits for
  gets no report. A query expression over a type whose methods run their function in the call,
  such as `from x in maybe select ...` over a `Maybe<T>`, runs where it is written. For a query
  over a type of your project whose methods for the clauses return no sequence, the analyzer
  reads each of those
  methods as a call where the query is written, with the result of the clause before it. Each
  expression of a clause is a lambda that the method gets, so the analyzer reads it where the code of
  the method runs that lambda, with the range variable that the method gives it, and not where the
  method only keeps it. Where the code takes only one item from the call of an iterator, with
  `First`, `FirstOrDefault` with no
  predicate, `Any()` with no predicate, `Take(1)`, `ElementAt(0)`, or `ElementAtOrDefault(0)`, also
  after a `Select`, an `AsEnumerable`, or a query expression with only a `select` clause, or with a
  `foreach` that leaves the loop in its first pass on every path, by a `break`, a `return`, a
  `throw`, or a `goto`, the iterator runs only up to its first `yield return` and the `finally`
  blocks around it. For a `yield return` in a loop, the code after the loop is skipped only when the
  loop always runs its first turn and no `break` or `continue` of the loop comes before the
  `yield return`, the same rule as for an `await` below. The code after an `if` is skipped when the
  `if` has an `else` and each branch stops at a `yield` with no `goto`, `break`, or `continue`
  before it. The code after a `switch` is skipped when each section stops at a `yield` before its
  `break` and one section takes every value, the same rule as for an `await` below. A label after
  the `yield return` keeps the code after the label, because a `goto` before the `yield return` can
  go there. A `yield return` in one branch only skips nothing after the `if`. Where the code takes
  no item, with `Take` of zero or of a negative count, the iterator does not run at all. The
  analyzer still reads the whole iterator where the code moves its enumerator by hand, where a
  method of yours gets the call and takes one item, and where `Zip` stops at the end of a shorter
  sequence. An `async` method or lambda runs in the block up to its
  first `await` of a task that may not be complete, and the rest of it runs later, also where its
  body is a single expression, such as `async () => await t + x`. That holds for an `await` in a
  `switch` section, in a `finally` block, in the condition of a `do`, or in the header of a `for`,
  `foreach`, `switch`, `while`, `using`, or `lock` too. It also holds for an `await` in the branch
  of an `if` or the arm of a `?:` that a constant condition selects, and in the one `catch` clause
  of a `try` whose block starts with a `throw`. The `Dispose` at the end of a `using` or a `foreach`
  runs later as well when such an `await` comes before it in its scope. An `await using` awaits the
  task of each `DisposeAsync` that it calls, so the code after its scope runs later unless each of
  them is a method of yours that returns only complete tasks. That code is the rest of the block
  around the `await using`. Where that block is the body of a loop or a `switch` section, the code
  after the loop or the `switch` runs later only when every way to it passes the `await`, and the
  same holds for an `await` or an `await foreach` there. For a loop, that means the loop always
  runs its first turn and no `break` or `continue` of the loop comes before the `await`. The loops
  that always run their first turn are a `do`, a `while (true)`, a `for` with no condition, or one
  that counts up from a constant with `<` to a larger constant or with `<=` to one that is not
  smaller, and a `foreach` over an array with an element: an array literal with an item, a
  collection expression of an array type with an element that is not a spread, an array creation
  whose sizes are constants larger than zero, or a local with such a value that no code writes.
  For a `switch`, it means each section awaits before each `break` of the `switch`, and one section
  takes every value: a `default`, or a `var` pattern with no `when`. Otherwise, the code after the
  loop or the `switch` counts as code that runs in the block, because the loop can run no turn, a
  section can leave before its `await`, or no section can run. An
  `await foreach` runs its body in the block only for an item that the sequence gives at once, and
  the code after it only when every item comes at once. An async iterator runs nothing until
  something enumerates it. An `await foreach` over `ConfigureAwait` or `WithCancellation` of the
  platform enumerates the sequence inside them, so the iterator or the enumerator object of yours
  runs as it does without them. A method of `SodaFlow.Functional` that gets a task and returns one, such
  as the `Map` of a `Task<Maybe<T>>`, awaits that task first, so its function runs in the block only
  when the analyzer knows that the task is complete.
- The serializers of `System.Text.Json`, Json.NET (also `FromObject` of `JToken`, `JObject`, and
  `JArray`), `XmlSerializer`, and the data contract serializers, and LINQ to XML, which call your
  getters and enumerate your sequences. `System.Text.Json` writes the properties of the type that
  the call, a `Type` argument written as `typeof`, or the property names, unless that type is
  `object` or has `[JsonDerivedType]`, and the others write those of the object. The analyzer also
  reads a non-public member with `[JsonInclude]` or with the `[JsonProperty]` of Json.NET, the
  public fields where the serializer writes them, and a converter that a `[JsonConverter]` or the
  `Converters` of the options names. `XmlSerializer` writes a property that it can set, and also a
  get-only property whose type is a collection other than an array. Json.NET treats a type with
  `[DataContract]` as `MemberSerialization.OptIn` unless a `[JsonObject]` says otherwise. Of such a
  type, it writes only the members with `[JsonProperty]`, and also those with `[DataMember]` where
  the type has `[DataContract]`, private ones included. With `MemberSerialization.Fields`, it
  writes every field, private ones included, and no property.
  Json.NET and `XmlSerializer` call a public `ShouldSerialize` method of a member, and the getter
  of a public `bool` property whose name is that of the member with `Specified` after it, before
  they write the member. Json.NET reads that getter even where the property has `[JsonIgnore]`.
  The data contract serializers write the members with `[DataMember]` of a type with
  `[DataContract]`, the fields without `[NonSerialized]` of a type with `[Serializable]`, and the
  public members that they can set, without `[IgnoreDataMember]`, of each other type.
  Each serializer skips a member that has the ignore attribute of its own library, `[JsonIgnore]`
  or `[XmlIgnore]`, and reads one that has only the attribute of another library. Json.NET also
  skips a field with `[NonSerialized]`, and a member with `[IgnoreDataMember]` of a type that is
  not opt-in. A `[JsonIgnore]`
  with a `Condition` other than `Always` still reads the value to decide, so the analyzer reads that
  member.
  It reads `IncludeFields`, `IgnoreReadOnlyProperties`, and `IgnoreReadOnlyFields` where you create
  the options in the call. It reads the object that you give the serializer and each object inside
  it with its own state, also two objects of one type, to the depth of 64 at which
  `System.Text.Json` stops by default. A `JsonValue` that `JsonValue.Create` makes keeps its value,
  and so does a `JsonObject` or a `JsonArray` that holds such a node. The analyzer reads a
  `ToJsonString`, a `WriteTo`, or a `ToString` of the node, or a serializer call with it, as a
  write of that value. There it reads the members of the type of the object, not those of the type
  argument of `Create`, so it can report a getter that the node does not call.

### Its limits

The analyzer does not replace the check at run time. Where it cannot be sure, it reports on the
careful side, so it can report a read that does not throw:

- A collection that it cannot follow item by item has the state of all its items. A list that
  holds one pending cell and one plain cell then reports a read of either. An array made with
  `new T[n]`, and an array of two or more dimensions, is such a collection: a pending cell in one
  element reports a read of any other element, also after a plain value is written over it, and so
  does a write that a method you call makes at an index that it gets.
- A `foreach` that sets a member of each item does not clear a pending value when the `foreach`
  runs inside another loop. It can also leave the value for a list that the code changes in a way
  other than `Add`, `Insert`, or `AddRange`, or that gets an item under a condition with no loop
  that adds on every pass, and for an item that an `AddRange` of anything but a literal put in.
- The `Updates` of a cell count as firing the first value of the cell too, unless the cell is a
  sink that the analyzer sees made. So a hold of the updates of a hold, switched as the close, can
  get `SF1002` although it returns.
- `Execute` of an action that is never enabled still counts as a send where the analyzer does not
  see `Cell.Constant(false)` in the creation call, such as an action in a field or from a method,
  or an enablement cell that is false but not a constant.
- Serializer options that you do not create in the call, such as options in a local or a field,
  count as options that write public fields and read-only properties.
- A sample in the function of a task that `RunSynchronously` runs gets `SF1001`, although the task
  keeps the exception and the code after the call goes on. Only a later `Wait` or `Result` throws.
  The same holds for a sample before the first `await` of an `async` method, local function, or
  lambda whose task nothing waits for.
- A write through a conditional `ref` whose condition is not a constant, directly or through a
  `ref` local or a returned `ref`, goes to both of its locations and replaces the value of neither.
  So a pending cell written that way makes a read of either location report, also a read of the
  location that the condition rules out.
- A `finally` block that throws a different object replaces the object of its `try` block and of
  its `catch` clauses. The analyzer knows that only for a `throw` statement at the first level of
  the `finally` block. A call there that throws, or a `Dispose` that throws at the end of a
  `using`, leaves the first object going on, so a `catch` of it further out can get `SF1001`
  although it never runs.
- A `catch` that takes the exception of an early read, such as a `catch (InvalidOperationException)`
  or a `catch (Exception)` around a `Sample` of the looped value, does not clear the report of that
  read. The code still reads the value before the loop closes, and the `catch` only hides it. The
  same holds for a read in a query and for a read in a task.
- A `?.` on a local that is `null`, a null-conditional assignment on `null`, and `??=` on a local
  that already has a value skip their value at run time, but the analyzer still reads it. A sample
  or a pending cell there gets a report.
- A read after a `switch` whose sections all await, where no `default` or `var` pattern takes
  every value, also when its `case` labels cover every value, as `case true` and `case false` do.
  The analyzer cannot know that a section runs, so it reads the code after the `switch` as code
  that runs in the block.
- An `await using` whose `DisposeAsync` never completes. The exception of a read in its block, or
  in a `DisposeAsync` that runs before that one, stays in the task, and a `DisposeAsync` after that
  one never runs. Nothing throws, but the analyzer cannot know that the task never completes, so
  it still reports the read.
- The function of a `Map`, a `Lift`, a collection view, or a `LazySample.Defer` also counts with
  the values that it read where you built the cell. If it read a pending value there, the first
  read of the cell gets a report, even when the code gave that capture or member a plain value
  before the read.
- A cell or a `Lazy` that a constructor or a method of an object builds into a member, when nothing
  made from the placeholder goes into that call. The analyzer does not read such a call. Where it
  reads that member and knows nothing of its value, it takes the value of each assignment to the
  member in the code of the class of the object, where the analyzer knows that class, or else of the
  class that the read names, and of its base classes, with the members that the object has at the
  read. An initializer of the member, a part of a deconstruction of a tuple literal into it, and an
  `Interlocked.Exchange` or `CompareExchange` of it count as such assignments, and a value counts
  whatever its form, such as a cast or a conditional of a method group. A read through a local that
  only ever holds the member, or through a getter of the object that only returns it, counts as a
  read of the member. It does not know whether that code ran, so a read can get a report from an
  assignment in a method that you never called.
- The `Result` of a task that you create with `new` has the value that its function returns even
  where no code runs the task. Such a `Result` waits forever, and the code after it does not run.
- A method that writes a member through an object that the analyzer does not know, such as an
  element of an array in a static field, can have written that member of any such object. So a
  read of the member through an element whose object the analyzer does not know, or through a
  local that got such an element, after a call that wrote that member, also gets what the call
  wrote. That is so even when the call wrote a different object. In the same way, a write into a
  member of an object of your class through an element of an array, a static field, an element of an
  array in a static field, or a field of a holder object reaches a read of that member through any
  other object of the class, also one that the code made with `new` itself.
- A local that gets a field of `this` that is not `readonly`, with a call between that can give the
  field a different object, as in `var slot = this.slot; this.Renew(); slot.Share = value;`. The
  analyzer does not read whether the call did, so a write through the local still reaches a read
  through the field, also where the call gave the field a new object.
- A constructor that puts `this` into an element of an array or a list in a static field only on one
  path, at an index that is not a constant, or in a call that does not always run. Each element of
  the collection then counts as the new object also, so a read through an element that kept its
  first object gets what the new object has.
- A new object that a method you call puts through a `ref` parameter into an element of an array in
  a static field, or into an element at an index in a local, does not replace the object that was
  there before. A read through that location still gets what the earlier object had.
- It reports a read under a condition, whether or not that path runs. A comparer that only a tie
  calls, and an `Equals` that only two equal hash codes call, are reads of this kind. So is the
  function of a `Merge`, which runs only when both streams fire in one transaction: what it returns
  counts as a value of the merge even where one stream fires, or none does. Only a condition that is
  a constant, also one made with `nameof`, `sizeof`, `default`, a cast, or a `?:` of constants, is
  read for what it says: the analyzer skips the branch of an `if` and the arm of a `?:` that the
  constant rules out, the right side of a `&&` whose left side is the constant `false` and of a
  `||` whose left side is the constant `true`, the body of a `while` or a `for` whose condition is
  the constant `false`, and the second turn of a `do` whose condition is the constant `false`. A
  `switch` statement on a constant skips each section that the compiler finds the constant cannot
  select. A `switch` expression on a constant skips each arm whose constant pattern is a different
  value, and each arm after the first one whose constant pattern is that value. A statement of a
  block that comes after a `return`, a `yield break`, a `goto`, a `break`, a `continue`, or a
  `throw`, and that the compiler finds no path to, is not read. It still reads the code after an
  `if` on a constant whose branch always returns, the body of a `foreach` over an empty array, and a
  lambda that the code calls only in a branch that does not run.
- A first read of a cell that the analyzer cannot be sure runs leaves the cell for the next read,
  which then runs the function again. That is a read in a branch of an `if` or an arm of a `?:`
  whose condition is not a constant, a read in a `switch` section, a read in a labeled statement
  that a `goto` names, and a read in a `catch` or `finally` block. A read in the branch of an `if`,
  also the `else`, or the arm of a `?:` that a constant condition selects counts as one that runs.
  So does a read in the only section of a `switch` where that section takes every value, with a
  `default` or a `var` pattern with no `when`, and no `break` comes before the read. A read in a
  `try` block counts only for the code after the `try`, and only when the `try` has no `catch`
  clause. A read in a loop counts for the code after the loop only when the loop always runs its
  first turn: a `do`, a `while (true)`, a `for` with no condition, or one that counts up from a
  constant with `<` to a larger constant or with `<=` to one that is not smaller, and a `foreach`
  over an array with an element: an array literal with an item, a collection expression of an array
  type with an element that is not a spread, an array creation whose sizes are constants larger than
  zero, or a local with such a value that no code writes. Inside any loop, a read that each turn
  runs counts for the turns after it.
- Where one call builds two or more cells, a first read counts only for the name that the code
  reads through, and only where the read always runs, outside a loop. A read of the same cell
  through a different name, such as a local that got `cells[0]`, runs the function again.
- The `Value` of a `Lazy<T>` runs no function where an earlier read of `Value` through the same
  local, parameter, or field of the object always ran before it in the same method. Each other
  read runs the function again, also a read through a second name of the same `Lazy<T>`.
- The coalesce function of a sink runs only when two sends go into the sink in one transaction. The
  analyzer counts the sends that are statements of the block itself, with no `return` or `goto`
  before them: one such send does not run the function, and two or more fire only what it returns.
  With exactly two such sends into a sink of a stream, the first parameter of the function gets the
  value of the first send and the second parameter that of the second. With more, and for a sink of
  a cell, each parameter gets the value of each send.
  Any other send, such as one under a condition, in a loop, or in a function, counts as a send that
  can run the function, and its own value counts as well. A value that you make from the sink after
  the sends also gets the value of each send.
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
  come between them if it gives none of the names a new object. So can a `new` of one of your types
  whose constructor has such a body, calls no other constructor with code, and has no initializer
  of an instance field or property to run. A conversion of your own makes a new object, so it gives
  no second name.
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
  removes that one handler in a straight line. A `finally` block, the branch of an `if` that a
  constant selects, and a `try` block with no `catch` whose `finally` block does not name the
  handler or the local of the event are part of a straight line. The handler must be a local that
  holds one lambda, and the code must add it to and remove it from a local delegate, or an event of
  a local that holds an object made with `new`. A local delegate can have any code between the add
  and the remove. An event of such an object can too, as long as no code before the remove uses the
  object other than to add or remove handlers, and no constructor that `new` runs passes `this` on.
  Otherwise, nothing can come between the add and the remove. An event of `this`, of a field, or of
  a parameter never qualifies. A remove in a lambda in a local, a local function, or a method of
  yours that is not virtual counts as a remove at the call, where the call runs on every path, the
  remove is the first statement of that function, and the function names the handler nowhere else.
  A parameter of the function stands for its argument, which must be a local, and any other name
  is the local of the code that calls it. A remove from a delegate parameter changes only the copy
  that the function got, so it does not count, and neither does a call of a function that calls
  such a function.
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
- A local of a constructor or a method of your object, such as a delegate or a cell that the code
  gives to a `Map`, a `Lift`, or a `Lazy<T>` that a different method reads, counts with each of
  its values when a branch, a loop, a `goto`, or a lambda can give it a value before the build, and
  when a lambda of the build reads it, because that lambda can run after a later value. An `out`
  argument of a method of yours, a pattern, and a deconstruction of a tuple literal each give the
  local a value too. A branch of an `if` that a constant condition rules out, such as `if (false)`
  or the `else` of a `const` that is `true`, gives no value. A `static readonly` field is not a
  constant, so a branch on one still counts. Where the last value before the build is sure, only
  that value counts. The same holds for a delegate in a member of the object. There, a call, a
  creation, or an assignment through a property setter with code between that value and the build
  makes each value that the code of the class gives the member count. So does a call or a creation
  before the build that gets `this` as an argument, in a constructor that the analyzer reads, even
  when that call does not change the member. A build in a method, not in a constructor, also counts
  the value that the member has where the different method reads.

Some reads that throw it does not see:

- A first read of a cell that one call builds two or more times, through a name that a method, a
  `ref` or `out` argument, or a lambda gives a different cell after an earlier read through that
  name. The analyzer clears the record of the name only at an assignment in the code that it
  reads.
- A cell that a method of yours builds and returns, where the call gives the method no value made
  from the placeholder, and where the function of the cell reads a static member that the block
  makes pending after the call. The analyzer does not read such a method, so the cell has no
  function to run at its first read.
- Reflection, such as `Activator.CreateInstance` or `MethodInfo.Invoke`. A value made from the
  placeholder that you pass to one of these gets `SF1003`. The analyzer follows these literal
  forms: `typeof(T).GetMethod("M").Invoke(obj, [a])`, where `T` and its base types have one public
  method named `M`, which is not generic and not virtual and takes those arguments,
  `Activator.CreateInstance(typeof(T), a)`, where one public constructor of `T` takes that many
  arguments, also with no arguments, and `Activator.CreateInstance<T>()`, which it reads as
  `new T()`. The name can be a string or a `nameof`, and the arguments must be written in the call,
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
- A delegate that a constructor of your object reads from a member or a getter of a different
  object and gives to a `Map` or a `Lazy<T>`, where a different method of your object reads the
  value, and the code of that object gives the delegate a lambda, as in
  `this.Factory = () => this.Share.Sample();`. Such a read gets `SF1005` at the call where
  `SF1004` at the argument is right. The same holds for a `Lazy<T>` that a getter of the other
  object makes, and for an object that the constructor makes in a local. A method of that object,
  as in `this.Factory = this.Read;` in its constructor or `Func<int> Factory => this.Read;`, is
  seen, for `new Lazy<int>(holder.Factory)` and for `this.holder.Factory` alike: the analyzer reads
  the value that the code of the other object gives the member, as it does for a member that holds
  a cell. A delegate that your object keeps in a local or in a member of its own, such as
  `this.factory = this.Read;` and then `new Lazy<int>(this.factory)`, is seen. So is a member of a
  different object that the constructor writes before the build, such as `this.box.F = this.Add;`
  and then `Map(this.box.F)`, and a delegate made from one function, such as
  `new Func<int>(this.Read)` or `new(this.Read)`, which counts as that function.
- The objects of a task for a `catch` around `Task.WaitAll`, `Task.WaitAny`, or a wait of a task
  from `Task.Factory.StartNew`. The analyzer gives such a `catch` no object.
- The function of a `ContinueWith` with `ExecuteSynchronously` on a task that the analyzer does not
  know is complete, or with options that are not a constant. Such a function runs where the first
  task completes, which can be in the block, as `SetResult` of a `TaskCompletionSource` does, or on
  a different thread. The analyzer does not read it, so a wait for its task gets no report.
- A static initializer of a class with no static constructor. The runtime can run such initializers
  at any time before the first read of a field. The analyzer reads them at the first read of a
  static field, or of a static property with no code, through the name of the class or in the code
  of the class itself, as in a method of the class that the block calls, which is the last time that
  they can run. Where the runtime ran them earlier, they can have read a different value. A read in
  the code of the class does not run them when the block itself is in that class, because the code
  around the block can have run them first.
- A write into an element of an array in a field that a method you call makes through a second
  name of the array that the analyzer does not follow, read through a local that got the array
  before the call. It follows a `ref` local, `Array.Copy` into a static field, the array of a static
  field that a method returns when the method only returns it, a second static `readonly` field
  whose initializer names the first one, and a `ref` that a method of your object returns of an
  element of its `readonly` field.
- A `ref` field that a constructor of yours does not set from a `ref` parameter, or that the code
  gives a second `ref`, as in `r.Target = ref other`. A write through it does not reach the local
  that it refers to, and a read through it does not see a write of that local.
- A write through a span, a memory, or a segment that a method gets as a parameter, that
  `MemoryMarshal` makes, that `CollectionsMarshal` makes of a list that is not in a local, or that a
  lambda or a local function uses. The write does not reach the array.
- A write in the function of a `Map`, a `Lift`, or a collection view that the analyzer did not see
  you build, such as a cell in a field that a read in the block runs for the first time. A function
  that you give to `Map` or `Lift` should have no side effects in any case.
- A member that the platform calls from a method that is not in its table of common methods.
- A converter of serializer options that you do not create in the call, a converter that a
  `JsonConverterFactory` makes, the settings of Json.NET, and an object more than 64 levels inside
  the one that you give a serializer.
- An interpolated string that becomes a `FormattableString` or an `IFormattable` and that code
  formats later in a way other than these. It calls no `ToString` where you write it. The analyzer
  reads the `ToString` calls where `FormattableString.Invariant` gets the string, and where the
  code formats a local that holds only that string, with its `ToString` or with
  `FormattableString.Invariant` or `FormattableString.CurrentCulture`. It reads each value as it is
  at the format. Any other format, such as one in a method that gets the string or one through a
  field, calls no `ToString` for the analyzer.
- A function given to a `Map`, a `Lift`, or a collection view that reads a value only through a
  compound assignment operator, such as `operator +=`, that a type of yours declares.
- A function that a constructor from outside your project keeps, other than a `Lazy<T>`, a
  `ThreadLocal<T>`, the comparer of a platform collection, or a `Task` that `RunSynchronously` runs.
  The analyzer does not read what the function of a task does where `Start` runs it on another
  thread, although it sees the value that the function gives to `Result`. It does not see that
  value for a task from `Task.Run` or `Task.Factory.StartNew`.
- Code after an `await` of a task that is already complete when the analyzer cannot tell that it
  is. That code runs in the block, and the analyzer reads it as code that runs later. The same
  goes for the body of an `await foreach` over a sequence other than a call of your own async
  iterator or an enumerator object of yours whose `MoveNextAsync` returns only complete tasks, each
  also through `WithCancellation` or `ConfigureAwait`, and over an iterator whose first
  `yield return` is not at the top level of its body. The analyzer reads the `DisposeAsync` of the
  enumerator at the end of such a loop as code that runs later too. The analyzer knows the complete
  tasks that the platform makes, such as `Task.CompletedTask`, `Task.FromResult`,
  `Task.FromException`, and `Task.FromCanceled`, a local, a
  `readonly` field, or a get-only property whose initializer is such a task, and a method or a
  getter of your own that returns only such tasks. An awaiter of your own whose `IsCompleted` always
  returns `true`, and is not virtual, is complete too.
- Two calls of your methods that give the same object, in two cases. A read through a local that
  has a value of its own, written through that local, does not see a later write through a second
  call. And when a method returns a sealed class, the result of a call that comes before the first
  write through a result of a call counts as a new object, so a read through it does not see that
  write.
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
  class that the object can be, so it can report a read that only one of them makes. A virtual or
  an interface method that you give by name as a function, as in `Map(o.Add)`, runs the override of
  the class that the analyzer saw the object made with. On an object that it did not see built, the
  analyzer reads the method that the name gives, with no `SF1003`.
