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
