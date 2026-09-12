using System.Windows;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Samples.Counter.ViewModels;

namespace SodaFlow.Samples.Counter.Wpf.NetFramework;

/// <summary>
///     Builds the view model, hands it to the window as its data context, and shows the window.
/// </summary>
/// <remarks>
///     <para>
///         Line for line the .NET 10 head's startup, and that is the point of this project: nothing
///         here knows or cares that the runtime underneath is .NET Framework, because nothing the
///         view model is made of does either.
///     </para>
///     <para>
///         The window does not build its own view model. A view that constructs what it binds to
///         knows the concrete type and the factory that makes it, which is exactly what binding
///         against <see cref="ICounterViewModel" /> was for; the data context is something the
///         window is given, and the composition happens here where the application is assembled.
///     </para>
///     <para>
///         It is also what lets the context be in place before the window is shown, rather than
///         partway through its constructor. That is why there is no <c>StartupUri</c>: it
///         constructs and shows the window in one step, leaving nowhere to set anything in between.
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

        // Pinned before anything bindable exists, so nothing afterward depends on which
        // thread a bindable happened to be built on. Without it each one captures the
        // synchronization context of its constructing thread, and a view model built off
        // the UI thread would quietly get the wrong one - or none, and run inline.
        BindingScheduler.Default = SynchronizationContextBindingScheduler.Capture();

        this.viewModel = CounterViewModel.Create();

        MainWindow window = new() { DataContext = this.viewModel };

        window.Show();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The view model holds subscriptions into the FRP graph, and whoever built one releases
    ///     it - which is the application rather than the window.
    /// </remarks>
    protected override void OnExit(ExitEventArgs e)
    {
        this.viewModel?.Dispose();

        base.OnExit(e);
    }
}
