namespace SitStand.Core.Stats;

/// <summary>
/// The half-open interval [Start, End) that a calendar "day" covers, honouring the configured day
/// boundary and the local time zone. Built from calendar dates, never by adding 24 hours, so DST
/// days are 23 or 25 hours long as they should be.
/// </summary>
public readonly record struct DayWindow(DateOnly Day, DateTimeOffset Start, DateTimeOffset End)
{
    public TimeSpan Length => End - Start;

    public bool Contains(DateTimeOffset instant) => instant >= Start && instant < End;

    public static DayWindow For(DateOnly day, TimeOnly dayStart, TimeZoneInfo zone) => new(
        day,
        ToOffset(day.ToDateTime(dayStart), zone),
        ToOffset(day.AddDays(1).ToDateTime(dayStart), zone));

    /// <summary>Which day an instant belongs to, given the day boundary.</summary>
    public static DateOnly DayOf(DateTimeOffset instant, TimeOnly dayStart, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone).DateTime;
        var date = DateOnly.FromDateTime(local);
        return TimeOnly.FromDateTime(local) < dayStart ? date.AddDays(-1) : date;
    }

    private static DateTimeOffset ToOffset(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // If the day boundary falls in a spring-forward gap (e.g. 02:30 when clocks jump 02:00 -> 03:00), nudge past it.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(30);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
