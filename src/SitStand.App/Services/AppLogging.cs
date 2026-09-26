using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SitStand.Storage;

namespace SitStand.App.Services;

public static class AppLogging
{
    public static ILoggerFactory CreateLoggerFactory(string logDirectory)
    {
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDirectory, "sitstand-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        return new SerilogLoggerFactory(serilog, dispose: true);
    }

    /// <summary>Last resort when the app dies before or outside logging: a plain text file next to the logs.</summary>
    public static void WriteCrashFile(Exception ex)
    {
        try
        {
            var paths = AppPaths.Resolve().EnsureDirectories();
            var file = Path.Combine(paths.LogDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, ex.ToString());
        }
        catch
        {
            // Nothing left to try.
        }
    }
}
