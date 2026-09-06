using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SodaFlow.Samples.Bounce.ViewModels;

namespace SodaFlow.Samples.Bounce.Avalonia;

/// <summary>
///     The entire Avalonia side of this sample. Compare it with the WPF window: different
///     framework, different XAML dialect, same view model with nothing changed.
/// </summary>
/// <remarks>
///     Nothing is disposed when this closes, unlike the other samples. There is nothing to
///     dispose: no part of this holds a subscription into the graph, because a behavior is read by
///     sampling rather than by subscribing.
/// </remarks>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        this.DataContext = BounceViewModel.Create(ex => System.Diagnostics.Debug.WriteLine(ex));
    }
}
