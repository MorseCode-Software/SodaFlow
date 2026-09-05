using SodaFlow.Samples.Search.ViewModels;

namespace SodaFlow.Samples.Search.Wpf;

public partial class MainWindow
{
    private readonly SearchViewModel viewModel = SearchViewModel.Create();

    public MainWindow()
    {
        this.InitializeComponent();

        this.DataContext = this.viewModel;

        // Disposing the view model also tears down the async pipeline and cancels anything
        // still in flight.
        this.Closed += (_, _) => this.viewModel.Dispose();
    }
}