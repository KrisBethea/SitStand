using System.Text.Json;
using System.Text.Json.Serialization;
using SitStand.Core.Model;

namespace SitStand.Core.Journal;

/// <summary>
/// The persisted form of the timeline: an append-only stream of facts. Segments and reminders are
/// never rewritten on disk; a change is a new entry. <see cref="ActivityHistory"/> folds the stream
/// back into <see cref="Segment"/> and <see cref="Reminder"/> objects.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SegmentOpened), "segment-opened")]
[JsonDerivedType(typeof(SegmentClosed), "segment-closed")]
[JsonDerivedType(typeof(ReminderFired), "reminder-fired")]
[JsonDerivedType(typeof(ReminderResponded), "reminder-responded")]
public abstract record JournalEntry(DateTimeOffset At);

public sealed record SegmentOpened(Guid SegmentId, Activity Activity, DateTimeOffset At, SegmentSource Source)
    : JournalEntry(At);

public sealed record SegmentClosed(Guid SegmentId, DateTimeOffset At, SegmentSource Source)
    : JournalEntry(At);

public sealed record ReminderFired(Guid ReminderId, DateTimeOffset At, Activity Activity)
    : JournalEntry(At);

public sealed record ReminderResponded(Guid ReminderId, DateTimeOffset At, ReminderResponse Response)
    : JournalEntry(At);

public static class JournalJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        // Hand-edited files shouldn't break because someone moved the "type" property.
        AllowOutOfOrderMetadataProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>One JSON object on one line, suitable for appending to a .jsonl file.</summary>
    public static string Serialize(JournalEntry entry) => JsonSerializer.Serialize(entry, Options);

    public static JournalEntry? Deserialize(string line) =>
        string.IsNullOrWhiteSpace(line) ? null : JsonSerializer.Deserialize<JournalEntry>(line, Options);
}
