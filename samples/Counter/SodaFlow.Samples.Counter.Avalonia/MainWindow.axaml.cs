using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SodaFlow.Samples.Counter;

namespace SodaFlow.Samples.Counter.AvaloniaUi
{
    /// <summary>
    ///     The entire Avalonia side of this sample. Compare it with the WPF window: different
    ///     framework, different XAML dialect, same view model with nothing changed.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly CounterViewModel viewModel = new CounterViewModel();

        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);

            this.DataContext = this.viewModel;

            // The view model holds subscriptions into the FRP graph. Disposing it releases them.
            this.Closed += (_, __) => this.viewModel.Dispose();
        }
    }
}
