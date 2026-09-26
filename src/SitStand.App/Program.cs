using Avalonia;
using SitStand.App.Services;

namespace SitStand.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // A tray app has no console; make sure the reason it died ends up somewhere we can read.
            AppLogging.WriteCrashFile(ex);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Menu-bar-only on macOS: no Dock icon. (The .app bundle also sets LSUIElement for the same effect.)
            .With(new MacOSPlatformOptions { ShowInDock = false })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
