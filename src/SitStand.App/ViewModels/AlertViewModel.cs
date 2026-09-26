using CommunityToolkit.Mvvm.Input;
using SitStand.App.Services;
using SitStand.Core;
using SitStand.Core.Model;

namespace SitStand.App.ViewModels;

/// <summary>The four-button interrupt. Each command applies to the tracker and asks the window to close.</summary>
public sealed partial class AlertViewModel : ViewModelBase
{
    private readonly ActivityTracker _tracker;

    public event Action? RequestClose;

    public string Title { get; }
    public string Message { get; }
    public string SnoozeLabel { get; }
    public bool ShowStanding { get; }

    public AlertViewModel(ActivityTracker tracker, Reminder reminder)
    {
        _tracker = tracker;
        var snapshot = tracker.Snapshot;
        var settings = tracker.Settings;

        var standing = reminder.ActivityAtFire == Activity.Standing;
        Title = standing ? "You've been standing a while" : "Time to move";
        Message = $"You've been {Formatting.Label(reminder.ActivityAtFire).ToLowerInvariant()} for {Formatting.Coarse(snapshot.Elapsed)}."
                  + (snapshot.SnoozeCount > 0 ? $" Snoozed {snapshot.SnoozeCount}× already." : "");
        SnoozeLabel = $"Snooze {settings.SnoozeDuration.TotalMinutes:0} min";
        ShowStanding = !standing && settings.StandingReminderInterval is not null;
    }

    [RelayCommand]
    private void Moving()
    {
        _tracker.StartMoving();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Standing()
    {
        _tracker.StartStanding();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Snooze()
    {
        _tracker.Snooze();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Skip()
    {
        _tracker.Skip();
        RequestClose?.Invoke();
    }
}
