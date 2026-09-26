using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SitStand.Core.Journal;

namespace SitStand.Storage;

/// <summary>
/// Append-only JSON Lines journal, one file per calendar year (journal-2026.jsonl). Each line is one
/// <see cref="JournalEntry"/>. Nothing is ever rewritten, so a crash mid-write loses at most one line,
/// and the files are readable and hand-editable.
/// </summary>
public sealed class JsonlJournal : IJournalWriter, IJournalReader
{
    private readonly string _directory;
    private readonly ILogger<JsonlJournal> _log;
    private readonly object _gate = new();

    public JsonlJournal(string directory, ILogger<JsonlJournal>? log = null)
    {
        _directory = directory;
        _log = log ?? NullLogger<JsonlJournal>.Instance;
        Directory.CreateDirectory(_directory);
    }

    public void Append(JournalEntry entry)
    {
        var line = JournalJson.Serialize(entry);
        var path = FileFor(entry.At);
        lock (_gate)
        {
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(line);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
    }

    public IReadOnlyList<JournalEntry> ReadAll()
    {
        var entries = new List<JournalEntry>();
        lock (_gate)
        {
            foreach (var path in Directory.EnumerateFiles(_directory, "journal-*.jsonl").OrderBy(p => p, StringComparer.Ordinal))
            {
                var lineNumber = 0;
                foreach (var line in File.ReadLines(path))
                {
                    lineNumber++;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        if (JournalJson.Deserialize(line) is { } entry) entries.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        // A single bad line (truncated write, hand-edit typo) must not take the whole history down.
                        _log.LogWarning(ex, "Skipping unreadable journal line {File}:{Line}", Path.GetFileName(path), lineNumber);
                    }
                }
            }
        }

        // Files are per-year but an entry's year comes from its own offset, so stitch by timestamp to be safe.
        entries.Sort((a, b) => a.At.CompareTo(b.At));
        return entries;
    }

    private string FileFor(DateTimeOffset at) => Path.Combine(_directory, $"journal-{at.Year:D4}.jsonl");
}
