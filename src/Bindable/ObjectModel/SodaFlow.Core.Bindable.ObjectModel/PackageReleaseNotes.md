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
