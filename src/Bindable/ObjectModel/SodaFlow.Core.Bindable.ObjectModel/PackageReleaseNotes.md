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
