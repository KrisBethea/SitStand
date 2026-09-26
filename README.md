# SitStand

A small menu-bar / system-tray app that tracks how long you've been sitting, interrupts you when it's time to move, and keeps an honest record of whether you actually did.

Cross-platform .NET 10 + Avalonia. Runs on macOS (Apple Silicon and Intel) and Windows. Everything stays on your machine.

## Design in three sentences

1. **Time is never counted, only computed.** The app stores timestamps (`StretchStartedAt`) and derives elapsed time from `now`. Sleep, suspend, and starved timers cannot corrupt it.
2. **The timeline is a list of segments, not a set of counters.** Every statistic — breaks, longest sit, average sit, moving time — is a projection over `Segment` records. Change how you define a metric and history recomputes.
3. **Gaps are recorded as gaps.** If the machine sleeps for three hours, the open sitting stretch is closed at the last known heartbeat and the gap is written as `Away`. Your "longest sitting stretch" is never a lie about a closed laptop.

## Layout

```
src/
  SitStand.Core/        Domain, tracker, scheduler, stats. Zero UI dependencies. This is the app.
  SitStand.Storage/     Append-only JSONL journal, heartbeat file, settings.json, per-OS paths.
  SitStand.App/         Avalonia shell: tray icon + native menu, alert window, dashboard.
tests/
  SitStand.Core.Tests/  70 tests against FakeTimeProvider: sleep, snooze, skip, midnight, DST, restarts.
scripts/
  bundle-macos.sh       Builds a menu-bar-only SitStand.app.
```

Key types:

| Type | Role |
| --- | --- |
| `ActivityTracker` | The only thing allowed to change the timeline. All commands are `(state, command, now) → journal entries`. |
| `ActivityHistory` | In-memory fold of the journal into `Segment` / `Reminder` lists. Same code path for replay and live. |
| `TrackerLoop` | The one timer. Ticks every 10 s, calls `Tick()`, writes the heartbeat. Never sleeps "until the reminder". |
| `DailyStatsCalculator` | Clips segments to a `DayWindow` and sums. DST-safe, honours the configurable day boundary. |
| `TrayController` | Builds the native menu in code, refreshes the live header, renders the state icon. |

## Build, test, run

```bash
dotnet test
```

```bash
dotnet run --project src/SitStand.App
```

On Windows a tray icon appears (Windows 11 hides new icons in the overflow chute by default — drag it out once). On macOS, running via `dotnet run` gives you a menu-bar item but macOS may still show a Dock icon; use the bundle script below for the real menu-bar-only experience.

### The menu

```
Sitting: 37:14  ·  snoozed ×1
Today: 3 movement breaks  ·  longest sit 52m
────────────────────────────
I'm Moving
I'm Standing
Back to Sitting
────────────────────────────
Snooze 10 minutes
Pause reminders for 1h 0m
────────────────────────────
Open Dashboard
Open Settings File…
Reload Settings
Open Data Folder…
────────────────────────────
Quit SitStand
```

Icon shape is the activity (disc = sitting, bar = standing, ring = moving); colour is urgency (green → amber at 75 % of the interval → red when due or overdue; grey when paused). On Windows, left-click opens the dashboard; right-click opens the menu. On macOS, click opens the menu.

### The alert

When a reminder is due, a small always-on-top window appears centre-screen with **I'm Moving / I'm Standing / Snooze / Skip**. It is deliberately *not* an OS notification: toasts get swallowed by Focus Assist / Do Not Disturb, need per-platform registration, and vanish into a notification centre — the opposite of what an interrupt is for.

- **I'm Moving** — closes the sitting stretch, opens a `Moving` segment. Click **Back to Sitting** when you return.
- **I'm Standing** — opens a `Standing` stretch. Standing still is also sedentary, so it gets its own (longer) reminder interval rather than switching reminders off.
- **Snooze** — pushes the reminder out; the sitting stretch keeps accumulating. The menu shows `snoozed ×N`.
- **Skip** — restarts the reminder clock from now; the sitting stretch keeps running.
- **No answer for `reminderTimeout`** (default 3 min) — recorded as `Ignored`; the reminder comes back after `snoozeDuration`.

## Settings

`settings.json` is created with defaults on first run. Edit it, then **Reload Settings** from the menu.

```jsonc
{
  "sittingReminderInterval": "00:50:00",
  "standingReminderInterval": "01:30:00",   // null disables standing reminders
  "snoozeDuration": "00:10:00",
  "reminderTimeout": "00:03:00",            // unanswered alert → Ignored
  "gapThreshold": "00:05:00",               // silence longer than this = machine was asleep / app wasn't running
  "pauseDuration": "01:00:00",              // "Pause reminders" menu item
  "dayStartsAt": "04:00:00",                // 1 a.m. work counts toward the previous day
  "activeHours": null                       // or: { "start": "08:00", "end": "18:00", "days": "Weekdays" }
}
```

`days` accepts flag names: `Monday`, …, `Weekdays`, `Weekend`, `EveryDay`, or combinations like `"Weekdays, Saturday"`. Tracking runs regardless of active hours; only reminders are gated. Comments and trailing commas are tolerated.

## Data

| | Location |
| --- | --- |
| Windows | `%APPDATA%\SitStand\` |
| macOS | `~/Library/Application Support/SitStand/` |
| Linux | `~/.local/share/SitStand/` |

Set `SITSTAND_DATA_DIR` to point somewhere else (handy for a throwaway test copy).

```
settings.json
heartbeat                 last "alive" timestamp, rewritten every 10 s
instance.lock             single-instance guard
journal/journal-2026.jsonl
logs/sitstand-20260928.log
```

The journal is one JSON object per line, append-only, one file per year:

```json
{"type":"segment-opened","segmentId":"…","activity":"Sitting","at":"2026-09-28T09:00:00-04:00","source":"Startup"}
{"type":"reminder-fired","reminderId":"…","at":"2026-09-28T09:50:00-04:00","activity":"Sitting"}
{"type":"reminder-responded","reminderId":"…","at":"2026-09-28T09:50:41-04:00","response":"Moving"}
{"type":"segment-closed","segmentId":"…","at":"2026-09-28T09:50:41-04:00","source":"UserAction"}
{"type":"segment-opened","segmentId":"…","activity":"Moving","at":"2026-09-28T09:50:41-04:00","source":"UserAction"}
```

Nothing is ever rewritten. A bad line is skipped with a warning in the log. Hand-editing is fine; the `type` property can be anywhere in the object. `source` tells you whether a boundary was a click (`UserAction`), a restart (`Startup`), or inferred (`GapInferred`, `AutoClosed`).

## macOS bundle

```bash
./scripts/bundle-macos.sh              # osx-arm64 → artifacts/SitStand.app
```

The bundle sets `LSUIElement` (no Dock icon) and is ad-hoc signed, which is enough to run on the machine that built it. If you copy it elsewhere Gatekeeper will object until you either strip the quarantine attribute (`xattr -dr com.apple.quarantine SitStand.app`) or sign with a Developer ID and notarize. Change `CFBundleIdentifier` in `src/SitStand.App/macos/Info.plist` before doing that.

## Windows publish

```bash
dotnet publish src/SitStand.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/win-x64
```

Unsigned executables get a SmartScreen warning on other machines. That's a code-signing certificate problem, not a code problem.

## Verifying it (suggested first-week checklist)

- [ ] Set `sittingReminderInterval` to `00:02:00` and `reminderTimeout` to `00:00:30`, reload, and watch the icon go green → amber → red and the alert appear.
- [ ] Click **I'm Moving**, wait a minute, click **Back to Sitting**. Dashboard should show 1 break, moving ≈ 1m.
- [ ] Let an alert time out. Dashboard should show 1 ignored; a second alert should appear `snoozeDuration` later.
- [ ] Snooze once and confirm the header shows `snoozed ×1` and `Sitting:` keeps counting.
- [ ] Close the lid / sleep for > 5 minutes. On wake: header should read `Sitting: 00:0x`, and the dashboard should show an `Away` stretch — not a giant sit.
- [ ] Quit and relaunch quickly. Header should continue from where it was, not reset.
- [ ] Kill the process (Task Manager / `kill -9`), wait 10 minutes, relaunch. Open segment should be closed at the last heartbeat with `Away` after it.
- [ ] Set `activeHours` to a window that excludes now. Confirm no alerts and `outside active hours` in the header.
- [ ] Work past midnight (or set `dayStartsAt` to a minute from now). Today's totals should split cleanly.
- [ ] On macOS, confirm the alert window actually comes to the front over a full-screen app. This is the one place Avalonia + `LSUIElement` might need attention.

## Known gaps (deliberate, for v0)

- **Forgetting to click Back to Sitting.** Nothing detects that you returned. Idle-input detection (`GetLastInputInfo` / `CGEventSourceSecondsSinceLastEventType`) is the planned fix — the `MarkAway(from, to)` hook on the tracker already exists for it.
- **Overnight-awake machines.** Gap detection catches sleep; it does not catch "machine on all night, nobody there". Same fix as above.
- **No sound.** Avalonia has no audio; add `NetCoreAudio` or a platform call if a silent window isn't enough.
- **No launch-at-login.** Windows: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. macOS: `SMAppService` or a LaunchAgent plist.
- **No settings UI.** Edit the file; it's a v1 item.
- **No goals or streaks.** When they come, prefer "5 of the last 7 days" over consecutive counters — streaks punish sick days.
- **Windows tray overflow.** New icons are hidden by default and there is no API to promote them.

## Roadmap sketch

- **v1** — settings window, idle detection, launch at login, CSV export, dashboard polish.
- **v2** — goals, gentle streaks, weekly summary, optional OS toast as a secondary channel.
- **v3** — natural-language layer ("I was walking for the last ten minutes", "how am I doing this week?") implemented as a *command translator* over the existing tracker API. History edits go through a confirmation step and are written as `Backfilled` corrections. The deterministic core never depends on it; the app must work fully offline.

## If this goes beyond one desk

Everything is local and no network call exists in the codebase. Keep it that way by default. A tracker that watches whether someone is at their desk reads very differently when IT installs it than when you build it for yourself — local-only storage, no central aggregation, one-click export and delete, and an explicit opt-in for anything that leaves the machine are the difference between a tool and evidence.
