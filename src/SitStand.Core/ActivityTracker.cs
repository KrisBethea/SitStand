using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SitStand.Core.Journal;
using SitStand.Core.Model;

namespace SitStand.Core;

/// <summary>
/// The one object allowed to change the timeline. Every command is a pure function of
/// (current state, command, now) that appends journal entries and updates <see cref="History"/>.
///
/// Time comes only from the injected <see cref="TimeProvider"/>. Nothing here counts ticks:
/// elapsed time is always <c>now - StretchStartedAt</c>, and reminders are due when
/// <c>now >= NextReminderAt</c>. That is what makes sleep, suspend and missed timers harmless.
///
/// Thread-safe. Events are raised outside the lock and may arrive on the timer thread;
/// UI callers must marshal to their dispatcher.
/// </summary>
public sealed class ActivityTracker
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly IJournalWriter _journal;
    private readonly ILogger<ActivityTracker> _log;

    private TrackerSettings _settings;
    private bool _initialized;

    // Per-process state. Deliberately not persisted: a restart clears snoozes and pauses.
    private DateTimeOffset _lastTickAt;
    private DateTimeOffset _reminderAnchor;
    private DateTimeOffset? _snoozedUntil;
    private DateTimeOffset? _pausedUntil;
    private int _snoozeCount;

    public ActivityHistory History { get; }

    public TrackerSettings Settings
    {
        get { lock (_gate) return _settings; }
    }

    /// <summary>Raised after any state change. Payload is the snapshot as of the change.</summary>
    public event EventHandler<TrackerSnapshot>? StateChanged;

    /// <summary>Raised when a reminder becomes due. The UI should show the alert and call one of the response commands.</summary>
    public event EventHandler<Reminder>? ReminderDue;

    public ActivityTracker(
        TrackerSettings settings,
        ActivityHistory history,
        IJournalWriter journal,
        TimeProvider time,
        ILogger<ActivityTracker>? log = null)
    {
        settings.Validate();
        _settings = settings;
        History = history;
        _journal = journal;
        _time = time;
        _log = log ?? NullLogger<ActivityTracker>.Instance;
    }

    /// <summary>The open segment. Always present once <see cref="Initialize"/> has run.</summary>
    public Segment Current => History.OpenSegment
        ?? throw new InvalidOperationException("Tracker has no open segment. Call Initialize() first.");

    public TrackerSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                EnsureInitialized();
                return BuildSnapshot(Now);
            }
        }
    }

    /// <summary>
    /// Reconcile persisted history with reality and start tracking. If the previous process left a
    /// segment open, the heartbeat tells us when it was last alive: a short gap means we simply continue;
    /// a long gap means the user was away and we say so.
    /// </summary>
    public void Initialize(DateTimeOffset? lastHeartbeat)
    {
        lock (_gate)
        {
            if (_initialized) throw new InvalidOperationException("Tracker is already initialized.");

            var now = Now;
            var open = History.OpenSegment;

            if (open is null)
            {
                Open(Activity.Sitting, now, SegmentSource.Startup);
            }
            else
            {
                var lastKnown = lastHeartbeat ?? open.StartedAt;
                if (lastKnown < open.StartedAt) lastKnown = open.StartedAt;

                if (now - lastKnown > _settings.GapThreshold)
                {
                    RecordAway(lastKnown, now);
                }
                else if (History.PendingReminder is { } stale)
                {
                    // Quick restart with an alert that never got answered: nobody saw it.
                    Respond(stale, ReminderResponse.Ignored, lastKnown);
                }
            }

            ResetStretch(Current.StartedAt);
            _lastTickAt = now;
            _initialized = true;
            _log.LogInformation("Tracker initialized: {Activity} since {Since:t}", Current.Activity, Current.StartedAt);
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// Periodic evaluation. Detects time gaps, times out unanswered reminders, and fires due reminders.
    /// Safe to call as often as you like; nothing accumulates.
    /// </summary>
    public void Tick()
    {
        Reminder? fired = null;
        var changed = false;

        lock (_gate)
        {
            EnsureInitialized();
            var now = Now;

            if (now - _lastTickAt > _settings.GapThreshold)
            {
                _log.LogInformation("Time gap of {Gap} detected; recording Away", now - _lastTickAt);
                RecordAway(_lastTickAt, now);
                changed = true;
            }
            _lastTickAt = now;

            if (History.PendingReminder is { } pending && now - pending.FiredAt >= _settings.ReminderTimeout)
            {
                Respond(pending, ReminderResponse.Ignored, now);
                _snoozedUntil = now + _settings.SnoozeDuration; // re-nag later rather than never
                changed = true;
            }

            if (History.PendingReminder is null
                && ComputeNextReminderAt(now) is { } due
                && now >= due
                && IsWithinActiveHours(now))
            {
                fired = new Reminder(Guid.NewGuid(), now, Current.Activity, null, null);
                Apply(new ReminderFired(fired.Id, now, fired.ActivityAtFire));
                changed = true;
            }
        }

        if (changed) RaiseStateChanged();
        if (fired is not null) ReminderDue?.Invoke(this, fired);
    }

    public void StartMoving()
    {
        lock (_gate)
        {
            EnsureInitialized();
            if (Current.Activity == Activity.Moving) return;
            var now = Now;
            RespondToPending(ReminderResponse.Moving, now);
            Transition(Activity.Moving, now, SegmentSource.UserAction);
        }
        RaiseStateChanged();
    }

    public void StartStanding()
    {
        lock (_gate)
        {
            EnsureInitialized();
            if (Current.Activity == Activity.Standing) return;
            var now = Now;
            RespondToPending(ReminderResponse.Standing, now);
            Transition(Activity.Standing, now, SegmentSource.UserAction);
        }
        RaiseStateChanged();
    }

    public void BackToSitting()
    {
        lock (_gate)
        {
            EnsureInitialized();
            if (Current.Activity == Activity.Sitting) return;
            var now = Now;
            // Sitting down in answer to a standing reminder isn't moving; treat it as a skip.
            RespondToPending(ReminderResponse.Skipped, now);
            Transition(Activity.Sitting, now, SegmentSource.UserAction);
        }
        RaiseStateChanged();
    }

    /// <summary>Delay the next reminder. The current stretch keeps accumulating — snoozing is not a break.</summary>
    public void Snooze(TimeSpan? duration = null)
    {
        lock (_gate)
        {
            EnsureInitialized();
            var now = Now;
            RespondToPending(ReminderResponse.Snoozed, now);
            _snoozedUntil = now + (duration ?? _settings.SnoozeDuration);
            _snoozeCount++;
        }
        RaiseStateChanged();
    }

    /// <summary>Dismiss this cycle: restart the reminder clock from now, but keep the current stretch running.</summary>
    public void Skip()
    {
        lock (_gate)
        {
            EnsureInitialized();
            var now = Now;
            RespondToPending(ReminderResponse.Skipped, now);
            _reminderAnchor = now;
            _snoozedUntil = null;
        }
        RaiseStateChanged();
    }

    /// <summary>Suppress reminders for a while (meeting, call, focus block). Tracking continues; a pause outlives stretch changes.</summary>
    public void Pause(TimeSpan? duration = null)
    {
        lock (_gate)
        {
            EnsureInitialized();
            var now = Now;
            RespondToPending(ReminderResponse.Skipped, now);
            _pausedUntil = now + (duration ?? _settings.PauseDuration);
        }
        RaiseStateChanged();
    }

    public void Resume()
    {
        lock (_gate)
        {
            EnsureInitialized();
            _pausedUntil = null;
        }
        RaiseStateChanged();
    }

    /// <summary>
    /// Record that the user was not at the computer between two instants. Used internally for time gaps
    /// and available to future idle detection. Closes the open segment at <paramref name="from"/>,
    /// records Away, and starts a fresh Sitting stretch at <paramref name="to"/>.
    /// </summary>
    public void MarkAway(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from) throw new ArgumentException("'to' must not be before 'from'.", nameof(to));
        lock (_gate)
        {
            EnsureInitialized();
            RecordAway(from, to);
        }
        RaiseStateChanged();
    }

    public void UpdateSettings(TrackerSettings settings)
    {
        settings.Validate();
        lock (_gate) _settings = settings;
        RaiseStateChanged();
    }

    // ---- internals (all called under _gate) ----

    private DateTimeOffset Now => _time.GetLocalNow();

    private void EnsureInitialized()
    {
        if (!_initialized) throw new InvalidOperationException("Tracker is not initialized. Call Initialize() first.");
    }

    private void Transition(Activity next, DateTimeOffset at, SegmentSource source)
    {
        Close(Current, at, source);
        Open(next, at, source);
        ResetStretch(at);
    }

    private void RecordAway(DateTimeOffset from, DateTimeOffset to)
    {
        var current = Current;
        if (from < current.StartedAt) from = current.StartedAt;
        if (to < from) to = from;

        if (History.PendingReminder is { } pending)
            Respond(pending, ReminderResponse.Ignored, from);

        Close(current, from, SegmentSource.AutoClosed);
        Open(Activity.Away, from, SegmentSource.GapInferred);
        Close(Current, to, SegmentSource.GapInferred);
        Open(Activity.Sitting, to, SegmentSource.GapInferred);
        ResetStretch(to);
    }

    private void ResetStretch(DateTimeOffset at)
    {
        _reminderAnchor = at;
        _snoozedUntil = null;
        _snoozeCount = 0;
    }

    private void Open(Activity activity, DateTimeOffset at, SegmentSource source) =>
        Apply(new SegmentOpened(Guid.NewGuid(), activity, at, source));

    private void Close(Segment segment, DateTimeOffset at, SegmentSource source) =>
        Apply(new SegmentClosed(segment.Id, at, source));

    private void RespondToPending(ReminderResponse response, DateTimeOffset at)
    {
        if (History.PendingReminder is { } pending) Respond(pending, response, at);
    }

    private void Respond(Reminder reminder, ReminderResponse response, DateTimeOffset at) =>
        Apply(new ReminderResponded(reminder.Id, at, response));

    private void Apply(JournalEntry entry)
    {
        History.Apply(entry);
        try
        {
            _journal.Append(entry);
        }
        catch (Exception ex)
        {
            // In-memory state is still correct for this session; losing one line of history is better than losing the tracker.
            _log.LogError(ex, "Failed to persist journal entry {Entry}", entry);
        }
    }

    private TimeSpan? IntervalFor(Activity activity) => activity switch
    {
        Activity.Sitting => _settings.SittingReminderInterval,
        Activity.Standing => _settings.StandingReminderInterval,
        _ => null,
    };

    private DateTimeOffset? ComputeNextReminderAt(DateTimeOffset now)
    {
        if (IntervalFor(Current.Activity) is not { } interval) return null;

        var due = _reminderAnchor + interval;
        if (_snoozedUntil is { } snoozed && snoozed > due) due = snoozed;
        if (_pausedUntil is { } paused && paused > now && paused > due) due = paused;
        return due;
    }

    private bool IsWithinActiveHours(DateTimeOffset now)
    {
        if (_settings.ActiveHours is not { } hours) return true;
        var local = TimeZoneInfo.ConvertTime(now, _time.LocalTimeZone).DateTime;
        return hours.Contains(local);
    }

    private TrackerSnapshot BuildSnapshot(DateTimeOffset now)
    {
        var current = Current;
        return new TrackerSnapshot(
            Now: now,
            Activity: current.Activity,
            StretchStartedAt: current.StartedAt,
            Elapsed: current.Duration(now),
            ReminderInterval: IntervalFor(current.Activity),
            NextReminderAt: ComputeNextReminderAt(now),
            SnoozedUntil: _snoozedUntil is { } s && s > now ? s : null,
            PausedUntil: _pausedUntil is { } p && p > now ? p : null,
            SnoozeCount: _snoozeCount,
            PendingReminder: History.PendingReminder,
            IsWithinActiveHours: IsWithinActiveHours(now));
    }

    private void RaiseStateChanged()
    {
        var handlers = StateChanged;
        if (handlers is null) return;
        TrackerSnapshot snapshot;
        lock (_gate) snapshot = BuildSnapshot(Now);
        handlers.Invoke(this, snapshot);
    }
}
