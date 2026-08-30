using System.Windows;

namespace SodaFlow.Samples.Search.Wpf
{
    public partial class MainWindow : Window
    {
        private readonly SearchViewModel viewModel = new SearchViewModel();

        public MainWindow()
        {
            this.InitializeComponent();

            this.DataContext = this.viewModel;

            // Disposing the view model also tears down the async pipeline and cancels anything
            // still in flight.
            this.Closed += (_, __) => this.viewModel.Dispose();
        }
    }
}
