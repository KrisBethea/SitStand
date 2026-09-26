namespace SitStand.Core.Model;

/// <summary>A reminder that fired, and how (or whether) the user responded to it.</summary>
public sealed record Reminder(
    Guid Id,
    DateTimeOffset FiredAt,
    Activity ActivityAtFire,
    ReminderResponse? Response,
    DateTimeOffset? RespondedAt)
{
    public bool IsPending => Response is null;
}
