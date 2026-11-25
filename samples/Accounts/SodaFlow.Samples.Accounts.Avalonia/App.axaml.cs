using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Samples.Accounts.ViewModels;

namespace SodaFlow.Samples.Accounts.Avalonia;

/// <summary>
///     Builds the view model and gives it to the window as its data context,
///     before the lifetime shows the window.
/// </summary>
/// <remarks>
///     The window does not build its own view model, for the cause that applies to
///     the counter sample. A view that constructs the object that it binds to
///     knows the concrete type and the factory that makes it, and a bind to
///     <see cref="IAccountsViewModel" /> prevents that.
/// </remarks>
// The last segment of the namespace is Avalonia. Thus, in the code of the namespace, the name
// Avalonia refers to this namespace and not to the root namespace of the framework. A full name
// such as Avalonia.Controls.Window is then an error that is difficult to read. The using
// directives above the namespace do not have this problem, thus this code uses them and no full
// names.
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
            IAccountsViewModel viewModel =
                AccountsViewModelOptimizedDrain.Create(
                    new BindableFactory(SynchronizationContextBindingScheduler.Capture()));

            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Disposal of this also releases the row projection, and with it each row bindable
            // that the projection holds. Those are the rows that stayed on the page and thus had
            // no eviction.
            desktop.Exit += (_, _) => viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
