using SitStand.Core.Model;

namespace SitStand.Core;

/// <summary>Immutable view of tracker state at one instant, for the UI. Recompute it; never cache it.</summary>
public sealed record TrackerSnapshot(
    DateTimeOffset Now,
    Activity Activity,
    DateTimeOffset StretchStartedAt,
    TimeSpan Elapsed,
    TimeSpan? ReminderInterval,
    DateTimeOffset? NextReminderAt,
    DateTimeOffset? SnoozedUntil,
    DateTimeOffset? PausedUntil,
    int SnoozeCount,
    Reminder? PendingReminder,
    bool IsWithinActiveHours)
{
    public bool IsPaused => PausedUntil is not null;
    public bool HasPendingReminder => PendingReminder is not null;

    /// <summary>0 at the start of a stretch, 1 when the reminder is due, above 1 when overdue. Null while moving.</summary>
    public double? ReminderProgress =>
        ReminderInterval is { } i && i > TimeSpan.Zero ? Elapsed / i : null;

    public TimeSpan? TimeUntilReminder =>
        NextReminderAt is { } due ? (due > Now ? due - Now : TimeSpan.Zero) : null;
}
