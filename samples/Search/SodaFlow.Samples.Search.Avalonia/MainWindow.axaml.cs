using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SodaFlow.Samples.Search.ViewModels;

namespace SodaFlow.Samples.Search.Avalonia;

/// <summary>
///     Compare with the WPF window: a different framework and a different XAML dialect, over
///     the same view model with nothing changed.
/// </summary>
public partial class MainWindow : Window
{
    private readonly SearchViewModel viewModel = SearchViewModel.Create();

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        this.DataContext = this.viewModel;

        this.Closed += (_, _) => this.viewModel.Dispose();
    }
}