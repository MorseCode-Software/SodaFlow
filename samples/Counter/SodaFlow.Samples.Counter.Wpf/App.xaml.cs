using System.Windows;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Samples.Counter.ViewModels;

namespace SodaFlow.Samples.Counter.Wpf;

/// <summary>
///     Builds the view model, gives it to the window as its data context, and shows the window.
/// </summary>
/// <remarks>
///     <para>
///         The window does not build its own view model, and that is the purpose of this class. A
///         view that constructs the object that it binds to knows the concrete type and the factory
///         that makes it, and a bind to <see cref="ICounterViewModel" /> prevents that. The window
///         receives the data context, and this code assembles the sample.
///     </para>
///     <para>
///         This also gives the window its data context before the window shows, and not during
///         its constructor. For that cause there is no <c>StartupUri</c>. StartupUri constructs the
///         window and shows it in one step, and gives no position to set the data context.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed partial class App
{
    private ICounterViewModel? viewModel;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // This code sets the scheduler before the first bindable, thus no subsequent code
        // depends on the thread of a bindable. Without this, each bindable captures the
        // synchronization context of the thread that constructs it. A view model that a
        // different thread builds then gets the incorrect context, or no context, and runs
        // inline.
        BindingScheduler.Default = SynchronizationContextBindingScheduler.Capture();

        this.viewModel = CounterViewModel.Create();

        MainWindow window = new() { DataContext = this.viewModel };

        window.Show();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The view model holds subscriptions into the FRP graph, and the code that built one
    ///     releases it. That is the sample and not the window.
    /// </remarks>
    protected override void OnExit(ExitEventArgs e)
    {
        this.viewModel?.Dispose();

        base.OnExit(e);
    }
}
