using Avalonia;

namespace Pos.Till;

internal static class Program
{
    // Avalonia desktop entry point. Must be STA; do NOT touch any Avalonia type before
    // AppBuilder.Configure (initialisation order matters for the platform back-ends).
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
