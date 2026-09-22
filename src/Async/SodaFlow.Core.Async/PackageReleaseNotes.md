5.0.0

BREAKING: the operation of a MapAsync call is a MapAsyncOperation<TInput,
TResult> delegate, where it was a Func<TInput, CancellationToken,
Task<TResult>>. It takes a third argument, a ResultFactory<TResult>,
and answers with what that factory makes:

  resultFactory.FromValue(value)        a value the operation has
  resultFactory.Construct(() => .) a function the pipeline calls

The second one is the cause for the change. The pipeline calls that function
in the transaction that sends the result, so a result that holds a cell, a
stream, or a view model built out of them comes into existence in that
transaction. An operation runs outside a transaction and cannot do that for
itself.

Keep such a function short, because it holds the transaction while it runs, and
put no effect in it that matters outside the value it returns: the pipeline
calls it only for an item that it publishes, and a strategy can decide not to
publish. A throw from it goes to the errors stream in place of a result.

A lambda takes the third parameter and compiles. A stored Func does not convert
to the delegate and needs its own edit.

BREAKING: a strategy no longer reads a result. OnCompleted takes an
AsyncCompletion - the operation returned, it threw, with the exception, or a
cancellation stopped it - in place of an AsyncOutcome<TStrategyResult>. The
pipeline asks the strategy first and makes the result after, and only for an
item that it publishes, which is what the paragraph above describes.

The result type of a strategy existed to carry that value, so it is gone with
it:

  AsyncConcurrencyStrategyBase<TInput, TResult>
      becomes AsyncConcurrencyStrategyBase<TInput>
  AsyncConcurrencyStrategy<TInput, TResult, TState>
      becomes AsyncConcurrencyStrategy<TInput, TState>

MapAsyncImpl loses its TStrategyResult and its resultConverter with them. None
of the strategies in this library read a result, and a strategy that wants to
select what reaches a consumer can filter the results stream instead.

BREAKING: AsyncMapStatus<TInput> is a class, where it was a readonly struct, and
it now extends a new non-generic AsyncMapStatus that carries IsRunning and
Dispose. Items stays on the generic type, because only that part depends on the
input type.

The point of the split is the caller that never reads Items. It can hold an
AsyncMapStatus and does not have to name the input type to do it - a view model
that shows a busy indicator and disposes the pipeline is the usual case.

Source that names AsyncMapStatus<TInput> and reads its members needs no edit.
Anything compiled against 4.x does, because a struct and a class are not the
same type to the runtime, which is what makes this a major. Two smaller changes
come with the type: the value is a reference now, so default(AsyncMapStatus<T>)
is null where it was a zeroed value, and two of them compare by reference where
they compared field by field.

4.0.1

Adds the package icon that nuget.org shows beside this package.

One expression in MapAsyncUtility is written on a single line where it was split
across two. Nothing else changed, and nothing behaves differently.

Every package here ships this release together, so the dependency versions
move with it.

4.0.0

No API change of its own. This release moves to SodaFlow.Core 4.x, and
is a major because taking it obliges a consumer to take that.

Nothing in this package's own code changed but its layout: file-scoped
namespaces in place of braced ones, which moves every line left by four
columns and alters no behavior.

Requires System.ValueTuple 4.6.2, where it required 4.4.0. Nothing here
uses it differently: this repository named two versions of it, one in
the shipping projects and one in the test projects, and now names a
single version in both. A consumer does nothing about this; NuGet
resolves the higher floor.

3.0.0

No code change. This release exists to move a dependency, and is a major
version because of what moving it does to a consumer.

Dependencies between these packages are now declared as ranges bounded at
the next major, so NuGet refuses a pairing which would fail rather than
resolving it and leaving the failure until the code runs. This package now
requires SodaFlow.Core 3.x, where it required 2.x before.

That ceiling is why this is not a minor version. Taking this release obliges
a consumer to take SodaFlow.Core 3.x as well, and one who names
SodaFlow.Core directly, or who uses anything removed there, cannot adopt it
without changing their own code. A version they cannot adopt is not a minor
one.

2.0.1

Adds the release notes below. 2.0.0 shipped without any, because the mechanism
that reads them from a file landed after that version was tagged. No code
change since 2.0.0.

2.0.0

No public API change. Major because this assembly calls SodaFlow.Core's
internals, and 2.0.0 of the core renamed them: ListenWeakImpl became
ListenImpl, while ListenImpl kept its name and changed meaning from the strong
listener to the weak one.

That makes the pairing load-bearing in both directions. Against a 1.0.0 core
this version throws MissingMethodException; a 1.0.0 of this package against a
2.x core binds to the strong listener instead and roots the whole async
pipeline for good. Upgrade the two together.

---

About this package

The engine behind MapAsync: the tracking, the concurrency strategies and the
AsyncMapStatus a caller holds, generic in the input type when the caller wants
the tracked items and non-generic when it does not. An operation answers with a
ResultConstructor, which carries a value or a function that this engine calls in
the transaction that publishes. Not installed directly - take SodaFlow.Async for
C# or SodaFlow.FSharp.Async for F#, both of which bring it with them.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
