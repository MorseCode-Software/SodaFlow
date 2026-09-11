using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SodaFlow.Samples.Accounts.Avalonia;

/// <summary>
///     Markup and nothing else. The data context is supplied by <see cref="App" /> before this is
///     shown, as in every sample here.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
// ReSharper disable once PartialTypeWithSinglePart - Partial due to generated code.
internal sealed partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
