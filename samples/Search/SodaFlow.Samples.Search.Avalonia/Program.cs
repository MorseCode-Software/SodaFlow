using Avalonia;

namespace SodaFlow.Samples.Search.AvaloniaUi
{
    public static class Program
    {
        public static int Main(string[] args) =>
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
