namespace SitStand.Core.Model;

/// <summary>What the user is doing during a stretch of time. Exactly one is true at any instant.</summary>
public enum Activity
{
    Sitting,
    Standing,
    Moving,
    /// <summary>Not at the computer. Inferred from a time gap (sleep, suspend, crash), never chosen by the user.</summary>
    Away,
}

/// <summary>Why a segment boundary exists. Lets statistics and future history edits tell user intent from system inference.</summary>
public enum SegmentSource
{
    /// <summary>The user clicked a button.</summary>
    UserAction,
    /// <summary>The app started and there was no open segment to continue.</summary>
    Startup,
    /// <summary>Derived from a detected time gap (machine slept, app was suspended or died).</summary>
    GapInferred,
    /// <summary>Closed by the system rather than the user (e.g. the segment that was open when a gap began).</summary>
    AutoClosed,
    /// <summary>Reserved for later: manual or AI-assisted history corrections.</summary>
    Backfilled,
}

public enum ReminderResponse
{
    Moving,
    Standing,
    Snoozed,
    Skipped,
    /// <summary>No response before the timeout, or the machine went away while the reminder was up.</summary>
    Ignored,
}
