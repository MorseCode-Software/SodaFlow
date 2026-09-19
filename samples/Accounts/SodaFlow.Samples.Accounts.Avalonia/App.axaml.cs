using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Samples.Accounts.ViewModels;

namespace SodaFlow.Samples.Accounts.Avalonia;

/// <summary>
///     Builds the view model and gives it to the window as its data context, before the lifetime
///     shows the window.
/// </summary>
/// <remarks>
///     The window does not build its own view model, for the cause that applies to the counter
///     sample. A view that constructs the object that it binds to knows the concrete type and the
///     factory that makes it, and a bind to <see cref="IAccountsViewModel" /> prevents that.
/// </remarks>
// The namespace is AvaloniaUi and not Avalonia. A namespace with Avalonia as its last segment
// hides the root namespace of the framework from the code in it, and a usual full name such as
// Avalonia.Controls.Window then becomes an error that is difficult to read.
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
            // This code sets the scheduler before the first bindable, thus no subsequent code
            // depends on the thread of a bindable. This is more important here than in the other
            // samples, because this view model builds one bindable for each row, as the rows come
            // into view.
            BindingScheduler.Default = SynchronizationContextBindingScheduler.Capture();

            IAccountsViewModel viewModel = AccountsViewModelOptimizedDrain.Create();

            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Disposal of this also releases the row projection, and with it each row bindable
            // that the projection holds. Those are the rows that stayed on the page and thus had
            // no eviction.
            desktop.Exit += (_, _) => viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
