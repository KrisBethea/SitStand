using Microsoft.Extensions.Logging;
using SitStand.Core;
using SitStand.Core.Journal;
using SitStand.Storage;

namespace SitStand.App.Services;

/// <summary>
/// Non-UI composition: paths, logging, settings, journal, tracker, loop. Deliberately hand-wired —
/// the graph is six objects. Swap in a DI container when it stops being obvious.
/// </summary>
public sealed class AppHost : IDisposable
{
    public AppPaths Paths { get; }
    public ILoggerFactory LoggerFactory { get; }
    public TimeProvider Time { get; } = TimeProvider.System;
    public ISettingsStore SettingsStore { get; }
    public ActivityTracker Tracker { get; }

    private readonly IHeartbeatStore _heartbeat;
    private readonly TrackerLoop _loop;
    private readonly ILogger<AppHost> _log;

    private AppHost(AppPaths paths, ILoggerFactory loggerFactory)
    {
        Paths = paths;
        LoggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger<AppHost>();

        SettingsStore = new JsonSettingsStore(paths.SettingsFile, loggerFactory.CreateLogger<JsonSettingsStore>());
        var settings = SettingsStore.Load();

        var journal = new JsonlJournal(paths.JournalDirectory, loggerFactory.CreateLogger<JsonlJournal>());
        var entries = journal.ReadAll();
        var history = ActivityHistory.FromEntries(entries);
        _log.LogInformation("Loaded {Entries} journal entries → {Segments} segments", entries.Count, history.Segments.Count);

        _heartbeat = new HeartbeatFile(paths.HeartbeatFile);
        Tracker = new ActivityTracker(settings, history, journal, Time, loggerFactory.CreateLogger<ActivityTracker>());
        _loop = new TrackerLoop(Tracker, _heartbeat, Time, loggerFactory.CreateLogger<TrackerLoop>());
    }

    public static AppHost Create()
    {
        var paths = AppPaths.Resolve().EnsureDirectories();
        var loggerFactory = AppLogging.CreateLoggerFactory(paths.LogDirectory);
        return new AppHost(paths, loggerFactory);
    }

    /// <summary>Reconcile with whatever the last run left behind, then start the heartbeat/reminder loop.</summary>
    public void StartTracking()
    {
        Tracker.Initialize(_heartbeat.Read());
        _heartbeat.Write(Time.GetLocalNow());
        _loop.Start();
    }

    public void Dispose()
    {
        _loop.Dispose();
        // A clean quit is a known-good "last seen" — the next start won't have to guess.
        try { _heartbeat.Write(Time.GetLocalNow()); } catch { /* best effort */ }
        LoggerFactory.Dispose();
    }
}
