using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SodaFlow.Samples.Counter.Avalonia
{
    // Namespace AvaloniaUi rather than Avalonia: a namespace whose last segment is Avalonia hides
    // the framework's own root namespace from anything written inside it, which turns ordinary
    // qualified names like Avalonia.Controls.Window into errors that read very strangely.
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
