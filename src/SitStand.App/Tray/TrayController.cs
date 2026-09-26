using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SitStand.App.Services;
using SitStand.Core;
using SitStand.Core.Model;
using SitStand.Core.Stats;

namespace SitStand.App.Tray;

public sealed record TrayActions(
    Action OpenDashboard,
    Action OpenSettingsFile,
    Action OpenDataFolder,
    Action ReloadSettings,
    Action Quit);

/// <summary>
/// The tray icon and its menu. Built in code rather than XAML because half the items change their
/// text or visibility with state, and a NativeMenu is a plain object graph either way.
/// The header line is refreshed once a second (cheap — it's a property set) and again whenever the
/// platform asks via <see cref="NativeMenu.NeedsUpdate"/> right before showing the menu.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly ActivityTracker _tracker;
    private readonly TimeProvider _time;
    private readonly TrayActions _actions;
    private readonly TrayIconRenderer _renderer = new();

    private readonly TrayIcon _icon;
    private readonly NativeMenuItem _status;
    private readonly NativeMenuItem _today;
    private readonly NativeMenuItem _moving;
    private readonly NativeMenuItem _standing;
    private readonly NativeMenuItem _sitting;
    private readonly NativeMenuItem _snooze;
    private readonly NativeMenuItem _pause;
    private readonly NativeMenuItem _resume;
    private readonly DispatcherTimer _timer;

    private (TrayIconRenderer.Shape, Avalonia.Media.Color)? _iconKey;
    private DailyStats? _todayStats;
    private bool _statsDirty = true;

    public TrayController(ActivityTracker tracker, TimeProvider time, TrayActions actions)
    {
        _tracker = tracker;
        _time = time;
        _actions = actions;

        _status = new NativeMenuItem("Starting…") { IsEnabled = false };
        _today = new NativeMenuItem("Today: —") { IsEnabled = false };

        _moving = Item("I'm Moving", tracker.StartMoving);
        _standing = Item("I'm Standing", tracker.StartStanding);
        _sitting = Item("Back to Sitting", tracker.BackToSitting);
        _snooze = Item("Snooze", () => tracker.Snooze());
        _pause = Item("Pause reminders", () => tracker.Pause());
        _resume = Item("Resume reminders", tracker.Resume);

        var menu = new NativeMenu();
        menu.Items.Add(_status);
        menu.Items.Add(_today);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_moving);
        menu.Items.Add(_standing);
        menu.Items.Add(_sitting);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_snooze);
        menu.Items.Add(_pause);
        menu.Items.Add(_resume);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item("Open Dashboard", actions.OpenDashboard));
        menu.Items.Add(Item("Open Settings File…", actions.OpenSettingsFile));
        menu.Items.Add(Item("Reload Settings", actions.ReloadSettings));
        menu.Items.Add(Item("Open Data Folder…", actions.OpenDataFolder));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item("Quit SitStand", actions.Quit));
        menu.NeedsUpdate += (_, _) => Refresh();

        _icon = new TrayIcon { Menu = menu, ToolTipText = "SitStand" };
        // Left-click (Windows/Linux only; macOS opens the menu regardless) goes straight to the dashboard.
        _icon.Clicked += (_, _) => actions.OpenDashboard();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _icon });

        tracker.StateChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _statsDirty = true;
            Refresh();
        });

        Refresh();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh());
        _timer.Start();
    }

    private void Refresh()
    {
        var s = _tracker.Snapshot;
        var settings = _tracker.Settings;

        _status.Header = StatusText(s);
        _today.Header = TodayText(s);

        _moving.IsEnabled = s.Activity != Activity.Moving;
        _standing.IsEnabled = s.Activity != Activity.Standing;
        _sitting.IsEnabled = s.Activity != Activity.Sitting;

        var remindable = s.Activity is Activity.Sitting or Activity.Standing;
        _snooze.IsVisible = remindable;
        _snooze.Header = $"Snooze {settings.SnoozeDuration.TotalMinutes:0} minutes";
        _pause.IsVisible = !s.IsPaused;
        _pause.Header = $"Pause reminders for {Formatting.Coarse(settings.PauseDuration)}";
        _resume.IsVisible = s.IsPaused;
        _resume.Header = s.PausedUntil is { } until ? $"Resume reminders (paused until {until.LocalDateTime:t})" : "Resume reminders";

        _icon.ToolTipText = $"SitStand — {_status.Header}";

        var key = _renderer.Describe(s);
        if (_iconKey != key)
        {
            _icon.Icon = _renderer.IconFor(s);
            _iconKey = key;
        }
    }

    private string StatusText(TrackerSnapshot s)
    {
        var text = $"{Formatting.Label(s.Activity)}: {Formatting.Clock(s.Elapsed)}";
        if (s.IsPaused) text += "  ·  paused";
        else if (s.HasPendingReminder) text += "  ·  time to move";
        else if (s.SnoozeCount > 0) text += $"  ·  snoozed ×{s.SnoozeCount}";
        else if (!s.IsWithinActiveHours) text += "  ·  outside active hours";
        return text;
    }

    private string TodayText(TrackerSnapshot s)
    {
        var zone = _time.LocalTimeZone;
        var today = DayWindow.DayOf(s.Now, _tracker.Settings.DayStartsAt, zone);

        if (_statsDirty || _todayStats is null || _todayStats.Day != today)
        {
            _todayStats = DailyStatsCalculator.ForDay(_tracker.History, today, _tracker.Settings, zone, s.Now);
            _statsDirty = false;
        }

        var breaks = Formatting.Plural(_todayStats.MovementBreaks, "movement break");
        return $"Today: {breaks}  ·  longest sit {Formatting.Coarse(_todayStats.LongestSittingStretch)}";
    }

    private static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    public void Dispose()
    {
        _timer.Stop();
        _icon.Dispose();
    }
}
