using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SodaFlow.Samples.Bounce.ViewModels;

namespace SodaFlow.Samples.Bounce.Avalonia;

/// <summary>
///     The entire Avalonia side of this sample. Compare it with the WPF window: different
///     framework, different XAML dialect, same view model with nothing changed.
/// </summary>
public partial class MainWindow : Window
{
    private readonly BounceViewModel viewModel =
        BounceViewModel.Create(ex => System.Diagnostics.Debug.WriteLine(ex));

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        this.DataContext = this.viewModel;

        // Only the bindable properties need this. The balls do not: they are behaviors, and
        // nothing subscribes to a behavior.
        this.Closed += (_, _) => this.viewModel.Dispose();
    }
}
