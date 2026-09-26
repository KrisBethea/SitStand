namespace SitStand.Core.Stats;

public sealed record DailyStats(
    DateOnly Day,
    int MovementBreaks,
    TimeSpan Sitting,
    TimeSpan Standing,
    TimeSpan Moving,
    TimeSpan Away,
    TimeSpan AverageSittingStretch,
    TimeSpan LongestSittingStretch,
    int RemindersFired,
    int Snoozed,
    int Skipped,
    int Ignored)
{
    /// <summary>Time the app believes you were at the computer.</summary>
    public TimeSpan Tracked => Sitting + Standing + Moving;

    public bool IsEmpty => Tracked == TimeSpan.Zero && Away == TimeSpan.Zero && RemindersFired == 0;

    public static DailyStats Empty(DateOnly day) => new(
        day, 0, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0, 0, 0, 0);
}
