using Microsoft.Extensions.Time.Testing;
using SitStand.Core;
using SitStand.Core.Journal;
using SitStand.Core.Model;
using SitStand.Core.Stats;

namespace SitStand.Core.Tests;

/// <summary>
/// Drives an <see cref="ActivityTracker"/> against a <see cref="FakeTimeProvider"/>.
/// <see cref="Advance"/> simulates the machine being awake (ticks keep arriving);
/// <see cref="Sleep"/> simulates suspend (one big jump between ticks).
/// </summary>
internal sealed class TrackerHarness
{
    // A Monday morning, fixed-offset zone so tests don't depend on the host machine.
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test-4", TimeSpan.FromHours(-4), "Test -4", "Test -4");
    public static readonly DateTimeOffset DefaultStart = new(2026, 9, 28, 9, 0, 0, TimeSpan.FromHours(-4));
    public static readonly TimeSpan TickPeriod = TimeSpan.FromSeconds(10);

    public FakeTimeProvider Time { get; }
    public InMemoryJournal Journal { get; }
    public TrackerSettings Settings { get; }
    public ActivityTracker Tracker { get; }
    public List<Reminder> Fired { get; } = new();
    public int StateChanges { get; private set; }

    public DateTimeOffset Start { get; }
    public DateTimeOffset Now => Time.GetLocalNow();

    public TrackerHarness(
        TrackerSettings? settings = null,
        IEnumerable<JournalEntry>? existing = null,
        DateTimeOffset? heartbeat = null,
        DateTimeOffset? start = null,
        TimeZoneInfo? zone = null,
        bool initialize = true)
    {
        Start = start ?? DefaultStart;
        // FakeTimeProvider's GetLocalNow() assumes GetUtcNow() returns a zero-offset value, so seed it in UTC.
        Time = new FakeTimeProvider(Start.ToUniversalTime());
        Time.SetLocalTimeZone(zone ?? Zone);
        Journal = new InMemoryJournal();
        Settings = settings ?? new TrackerSettings { ActiveHours = null };

        var history = existing is null ? new ActivityHistory() : ActivityHistory.FromEntries(existing);
        Tracker = new ActivityTracker(Settings, history, Journal, Time);
        Tracker.ReminderDue += (_, r) => Fired.Add(r);
        Tracker.StateChanged += (_, _) => StateChanges++;

        if (initialize) Tracker.Initialize(heartbeat);
    }

    /// <summary>Move time forward with the machine awake: tick every <see cref="TickPeriod"/>.</summary>
    public void Advance(TimeSpan by)
    {
        var target = Now + by;
        while (Now + TickPeriod <= target)
        {
            Time.Advance(TickPeriod);
            Tracker.Tick();
        }
        var remainder = target - Now;
        if (remainder > TimeSpan.Zero)
        {
            Time.Advance(remainder);
            Tracker.Tick();
        }
    }

    /// <summary>Move time forward as if the machine were suspended: no ticks until the end.</summary>
    public void Sleep(TimeSpan by)
    {
        Time.Advance(by);
        Tracker.Tick();
    }

    public IReadOnlyList<Segment> Segments => Tracker.History.Segments;
    public IReadOnlyList<Reminder> Reminders => Tracker.History.Reminders;

    public DailyStats StatsFor(DateOnly day) => DailyStatsCalculator.ForDay(Tracker.History, day, Settings, Time.LocalTimeZone, Now);
    public DailyStats Today => StatsFor(DayWindow.DayOf(Now, Settings.DayStartsAt, Time.LocalTimeZone));

    public static TimeSpan Minutes(double m) => TimeSpan.FromMinutes(m);
    public static TimeSpan Hours(double h) => TimeSpan.FromHours(h);
}
