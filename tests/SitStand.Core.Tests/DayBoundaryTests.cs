using SitStand.Core.Stats;
using static SitStand.Core.Tests.TrackerHarness;

namespace SitStand.Core.Tests;

public class DayBoundaryTests
{
    private static readonly TimeOnly Midnight = new(0, 0);

    [Fact]
    public void A_stretch_across_midnight_is_split_between_the_two_days()
    {
        var settings = new TrackerSettings { DayStartsAt = Midnight };
        var start = new DateTimeOffset(2026, 9, 28, 23, 30, 0, TimeSpan.FromHours(-4));
        var h = new TrackerHarness(settings, start: start);

        h.Advance(Hours(1)); // now 00:30 on the 29th; one open sitting segment 23:30 -> 00:30

        var day1 = h.StatsFor(new DateOnly(2026, 9, 28));
        var day2 = h.StatsFor(new DateOnly(2026, 9, 29));

        Assert.Equal(Minutes(30), day1.Sitting);
        Assert.Equal(Minutes(30), day1.LongestSittingStretch);
        Assert.Equal(Minutes(30), day2.Sitting);
        Assert.Equal(Minutes(30), day2.LongestSittingStretch);
    }

    [Fact]
    public void A_break_across_the_boundary_counts_once_on_the_day_it_started()
    {
        var settings = new TrackerSettings { DayStartsAt = Midnight };
        var start = new DateTimeOffset(2026, 9, 28, 23, 55, 0, TimeSpan.FromHours(-4));
        var h = new TrackerHarness(settings, start: start);

        h.Tracker.StartMoving();
        h.Advance(Minutes(10));
        h.Tracker.BackToSitting();

        Assert.Equal(1, h.StatsFor(new DateOnly(2026, 9, 28)).MovementBreaks);
        Assert.Equal(0, h.StatsFor(new DateOnly(2026, 9, 29)).MovementBreaks);
        Assert.Equal(Minutes(5), h.StatsFor(new DateOnly(2026, 9, 28)).Moving);
        Assert.Equal(Minutes(5), h.StatsFor(new DateOnly(2026, 9, 29)).Moving);
    }

    [Fact]
    public void Day_starting_at_4am_puts_a_1am_session_on_the_previous_day()
    {
        var oneAm = new DateTimeOffset(2026, 9, 29, 1, 0, 0, TimeSpan.FromHours(-4));

        Assert.Equal(new DateOnly(2026, 9, 28), DayWindow.DayOf(oneAm, new TimeOnly(4, 0), Zone));
        Assert.Equal(new DateOnly(2026, 9, 29), DayWindow.DayOf(oneAm, Midnight, Zone));
    }

    [Fact]
    public void Day_window_runs_from_boundary_to_boundary()
    {
        var window = DayWindow.For(new DateOnly(2026, 9, 28), new TimeOnly(4, 0), Zone);

        Assert.Equal(new DateTimeOffset(2026, 9, 28, 4, 0, 0, TimeSpan.FromHours(-4)), window.Start);
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 4, 0, 0, TimeSpan.FromHours(-4)), window.End);
        Assert.Equal(Hours(24), window.Length);
    }

    [Theory]
    [InlineData(2026, 3, 8, 23)]  // US spring forward: 2nd Sunday in March
    [InlineData(2026, 11, 1, 25)] // US fall back: 1st Sunday in November
    [InlineData(2026, 9, 28, 24)]
    public void Dst_transition_days_are_23_or_25_hours_long(int year, int month, int day, int expectedHours)
    {
        var eastern = FindEastern();
        if (eastern is null) return; // host has no tz database we recognise; nothing to assert against

        var window = DayWindow.For(new DateOnly(year, month, day), new TimeOnly(0, 0), eastern);

        Assert.Equal(Hours(expectedHours), window.Length);
    }

    [Fact]
    public void Day_boundary_inside_a_spring_forward_gap_is_nudged_forward()
    {
        var eastern = FindEastern();
        if (eastern is null) return;

        // 02:30 does not exist on 2026-03-08 in US Eastern.
        var window = DayWindow.For(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), eastern);

        Assert.False(eastern.IsInvalidTime(window.Start.DateTime));
        Assert.Equal(TimeSpan.FromHours(-4), window.Start.Offset); // already on daylight time
    }

    [Fact]
    public void Days_with_data_lists_every_touched_day_newest_first()
    {
        var settings = new TrackerSettings { DayStartsAt = Midnight };
        var start = new DateTimeOffset(2026, 9, 27, 22, 0, 0, TimeSpan.FromHours(-4));
        var h = new TrackerHarness(settings, start: start);

        h.Advance(Hours(4)); // spans into the 28th (with a gap? no — awake the whole time)

        var days = DailyStatsCalculator.DaysWithData(h.Tracker.History, settings, Zone, h.Now);

        Assert.Equal(new[] { new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 27) }, days);
    }

    private static TimeZoneInfo? FindEastern()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return null;
    }
}
