using System.Windows;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Samples.Accounts.ViewModels;

namespace SodaFlow.Samples.Accounts.Wpf;

/// <summary>
///     Builds the view model, gives it to the window as its data context, and shows the window.
/// </summary>
/// <remarks>
///     This has the shape of each other sample here. The window receives its data context and
///     does not build one, and the composition is where this code assembles the sample.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed partial class App
{
    private IAccountsViewModel? viewModel;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // This code sets the scheduler before the first bindable, thus no subsequent code depends
        // on the thread of a bindable. This is more important here than in the other samples,
        // because this view model builds one bindable for each row, as the rows come into view.
        BindingScheduler.Default = SynchronizationContextBindingScheduler.Capture();

        this.viewModel = AccountsViewModelOptimizedDrain.Create();

        MainWindow window = new() { DataContext = this.viewModel };

        window.Show();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Disposal of this also releases the row projection, and with it each row bindable that
    ///     the projection holds. Those are the rows that stayed on the page and thus had no
    ///     eviction.
    /// </remarks>
    protected override void OnExit(ExitEventArgs e)
    {
        this.viewModel?.Dispose();

        base.OnExit(e);
    }
}
