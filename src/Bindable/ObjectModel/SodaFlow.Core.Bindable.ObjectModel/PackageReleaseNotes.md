5.0.0

BREAKING: IOneWayToSourceBindableValue is gone, with the write-only value behind it. That value kept the last value
the view wrote and had no cell to correct it from. When the graph refused a write, the value went on holding what the
graph refused, and its equality test then dropped every retry of the same value, so the view was stuck until it wrote
something else. The two-way value has no such state: it refreshes from its cell after every write, so a refused write
leaves it showing what the graph holds. A value the view writes and also shows is a two-way value on a CellSink, or
on a Cell with a StreamSink for the edits. Something the view only sends is a bindable action whose CommandParameter
carries the value.

Fixed: with SynchronizationContextBindingScheduler, every transaction that posted to the binding thread ran the
transaction start hooks a second time. To send its batch of posts even when a transaction fails, the scheduler also
posted an empty action with a failure handler, and SodaFlow runs each posted action in a transaction of its own, which
runs the start hooks. An application with a TimerSystem therefore read the clock and sent Time a second time for each
such transaction, and anything that listened to the updates of Time saw the extra send. The scheduler now asks the
transaction directly to send the batch if it fails, and posts nothing extra. What reaches the binding thread is
unchanged. After a transaction that fails, the batch is still sent, now at the point where the transaction discards
its last actions, which comes after the failure handlers of other posted actions rather than among them.

Fixed: a one-way bindable value, and the copy of a command's enablement that CanExecute answers from, no longer show a
value from a transaction that failed. The value's listener runs while the transaction propagates and posts the value
it saw; a transaction that failed after that point discarded the cell's update but the batch still reached the
binding thread, so the screen showed a value the cell never took, until the next update. The two-way value was
immune because it reads its cell on the binding thread. The posted action now asks the transaction how it ended, which
SodaFlow.Core 5.0.1 records before any discard runs, and reads the cell only then; a transaction that commits costs
one more reference in the closure the value already allocates and reads no cell on the binding thread. The
scheduler sends its batch after every last action and every discard, so the answer is known before the batch leaves.
Measured with 10,000 one-way values updated in one transaction and every notification delivered, the cost against
the previous release is inside the noise of the runs: 20.2 ms to 19.5 ms on .NET 10 and 23.7 ms to 21.5 ms on .NET
Framework 4.7.2 for the 10,000 notifications, and 383 ns to 389 ns for one update. A read of the cell on every
notification, the alternative, measured 26.8 ms and 27.0 ms. OneWayScaleBenchmarks in SodaFlow.Benchmarks measures it.
A scheduler of your own that hands each action to the binding thread as it arrives can run the action before the
transaction ends. The value then finds the outcome still unknown and reads the cell, and that read waits for the
transaction, so the fix holds for any scheduler that keeps the Post contract, not only for the two in this package.

Requires SodaFlow.Core 5.0.1, which adds the members this release calls. The floor of the dependency is the version of
SodaFlow.Core the package is built against, so SodaFlow.Core 5.0.1 has to be released first.

4.3.0

Changed: a command sends nothing when its enablement cell is false at the moment it sends. CanExecute answers from a
copy of the cell that the binding scheduler carries to the UI thread, so just after the graph disables a command the
copy can still say it is enabled, and a click in that moment used to go through. So did a call to Execute made inside
an open transaction that then disabled the command, because the send waits for that transaction to close. The command
now reads the cell again in the transaction that sends the parameter, which is the check a Gate on the sink with the
same cell would make. Every graph built on the sink is covered, so a Gate you added only for this reason is no longer
needed.

This holds for a command with no parameter, with a parameter, and with an optional one. A send into the sink from
other code is not checked.

4.2.0

Changed: SynchronizationContextBindingScheduler sends everything one transaction posts to the binding thread as a single
item. Each bindable used to post its own refresh, so a transaction that changed a list and its selected item reached the
UI as two dispatcher items, and a control could apply the list before it saw the new selection. Avalonia's ComboBox, for
one, cleared its selection for the new list and wrote that back through the two-way binding before the selection
arrived. Now the binding thread gets all of a transaction's changes in one turn of its message loop, in the order they
were posted, with no other work between them.

The item is sent after every update of the transaction has propagated, and before any work the transaction schedules
with Transaction.Post, so that work still reaches the binding thread after it. A transaction that fails still delivers
what it had posted, as before. A post made while no transaction is open is still an item of its own.

Inside the item, every action runs even when one throws. The exception then leaves the item with its original stack
trace, or, when more than one action threw, as an AggregateException that holds each of them. Before, each throw left
an item of its own, and the refreshes posted after it still ran, as they still do. An unhandled-exception handler that
looks for a particular exception type should now also look inside AggregateException.

BindingScheduler.Immediate does not change, and neither does a scheduler you wrote yourself.

4.1.0

Changed: when the graph throws on a write through a two-way or one-way-to-source value, the setter no longer throws.
It posts the exception to the binding scheduler, which throws it on the binding thread with its original stack trace.
A binding engine catches whatever a setter throws: Avalonia shows it as a validation error, which the two-way refresh
clears a moment later, or, where the property has no data validation, discards it without a trace. A throw from the
graph is a bug in the graph rather than a verdict on the value, so it now reaches the dispatcher, where it stops the
application or reaches its unhandled-exception handler. The two-way refresh is scheduled first, so the value already
agrees with the cell when the exception is thrown.

With BindingScheduler.Immediate, which runs a post at once when no transaction is open, the caller of the setter still
gets the exception, as before. A setter called inside an open transaction is unchanged as well: the write waits for
that transaction to close, and the exception leaves the close. A scheduler you wrote yourself must let a posted action
throw rather than catch and continue, or it hides the bug again.

Fixed: with BindingScheduler.Immediate, a two-way value whose write the graph threw on kept reporting the refused
value. The write runs in a transaction of its own, and the refresh that corrects the cached value was posted inside
it, so the failed transaction dropped the refresh along with everything else it held. The refresh is now scheduled by
the setter after that transaction has closed, so the property reads back the cell's value and announces it. A
dispatcher scheduler was not affected, because its posts never wait for a transaction.

4.0.1

Fixed: a bindable can be built from a looped cell. Each bindable used to sample its cell in its constructor. Inside a
loop block the looped cell has no value yet, so building a one-way value, a two-way value, or a command from it threw
"BehaviorLoop was sampled before it was looped".

A bindable now takes its initial value when the transaction that builds it closes, and the loop is defined by then. As
in 4.0.0, the first read returns that value immediately: building a bindable posts nothing to the scheduler and raises
no notification, and a command with no enablement cell is available from the start.

One visible difference: a bindable built in a transaction that then changes its cell starts with the changed value. It
used to start with the earlier value and announce the change with PropertyChanged or CanExecuteChanged once the
scheduler ran. Now there is no change to announce.

As before, hand a bindable to the binding thread only after the transaction that builds it has closed. A read inside the
loop block itself, before the block returns, throws, just as Sample on the looped cell does there. Reads after the loop
closes work.

Requires SodaFlow.Core 5.x, as 4.0.0 did. The members of SodaFlow.Core that this release calls are all in 5.0.0.

4.0.0

BREAKING: BindingScheduler.Default is gone, and no bindable selects a scheduler for itself any more. Each one used to
take, in order, the scheduler it was given, then Default, then the SynchronizationContext of the thread that built it,
and last of all ran its handlers inline. That last step was silent:
a view model built on a background thread raised PropertyChanged off the UI thread, and the binding engine failed later
and far from the cause.

Each construction now takes a scheduler and throws ArgumentNullException without one. The factories in
SodaFlow.Bindable.ObjectModel and SodaFlow.FSharp.Bindable.ObjectModel are the only way to build a bindable, and each
takes the scheduler once. Build one factory at startup, on the UI thread, with
SynchronizationContextBindingScheduler.Capture (), and give it to each view model; a test gives
BindingScheduler.Immediate.

BREAKING for the two language surfaces: the internal construction methods take the scheduler as a required argument, and
ToBindableActionImpl takes it before the enablement cell. A package built against 3.x does not run against this; take
the 4.x of each with it.

Requires SodaFlow.Core 5.x. It could not stay on 3.x: its dependency on SodaFlow.Core is a range that ends before the
next major, thus 3.0.2 cannot install beside SodaFlow.Core 5.0.0. Nothing here calls the internals that SodaFlow.Core
5.0.0 changed.

3.0.2

Adds the package icon that nuget.org shows beside this package.

Two exception messages are built with a target-typed new and with their concatenation laid out differently. The messages
themselves, and everything else, are unchanged.

Every package here ships this release together, so the dependency versions move with it.

3.0.1

Fixed: a two-way bindable now raises PropertyChanged for a write made through it, so more than one control can bind to
the same value.

The reconciliation pass compared the cell against the cached value to decide whether anything had changed. The setter
has already moved that cached value optimistically, so a write the graph accepted unchanged looked like nothing having
happened and was never announced. Only the control that wrote it knew: a checkbox and a slider bound to the same
property left the slider unable to follow the checkbox, permanently.

Notifications now go out whenever the settled value differs from either the cached value - the writer's correction, as
before - or the value last announced, which is what every other binding is showing. A write that changes nothing is
still silent, and what is announced is still what the graph settled on rather than what was optimistically written, so a
value the graph rejected or normalized never reaches a binding.

Expect one notification where there was none, per write that changes the value. Code that treated PropertyChanged as
meaning "the graph changed this, not the view" no longer can, and never reliably could: an update arriving from the
graph in the same turn was indistinguishable already.

3.0.0

BREAKING: requires SodaFlow.Core 4.x, where it required 3.x. That package removed internal members, and this one is
rebuilt against what replaced them.

BREAKING: IBindableAction.Execute rejects null whatever the action's type argument, where it previously accepted null
for a type which could represent one.

Passing null to an action over a reference type used to fire its stream with null; it now throws
InvalidOperationException, which is what it always did for a value type. One rule instead of two, and the rule the
nullable annotations already stated. Code relying on a null firing has to send it another way - the sink is still
there - or stop.

This one is quiet. The signature is unchanged, so nothing stops compiling; the exception arrives at runtime, from a call
which used to work.

Fixed: a two-way bindable no longer puts a value back on screen that the caller has already replaced, and no longer
discards a write.

The update handler wrote back the value the update carried. Running later, on the binding thread, after a setter had
moved the cached value on, that put the older value up and then raised a second notification to take it away again -
visible in a text box as a flicker back to what was just typed over. It samples the cell instead, so an update arriving
late says what is true rather than what was true when it fired.

The setter skips a write whose value matches the cached one. That reads the cache as a statement about the graph, when
it is only a statement about the last time the two were compared: between an update and the refresh it queues, they
disagree, and a write matching the stale cache was dropped even though the graph never held that value. The check now
stands down while a refresh is outstanding and lets the write through.

Documented, rather than changed: a bindable's Value belongs to the binding engine. The instance can be constructed on
any thread, but the property is read and written on the binding thread and nowhere else. Reaching for it from
application code is a procedural way around the graph anyway - the value it reports is one the graph already holds, and
a value pushed into it is one a sink can be sent directly - so this costs nothing that was worth having. A binding
engine already calls from the right thread; it is worth a look at application code which reads or sets these from a
background task.

Also documented: an IBindingScheduler must not wait for the action it is given. Post is called from inside a
transaction, which holds a process-wide lock, and the binding thread reaches this library through setters that open
transactions of their own - so a scheduler which hands work over and blocks until it finishes can deadlock against a
binding thread already waiting for that lock. Anything built on a dispatcher is fine; a hand-written scheduler needs the
care.

BREAKING: IBindingScheduler gains CheckAccess, so a scheduler written outside this package has to implement it. It
answers whether the calling thread is the one the scheduler posts to - the name is DispatcherObject's, because the
question is the same one and the answer is used the same way - and it is deliberately biased: an implementation which
cannot tell MUST return true. A wrong true gives up a diagnostic that was never promised; a wrong false throws on
correct code.

What it buys: a bindable's Value now throws InvalidOperationException when it is read or written from anywhere but the
binding thread, instead of quietly returning a stale value - or, for a large struct, a torn one. Only raised where the
scheduler is certain, so nothing is accused that cannot be proven. ImmediateBindingScheduler answers true
unconditionally, having no thread of its own, so tests and headless hosts are unaffected.

This will turn code that has been working by luck into code that throws. That is the point, but it is worth knowing
before upgrading rather than after.

ToOneWayToSource takes an optional scheduler now. It had none - nothing flows back out to the view, so there was nothing
to marshal - which also left it the one bindable whose Value could not be checked. It still schedules nothing; the
scheduler is there to say which thread is the right one.

The check adds about 2ns to reading Value on .NET 8, and about 2.3ns on .NET Framework, allocating nothing. It cost far
more before the two halves were reordered: reading SynchronizationContext.Current is not the cheap thread-local fetch it
looks like, and on .NET Framework asking it first made the check 13.0ns rather than 3.1ns, and the whole read 18.3ns
rather than 6.4ns. The thread id is compared first, and the context only if that fails. See SodaFlow.Benchmarks, which
found it and which runs on both runtimes because this is the kind of thing that differs between them.

Requires System.ValueTuple 4.6.2, where it required 4.4.0. Nothing here uses it differently: this repository named two
versions of it, one in the shipping projects and one in the test projects, and now names a single version in both. A
consumer does nothing about this; NuGet resolves the higher floor.

2.0.0

No code change. This release exists to move a dependency, and is a major version because of what moving it does to a
consumer.

Dependencies between these packages are now declared as ranges bounded at the next major, so NuGet refuses a pairing
which would fail rather than resolving it and leaving the failure until the code runs. This package now requires
SodaFlow.Core 3.x, where it required 2.x before.

That ceiling is why this is not a minor version. Taking this release obliges a consumer to take SodaFlow.Core 3.x as
well, and one who names SodaFlow.Core directly, or who uses anything removed there, cannot adopt it without changing
their own code. A version they cannot adopt is not a minor one.

1.0.0

First release.

The engine behind the SodaFlow bindable object model. Declares the interfaces a XAML binding engine needs -
IOneWayBindableValue, ITwoWayBindableValue, IOneWayToSourceBindableValue and IBindableAction - along with the
implementations behind them and the IBindingScheduler that marshals notifications onto the binding thread.

You do not install this directly. Take SodaFlow.Bindable.ObjectModel for C# or SodaFlow.FSharp.Bindable.ObjectModel for
F#; both bring this with them, and both are what expose its operations.

Depends on SodaFlow.Core.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
