namespace SitStand.Core.Tests;

public class ActiveHoursTests
{
    private static readonly ActiveHours OfficeHours = new(new TimeOnly(9, 0), new TimeOnly(17, 0), ActiveDays.Weekdays);

    [Theory]
    [InlineData(2026, 9, 28, 9, 0, true)]   // Monday 09:00 — inclusive start
    [InlineData(2026, 9, 28, 16, 59, true)]
    [InlineData(2026, 9, 28, 17, 0, false)] // exclusive end
    [InlineData(2026, 9, 28, 8, 59, false)]
    [InlineData(2026, 9, 26, 12, 0, false)] // Saturday
    [InlineData(2026, 9, 27, 12, 0, false)] // Sunday
    public void Office_hours_on_weekdays(int y, int m, int d, int hh, int mm, bool expected)
    {
        Assert.Equal(expected, OfficeHours.Contains(new DateTime(y, m, d, hh, mm, 0)));
    }

    [Theory]
    [InlineData(23, 0, true)]
    [InlineData(1, 30, true)]
    [InlineData(2, 0, false)]
    [InlineData(12, 0, false)]
    public void Overnight_range_wraps_midnight(int hh, int mm, bool expected)
    {
        var nightShift = new ActiveHours(new TimeOnly(22, 0), new TimeOnly(2, 0), ActiveDays.EveryDay);
        Assert.Equal(expected, nightShift.Contains(new DateTime(2026, 9, 28, hh, mm, 0)));
    }

    [Fact]
    public void Day_flags_map_to_day_of_week()
    {
        var weekendOnly = new ActiveHours(new TimeOnly(0, 0), new TimeOnly(23, 59), ActiveDays.Weekend);
        Assert.True(weekendOnly.Contains(new DateTime(2026, 9, 26, 12, 0, 0)));  // Sat
        Assert.True(weekendOnly.Contains(new DateTime(2026, 9, 27, 12, 0, 0)));  // Sun
        Assert.False(weekendOnly.Contains(new DateTime(2026, 9, 28, 12, 0, 0))); // Mon
    }

    [Fact]
    public void Validation_rejects_empty_ranges_and_no_days()
    {
        Assert.Throws<ArgumentException>(() => new ActiveHours(new TimeOnly(9, 0), new TimeOnly(9, 0)).Validate());
        Assert.Throws<ArgumentException>(() => new ActiveHours(new TimeOnly(9, 0), new TimeOnly(17, 0), ActiveDays.None).Validate());
    }
}
