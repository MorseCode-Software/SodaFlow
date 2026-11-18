# SodaFlow.FSharp.Bindable.ObjectModel

Data binding for SodaFlow in F#. A cell or a sink goes in, and an object comes out that raises
`INotifyPropertyChanged` for its `Value`, or an `ICommand`. Thus, WPF and Avalonia can bind to a
SodaFlow graph without either side knowing about the other. Install it with SodaFlow.FSharp when you
write view models for a XAML UI. It pulls in SodaFlow.Bindable.ObjectModel.Core.

```bash
dotnet add package SodaFlow.FSharp.Bindable.ObjectModel
```

## Documentation

- [Data binding](https://morsecode-software.github.io/SodaFlow/docs/bindable.html)
- [Which package do I install?](https://morsecode-software.github.io/SodaFlow/docs/packages.html)
- [API reference](https://morsecode-software.github.io/SodaFlow/api/index.html)

The source is at <https://github.com/MorseCode-Software/SodaFlow>, under the BSD 3-Clause license.
