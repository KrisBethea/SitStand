namespace SitStand.Core.Journal;

/// <summary>Write side of the journal. The tracker calls this synchronously on every state change.</summary>
public interface IJournalWriter
{
    void Append(JournalEntry entry);
}

/// <summary>Read side of the journal, used once at startup to rebuild history.</summary>
public interface IJournalReader
{
    IReadOnlyList<JournalEntry> ReadAll();
}

/// <summary>
/// A single "last seen alive" timestamp, overwritten on every tick. On restart it tells us when the
/// previous process last knew the user was at the computer, so a dangling open segment can be closed
/// honestly instead of pretending the user sat through the whole outage.
/// </summary>
public interface IHeartbeatStore
{
    void Write(DateTimeOffset at);
    DateTimeOffset? Read();
}

public interface ISettingsStore
{
    TrackerSettings Load();
    void Save(TrackerSettings settings);
    string FilePath { get; }
}
