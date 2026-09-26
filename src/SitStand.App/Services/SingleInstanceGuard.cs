namespace SitStand.App.Services;

/// <summary>
/// Holds an exclusive lock file for the life of the process. Two copies of a tray app means two sets
/// of reminders and interleaved journal writes, so the second one just exits.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public static readonly SingleInstanceGuard None = new(null);

    private readonly FileStream? _lock;

    private SingleInstanceGuard(FileStream? lockStream) => _lock = lockStream;

    public static SingleInstanceGuard? TryAcquire(string lockFile)
    {
        try
        {
            var stream = new FileStream(
                lockFile,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return new SingleInstanceGuard(stream);
        }
        catch (IOException)
        {
            return null; // someone else holds it
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose() => _lock?.Dispose();
}
