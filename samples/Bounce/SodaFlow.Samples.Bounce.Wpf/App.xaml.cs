using System.Diagnostics;
using System.Windows;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Samples.Bounce.ViewModels;

namespace SodaFlow.Samples.Bounce.Wpf;

/// <summary>
///     Builds the view model, hands it to the window as its data context, and shows the window.
/// </summary>
/// <remarks>
///     <para>
///         The window does not build its own view model, and that is the point. A view that
///         constructs what it binds to knows the concrete type and the factory that makes it, which
///         is exactly what binding against <see cref="IBounceViewModel" /> was for; the data
///         context is something the window is given, and the composition happens here where the
///         application is assembled.
///     </para>
///     <para>
///         It is also what lets the context be in place before the window is shown, rather than
///         partway through its constructor. That is why there is no <c>StartupUri</c>: it
///         constructs and shows the window in one step, leaving nowhere to set anything in between.
///     </para>
///     <para>
///         The binding scheduler is pinned here too, before anything bindable exists, so that
///         nothing afterward depends on which thread a bindable happened to be built on.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed partial class App
{
    private IBounceViewModel? viewModel;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Pinned before anything bindable exists, so nothing afterward depends on which
        // thread a bindable happened to be built on. Without it each one captures the
        // synchronization context of its constructing thread, and a view model built off
        // the UI thread would quietly get the wrong one - or none, and run inline.
        BindingScheduler.Default = SynchronizationContextBindingScheduler.Capture();

        // The handler is called with anything raised while waiting for or firing a timer.
        // Timer callbacks run outside any call stack of yours, so an exception in one has
        // nowhere else to go.
        this.viewModel = BounceViewModel.Create(static ex => Debug.WriteLine(ex));

        MainWindow window = new() { DataContext = this.viewModel };

        window.Show();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Only the bindable properties need this. The balls do not: they are behaviors, and
    ///     nothing subscribes to a behavior. Whoever built the view model releases it, which is now
    ///     the application rather than the window.
    /// </remarks>
    protected override void OnExit(ExitEventArgs e)
    {
        this.viewModel?.Dispose();

        base.OnExit(e);
    }
}
