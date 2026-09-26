using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SitStand.Core.Journal;

namespace SitStand.Core;

/// <summary>
/// The only timer in the system. Wakes every few seconds, asks the tracker to evaluate, and writes
/// the heartbeat. It never sleeps for "the remaining time until the reminder" — if the machine
/// suspends, the next wake sees a large gap and the tracker handles it.
/// </summary>
public sealed class TrackerLoop : IDisposable
{
    public static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(10);

    private readonly ActivityTracker _tracker;
    private readonly IHeartbeatStore _heartbeat;
    private readonly TimeProvider _time;
    private readonly ILogger<TrackerLoop> _log;
    private readonly TimeSpan _period;
    private ITimer? _timer;
    private int _running;

    public TrackerLoop(
        ActivityTracker tracker,
        IHeartbeatStore heartbeat,
        TimeProvider time,
        ILogger<TrackerLoop>? log = null,
        TimeSpan? period = null)
    {
        _tracker = tracker;
        _heartbeat = heartbeat;
        _time = time;
        _log = log ?? NullLogger<TrackerLoop>.Instance;
        _period = period ?? DefaultPeriod;
    }

    public void Start()
    {
        _timer ??= _time.CreateTimer(_ => OnTick(), null, _period, _period);
    }

    private void OnTick()
    {
        // Timer callbacks can overlap if a tick runs long; skip rather than stack up.
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            _tracker.Tick();
            _heartbeat.Write(_time.GetLocalNow());
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Tracker tick failed");
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
