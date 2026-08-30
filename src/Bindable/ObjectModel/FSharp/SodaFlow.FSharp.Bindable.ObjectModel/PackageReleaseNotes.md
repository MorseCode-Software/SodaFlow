1.0.0

First release.

Exposes an FRP graph to a XAML binding engine, so a view model can hold cells
and streams and let WPF or Avalonia bind to them directly.

  Bindable.oneWay cell                     a read-only observable property
  Bindable.twoWay cell editsSink           the view writes, the graph decides
  Bindable.twoWayCS cellSink               the same, where the sink is the graph
  Bindable.oneWayToSource sink initial     control state pushed into the graph
  Bindable.toBindableAction sink           an ICommand carrying no parameter
  Bindable.toBindableActionWithValue sink  an ICommand carrying its parameter

Each has WithComparer, WithScheduler and WithComparerAndScheduler variants; the
command functions take AndIsEnabledCell to drive enablement from a Cell<bool>.
BindableFactory takes an optional IBindingScheduler for injection, which is how
a test substitutes BindingScheduler.Immediate for a real dispatcher.

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

Depends on SodaFlow.FSharp and SodaFlow.Bindable.ObjectModel.Core, so installing
this brings the FRP operations along with it.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
