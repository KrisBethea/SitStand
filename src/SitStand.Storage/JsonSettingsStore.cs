using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SitStand.Core;
using SitStand.Core.Journal;

namespace SitStand.Storage;

/// <summary>
/// settings.json. Missing file → defaults are written so the user has something to edit.
/// Unreadable or invalid file → defaults are used and the problem is logged, never thrown at startup.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly ILogger<JsonSettingsStore> _log;

    public string FilePath { get; }

    public JsonSettingsStore(string filePath, ILogger<JsonSettingsStore>? log = null)
    {
        FilePath = filePath;
        _log = log ?? NullLogger<JsonSettingsStore>.Instance;
    }

    public TrackerSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            Save(TrackerSettings.Default);
            return TrackerSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<TrackerSettings>(json, TrackerSettings.JsonOptions) ?? TrackerSettings.Default;
            settings.Validate();
            return settings;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "settings.json is unreadable or invalid; using defaults. Fix or delete {Path}", FilePath);
            return TrackerSettings.Default;
        }
    }

    public void Save(TrackerSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, TrackerSettings.JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }
}
