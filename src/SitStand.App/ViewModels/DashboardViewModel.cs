using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SitStand.App.Services;
using SitStand.Core;
using SitStand.Core.Stats;

namespace SitStand.App.ViewModels;

/// <summary>Today's numbers plus one row per recent day. Recomputed from the timeline on every state change.</summary>
public sealed partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private const int HistoryDays = 14;

    private readonly ActivityTracker _tracker;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _timer;
    private readonly EventHandler<TrackerSnapshot> _onStateChanged;

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _movementBreaks = "0";
    [ObservableProperty] private string _sitting = "0m";
    [ObservableProperty] private string _standing = "0m";
    [ObservableProperty] private string _moving = "0m";
    [ObservableProperty] private string _away = "0m";
    [ObservableProperty] private string _averageSit = "0m";
    [ObservableProperty] private string _longestSit = "0m";
    [ObservableProperty] private string _reminders = "0";
    [ObservableProperty] private string _dataLocation = "";

    public ObservableCollection<DayRow> Days { get; } = new();

    public DashboardViewModel(ActivityTracker tracker, TimeProvider time)
    {
        _tracker = tracker;
        _time = time;

        _onStateChanged = (_, _) => Dispatcher.UIThread.Post(Refresh);
        tracker.StateChanged += _onStateChanged;

        Refresh();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => Refresh());
        _timer.Start();
    }

    private void Refresh()
    {
        var snapshot = _tracker.Snapshot;
        var settings = _tracker.Settings;
        var zone = _time.LocalTimeZone;
        var now = snapshot.Now;
        var today = DayWindow.DayOf(now, settings.DayStartsAt, zone);

        Status = $"{Formatting.Label(snapshot.Activity)} for {Formatting.Clock(snapshot.Elapsed)}"
                 + (snapshot.NextReminderAt is { } due && !snapshot.HasPendingReminder
                     ? $" · next reminder {(due > now ? "in " + Formatting.Coarse(due - now) : "due")}"
                     : "");

        var stats = DailyStatsCalculator.ForDay(_tracker.History, today, settings, zone, now);
        MovementBreaks = stats.MovementBreaks.ToString();
        Sitting = Formatting.Coarse(stats.Sitting);
        Standing = Formatting.Coarse(stats.Standing);
        Moving = Formatting.Coarse(stats.Moving);
        Away = Formatting.Coarse(stats.Away);
        AverageSit = Formatting.Coarse(stats.AverageSittingStretch);
        LongestSit = Formatting.Coarse(stats.LongestSittingStretch);
        Reminders = RemindersText(stats);

        var days = DailyStatsCalculator.DaysWithData(_tracker.History, settings, zone, now)
            .Take(HistoryDays)
            .Select(day => DayRow.From(DailyStatsCalculator.ForDay(_tracker.History, day, settings, zone, now), day == today))
            .ToList();

        // Replace in place so the list doesn't flicker on every tick.
        for (var i = 0; i < days.Count; i++)
        {
            if (i < Days.Count) Days[i] = days[i];
            else Days.Add(days[i]);
        }
        while (Days.Count > days.Count) Days.RemoveAt(Days.Count - 1);
    }

    private static string RemindersText(DailyStats s)
    {
        if (s.RemindersFired == 0) return "0";
        var parts = new List<string>();
        if (s.Snoozed > 0) parts.Add($"{s.Snoozed} snoozed");
        if (s.Skipped > 0) parts.Add($"{s.Skipped} skipped");
        if (s.Ignored > 0) parts.Add($"{s.Ignored} ignored");
        return parts.Count == 0 ? s.RemindersFired.ToString() : $"{s.RemindersFired} · {string.Join(", ", parts)}";
    }

    public void Dispose()
    {
        _timer.Stop();
        _tracker.StateChanged -= _onStateChanged;
    }
}

public sealed record DayRow(
    string Day,
    string Breaks,
    string Sitting,
    string LongestSit,
    string Moving,
    string Standing,
    string Away,
    string Reminders,
    bool IsToday)
{
    public static DayRow From(DailyStats s, bool isToday) => new(
        Day: isToday ? "Today" : s.Day.ToString("ddd MMM d"),
        Breaks: s.MovementBreaks.ToString(),
        Sitting: Formatting.Coarse(s.Sitting),
        LongestSit: Formatting.Coarse(s.LongestSittingStretch),
        Moving: Formatting.Coarse(s.Moving),
        Standing: Formatting.Coarse(s.Standing),
        Away: Formatting.Coarse(s.Away),
        Reminders: s.RemindersFired == 0 ? "–" : $"{s.RemindersFired} ({s.Snoozed}s/{s.Skipped}k/{s.Ignored}i)",
        IsToday: isToday);
}
