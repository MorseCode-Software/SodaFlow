using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Samples.Accounts.ViewModels;

namespace SodaFlow.Samples.Accounts.Avalonia;

/// <summary>
///     Builds the view model and hands it to the window as its data context, before the lifetime
///     shows it.
/// </summary>
/// <remarks>
///     The window does not build its own view model, for the reason the counter sample's does not:
///     a view that constructs what it binds to knows the concrete type and the factory that makes
///     it, which is what binding against <see cref="IAccountsViewModel" /> was meant to avoid.
/// </remarks>
// Namespace AvaloniaUi rather than Avalonia: a namespace whose last segment is Avalonia hides
// the framework's own root namespace from anything written inside it, which turns ordinary
// qualified names like Avalonia.Controls.Window into errors that read very strangely.
// ReSharper disable once InheritdocConsiderUsage
internal sealed class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Pinned before anything bindable exists, so nothing afterward depends on which thread
            // a bindable happened to be built on. It matters more here than in the other samples:
            // this view model builds a bindable per row, on demand, as rows come into view.
            BindingScheduler.Default = SynchronizationContextBindingScheduler.Capture();

            IAccountsViewModel viewModel = AccountsViewModel.Create();

            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Disposing this releases the row projection too, and with it every row bindable still
            // held - the ones that never left the page and so were never evicted.
            desktop.Exit += (_, _) => viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
