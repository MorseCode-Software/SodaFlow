using Avalonia;

namespace SodaFlow.Samples.Search.Avalonia;

public static class Program
{
    public static int Main(string[] args) => Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}