using SitStand.Core.Journal;
using SitStand.Core.Model;
using SitStand.Storage;

namespace SitStand.Core.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SitStand.Tests", Guid.NewGuid().ToString("N"));

    public StorageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Jsonl_journal_appends_and_reads_back_in_order()
    {
        var journal = new JsonlJournal(_dir);
        var at = new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.FromHours(-4));
        var id = Guid.NewGuid();

        journal.Append(new SegmentOpened(id, Activity.Sitting, at, SegmentSource.Startup));
        journal.Append(new SegmentClosed(id, at.AddMinutes(30), SegmentSource.UserAction));

        var entries = new JsonlJournal(_dir).ReadAll();

        Assert.Equal(2, entries.Count);
        Assert.IsType<SegmentOpened>(entries[0]);
        Assert.IsType<SegmentClosed>(entries[1]);
        Assert.Single(Directory.GetFiles(_dir, "journal-2026.jsonl"));
    }

    [Fact]
    public void Jsonl_journal_splits_files_by_year_and_stitches_on_read()
    {
        var journal = new JsonlJournal(_dir);
        var newYearsEve = new DateTimeOffset(2026, 12, 31, 23, 50, 0, TimeSpan.FromHours(-4));
        var id = Guid.NewGuid();

        journal.Append(new SegmentOpened(id, Activity.Sitting, newYearsEve, SegmentSource.UserAction));
        journal.Append(new SegmentClosed(id, newYearsEve.AddMinutes(20), SegmentSource.UserAction));

        Assert.True(File.Exists(Path.Combine(_dir, "journal-2026.jsonl")));
        Assert.True(File.Exists(Path.Combine(_dir, "journal-2027.jsonl")));

        var history = ActivityHistory.FromEntries(journal.ReadAll());
        var segment = Assert.Single(history.Segments);
        Assert.Equal(TimeSpan.FromMinutes(20), segment.Duration(newYearsEve.AddHours(1)));
    }

    [Fact]
    public void Jsonl_journal_skips_a_corrupt_line_and_keeps_the_rest()
    {
        var journal = new JsonlJournal(_dir);
        var at = new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.FromHours(-4));
        journal.Append(new SegmentOpened(Guid.NewGuid(), Activity.Sitting, at, SegmentSource.Startup));
        File.AppendAllText(Path.Combine(_dir, "journal-2026.jsonl"), "{this is not json\n");
        journal.Append(new SegmentOpened(Guid.NewGuid(), Activity.Moving, at.AddMinutes(5), SegmentSource.UserAction));

        var entries = journal.ReadAll();

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void Heartbeat_round_trips_and_is_null_when_missing()
    {
        var path = Path.Combine(_dir, "heartbeat");
        var heartbeat = new HeartbeatFile(path);
        Assert.Null(heartbeat.Read());

        var at = new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.FromHours(-4));
        heartbeat.Write(at);

        Assert.Equal(at, new HeartbeatFile(path).Read());
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Settings_store_writes_defaults_when_missing_and_reads_edits_back()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();
        Assert.Equal(TrackerSettings.Default, loaded);
        Assert.True(File.Exists(path));

        store.Save(loaded with { SittingReminderInterval = TimeSpan.FromMinutes(35) });
        Assert.Equal(TimeSpan.FromMinutes(35), new JsonSettingsStore(path).Load().SittingReminderInterval);
    }

    [Fact]
    public void Settings_store_falls_back_to_defaults_on_garbage()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "not json at all");

        Assert.Equal(TrackerSettings.Default, new JsonSettingsStore(path).Load());
    }

    [Fact]
    public void Settings_store_falls_back_to_defaults_on_invalid_values()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "sittingReminderInterval": "00:00:00" }""");

        Assert.Equal(TrackerSettings.Default, new JsonSettingsStore(path).Load());
    }

    [Fact]
    public void App_paths_honour_the_override_environment_variable()
    {
        var paths = AppPaths.At(_dir).EnsureDirectories();

        Assert.Equal(Path.Combine(_dir, "journal"), paths.JournalDirectory);
        Assert.Equal(Path.Combine(_dir, "settings.json"), paths.SettingsFile);
        Assert.True(Directory.Exists(paths.JournalDirectory));
        Assert.True(Directory.Exists(paths.LogDirectory));
    }
}
