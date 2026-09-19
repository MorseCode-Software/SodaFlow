using System.Diagnostics;
using System.Windows;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Samples.Bounce.ViewModels;

namespace SodaFlow.Samples.Bounce.Wpf;

/// <summary>
///     Builds the view model, gives it to the window as its data context, and shows the window.
/// </summary>
/// <remarks>
///     <para>
///         The window does not build its own view model, and that is the purpose of this class. A
///         view that constructs the object that it binds to knows the concrete type and the factory
///         that makes it, and a bind to <see cref="IBounceViewModel" /> prevents that. The window
///         receives the data context, and this code assembles the sample.
///     </para>
///     <para>
///         This also gives the window its data context before the window shows, and not during
///         its constructor. For that cause there is no <c>StartupUri</c>. StartupUri constructs the
///         window and shows it in one step, and gives no position to set the data context.
///     </para>
///     <para>
///         This code also sets the binding scheduler here, before the first bindable, thus no
///         subsequent code depends on the thread of a bindable.
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

        // This code sets the scheduler before the first bindable, thus no subsequent code
        // depends on the thread of a bindable. Without this, each bindable captures the
        // synchronization context of the thread that constructs it. A view model that a
        // different thread builds then gets the incorrect context, or no context, and runs
        // inline.
        BindingScheduler.Default = SynchronizationContextBindingScheduler.Capture();

        // The handler receives each exception from a wait on a timer and from a timer that
        // fires. A timer callback does not run on a call stack of the caller, thus there is
        // no other destination for its exception. This code uses Trace and not Debug, because
        // the compiler removes Debug.WriteLine from a release build and the handler then does
        // nothing.
        this.viewModel = BounceViewModel.Create(static ex => Trace.WriteLine(ex));

        MainWindow window = new() { DataContext = this.viewModel };

        window.Show();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Only the bindable properties use this. The balls do not use it, because they are
    ///     behaviors and no code subscribes to a behavior. The code that built the view model
    ///     releases it, and that is the sample and not the window.
    /// </remarks>
    protected override void OnExit(ExitEventArgs e)
    {
        this.viewModel?.Dispose();

        base.OnExit(e);
    }
}
