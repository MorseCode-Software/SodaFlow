using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SodaFlow.Samples.Counter.Avalonia;

/// <summary>
///     Markup and nothing else. The data context is supplied by <see cref="App" /> before this is
///     shown - compare the WPF window, which is now the same.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
