using Avalonia;

namespace SodaFlow.Samples.Counter.Avalonia;

public static class Program
{
    public static int Main(string[] args) => Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}