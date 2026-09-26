using System.Text.Json;
using SitStand.Core.Journal;
using SitStand.Core.Model;

namespace SitStand.Core.Tests;

public class SerializationTests
{
    [Fact]
    public void Journal_entries_round_trip_through_jsonl()
    {
        var at = new DateTimeOffset(2026, 9, 28, 9, 15, 30, TimeSpan.FromHours(-4));
        var segmentId = Guid.NewGuid();
        var reminderId = Guid.NewGuid();
        var entries = new JournalEntry[]
        {
            new SegmentOpened(segmentId, Activity.Sitting, at, SegmentSource.Startup),
            new ReminderFired(reminderId, at.AddMinutes(50), Activity.Sitting),
            new ReminderResponded(reminderId, at.AddMinutes(51), ReminderResponse.Moving),
            new SegmentClosed(segmentId, at.AddMinutes(51), SegmentSource.UserAction),
        };

        var lines = entries.Select(JournalJson.Serialize).ToArray();
        var back = lines.Select(l => JournalJson.Deserialize(l)!).ToArray();

        Assert.Equal(entries, back);
        Assert.All(lines, l => Assert.DoesNotContain('\n', l));
    }

    [Fact]
    public void Journal_lines_are_readable_by_humans()
    {
        var at = new DateTimeOffset(2026, 9, 28, 9, 15, 30, TimeSpan.FromHours(-4));
        var line = JournalJson.Serialize(new SegmentOpened(Guid.Empty, Activity.Moving, at, SegmentSource.UserAction));

        Assert.Contains("\"type\":\"segment-opened\"", line);
        Assert.Contains("\"activity\":\"Moving\"", line);          // enum names, not numbers
        Assert.Contains("\"at\":\"2026-09-28T09:15:30-04:00\"", line); // local offset preserved
    }

    [Fact]
    public void Type_discriminator_may_appear_anywhere_in_the_object()
    {
        // Someone hand-edits the file and moves "type" to the end.
        var line = """{"segmentId":"00000000-0000-0000-0000-000000000000","at":"2026-09-28T09:15:30-04:00","source":"UserAction","type":"segment-closed"}""";

        var entry = Assert.IsType<SegmentClosed>(JournalJson.Deserialize(line));
        Assert.Equal(SegmentSource.UserAction, entry.Source);
    }

    [Fact]
    public void Replaying_the_journal_rebuilds_identical_history()
    {
        var h = new TrackerHarness();
        h.Advance(TimeSpan.FromMinutes(50));
        h.Tracker.Snooze();
        h.Advance(TimeSpan.FromMinutes(10));
        h.Tracker.StartMoving();
        h.Advance(TimeSpan.FromMinutes(5));
        h.Tracker.StartStanding();
        h.Sleep(TimeSpan.FromHours(2));

        var lines = h.Journal.ReadAll().Select(JournalJson.Serialize);
        var replayed = ActivityHistory.FromEntries(lines.Select(l => JournalJson.Deserialize(l)!));

        Assert.Equal(h.Tracker.History.Segments, replayed.Segments);
        Assert.Equal(h.Tracker.History.Reminders, replayed.Reminders);
        Assert.Equal(h.Tracker.History.OpenSegment, replayed.OpenSegment);
    }

    [Fact]
    public void Settings_round_trip_including_active_hours_flags()
    {
        var settings = new TrackerSettings
        {
            SittingReminderInterval = TimeSpan.FromMinutes(45),
            StandingReminderInterval = null,
            DayStartsAt = new TimeOnly(5, 30),
            ActiveHours = new ActiveHours(new TimeOnly(8, 0), new TimeOnly(18, 0), ActiveDays.Weekdays | ActiveDays.Saturday),
        };

        var json = JsonSerializer.Serialize(settings, TrackerSettings.JsonOptions);
        var back = JsonSerializer.Deserialize<TrackerSettings>(json, TrackerSettings.JsonOptions);

        Assert.Equal(settings, back);
        Assert.Contains("\"sittingReminderInterval\": \"00:45:00\"", json);
        Assert.Contains("Weekdays, Saturday", json);
    }

    [Fact]
    public void Settings_json_tolerates_comments_and_trailing_commas()
    {
        var json = """
        {
          // fifty minutes
          "sittingReminderInterval": "00:50:00",
          "snoozeDuration": "00:15:00",
        }
        """;

        var settings = JsonSerializer.Deserialize<TrackerSettings>(json, TrackerSettings.JsonOptions)!;

        Assert.Equal(TimeSpan.FromMinutes(15), settings.SnoozeDuration);
        Assert.Equal(TrackerSettings.Default.GapThreshold, settings.GapThreshold); // unspecified keeps default
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    public void Invalid_settings_are_rejected(int sittingMinutes, int _)
    {
        var settings = new TrackerSettings { SittingReminderInterval = TimeSpan.FromMinutes(sittingMinutes) };
        Assert.Throws<ArgumentException>(settings.Validate);
    }
}
