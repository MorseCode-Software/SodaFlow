using SodaFlow.Samples.Counter.ViewModels;

namespace SodaFlow.Samples.Counter.Wpf
{
    /// <summary>
    ///     The entire WPF side of this sample: build the view model, bind to it, dispose it.
    /// </summary>
    public partial class MainWindow
    {
        private readonly CounterViewModel viewModel = CounterViewModel.Create();

        public MainWindow()
        {
            this.InitializeComponent();

            this.DataContext = this.viewModel;

            // The view model holds subscriptions into the FRP graph. Disposing it releases them.
            this.Closed += (_, _) => this.viewModel.Dispose();
        }
    }
}
