using SodaFlow.Samples.Bounce.ViewModels;

namespace SodaFlow.Samples.Bounce.Wpf;

/// <summary>
///     The entire WPF side of this sample: build the view model, bind to it, dispose it.
/// </summary>
public partial class MainWindow
{
    private readonly IBounceViewModel viewModel =
        BounceViewModel.Create(ex => System.Diagnostics.Debug.WriteLine(ex));

    public MainWindow()
    {
        this.InitializeComponent();

        this.DataContext = this.viewModel;

        // Only the bindable properties need this. The balls do not: they are behaviors, and
        // nothing subscribes to a behavior.
        this.Closed += (_, _) => this.viewModel.Dispose();
    }
}
