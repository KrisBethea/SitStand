using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SitStand.App.Services;

public static class Shell
{
    /// <summary>Open a file or folder with whatever the OS associates with it (editor, Finder, Explorer).</summary>
    public static void Open(string path, ILogger log)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Could not open {Path}", path);
        }
    }
}
