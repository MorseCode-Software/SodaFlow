using System.Windows;

namespace SodaFlow.Samples.Counter.Wpf
{
    /// <summary>
    ///     The entire WPF side of this sample: build the view model, bind to it, dispose it.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly CounterViewModel viewModel = new CounterViewModel();

        public MainWindow()
        {
            this.InitializeComponent();

            this.DataContext = this.viewModel;

            // The view model holds subscriptions into the FRP graph. Disposing it releases them.
            this.Closed += (_, __) => this.viewModel.Dispose();
        }
    }
}
