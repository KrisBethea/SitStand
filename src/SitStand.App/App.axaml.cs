using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SitStand.App;

public partial class App : Application
{
    private AppShell? _shell;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // There is no main window. Closing the dashboard or an alert must not quit the app; only "Quit" does.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _shell = AppShell.Start(desktop);
            desktop.Exit += (_, _) => _shell?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
