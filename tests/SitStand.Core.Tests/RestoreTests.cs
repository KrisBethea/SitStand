using SitStand.Core.Journal;
using SitStand.Core.Model;
using static SitStand.Core.Tests.TrackerHarness;

namespace SitStand.Core.Tests;

/// <summary>What happens at startup when the previous process left things open.</summary>
public class RestoreTests
{
    [Fact]
    public void Stale_heartbeat_closes_the_dangling_segment_at_the_heartbeat_and_records_away()
    {
        var openedAt = DefaultStart - Hours(3);
        var heartbeat = DefaultStart - Hours(2) - Minutes(55);
        var existing = new JournalEntry[]
        {
            new SegmentOpened(Guid.NewGuid(), Activity.Sitting, openedAt, SegmentSource.UserAction),
        };

        var h = new TrackerHarness(existing: existing, heartbeat: heartbeat);

        Assert.Collection(h.Segments,
            s =>
            {
                Assert.Equal(Activity.Sitting, s.Activity);
                Assert.Equal(heartbeat, s.EndedAt);
                Assert.Equal(Minutes(5), s.Duration(h.Now));
            },
            s =>
            {
                Assert.Equal(Activity.Away, s.Activity);
                Assert.Equal(heartbeat, s.StartedAt);
                Assert.Equal(h.Start, s.EndedAt);
            },
            s =>
            {
                Assert.Equal(Activity.Sitting, s.Activity);
                Assert.Equal(h.Start, s.StartedAt);
                Assert.True(s.IsOpen);
            });
        Assert.Equal(TimeSpan.Zero, h.Tracker.Snapshot.Elapsed);
    }

    [Fact]
    public void Fresh_heartbeat_continues_the_open_segment_and_keeps_the_reminder_clock()
    {
        var openedAt = DefaultStart - Minutes(40);
        var existing = new JournalEntry[]
        {
            new SegmentOpened(Guid.NewGuid(), Activity.Sitting, openedAt, SegmentSource.UserAction),
        };

        var h = new TrackerHarness(existing: existing, heartbeat: DefaultStart - TimeSpan.FromSeconds(8));

        var segment = Assert.Single(h.Segments);
        Assert.Equal(openedAt, segment.StartedAt);
        Assert.Equal(Minutes(40), h.Tracker.Snapshot.Elapsed);

        h.Advance(Minutes(9));
        Assert.Empty(h.Fired);
        h.Advance(Minutes(1));
        Assert.Single(h.Fired); // 50 minutes after the *original* start, not after restart
    }

    [Fact]
    public void No_heartbeat_at_all_uses_the_segment_start_as_last_known()
    {
        var openedAt = DefaultStart - Hours(1);
        var existing = new JournalEntry[]
        {
            new SegmentOpened(Guid.NewGuid(), Activity.Sitting, openedAt, SegmentSource.UserAction),
        };

        var h = new TrackerHarness(existing: existing, heartbeat: null);

        Assert.Equal(3, h.Segments.Count);
        Assert.Equal(openedAt, h.Segments[0].EndedAt); // zero-length: we honestly know nothing about that hour
        Assert.Equal(Activity.Away, h.Segments[1].Activity);
    }

    [Fact]
    public void Pending_reminder_from_a_previous_run_is_marked_ignored_on_quick_restart()
    {
        var openedAt = DefaultStart - Minutes(55);
        var reminderId = Guid.NewGuid();
        var heartbeat = DefaultStart - TimeSpan.FromSeconds(10);
        var existing = new JournalEntry[]
        {
            new SegmentOpened(Guid.NewGuid(), Activity.Sitting, openedAt, SegmentSource.UserAction),
            new ReminderFired(reminderId, DefaultStart - Minutes(5), Activity.Sitting),
        };

        var h = new TrackerHarness(existing: existing, heartbeat: heartbeat);

        var reminder = Assert.Single(h.Reminders);
        Assert.Equal(ReminderResponse.Ignored, reminder.Response);
        Assert.Equal(heartbeat, reminder.RespondedAt);
        Assert.False(h.Tracker.Snapshot.HasPendingReminder);
    }

    [Fact]
    public void Damaged_journal_with_two_open_segments_self_heals()
    {
        var first = Guid.NewGuid();
        var existing = new JournalEntry[]
        {
            new SegmentOpened(first, Activity.Sitting, DefaultStart - Hours(2), SegmentSource.UserAction),
            // Missing SegmentClosed for `first` — crashed between writes.
            new SegmentOpened(Guid.NewGuid(), Activity.Moving, DefaultStart - Hours(1), SegmentSource.UserAction),
        };

        var history = ActivityHistory.FromEntries(existing);

        Assert.Equal(2, history.Segments.Count);
        Assert.Equal(DefaultStart - Hours(1), history.Segments[0].EndedAt);
        Assert.Equal(SegmentSource.AutoClosed, history.Segments[0].Source);
        Assert.Equal(Activity.Moving, history.OpenSegment!.Activity);
    }

    [Fact]
    public void Initialize_twice_throws()
    {
        var h = new TrackerHarness();
        Assert.Throws<InvalidOperationException>(() => h.Tracker.Initialize(null));
    }
}
