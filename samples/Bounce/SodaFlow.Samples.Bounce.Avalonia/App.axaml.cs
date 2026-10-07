using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Samples.Bounce.ViewModels;

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
            IBounceViewModel viewModel =
                BounceViewModel.Create(
                    bindableFactory: new BindableFactory(SynchronizationContextBindingScheduler.Capture()),
                    handleException: static ex => Trace.WriteLine(ex));

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
