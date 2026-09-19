using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SodaFlow.Samples.Counter.Avalonia;

/// <summary>
///     This holds only markup. <see cref="App" /> gives the data context before this window
///     shows. Compare the WPF window, which is the same.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
// ReSharper disable once PartialTypeWithSinglePart - Partial due to generated code.
internal sealed partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
