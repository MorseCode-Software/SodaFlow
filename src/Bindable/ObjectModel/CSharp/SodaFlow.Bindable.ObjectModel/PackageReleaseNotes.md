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

Depends on SodaFlow.Bindable.ObjectModel.Core and SodaFlow.Functional. Install
SodaFlow alongside it for the FRP operations themselves.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
