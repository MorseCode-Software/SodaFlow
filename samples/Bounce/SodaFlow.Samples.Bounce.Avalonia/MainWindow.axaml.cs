using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SodaFlow.Samples.Bounce.Avalonia;

/// <summary>
///     Markup and nothing else. The data context is supplied by <see cref="App" /> before this is
///     shown - compare the WPF window, which is now the same.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
// ReSharper disable once PartialTypeWithSinglePart - Partial due to generated code.
internal sealed partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
