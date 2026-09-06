3.0.0

BREAKING: ToOneWayToSource takes its optional parameters in the order
the other factories use, scheduler before comparer. A call which passed
a comparer positionally - ToOneWayToSource(myComparer) - now binds it to
the scheduler parameter and stops compiling. The types differ, so this
is a compiler error rather than a silent misbinding; name the argument,
or move it along one.

ToOneWayToSource takes an optional scheduler, matching the other three.
It schedules nothing - nothing flows back out to the view - but it
identifies the binding thread, which is what lets Value throw when it is
touched from another one. BindableFactory passes its own scheduler
through. See SodaFlow.Bindable.ObjectModel.Core for what the check does
and what it costs.

BREAKING: the factory methods are renamed from To to Create, on both
BindableFactory and IBindableFactory: ToBindableAction, ToOneWay,
ToOneWayToSource and ToTwoWay become CreateBindableAction, CreateOneWay,
CreateOneWayToSource and CreateTwoWay. They construct rather than convert,
and now say so. The ToBindableAction and ToOneWay extension methods keep
their names, since those do read as conversions of the thing they extend.

BREAKING: BindableExtensionMethods.BindableAction is gone, replaced by
BindableMaybeAction<T>, alongside a new ToBindableAction overload taking a
StreamSink<Maybe<T>> so that an action can carry a parameter which may be
absent.

BREAKING: requires SodaFlow 4.x, SodaFlow.Bindable.ObjectModel.Core 3.x and
SodaFlow.Functional 3.x. See those packages for what moved; the Execute
change in the core one is the one to read, because it is the only break here
that a compiler will not point at.

Requires System.ValueTuple 4.6.2, where it required 4.4.0. Nothing here
uses it differently: this repository named two versions of it, one in
the shipping projects and one in the test projects, and now names a
single version in both. A consumer does nothing about this; NuGet
resolves the higher floor.

2.0.0

No code change. This release exists to move a dependency, and is a major
version because of what moving it does to a consumer.

Dependencies between these packages are now declared as ranges bounded at
the next major, so NuGet refuses a pairing which would fail rather than
resolving it and leaving the failure until the code runs. This package now
requires SodaFlow 3.x, SodaFlow.Bindable.ObjectModel.Core 2.x and
SodaFlow.Functional 2.x.

That ceiling is why this is not a minor version. Taking this release obliges
a consumer to take those majors as well, and one who names SodaFlow or
SodaFlow.Functional directly, or who uses anything removed there, cannot
adopt it without changing their own code. A version they cannot adopt is not
a minor one.

1.0.0

First release.

Exposes an FRP graph to a XAML binding engine, so a view model can hold cells
and streams and let WPF or Avalonia bind to them directly.

  cell.ToOneWay()                a cell as a read-only observable property
  cell.ToTwoWay(editsSink)       the view writes, the graph stays authoritative
  cellSink.ToTwoWay()            the same, where the sink is the whole graph
  sink.ToOneWayToSource(...)     control state pushed into the graph
  sink.ToBindableAction(...)     an ICommand enabled by a Cell<bool>

The binding path is {Binding SomeProperty.Value} - the property name raised on
PropertyChanged is always "Value".

A two-way value is optimistic: it shows the write immediately so it does not
fight the caret, then reconciles against the cell once the graph settles,
correcting anything the graph normalized or rejected.

Bindables may be constructed on any thread, so a view model needs no knowledge
of the binding thread. Every one of them is IDisposable and holds a weak
subscription, so a bindable that becomes unreachable without being disposed is
collected rather than rooted for the lifetime of the sink it observes. Writes
never reach a sink from inside a callback.

BindableFactory takes an IBindingScheduler by constructor injection, which is
how a test substitutes BindingScheduler.Immediate for a real dispatcher.

Depends on SodaFlow, SodaFlow.Bindable.ObjectModel.Core and SodaFlow.Functional,
so installing this brings the FRP operations along with it.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
