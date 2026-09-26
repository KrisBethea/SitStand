using Microsoft.Extensions.Time.Testing;
using SitStand.Core.Journal;

namespace SitStand.Core.Tests;

public class TrackerLoopTests
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(10);

    [Fact]
    public void Loop_ticks_the_tracker_and_writes_the_heartbeat()
    {
        var (time, tracker) = Setup();
        var heartbeat = new InMemoryHeartbeat();
        var fired = 0;
        tracker.ReminderDue += (_, _) => fired++;

        using var loop = new TrackerLoop(tracker, heartbeat, time, period: Period);
        loop.Start();

        Run(time, TimeSpan.FromMinutes(49));
        Assert.Equal(0, fired);
        Assert.Equal(time.GetLocalNow(), heartbeat.Read());

        Run(time, TimeSpan.FromMinutes(1));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Loop_detects_a_gap_after_the_machine_slept()
    {
        var (time, tracker) = Setup();

        using var loop = new TrackerLoop(tracker, new InMemoryHeartbeat(), time, period: Period);
        loop.Start();
        Run(time, TimeSpan.FromMinutes(20));

        // Suspend: the timer doesn't fire while the machine is asleep, then one tick arrives after a long silence.
        loop.Dispose();
        time.Advance(TimeSpan.FromHours(2));
        using var resumed = new TrackerLoop(tracker, new InMemoryHeartbeat(), time, period: Period);
        resumed.Start();
        time.Advance(Period);

        Assert.Equal(3, tracker.History.Segments.Count);
        Assert.Equal(Model.Activity.Away, tracker.History.Segments[1].Activity);
        Assert.Equal(time.GetLocalNow(), tracker.History.Segments[1].EndedAt); // gap ends at the tick that noticed it
        Assert.Equal(TimeSpan.Zero, tracker.Snapshot.Elapsed);                    // and a fresh stretch starts there
    }

    [Fact]
    public void Start_is_idempotent()
    {
        var (time, tracker) = Setup();
        var changes = 0;
        tracker.StateChanged += (_, _) => changes++;

        using var loop = new TrackerLoop(tracker, new InMemoryHeartbeat(), time, period: Period);
        loop.Start();
        loop.Start();

        Run(time, TimeSpan.FromMinutes(50));
        Assert.Equal(1, changes); // exactly one reminder-fired state change, not two timers' worth
    }

    private static (FakeTimeProvider Time, ActivityTracker Tracker) Setup()
    {
        var time = new FakeTimeProvider(TrackerHarness.DefaultStart.ToUniversalTime());
        var tracker = new ActivityTracker(new TrackerSettings(), new ActivityHistory(), new InMemoryJournal(), time);
        tracker.Initialize(null);
        return (time, tracker);
    }

    /// <summary>
    /// Advance with the machine awake. FakeTimeProvider moves the clock to the target and then fires the
    /// missed callbacks, so a single large Advance would look like a suspend to the tracker; step instead.
    /// </summary>
    private static void Run(FakeTimeProvider time, TimeSpan total)
    {
        var remaining = total;
        while (remaining > TimeSpan.Zero)
        {
            var step = remaining < Period ? remaining : Period;
            time.Advance(step);
            remaining -= step;
        }
    }
}
