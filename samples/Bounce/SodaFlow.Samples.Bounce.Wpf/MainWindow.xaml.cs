using SodaFlow.Samples.Bounce.ViewModels;

namespace SodaFlow.Samples.Bounce.Wpf;

/// <summary>
///     The entire WPF side of this sample: build the view model and bind to it.
/// </summary>
/// <remarks>
///     Nothing is disposed when this closes, unlike the other samples. There is nothing to
///     dispose: no part of this holds a subscription into the graph, because a behavior is read by
///     sampling rather than by subscribing.
/// </remarks>
public partial class MainWindow
{
    public MainWindow()
    {
        this.InitializeComponent();

        this.DataContext = BounceViewModel.Create(ex => System.Diagnostics.Debug.WriteLine(ex));
    }
}
