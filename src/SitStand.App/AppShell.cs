using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SitStand.App.Services;
using SitStand.App.Tray;
using SitStand.App.ViewModels;
using SitStand.App.Views;
using SitStand.Core;
using SitStand.Core.Model;

namespace SitStand.App;

/// <summary>
/// UI composition root: owns the tray icon, the alert window and the dashboard, and wires tracker
/// events (which arrive on the timer thread) onto the UI thread. Everything below this is either
/// plain Core/Storage or a dumb view.
/// </summary>
public sealed class AppShell : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly AppHost _host;
    private readonly ILogger<AppShell> _log;
    private readonly SingleInstanceGuard _instance;
    private TrayController? _tray;
    private AlertWindow? _alert;
    private DashboardWindow? _dashboard;

    private AppShell(IClassicDesktopStyleApplicationLifetime desktop, AppHost host, SingleInstanceGuard instance)
    {
        _desktop = desktop;
        _host = host;
        _instance = instance;
        _log = host.LoggerFactory.CreateLogger<AppShell>();
    }

    public static AppShell Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var host = AppHost.Create();

        var instance = SingleInstanceGuard.TryAcquire(host.Paths.LockFile);
        if (instance is null)
        {
            host.LoggerFactory.CreateLogger<AppShell>().LogWarning("Another instance is already running; exiting.");
            Dispatcher.UIThread.Post(() => desktop.Shutdown(1));
            return new AppShell(desktop, host, SingleInstanceGuard.None);
        }

        host.StartTracking();

        var shell = new AppShell(desktop, host, instance);
        shell.WireUp();
        return shell;
    }

    private void WireUp()
    {
        var tracker = _host.Tracker;

        _tray = new TrayController(tracker, _host.Time, new TrayActions(
            OpenDashboard: ShowDashboard,
            OpenSettingsFile: () => Shell.Open(_host.SettingsStore.FilePath, _log),
            OpenDataFolder: () => Shell.Open(_host.Paths.DataDirectory, _log),
            ReloadSettings: ReloadSettings,
            Quit: Quit));

        tracker.ReminderDue += (_, reminder) => Dispatcher.UIThread.Post(() => ShowAlert(reminder));
        tracker.StateChanged += (_, snapshot) => Dispatcher.UIThread.Post(() =>
        {
            // Alert answered, timed out, or made moot by a time gap: take it down.
            if (!snapshot.HasPendingReminder) _alert?.Close();
        });

        _log.LogInformation("SitStand ready. Data in {Dir}", _host.Paths.DataDirectory);
    }

    private void ShowAlert(Reminder reminder)
    {
        if (_alert is not null)
        {
            _alert.Activate();
            return;
        }

        var vm = new AlertViewModel(_host.Tracker, reminder);
        var window = new AlertWindow { DataContext = vm };
        vm.RequestClose += () => window.Close();
        window.Closed += (_, _) => _alert = null;
        _alert = window;
        window.Show();
        window.Activate();
    }

    private void ShowDashboard()
    {
        if (_dashboard is not null)
        {
            _dashboard.Activate();
            return;
        }

        var vm = new DashboardViewModel(_host.Tracker, _host.Time);
        var window = new DashboardWindow { DataContext = vm };
        window.Closed += (_, _) =>
        {
            vm.Dispose();
            _dashboard = null;
        };
        _dashboard = window;
        window.Show();
        window.Activate();
    }

    private void ReloadSettings()
    {
        try
        {
            var settings = _host.SettingsStore.Load();
            _host.Tracker.UpdateSettings(settings);
            _log.LogInformation("Settings reloaded from {Path}", _host.SettingsStore.FilePath);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to reload settings");
        }
    }

    private void Quit()
    {
        _log.LogInformation("Quit requested");
        _desktop.Shutdown();
    }

    public void Dispose()
    {
        _tray?.Dispose();
        _host.Dispose();
        _instance.Dispose();
    }
}
