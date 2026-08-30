using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SodaFlow.Samples.Search;

namespace SodaFlow.Samples.Search.AvaloniaUi
{
    /// <summary>
    ///     Compare with the WPF window: a different framework and a different XAML dialect, over
    ///     the same view model with nothing changed.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly SearchViewModel viewModel = new SearchViewModel();

        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);

            this.DataContext = this.viewModel;

            this.Closed += (_, __) => this.viewModel.Dispose();
        }
    }
}
