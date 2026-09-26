using System.Runtime.InteropServices;

namespace SitStand.Storage;

/// <summary>
/// Where the app keeps its files. Chosen explicitly per platform rather than trusting
/// Environment.SpecialFolder to land somewhere sensible on macOS.
/// </summary>
public sealed class AppPaths
{
    public const string AppFolderName = "SitStand";

    public string DataDirectory { get; }
    public string JournalDirectory => Path.Combine(DataDirectory, "journal");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");
    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public string HeartbeatFile => Path.Combine(DataDirectory, "heartbeat");
    public string LockFile => Path.Combine(DataDirectory, "instance.lock");

    private AppPaths(string dataDirectory) => DataDirectory = dataDirectory;

    /// <summary>Resolve the default location, honouring a SITSTAND_DATA_DIR override for testing side-by-side copies.</summary>
    public static AppPaths Resolve()
    {
        var overrideDir = Environment.GetEnvironmentVariable("SITSTAND_DATA_DIR");
        return new AppPaths(string.IsNullOrWhiteSpace(overrideDir) ? DefaultDataDirectory() : overrideDir);
    }

    public static AppPaths At(string dataDirectory) => new(dataDirectory);

    public AppPaths EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(JournalDirectory);
        Directory.CreateDirectory(LogDirectory);
        return this;
    }

    private static string DefaultDataDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // %APPDATA%\SitStand
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // ~/Library/Application Support/SitStand
            return Path.Combine(home, "Library", "Application Support", AppFolderName);
        }

        // Linux: $XDG_DATA_HOME/SitStand or ~/.local/share/SitStand
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var baseDir = string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".local", "share") : xdg;
        return Path.Combine(baseDir, AppFolderName);
    }
}
