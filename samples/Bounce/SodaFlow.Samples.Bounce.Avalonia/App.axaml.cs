using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Samples.Bounce.ViewModels;
using SodaFlow.Time;

namespace SodaFlow.Samples.Bounce.Avalonia;

/// <summary>
///     Builds the view model and gives it to the window as its data context,
///     before the lifetime shows the window.
/// </summary>
/// <remarks>
///     The window does not build its own view model. A view that constructs the
///     object that it binds to knows the concrete type and the factory that makes
///     it, and a bind to <see cref="IBounceViewModel" /> prevents that. The window
///     receives the data context, and this code does the composition. Compare the
///     WPF head, which does the same operation in <c>OnStartup</c>.
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
            // The clock belongs to the startup and not to the view model. The timer system keeps a thread
            // while the process runs, thus one timer system serves the full sample.
            SecondsTimerSystem timers = new(handleException: static ex => Trace.WriteLine(ex));

            IBounceViewModel viewModel =
                BounceViewModel.Create(
                    bindableFactory: new BindableFactory(SynchronizationContextBindingScheduler.Capture()),
                    timers: timers);

            // This code assigns the window and does not show it. The lifetime shows this window
            // after this method returns, thus the window has its data context before it is on
            // the screen.
            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Only the bindable properties use this. The balls do not use it, because they are
            // behaviors and no code subscribes to a behavior. The code that built the view model
            // releases it, and that is the sample and not the window.
            desktop.Exit += (_, _) => viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
