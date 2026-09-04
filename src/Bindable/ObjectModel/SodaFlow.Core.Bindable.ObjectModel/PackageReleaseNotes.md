3.0.0

BREAKING: IBindableAction.Execute rejects null whatever the action's type
argument, where it previously accepted null for a type which could represent
one.

Passing null to an action over a reference type used to fire its stream with
null; it now throws InvalidOperationException, which is what it always did
for a value type. One rule instead of two, and the rule the nullable
annotations already stated. Code relying on a null firing has to send it
another way - the sink is still there - or stop.

This one is quiet. The signature is unchanged, so nothing stops compiling;
the exception arrives at runtime, from a call which used to work.

Fixed: a two-way bindable no longer puts a value back on screen that the
caller has already replaced, and no longer discards a write.

The update handler wrote back the value the update carried. Running
later, on the binding thread, after a setter had moved the cached value
on, that put the older value up and then raised a second notification to
take it away again - visible in a text box as a flicker back to what was
just typed over. It samples the cell instead, so an update arriving late
says what is true rather than what was true when it fired.

The setter skips a write whose value matches the cached one. That reads
the cache as a statement about the graph, when it is only a statement
about the last time the two were compared: between an update and the
refresh it queues, they disagree, and a write matching the stale cache
was dropped even though the graph never held that value. The check now
stands down while a refresh is outstanding and lets the write through.

Documented, rather than changed: a bindable's Value belongs to the
binding engine. The instance can be constructed on any thread, but the
property is read and written on the binding thread and nowhere else.
Reaching for it from application code is a procedural way around the
graph anyway - the value it reports is one the graph already holds, and
a value pushed into it is one a sink can be sent directly - so this
costs nothing that was worth having. A binding engine already calls from
the right thread; it is worth a look at application code which reads or
sets these from a background task.

Also documented: an IBindingScheduler must not wait for the action it is
given. Post is called from inside a transaction, which holds a
process-wide lock, and the binding thread reaches this library through
setters that open transactions of their own - so a scheduler which hands
work over and blocks until it finishes can deadlock against a binding
thread already waiting for that lock. Anything built on a dispatcher is
fine; a hand-written scheduler needs the care.

BREAKING: IBindingScheduler gains IsOnBindingThread, so a scheduler
written outside this package has to implement it. It answers whether the
calling thread is the one the scheduler posts to, and it is deliberately
biased: an implementation which cannot tell MUST return true. A wrong
true gives up a diagnostic that was never promised; a wrong false throws
on correct code.

What it buys: a bindable's Value now throws InvalidOperationException
when it is read or written from anywhere but the binding thread, instead
of quietly returning a stale value - or, for a large struct, a torn one.
Only raised where the scheduler is certain, so nothing is accused that
cannot be proven. ImmediateBindingScheduler answers true
unconditionally, having no thread of its own, so tests and headless
hosts are unaffected.

This will turn code that has been working by luck into code that throws.
That is the point, but it is worth knowing before upgrading rather than
after.

ToOneWayToSource takes an optional scheduler now. It had none - nothing
flows back out to the view, so there was nothing to marshal - which also
left it the one bindable whose Value could not be checked. It still
schedules nothing; the scheduler is there to say which thread is the
right one.

The check costs a few nanoseconds per access, having first cost about
thirteen: reading SynchronizationContext.Current is not the cheap
thread-local fetch it looks like on .NET Framework, so the thread id is
compared first and the context only if that fails. See
BindableValueGuardBenchmark, which is what found it.

2.0.0

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

1.0.0

First release.

The engine behind the SodaFlow bindable object model. Declares the interfaces a
XAML binding engine needs - IOneWayBindableValue, ITwoWayBindableValue,
IOneWayToSourceBindableValue and IBindableAction - along with the
implementations behind them and the IBindingScheduler that marshals
notifications onto the binding thread.

You do not install this directly. Take SodaFlow.Bindable.ObjectModel for C# or
SodaFlow.FSharp.Bindable.ObjectModel for F#; both bring this with them, and both
are what expose its operations.

Depends on SodaFlow.Core.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
