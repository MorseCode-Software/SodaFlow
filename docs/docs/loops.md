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

The analyzer follows a value through locals, the elements of a tuple, collections, fields the block
sets, `foreach`, deconstructions, patterns, and `out` arguments. A value keeps what the analyzer
knows about it when a platform type holds it or hands it back, as in `pair.Value`, `task.Result`, or
`list.First()`, and also where its type is only `object`. A `KeyValuePair`, a `Tuple`, or a
`DictionaryEntry` that the block builds keeps what the analyzer knows about each part, so a pending
value does not mark a plain key, whether you read the part by name, by a pattern, or by a
deconstruction. A list or an array written as a literal keeps each element apart in the same way
while the block only reads it: `cells[0]`, `cells.First()`, and `cells.Last()` are then that one
element. An object or a tuple of yours held as
`object` is still read as what it is, so `o.ToString()` reaches your `ToString`, and an object keeps
its type when the block sets its properties after `new`. Where a value can be one of several of your
types, as after a `?:` or in a list of mixed rows, a call through `object` or an interface is read
for each of them. It follows it into your own helpers, constructors, getters, records, and object
initializers, and through an interface to the object you built behind it. When one of your helpers
or constructors puts a value into a collection or an object you passed to it, the caller's
collection or object has that value afterwards. A change made through one item of a collection is a
change to the collection: `rows[0].Add(x)`, a property set on the variable of a `foreach`, or an
`Add` on what `Find`, `Last`, or `TryGetValue` handed back. The same holds for an object you keep
in a variable and also put into a collection, with a literal, an `Add`, or an assignment to an
element: build a row, add it to the rows, then fill the row, and the rows have what you filled in.
A dictionary has its keys as well as its values, also when the key arrives through the indexer, as
in `map[key] = value`. A value put into a collection that a property or a field of one of your
objects holds, as in `box.Items.Add(x)`, is read back through that property.

An object the block builds is one object under every name it has. Give it a second variable, put it
in a list, a tuple, a `KeyValuePair`, or a property of another object, or hand it to a helper or a
constructor that keeps it, and a change made through any of those names is seen through all the
others. After `other = slot; other.Share = pending;`, `slot.Share` is pending too. A method without
source that takes the object is taken to keep it, for an indexer of its owner, a `foreach` over the
owner, or another method of the owner to hand back. An item read by index, or handed back by a
method such as `First`, is such a name as well: after `rows.Add(row); row.Share = plain;`,
`rows[0].Share` is the plain value, whatever the row held when it went in. A change made through an
item, as `rows[0].Share = x` makes, is a change to that object too, as long as the analyzer saw
everything that went into the collection: the block built it empty or from a literal, every object
put into it since is one the block built, and its variable holds that collection and nothing else.
Where it cannot know that, it cannot tell which item was changed, or whether the collection holds
one it never saw go in, so from then on the items keep everything that any of them was given. That
is so for a collection that came from a call or a property, one that took an object from outside
the block, and one that code without source was handed. A collection is such an object as well:
`cells.Add(x)` is seen through a second variable for `cells`, and through an outer list that holds
it. So is what a call hands back, from a helper of yours or from the platform:
the result of `MakeRow()` or of `Activator.CreateInstance<T>()` is one object under the names the
block gives it. A copy is a different object. `new List<T>(list)`, `list.ToList()`, and a spread make
a new collection that holds the same items, a `with` expression makes a new record, and a struct is
copied at every assignment, so a change to the copy is not a change to the original.

A later assignment replaces an earlier one only where it is sure to run: `slot.Share = pending;`
followed by `slot.Share = plain;` leaves the plain value, through either name. An assignment under an
`if`, after a `return` that can leave its method or lambda first, in a helper that was handed the
object, in a lambda or a local function whose call might not run, or in a loop that the object was
built before adds to what the object already has, because the analyzer does not know that it runs.

The body of a `for`, `foreach`, `while`, or `do` builds a new object on every pass. The analyzer
follows the object of the current pass apart from the ones built on earlier passes. Inside the body
it is one object under every name that is new on each pass: a variable declared in the body, a list,
a tuple, or an object built on the same pass, a property that its constructor sets, and a variable
of a helper called from the body. A helper that the body calls builds one object on each call, and
so does a lambda or a local function that the code calls itself, in a loop or outside one: inside
it, a second variable for the object it built is the same object. A name that outlives the pass can
still hold an object from an earlier pass: a variable declared outside the loop, a field, and a list
or an object built before the loop. So can a member or an element of what a call hands back, because
a call may hand back the same object every time, a member of the object a method was called on, and
a variable that a lambda or a local function reads from the code around it, because the function may
have been kept from an earlier pass. A function that the pass builds and calls itself reads the
variables of that pass: a local function called by its name, or a lambda in a variable of the body
that is given nothing else, also when it is handed by that name to a method of the platform such as
`List<T>.ForEach`. A change through a name that can hold an earlier object is added to the
object of this pass and to the earlier ones, and a read through it sees both. In a loop inside a
loop, an object from an earlier pass of the inner loop still belongs to this pass of the outer one,
so what it holds from the outer pass is one object. An assignment at the top level of the
body replaces the earlier value on the object of the current pass, as it does outside a loop, unless
a `continue`, `break`, `return`, `throw`, `goto`, or `yield break` earlier in the body can skip it.
After the loop
every object it built is one of the earlier ones, with what it had when its pass ended.

The analyzer reads a loop more than once, because a later pass can read what an earlier one left
behind: a variable declared outside the loop that the body reads near its top and sets near its
bottom, a list the body adds to, or a property of an object built before the loop. The same holds
for the condition of the loop, a lambda the body runs, and a helper it calls. It stops when a pass
leaves nothing new. A function that a method such as `List<T>.ForEach` or `Select` runs for each
item is read the same way: one run is a pass. A variable declared outside the loop that the top
level of the body sets has only the new value for the rest of that pass, so a value it is given
later in the pass, and replaced at the top of the next one, is not read. The object it is given
there is the object of that pass and no earlier one, so a plain value set through the variable
clears a pending one. After the loop the variable can hold what any pass gave it, or what it held
before the loop, since the loop may not run at all.

It knows which library methods run a function at once: a transaction, a loop block, and the `Fold`
of a collection. Code outside the library that takes a function, such as `List<T>.ForEach` or
`Maybe.Match`, is taken to run it in the call, and a constructor is taken to keep it. A `Lazy<T>`
keeps its function until something reads `Value`, so the analyzer reads the function at that read,
including through the `Map` and `Lift` of `SodaFlow.Functional`. A lambda that one of your own
constructors or helpers keeps is read where it finally runs, and reported there, and so is one that
a helper builds and hands back. A property pattern
reads a property just as a member access does, so `row is { Total: > 0 }` is checked like
`row.Total > 0`.

A LINQ query runs nothing when you build it. `Select`, `Where`, `OrderBy`, and the other methods of
`Enumerable` that return a sequence keep their functions until something enumerates the result. A
query expression does the same, and so do `Choose` and `WhereSome` of `SodaFlow.Functional` and the
body of an iterator method or an iterator property of yours. The analyzer reads that code at the
place that enumerates the sequence: a `foreach`, a spread, a method such as `ToList`, `First`, or
`string.Join`, or any other code without source that takes it as a sequence. It follows the query
there through locals, fields, collections, helpers, and a `Lazy<T>`. A query that the block only
builds and keeps, to be enumerated after the close, is not reported.

A query expression is followed like the method form. Each range variable is an item of its source, a
`let` variable is what its expression gives, the variable after `into` is what the clause before it
built, and the result holds what the last `select` or `group` builds. So `from c in cells select
c.Sample()` reads each cell where the query is enumerated, and the items of `(from row in rows select
row.Total).ToList()` are those totals.

A query inside a collection, or inside another query, is read where the code enumerates that query
itself. Counting a list of queries, keeping it, or looping over it without touching the items runs
none of them, and the analyzer reports none. It reports an inner query at the `foreach` over that
item, at `list[0].ToList()`, or at a `SelectMany`, which enumerates what its function returns. A
query also enumerates the queries it was built from, so a chain such as
`numbers.Where(...).Select(...)` is read as a whole, also when a helper of yours returns it.

It also reads the members of yours that C# calls without call syntax: an indexer, an operator, a
conversion, a property setter or an event accessor, a `Deconstruct`, `ToString` in an interpolated
string or a concatenation, `Dispose` at the end of a `using`, the enumerator behind a `foreach`,
and the `Add` behind a collection initializer.

The platform calls members of your types as well, with no call in your code at all. A sort calls
`CompareTo`. A search such as `Contains` calls `Equals`. A set or a dictionary calls `GetHashCode`,
and `Equals` when two hash codes match. `string.Join`, `string.Format`, and the other methods that
build text call `ToString`. A collection constructor, a spread, or a LINQ query once something
enumerates it, enumerates a sequence type of yours. The analyzer has a table of the collection,
LINQ, span, and text methods of the platform that do this, and it reads the member where the call
is, or for a query, where the query is enumerated. The collection part of the table comes from a run
over the constructors and methods of the platform's collection types, and over the members of the
collection interfaces on each type that implements them. It has the set operations, the sorted,
immutable, frozen, and concurrent collections, and the older `ArrayList` and `Hashtable`. A `Tuple`
object compares, hashes, and writes its elements, as a tuple in parentheses does. The span methods
matter more than their name suggests: in C# 14, `Contains`, `IndexOf`, or `SequenceEqual` called on
an array is a method of `MemoryExtensions`. A collection expression written directly as an argument,
as in `new SortedSet<T>([a, b])`, is read like a collection in a variable. Arguments given one by
one to a `params` parameter are separate values: `string.Join(",", query, 1)` writes the query
object as text and does not enumerate it. A method that gets a comparer calls the comparer and not
these members, and the analyzer knows that too. A comparer argument written as `null` or `default`
is no comparer: the method falls back to these members, and they are read. So is a local that only
ever gets `null` or `default` in its block, and a comparer parameter of one of your helpers when the
call passes `null` or `default`, or leaves out an optional comparer whose default is `null`, and the
helper does not change it. A collection built with a comparer of yours, such as a
`SortedSet<T>`, a `Dictionary<TKey, TValue>`, or a `PriorityQueue<TElement, TPriority>`, keeps it
and calls it from its own methods, and the analyzer reads the comparer at those calls.

A call through an interface of the platform, such as `ICollection<T>.Contains` or
`IDictionary<TKey, TValue>.Add`, runs the method of the object behind it. Where the variable got
that object from a constructor in the block, the analyzer reads the method of that type. The same
holds for a method of yours that makes the object, when every return in it gives an object of one
type, or gives back an argument that the method does not change. A method that a derived class can
override gives no type, because the override can return something else. Otherwise the analyzer does
not know which collection the object is, and it reads the members that every collection of the
platform calls for that interface member.

A pair is compared by its parts. A dictionary seen as a collection of `KeyValuePair<TKey, TValue>`,
as through `ICollection<T>`, looks up the key of the pair you pass, and `Contains` and `Remove` then
compare the values. The `Equals` of a `KeyValuePair` itself calls `Equals` on its key and its value,
so a list of pairs compares both too. Where a method compares the values of only some of its type
parameters, the analyzer reads only those, also when two type parameters have the same type:
`Enqueue` of a `PriorityQueue<TElement, TPriority>` compares the priorities and not the elements,
`Remove` compares the elements and not the priorities, and `Add` of a dictionary compares the key of
the pair and not its value. An immutable dictionary is the exception for values: when it is handed
a key it already holds, `Add`, `AddRange`, `SetItem`, `SetItems`, the indexer of its builder, and
the factories such as `ToImmutableDictionary` call `Equals` on the value already held, with the new
value as its argument, and the analyzer reads that call. The builder of an immutable collection
compares as its collection does, and the analyzer reads its methods too: `Add`, `Contains`, and
`Remove` of the builder of a set, the methods and the indexer of the builder of a dictionary, the
searches of the builder of an `ImmutableList<T>` or an `ImmutableArray<T>`, and the set operations
such as `UnionWith`. A function argument does not always pick what is compared either.
`GetOrAdd` and `AddOrUpdate` of a `ConcurrentDictionary<TKey, TValue>` compare the key you pass, and
their function only makes the value. `ExceptBy` and `IntersectBy` compare the keys their function
returns with the keys in the second sequence.

Two things narrow what is read there. A sort or a set of a single element compares nothing, and the
analyzer knows the count when the collection is a literal of one element that the block does not
grow. And where the platform compares a value you pass with an item a collection already holds, as
`Contains`, `IndexOf`, `Remove`, `BinarySearch`, and the lookups of a set or a dictionary do, only
one of the two objects gets the call. A run on .NET Framework and on each .NET from 6 to 11 shows
which one, and the repository's tests hold that on the runtimes they target. The analyzer reads the
member of that object, with the other one as its argument. For a list, a hash set, a dictionary, an
`ImmutableArray<T>`, and most other collections that hash or keep an order, it is the item already
held. For the sorted collections, a `PriorityQueue<TElement, TPriority>`, the frozen sets, and the
immutable lists, sets, and sorted dictionaries, it is the value you pass. A span method depends on
the element type: most of the searches give the call to the value you pass when the type is
`IEquatable<T>`, and to the item already held when it is not.

A set or a dictionary that a collection expression or an initializer fills is read as the `Add`
calls it stands for. Each element is compared with the elements written before it, and the same
object gets the call as in an `Add` on that collection. A constructor that was given only a comparer
or a capacity leaves the collection empty, so the first element of its initializer compares nothing,
and neither does an `Add` on the `Empty` instance of an immutable dictionary.

A set operation such as `IsSubsetOf`, `SetEquals`, or `UnionWith` depends on its argument. With an
array, a list, or a query, one side gets the call, as above. With a set of the same kind and the
same comparer, several of these methods take a faster path that gives the call to the other side,
and some compare the items of one side with each other. The analyzer follows the faster path when it
sees both sets built with the default comparer: with no comparer argument, with `null`, or with
`EqualityComparer<T>.Default`. When it sees that one of the two got a different comparer, the
platform takes the ordinary path, and so does the analyzer. It reads the items of one side against
each other where the platform compares them.

A serializer does more with a value than keep it. `JsonSerializer` of `System.Text.Json`,
`JsonConvert` and `JsonSerializer` of Json.NET, and `XmlSerializer` enumerate the sequences in the
value they write and call the public getters of your objects in it. The analyzer reads both at the
call that serializes, and leaves out a property the serializer skips, such as one marked
`[JsonIgnore]`. LINQ to XML is similar: `XElement` and `XDocument` take their content as `object`,
and their constructors and the methods that add or replace content enumerate every sequence in it
and call `ToString` on anything else. An `XStreamingElement` keeps its content until it is written,
and the analyzer reads it at `ToString`, `Save`, or `WriteTo`.

It does not see everything, and in a few places it guesses on the careful side:

- A collection has the state of everything put into it. A list that holds one pending cell and one
  plain one reports a read of either, unless it is a literal that the block only reads through a
  constant index, `Count` or `Length`, `First()` or `Last()`, or a `foreach`. An index held in a
  variable, a call that could change the collection, or a second name for it puts the collection
  back to one state for all its elements, and so does a set or any other type that does not keep the
  order you wrote. A pair that comes out of a dictionary has the state of its key and its value
  together, and so does a tuple whose elements are read by index through `ITuple`. The analyzer
  tells a collection from one of its items by the type of the expression, and where that type is
  `object` or a type parameter, by how the value was made. A collection taken out of a collection of
  `object` and then held as `object` is still read as an item, because no type says what it is.
- A field set on an object the analyzer did not see built counts for every object of that type,
  to the end of the block.
- A read under a condition is reported whether or not that path runs. A comparer of keys is such a
  read: a sort calls it only when two items tie, and the analyzer does not know your data. So is
  `Equals` in a set, which runs only for two items with one hash code, and `CompareTo` in a sort of
  a single item that is not a literal of one element. A comparer kept by a collection is the same,
  except for the first value put into a collection that the analyzer saw built empty: nothing is
  there to compare it with, so only a `GetHashCode` that reads is reported. So is `Equals` of a
  value in an immutable dictionary, which runs only when a key repeats: the analyzer does not know
  your keys. It knows that a dictionary calls its value comparer instead only where it sees the
  dictionary built with one. A value that may be one of two types is read the same way: the member
  of each type is read, though only one object is there at run time.

- In a sort, and in the other methods that compare the items of one sequence with each other,
  the analyzer does not know which of two objects gets the call, and the runtimes do not agree. A
  `CompareTo` or an `Equals` that reads only its own object is reported for either.
- Code without source that takes a query as `object` or as a type parameter is taken to keep it,
  as `list.Add(query)` does. The serializers and LINQ to XML named above are the exceptions. Other
  code that walks a value it was handed as `object` is not seen, and a serializer is read as it
  behaves without options: one told to write fields, or given a converter of its own, is not
  followed.
- An object is one object under all its names only where the code makes it once, once on each pass
  of a loop, or once in each call of a helper, a lambda, or a local function that the code calls
  itself, or once on each run of a function that a method such as `List<T>.ForEach` or `Select`
  runs for each item. An object built in a clause of a query expression is many objects at run
  time, and the analyzer follows each name of it on its own, as it does for an object that came
  into the block from outside: a change through one name is not seen through a second variable,
  though a collection still has what was put into it, and what any of its items was given later.
  Where a name can be one of several objects, as an element of a list of several, a variable set on
  two paths, a value that is either an object of the block or one from outside it, or a name that
  outlives a pass of a loop, a change through it is added to each of them, so a plain value set
  that way does not clear a pending one. Two cases are still read exactly. A second value set
  through the same variable replaces the first, where nothing between the two can have given the
  variable another object. And a read through a variable straight after a cell was set through
  that same variable reads that cell.
- After something is set through an item, the items are read as the objects they are only where
  the analyzer saw everything that went into the collection, and only through the variable that the
  collection was built in, or a variable declared as a copy of that one. A read through any other
  name of the collection, as a property, a parameter, or a variable that a lambda reads from the
  code around it, sees everything that any item was given, so a plain value set later does not
  clear a pending one there. A call to code without source ends this for every collection that the
  code could reach: one that was handed to something, stored somewhere, or used in a lambda. A list
  that is only read and filled through its own variable stays known.
- A value set through an item replaces an earlier one only where the analyzer saw everything that
  went into the collection. An item of any other collection may be an object the analyzer never
  saw: one that code without source put there, or one that was already in a collection the block
  was handed. So the value is added to each object the item could be, and a plain value set that
  way does not clear a pending one. The same holds for a variable that was given such an item, for
  a parameter the item was passed to, and for a member of it. An item at a known position is the
  object written at that position. A collection reached through a member of an object, or built
  for a `params` parameter, is read as one that could hold more, even where the block filled it.
- What is set on a struct in a variable, or on an object that came into the block from outside, is
  kept with that variable. There, as on an object the block built, a later value replaces an
  earlier one only where the assignment is sure to run. One under an `if`, in a loop that the
  variable was declared before, or in a lambda or a local function whose call might not run is
  added.
- A `ref` variable or parameter names a place, not one object. It starts with what the place holds
  when the `ref` is taken, and a value set through it is added, because the place may hold another
  object by then. The analyzer does not follow the place after that: a read through the `ref` name
  still sees the object from when the `ref` was taken, and an assignment to the `ref` name is not
  seen as an assignment to the place.
- A list built with `Add` statements at the top level of its block keeps its positions, as a
  literal does, so `list[0]` is the first item added. An `Add` in a branch or a loop, or any other
  change to the list, ends that.
- A helper replaces what it sets on an object of its caller only where the statement always runs
  in the helper and the call always runs in the block. A helper called in a branch or a loop adds.
- A `foreach` replaces what its variable sets on every item once the loop is done, where the
  collection has known positions, the loop always runs, nothing in its body can skip an item or
  leave early, and the body sets nothing else. Inside the body, and in every other loop, a value
  set through the variable is added.
- The analyzer reads a loop until a pass leaves nothing new. A value handed down a chain of
  variables, one on each pass, needs a pass for each of them. A guard far beyond what code needs
  stops a loop that would never settle.
- What a method without source returns, on an object that keeps what it was handed, is taken to be
  possibly one of those objects. So a plain value set through such a result is added, and does not
  clear a pending one for any other name of it, even when the method returned something new. A
  read through the same variable straight after the value was set does read that value.
- For a few searches the object that gets the call depends on the data or the runtime, and the
  analyzer reads both. A builder with hash codes is one of them for `Remove` and for the lookups of
  a dictionary, and the set operations of a builder are read for both objects too. The lookups of an
  `ImmutableDictionary<TKey, TValue>` give the call to the key already held when it is the first one
  with its hash code, and to the key you pass when it is not. A span method called with a span of
  values, such as `IndexOfAny(values)`, differs between .NET Framework and .NET.
- A set operation whose argument is a set reads both objects when the analyzer cannot tell which
  path the platform takes: where a comparer comes from a variable or a call, where both sets got a
  comparer, where the type of the argument is not known, and for the few combinations where the
  runtimes differ or each side gets a call.
- A group from a `group` clause has what the analyzer knows about its key and its items
  together, as a pair that comes out of a dictionary does.
- A call through an interface on an object whose type the analyzer does not know reads both
  objects, because the collections do not agree on which one gets the call. `ICollection<T>.Add`
  on such an object is not read at all, because a list adds without comparing.
- A query is told from a collection that holds it by its type. Where the type says too little,
  as with `object`, the non-generic `IEnumerable`, or a type parameter, enumerating the outer value
  is read as enumerating each query in it.
- Only the methods of `System.Linq.Enumerable` and of `SodaFlow.Functional` are known to wait. A
  method of another library that returns a sequence is taken to run its function in the call.
- Reflection is not followed. An object from `Activator.CreateInstance`, or a result of
  `MethodInfo.Invoke`, carries nothing the analyzer knows.
- A chain of your own calls is followed four calls deep. Past that, the code is not read, and the
  analyzer reports `SF1003` at the call it stopped at: for a pending value you pass, and for a
  member of one of your objects when the object holds a value the analyzer is following. A helper
  that is handed only an object, with nothing from the loop in it, is read to the same depth so
  that the analyzer sees where it keeps the object, and gets no report past it.
- A function kept by a constructor outside your project is not read when the block then uses that
  object. `Lazy<T>` and a comparer kept by a platform collection are the exceptions, and a
  `Lazy<T>` handed to code outside your project is taken as kept, not forced.
- The table of platform methods that call a member of yours holds the common ones, not all of
  them, and nothing outside the platform. A read in a member that some other method calls is not
  seen.
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
