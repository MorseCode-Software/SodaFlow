using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Samples.Counter.ViewModels;

namespace SodaFlow.Samples.Counter.Avalonia;

/// <summary>
///     Builds the view model and hands it to the window as its data context, before the lifetime
///     shows it.
/// </summary>
/// <remarks>
///     The window does not build its own view model. A view that constructs what it binds to knows
///     the concrete type and the factory that makes it, which is what binding against
///     <see cref="ICounterViewModel" /> was meant to avoid; the data context is something the
///     window is given, and the composition happens here. Compare the WPF head, which does the same
///     thing in <c>OnStartup</c>.
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
            // Pinned before anything bindable exists, so nothing afterward depends on
            // which thread a bindable happened to be built on. Without it each one
            // captures the synchronization context of its constructing thread, and a view
            // model built off the UI thread would quietly get the wrong one - or none, and
            // run inline.
            BindingScheduler.Default = SynchronizationContextBindingScheduler.Capture();

            ICounterViewModel viewModel = CounterViewModel.Create();

            // Assigned rather than shown: the lifetime shows this window once this method
            // returns, so the data context is in place before anything is on screen.
            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // The view model holds subscriptions into the FRP graph, and whoever built one
            // releases it - which is now the application rather than the window.
            desktop.Exit += (_, _) => viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
