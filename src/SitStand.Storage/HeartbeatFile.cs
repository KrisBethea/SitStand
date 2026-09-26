using System.Globalization;
using SitStand.Core.Journal;

namespace SitStand.Storage;

/// <summary>A single ISO-8601 timestamp in a file, replaced atomically on every tick.</summary>
public sealed class HeartbeatFile : IHeartbeatStore
{
    private readonly string _path;
    private readonly string _tempPath;

    public HeartbeatFile(string path)
    {
        _path = path;
        _tempPath = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Write(DateTimeOffset at)
    {
        File.WriteAllText(_tempPath, at.ToString("O", CultureInfo.InvariantCulture));
        File.Move(_tempPath, _path, overwrite: true);
    }

    public DateTimeOffset? Read()
    {
        if (!File.Exists(_path)) return null;
        var text = File.ReadAllText(_path).Trim();
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;
    }
}
