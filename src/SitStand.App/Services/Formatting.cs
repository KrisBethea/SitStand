using SitStand.Core.Model;

namespace SitStand.App.Services;

public static class Formatting
{
    /// <summary>37:14, or 1:02:09 once past an hour. For the live counter.</summary>
    public static string Clock(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes:D2}:{t.Seconds:D2}";
    }

    /// <summary>"1h 12m", "37m", "45s". For statistics, where seconds are noise.</summary>
    public static string Coarse(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m";
        return $"{t.Seconds}s";
    }

    public static string Label(Activity activity) => activity switch
    {
        Activity.Sitting => "Sitting",
        Activity.Standing => "Standing",
        Activity.Moving => "Moving",
        Activity.Away => "Away",
        _ => activity.ToString(),
    };

    public static string Plural(int count, string singular, string? plural = null) =>
        count == 1 ? $"{count} {singular}" : $"{count} {plural ?? singular + "s"}";
}
