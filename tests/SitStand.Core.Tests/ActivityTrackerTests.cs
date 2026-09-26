using SitStand.Core.Journal;
using SitStand.Core.Model;
using static SitStand.Core.Tests.TrackerHarness;

namespace SitStand.Core.Tests;

public class ActivityTrackerTests
{
    [Fact]
    public void Fresh_start_opens_a_sitting_segment()
    {
        var h = new TrackerHarness();

        var segment = Assert.Single(h.Segments);
        Assert.Equal(Activity.Sitting, segment.Activity);
        Assert.Equal(SegmentSource.Startup, segment.Source);
        Assert.True(segment.IsOpen);
        Assert.Equal(h.Start, segment.StartedAt);
        Assert.Equal(Activity.Sitting, h.Tracker.Snapshot.Activity);
    }

    [Fact]
    public void Elapsed_is_derived_from_timestamps_not_ticks()
    {
        var h = new TrackerHarness();

        // No ticks at all — a display timer that never ran must not matter.
        h.Time.Advance(new TimeSpan(0, 37, 14));

        Assert.Equal(new TimeSpan(0, 37, 14), h.Tracker.Snapshot.Elapsed);
    }

    [Fact]
    public void Reminder_fires_at_the_interval_and_not_before()
    {
        var h = new TrackerHarness();

        h.Advance(Minutes(49));
        Assert.Empty(h.Fired);

        h.Advance(Minutes(1));
        var reminder = Assert.Single(h.Fired);
        Assert.Equal(Activity.Sitting, reminder.ActivityAtFire);
        Assert.Equal(h.Start + Minutes(50), reminder.FiredAt);
        Assert.True(h.Tracker.Snapshot.HasPendingReminder);
    }

    [Fact]
    public void Moving_before_the_alert_resets_the_stretch()
    {
        var h = new TrackerHarness();

        h.Advance(Minutes(30));
        h.Tracker.StartMoving();
        h.Advance(Minutes(5));
        h.Tracker.BackToSitting();

        h.Advance(Minutes(49));
        Assert.Empty(h.Fired);

        h.Advance(Minutes(1));
        Assert.Single(h.Fired);
        Assert.Equal(Minutes(50), h.Tracker.Snapshot.Elapsed);
    }

    [Fact]
    public void A_movement_break_is_recorded_and_counted()
    {
        var h = new TrackerHarness();

        h.Advance(Minutes(30));
        h.Tracker.StartMoving();
        h.Advance(Minutes(5));
        h.Tracker.BackToSitting();
        h.Advance(Minutes(10));

        Assert.Collection(h.Segments,
            s => { Assert.Equal(Activity.Sitting, s.Activity); Assert.Equal(Minutes(30), s.Duration(h.Now)); },
            s => { Assert.Equal(Activity.Moving, s.Activity); Assert.Equal(Minutes(5), s.Duration(h.Now)); Assert.Equal(SegmentSource.UserAction, s.Source); },
            s => { Assert.Equal(Activity.Sitting, s.Activity); Assert.True(s.IsOpen); });

        var today = h.Today;
        Assert.Equal(1, today.MovementBreaks);
        Assert.Equal(Minutes(5), today.Moving);
        Assert.Equal(Minutes(40), today.Sitting);
        Assert.Equal(Minutes(30), today.LongestSittingStretch);
        Assert.Equal(Minutes(20), today.AverageSittingStretch);
    }

    [Fact]
    public void Responding_to_an_alert_with_moving_records_the_response()
    {
        var h = new TrackerHarness();

        h.Advance(Minutes(50));
        Assert.Single(h.Fired);
        h.Tracker.StartMoving();

        var reminder = Assert.Single(h.Reminders);
        Assert.Equal(ReminderResponse.Moving, reminder.Response);
        Assert.Equal(h.Now, reminder.RespondedAt);
        Assert.False(h.Tracker.Snapshot.HasPendingReminder);
        Assert.Equal(Activity.Moving, h.Tracker.Snapshot.Activity);
        Assert.Null(h.Tracker.Snapshot.NextReminderAt);
    }

    [Fact]
    public void Snooze_delays_the_alert_but_the_sitting_stretch_keeps_accumulating()
    {
        var h = new TrackerHarness();

        h.Advance(Minutes(50));
        h.Tracker.Snooze();
        Assert.Equal(1, h.Tracker.Snapshot.SnoozeCount);

        h.Advance(Minutes(9));
        Assert.Single(h.Fired);

        h.Advance(Minutes(1));
        Assert.Equal(2, h.Fired.Count);
        Assert.Equal(Minutes(60), h.Tracker.Snapshot.Elapsed);
        Assert.Equal(ReminderResponse.Snoozed, h.Reminders[0].Response);
        Assert.Single(h.Segments); // still one sitting segment — snoozing is not a break
    }

    [Fact]
    public void Skip_restarts_the_reminder_clock_but_not_the_stretch()
    {
        var h = new TrackerHarness();

        h.Advance(Minutes(50));
        h.Tracker.Skip();

        h.Advance(Minutes(49));
        Assert.Single(h.Fired);

        h.Advance(Minutes(1));
        Assert.Equal(2, h.Fired.Count);
        Assert.Equal(Minutes(100), h.Tracker.Snapshot.Elapsed);
        Assert.Equal(ReminderResponse.Skipped, h.Reminders[0].Response);
    }

    [Fact]
    public void Unanswered_alert_times_out_as_ignored_and_renags_after_snooze_duration()
    {
        var h = new TrackerHarness();

        h.Advance(Minutes(50));
        Assert.Single(h.Fired);

        h.Advance(Minutes(3)); // ReminderTimeout
        Assert.Equal(ReminderResponse.Ignored, h.Reminders[0].Response);
        Assert.False(h.Tracker.Snapshot.HasPendingReminder);

        h.Advance(Minutes(9));
        Assert.Single(h.Fired);

        h.Advance(Minutes(1)); // 53 + 10
        Assert.Equal(2, h.Fired.Count);
    }

    [Fact]
    public void Long_gap_closes_sitting_at_last_tick_records_away_and_starts_fresh()
    {
        var h = new TrackerHarness();

        h.Advance(Minutes(40));
        var lastTick = h.Now;
        h.Sleep(Hours(3)); // lid closed

        Assert.Collection(h.Segments,
            s =>
            {
                Assert.Equal(Activity.Sitting, s.Activity);
                Assert.Equal(lastTick, s.EndedAt);
                Assert.Equal(Minutes(40), s.Duration(h.Now));
            },
            s =>
            {
                Assert.Equal(Activity.Away, s.Activity);
                Assert.Equal(SegmentSource.GapInferred, s.Source);
                Assert.Equal(Hours(3), s.Duration(h.Now));
            },
            s =>
            {
                Assert.Equal(Activity.Sitting, s.Activity);
                Assert.True(s.IsOpen);
                Assert.Equal(h.Now, s.StartedAt);
            });

        Assert.Equal(TimeSpan.Zero, h.Tracker.Snapshot.Elapsed);
        Assert.Equal(Minutes(40), h.Today.LongestSittingStretch); // not 3h40m
        Assert.Equal(Hours(3), h.Today.Away);
        Assert.Empty(h.Fired); // the reminder that would have been due mid-nap never fires
    }

    [Fact]
    public void Short_gap_under_threshold_is_not_a_break()
    {
        var h = new TrackerHarness();

        h.Advance(Minutes(10));
        h.Sleep(Minutes(2));

        Assert.Single(h.Segments);
        Assert.Equal(Minutes(12), h.Tracker.Snapshot.Elapsed);
    }

    [Fact]
    public void Gap_while_an_alert_is_up_marks_it_ignored_at_the_gap_start()
    {
        var h = new TrackerHarness();

        h.Advance(Minutes(50));
        var gapStart = h.Now;
        h.Sleep(Hours(2));

        var reminder = Assert.Single(h.Reminders);
        Assert.Equal(ReminderResponse.Ignored, reminder.Response);
        Assert.Equal(gapStart, reminder.RespondedAt);
    }

    [Fact]
    public void Pause_suppresses_reminders_then_fires_when_it_ends_if_overdue()
    {
        var h = new TrackerHarness();

        h.Tracker.Pause(Hours(2));
        Assert.True(h.Tracker.Snapshot.IsPaused);

        h.Advance(Hours(2) - Minutes(1));
        Assert.Empty(h.Fired);

        h.Advance(Minutes(1));
        Assert.Single(h.Fired);
        Assert.False(h.Tracker.Snapshot.IsPaused);
    }

    [Fact]
    public void Resume_clears_a_pause()
    {
        var h = new TrackerHarness();

        h.Advance(Minutes(55));
        h.Tracker.Skip();
        h.Tracker.Pause(Hours(2));
        h.Advance(Minutes(50));
        Assert.Single(h.Fired);

        h.Tracker.Resume();
        h.Advance(Minutes(1));
        Assert.Equal(2, h.Fired.Count);
    }

    [Fact]
    public void Standing_uses_its_own_longer_interval()
    {
        var h = new TrackerHarness();

        h.Tracker.StartStanding();
        h.Advance(Minutes(50));
        Assert.Empty(h.Fired);

        h.Advance(Minutes(40));
        var reminder = Assert.Single(h.Fired);
        Assert.Equal(Activity.Standing, reminder.ActivityAtFire);
    }

    [Fact]
    public void Standing_reminders_can_be_disabled()
    {
        var h = new TrackerHarness(new TrackerSettings { StandingReminderInterval = null });

        h.Tracker.StartStanding();
        h.Advance(Hours(5));

        Assert.Empty(h.Fired);
        Assert.Null(h.Tracker.Snapshot.NextReminderAt);
    }

    [Fact]
    public void Back_to_sitting_from_a_standing_alert_is_a_skip()
    {
        var h = new TrackerHarness();

        h.Tracker.StartStanding();
        h.Advance(Minutes(90));
        Assert.Single(h.Fired);
        h.Tracker.BackToSitting();

        Assert.Equal(ReminderResponse.Skipped, h.Reminders[0].Response);
        Assert.Equal(Activity.Sitting, h.Tracker.Snapshot.Activity);
    }

    [Fact]
    public void No_reminders_outside_active_hours_but_one_fires_on_entry_if_overdue()
    {
        var settings = new TrackerSettings
        {
            ActiveHours = new ActiveHours(new TimeOnly(9, 0), new TimeOnly(17, 0), ActiveDays.Weekdays),
        };
        var start = new DateTimeOffset(2026, 9, 28, 7, 0, 0, TimeSpan.FromHours(-4)); // Monday 07:00
        var h = new TrackerHarness(settings, start: start);

        h.Advance(Hours(2) - Minutes(1)); // 08:59, overdue since 07:50
        Assert.Empty(h.Fired);
        Assert.False(h.Tracker.Snapshot.IsWithinActiveHours);

        h.Advance(Minutes(1)); // 09:00
        Assert.Single(h.Fired);
        Assert.True(h.Tracker.Snapshot.IsWithinActiveHours);
    }

    [Fact]
    public void No_reminders_on_inactive_days()
    {
        var settings = new TrackerSettings
        {
            ActiveHours = new ActiveHours(new TimeOnly(9, 0), new TimeOnly(17, 0), ActiveDays.Weekdays),
        };
        var saturday = new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.FromHours(-4));
        var h = new TrackerHarness(settings, start: saturday);

        h.Advance(Hours(3));

        Assert.Empty(h.Fired);
    }

    [Fact]
    public void Redundant_commands_are_silent_no_ops()
    {
        var h = new TrackerHarness();
        var before = h.StateChanges;

        h.Tracker.BackToSitting(); // already sitting
        h.Tracker.StartMoving();
        h.Tracker.StartMoving();   // already moving

        Assert.Equal(2, h.Segments.Count);
        Assert.Equal(before + 1, h.StateChanges); // only the real transition raised an event
    }

    [Fact]
    public void Snapshot_progress_reflects_fraction_of_interval()
    {
        var h = new TrackerHarness();

        h.Advance(Minutes(25));
        Assert.Equal(0.5, h.Tracker.Snapshot.ReminderProgress!.Value, precision: 6);
        Assert.Equal(Minutes(25), h.Tracker.Snapshot.TimeUntilReminder);

        h.Advance(Minutes(25)); // due now, alert pending
        Assert.Equal(1.0, h.Tracker.Snapshot.ReminderProgress!.Value, precision: 6);
        Assert.Equal(TimeSpan.Zero, h.Tracker.Snapshot.TimeUntilReminder);
        Assert.True(h.Tracker.Snapshot.HasPendingReminder);

        h.Advance(Minutes(2)); // still pending, now overdue
        Assert.Equal(1.04, h.Tracker.Snapshot.ReminderProgress!.Value, precision: 6);

        h.Tracker.StartMoving();
        Assert.Null(h.Tracker.Snapshot.ReminderProgress);
        Assert.Null(h.Tracker.Snapshot.TimeUntilReminder);
    }

    [Fact]
    public void Clock_moving_backwards_does_not_produce_negative_elapsed()
    {
        // FakeTimeProvider refuses to go backwards, so use a clock that will.
        var clock = new ManualClock(DefaultStart);
        var tracker = new ActivityTracker(new TrackerSettings(), new ActivityHistory(), new InMemoryJournal(), clock);
        tracker.Initialize(null);

        clock.UtcNow += Minutes(5);
        tracker.Tick();
        clock.UtcNow -= Minutes(15); // NTP correction backwards
        tracker.Tick();

        Assert.Equal(TimeSpan.Zero, tracker.Snapshot.Elapsed);
        Assert.Single(tracker.History.Segments);
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = start.ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override TimeZoneInfo LocalTimeZone => Zone;
    }

    [Fact]
    public void Commands_before_initialize_throw()
    {
        var h = new TrackerHarness(initialize: false);

        Assert.Throws<InvalidOperationException>(() => h.Tracker.StartMoving());
        Assert.Throws<InvalidOperationException>(() => h.Tracker.Tick());
        Assert.Throws<InvalidOperationException>(() => h.Tracker.Snapshot);
    }

    [Fact]
    public void Every_state_change_is_journaled()
    {
        var h = new TrackerHarness();

        h.Advance(Minutes(50));
        h.Tracker.Snooze();
        h.Advance(Minutes(10));
        h.Tracker.StartMoving();
        h.Advance(Minutes(5));
        h.Tracker.BackToSitting();

        var replayed = ActivityHistory.FromEntries(h.Journal.ReadAll());
        Assert.Equal(h.Segments, replayed.Segments);
        Assert.Equal(h.Reminders, replayed.Reminders);
    }
}
