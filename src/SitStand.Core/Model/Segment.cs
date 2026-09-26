namespace SitStand.Core.Model;

/// <summary>
/// A contiguous stretch of one activity. The timeline is a sequence of non-overlapping segments;
/// at most one is open (<see cref="EndedAt"/> is null) at any time.
/// Every statistic in the app is a projection over segments — nothing is counted incrementally.
/// </summary>
public sealed record Segment(
    Guid Id,
    Activity Activity,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    SegmentSource Source)
{
    public bool IsOpen => EndedAt is null;

    public DateTimeOffset EndOrNow(DateTimeOffset now) => EndedAt ?? now;

    /// <summary>Elapsed time, clamped at zero so a clock that jumps backwards can't produce a negative duration.</summary>
    public TimeSpan Duration(DateTimeOffset now)
    {
        var d = EndOrNow(now) - StartedAt;
        return d < TimeSpan.Zero ? TimeSpan.Zero : d;
    }
}
