using System.Text.Json;
using System.Text.Json.Serialization;

namespace SitStand.Core;

/// <summary>User-tunable behaviour. Persisted as settings.json; everything has a sensible default.</summary>
public sealed record TrackerSettings
{
    /// <summary>How long you may sit before a reminder fires.</summary>
    public TimeSpan SittingReminderInterval { get; init; } = TimeSpan.FromMinutes(50);

    /// <summary>
    /// How long you may stand still before a reminder fires. Static standing is also sedentary, so
    /// standing does not switch reminders off — it just gets a longer leash. Null disables it.
    /// </summary>
    public TimeSpan? StandingReminderInterval { get; init; } = TimeSpan.FromMinutes(90);

    /// <summary>How long a snooze pushes the next reminder out. Also used as the re-nag delay after an ignored reminder.</summary>
    public TimeSpan SnoozeDuration { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a reminder waits for a response before it is recorded as Ignored.</summary>
    public TimeSpan ReminderTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// A silence between ticks longer than this means the machine slept, was suspended, or the app was
    /// not running. The open segment is closed at the last tick and the gap is recorded as Away.
    /// </summary>
    public TimeSpan GapThreshold { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Default length for "Pause reminders" from the menu.</summary>
    public TimeSpan PauseDuration { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Where one day ends and the next begins, in local time. 04:00 means a session at 1 a.m. belongs to
    /// the previous day, which is usually what late-night work means.
    /// </summary>
    public TimeOnly DayStartsAt { get; init; } = new(4, 0);

    /// <summary>When reminders are allowed to fire. Tracking always runs; only reminders are gated. Null means always.</summary>
    public ActiveHours? ActiveHours { get; init; }

    public static TrackerSettings Default { get; } = new();

    public void Validate()
    {
        if (SittingReminderInterval <= TimeSpan.Zero) throw Invalid(nameof(SittingReminderInterval), "must be positive");
        if (StandingReminderInterval is { } s && s <= TimeSpan.Zero) throw Invalid(nameof(StandingReminderInterval), "must be positive or null");
        if (SnoozeDuration <= TimeSpan.Zero) throw Invalid(nameof(SnoozeDuration), "must be positive");
        if (ReminderTimeout <= TimeSpan.Zero) throw Invalid(nameof(ReminderTimeout), "must be positive");
        if (GapThreshold < TimeSpan.FromSeconds(30)) throw Invalid(nameof(GapThreshold), "must be at least 30 seconds");
        if (PauseDuration <= TimeSpan.Zero) throw Invalid(nameof(PauseDuration), "must be positive");
        ActiveHours?.Validate();

        static ArgumentException Invalid(string name, string why) => new($"{name} {why}.", name);
    }

    // Nulls are written on purpose: "standingReminderInterval": null is how you turn standing reminders off,
    // and omitting it would silently fall back to the default on the next load.
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

[Flags]
public enum ActiveDays
{
    None = 0,
    Sunday = 1 << 0,
    Monday = 1 << 1,
    Tuesday = 1 << 2,
    Wednesday = 1 << 3,
    Thursday = 1 << 4,
    Friday = 1 << 5,
    Saturday = 1 << 6,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
    EveryDay = Weekdays | Weekend,
}

/// <summary>A daily time range on selected days. Supports ranges that cross midnight (Start > End).</summary>
public sealed record ActiveHours(TimeOnly Start, TimeOnly End, ActiveDays Days = ActiveDays.Weekdays)
{
    public bool Contains(DateTime local)
    {
        var today = (ActiveDays)(1 << (int)local.DayOfWeek);
        if (!Days.HasFlag(today)) return false;

        var t = TimeOnly.FromDateTime(local);
        return Start <= End
            ? t >= Start && t < End
            : t >= Start || t < End;
    }

    public void Validate()
    {
        if (Start == End) throw new ArgumentException("ActiveHours.Start and End must differ.", nameof(ActiveHours));
        if (Days == ActiveDays.None) throw new ArgumentException("ActiveHours.Days must include at least one day.", nameof(ActiveHours));
    }
}
