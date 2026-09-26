using SitStand.Core.Model;

namespace SitStand.Core.Stats;

/// <summary>
/// Pure projections over the timeline. Segments are clipped to the day window, so a stretch that
/// crosses the day boundary contributes the right amount to each day.
/// </summary>
public static class DailyStatsCalculator
{
    public static DailyStats ForDay(
        ActivityHistory history,
        DateOnly day,
        TrackerSettings settings,
        TimeZoneInfo zone,
        DateTimeOffset now) =>
        Calculate(history.Segments, history.Reminders, DayWindow.For(day, settings.DayStartsAt, zone), now);

    public static DailyStats Calculate(
        IReadOnlyList<Segment> segments,
        IReadOnlyList<Reminder> reminders,
        DayWindow window,
        DateTimeOffset now)
    {
        var sitting = TimeSpan.Zero;
        var standing = TimeSpan.Zero;
        var moving = TimeSpan.Zero;
        var away = TimeSpan.Zero;
        var breaks = 0;
        var sittingStretches = new List<TimeSpan>();

        foreach (var segment in segments)
        {
            var start = Max(segment.StartedAt, window.Start);
            var end = Min(segment.EndOrNow(now), window.End);
            if (end <= start) continue;
            var duration = end - start;

            switch (segment.Activity)
            {
                case Activity.Sitting:
                    sitting += duration;
                    sittingStretches.Add(duration);
                    break;
                case Activity.Standing:
                    standing += duration;
                    break;
                case Activity.Moving:
                    moving += duration;
                    // A break belongs to the day it started on, so one that crosses the boundary isn't counted twice.
                    if (window.Contains(segment.StartedAt)) breaks++;
                    break;
                case Activity.Away:
                    away += duration;
                    break;
            }
        }

        var fired = 0; var snoozed = 0; var skipped = 0; var ignored = 0;
        foreach (var reminder in reminders)
        {
            if (!window.Contains(reminder.FiredAt)) continue;
            fired++;
            switch (reminder.Response)
            {
                case ReminderResponse.Snoozed: snoozed++; break;
                case ReminderResponse.Skipped: skipped++; break;
                case ReminderResponse.Ignored: ignored++; break;
            }
        }

        return new DailyStats(
            Day: window.Day,
            MovementBreaks: breaks,
            Sitting: sitting,
            Standing: standing,
            Moving: moving,
            Away: away,
            AverageSittingStretch: sittingStretches.Count == 0
                ? TimeSpan.Zero
                : TimeSpan.FromTicks(sittingStretches.Sum(s => s.Ticks) / sittingStretches.Count),
            LongestSittingStretch: sittingStretches.Count == 0 ? TimeSpan.Zero : sittingStretches.Max(),
            RemindersFired: fired,
            Snoozed: snoozed,
            Skipped: skipped,
            Ignored: ignored);
    }

    /// <summary>Every day that has at least one segment touching it, newest first.</summary>
    public static IReadOnlyList<DateOnly> DaysWithData(
        ActivityHistory history,
        TrackerSettings settings,
        TimeZoneInfo zone,
        DateTimeOffset now)
    {
        var days = new HashSet<DateOnly>();
        foreach (var segment in history.Segments)
        {
            var first = DayWindow.DayOf(segment.StartedAt, settings.DayStartsAt, zone);
            var last = DayWindow.DayOf(segment.EndOrNow(now), settings.DayStartsAt, zone);
            for (var d = first; d <= last; d = d.AddDays(1)) days.Add(d);
        }
        return days.OrderByDescending(d => d).ToArray();
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
