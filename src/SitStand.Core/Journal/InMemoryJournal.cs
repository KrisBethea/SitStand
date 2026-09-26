namespace SitStand.Core.Journal;

/// <summary>Journal that lives only in memory. Used by tests and handy for a dry-run mode.</summary>
public sealed class InMemoryJournal : IJournalWriter, IJournalReader
{
    private readonly object _gate = new();
    private readonly List<JournalEntry> _entries = new();

    public void Append(JournalEntry entry)
    {
        lock (_gate) _entries.Add(entry);
    }

    public IReadOnlyList<JournalEntry> ReadAll()
    {
        lock (_gate) return _entries.ToArray();
    }
}

public sealed class InMemoryHeartbeat : IHeartbeatStore
{
    private DateTimeOffset? _last;
    public void Write(DateTimeOffset at) => _last = at;
    public DateTimeOffset? Read() => _last;
}
