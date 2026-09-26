using SitStand.Core.Journal;
using SitStand.Core.Model;

namespace SitStand.Core;

/// <summary>
/// In-memory fold of the journal: the full list of segments and reminders, plus the one open
/// segment and the one pending reminder (if any). The same <see cref="Apply"/> path is used both
/// to replay the journal at startup and to apply live changes, so there is exactly one place where
/// "what does this entry mean" is defined.
/// </summary>
public sealed class ActivityHistory
{
    private readonly object _gate = new();
    private readonly List<Segment> _segments = new();
    private readonly Dictionary<Guid, int> _segmentIndex = new();
    private readonly List<Reminder> _reminders = new();
    private readonly Dictionary<Guid, int> _reminderIndex = new();

    public Segment? OpenSegment { get; private set; }
    public Reminder? PendingReminder { get; private set; }

    /// <summary>Snapshot copy, safe to enumerate while the tracker keeps mutating on another thread.</summary>
    public IReadOnlyList<Segment> Segments
    {
        get { lock (_gate) return _segments.ToArray(); }
    }

    public IReadOnlyList<Reminder> Reminders
    {
        get { lock (_gate) return _reminders.ToArray(); }
    }

    public static ActivityHistory FromEntries(IEnumerable<JournalEntry> entries)
    {
        var history = new ActivityHistory();
        foreach (var entry in entries) history.Apply(entry);
        return history;
    }

    public void Apply(JournalEntry entry)
    {
        lock (_gate)
        {
            switch (entry)
            {
                case SegmentOpened e: ApplyOpened(e); break;
                case SegmentClosed e: ApplyClosed(e); break;
                case ReminderFired e: ApplyFired(e); break;
                case ReminderResponded e: ApplyResponded(e); break;
                default: throw new ArgumentOutOfRangeException(nameof(entry), entry.GetType().Name);
            }
        }
    }

    private void ApplyOpened(SegmentOpened e)
    {
        // A journal that opens a new segment while one is still open is damaged (missed write, crash
        // between entries). Self-heal by closing the old one where the new one begins.
        if (OpenSegment is { } dangling)
            ReplaceSegment(dangling with { EndedAt = e.At, Source = SegmentSource.AutoClosed });

        var segment = new Segment(e.SegmentId, e.Activity, e.At, null, e.Source);
        _segmentIndex[segment.Id] = _segments.Count;
        _segments.Add(segment);
        OpenSegment = segment;
    }

    private void ApplyClosed(SegmentClosed e)
    {
        if (!_segmentIndex.TryGetValue(e.SegmentId, out var i)) return; // unknown id: nothing to close
        var existing = _segments[i];
        if (!existing.IsOpen) return; // already closed; a duplicate close is harmless

        var endedAt = e.At < existing.StartedAt ? existing.StartedAt : e.At;
        ReplaceSegment(existing with { EndedAt = endedAt });
    }

    private void ApplyFired(ReminderFired e)
    {
        if (PendingReminder is { } stale)
            ReplaceReminder(stale with { Response = ReminderResponse.Ignored, RespondedAt = e.At });

        var reminder = new Reminder(e.ReminderId, e.At, e.Activity, null, null);
        _reminderIndex[reminder.Id] = _reminders.Count;
        _reminders.Add(reminder);
        PendingReminder = reminder;
    }

    private void ApplyResponded(ReminderResponded e)
    {
        if (!_reminderIndex.TryGetValue(e.ReminderId, out var i)) return;
        var existing = _reminders[i];
        if (!existing.IsPending) return;
        ReplaceReminder(existing with { Response = e.Response, RespondedAt = e.At });
    }

    private void ReplaceSegment(Segment updated)
    {
        _segments[_segmentIndex[updated.Id]] = updated;
        if (OpenSegment?.Id == updated.Id) OpenSegment = updated.IsOpen ? updated : null;
    }

    private void ReplaceReminder(Reminder updated)
    {
        _reminders[_reminderIndex[updated.Id]] = updated;
        if (PendingReminder?.Id == updated.Id) PendingReminder = updated.IsPending ? updated : null;
    }
}
