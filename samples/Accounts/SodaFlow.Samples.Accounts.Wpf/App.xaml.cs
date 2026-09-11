using System.Windows;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Samples.Accounts.ViewModels;

namespace SodaFlow.Samples.Accounts.Wpf;

/// <summary>
///     Builds the view model, hands it to the window as its data context, and shows the window.
/// </summary>
/// <remarks>
///     The same shape as every other sample here: the window is given its data context rather than
///     building one, and the composition happens where the application is assembled.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed partial class App
{
    private IAccountsViewModel? viewModel;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Pinned before anything bindable exists, so nothing afterward depends on which thread a
        // bindable happened to be built on. It matters more here than in the other samples: this
        // view model builds a bindable per row, on demand, as rows come into view.
        BindingScheduler.Default = SynchronizationContextBindingScheduler.Capture();

        this.viewModel = AccountsViewModel.Create();

        MainWindow window = new() { DataContext = this.viewModel };

        window.Show();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Disposing this releases the row projection too, and with it every row bindable still
    ///     held - the ones that never left the page and so were never evicted.
    /// </remarks>
    protected override void OnExit(ExitEventArgs e)
    {
        this.viewModel?.Dispose();

        base.OnExit(e);
    }
}
