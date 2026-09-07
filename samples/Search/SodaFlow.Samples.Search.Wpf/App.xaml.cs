using System.Windows;
using SodaFlow.Samples.Search.ViewModels;

namespace SodaFlow.Samples.Search.Wpf;

/// <summary>
///     Builds the view model, hands it to the window as its data context, and shows the window.
/// </summary>
/// <remarks>
///     <para>
///         The window does not build its own view model, and that is the point. A view that
///         constructs what it binds to knows the concrete type and the factory that makes it, which
///         is exactly what binding against <see cref="ISearchViewModel" /> was for; the data
///         context is something the window is given, and the composition happens here where the
///         application is assembled.
///     </para>
///     <para>
///         It is also what lets the context be in place before the window is shown, rather than
///         partway through its constructor. That is why there is no <c>StartupUri</c>: it
///         constructs and shows the window in one step, leaving nowhere to set anything in between.
///     </para>
/// </remarks>
public partial class App
{
    private ISearchViewModel? viewModel;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        this.viewModel = SearchViewModel.Create();

        MainWindow window = new() { DataContext = this.viewModel };

        window.Show();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Disposing the view model also tears down the async pipeline and cancels anything still
    ///     in flight. Whoever built one releases it, which is now the application rather than the
    ///     window.
    /// </remarks>
    protected override void OnExit(ExitEventArgs e)
    {
        this.viewModel?.Dispose();

        base.OnExit(e);
    }
}
