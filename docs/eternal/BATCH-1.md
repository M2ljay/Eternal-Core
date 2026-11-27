# Batch 1: before bed

Four small features for the living-room PC, built on Core Health, update night and Core's Decky plugin. All four
follow the same rules as the rest of Mark III: the rules are pure and tested, notices come at most once a day and never
over a game, and every failure is logged and ignored rather than shown as an error at the TV.

Settings: Core > Gaming Mode > **Before bed** (stored in `%APPDATA%\GamingMode\config.json`, `gaming` section):
`saveBackupBeforeUpdates`, `windDownEnabled`, `windDownBedtime`, `windDownMinutes`, `autoCompletedEnabled`,
`autoCompletedCreate`. Agent state files next to it: `save-backup.json`, `decky-logs.json`, `wind-down.json`.

## 1. Save backup before update night

Before Core's update-night restart (5:00) and before an automatic Steam update, the agent runs
`ludusavi backup --force --api` for every game.

- **Only with Ludusavi installed.** Core looks for `ludusavi.exe` the way the Forge plugin does: the agent's PATH
  plus the user's current PATH, then winget's `Links` and `Packages\mtkennerly.ludusavi_*` folders (per user and
  machine-wide), then Scoop's `apps\ludusavi\current` and `shims`. Install it with
  `winget install -e --id mtkennerly.ludusavi`.
- **No window, time-boxed:** `CreateNoWindow`, below-normal priority, at most 10 minutes; a run that takes longer is
  stopped (its whole process tree).
- **Once a night.** The restart and a Steam update a few hours apart share one run; a failed run is not repeated the
  same night.
- **Never blocks.** Whatever happens, the restart or the Steam update goes ahead. If a game starts during the backup,
  the restart waits as usual (update night retries until 7:00).
- **All games, not just recent ones.** Ludusavi names games its own way, so backing up only recently played Steam
  games would need one `find --steam-id` lookup per game. Backing up everything is simpler and runs at 5:00 when
  nobody is playing.
- **Health row "Save backup":** *Last night OK* (steel), *Last night failed* with one reason (crimson, no notice),
  *Ludusavi not installed* with the install command (amber), *Off*, *Not run yet*. Older results show their date.
- **Setting:** *Back up saves before updates*, on by default. It does nothing until Ludusavi is installed.

Code: `GamingMode.Services/SaveBackup.cs` (rules), `MarkIIIHost.Batch.cs` (the run), hooked into
`CheckUpdateNightAsync` and the automatic `update-steam` job in `MarkIIIHost.cs`.

## 2. Decky health names the failing plugin (B11)

Core's heartbeat already tells whether Decky's UI loaded. Now Core also reads Decky's plugin logs,
`%USERPROFILE%\homebrew\logs\<plugin>\*.log`, and names the plugins that report errors.

- **Cheap:** at most once a minute; for each plugin only its two newest logs; only what was added since the last look
  (a remembered byte offset per file), whole lines only, and at most 256 KB per file per round. On the very first look
  Core only measures the files, so errors from before Core started looking are never reported. A file that appeared
  after the last look (a new plugin start) is read from its beginning.
- **What counts:** `ERROR`, `CRITICAL` and `FATAL` lines (Decky's `[ERROR]:` format and Python's `- ERROR -`), and
  Python tracebacks (a traceback written by `logger.exception` right after its ERROR line counts once).
- **What reaches the TV:** the plugin names and one line, the last error's message or the traceback's final
  `SomeError: ...` line. That line is masked first: JWTs, `Bearer`/`Basic` credentials, `user:password@` in URLs,
  query parameters and `name=value` pairs whose name looks like a token, key, secret, password, cookie, session or
  credential, long opaque strings (24+ characters of hex or base64 with letters and digits) and the Windows user
  folder's name are replaced; control characters go, and the line is cut to 120 characters. Nothing else from a log is
  shown or stored.
- **Health row "Decky":** *Plugins loaded* (steel); *Waiting for plugins* or *Not in Gaming Mode* (amber); *Plugins
  not loaded* or *Forge is reporting errors* (crimson; details: errors in the last 6 hours and the masked line). A
  plugin is "reporting errors" for 6 hours after Core last saw one. Core's own plugin is called Core, not Playhub.
- **Notices:** one Core notice, "Decky: Forge is reporting errors. Details are in Quick Access > Core.", under Core
  Health's rules (Health alerts on, at most once a day, never during a game or while streaming, again when the set of
  failing plugins changes; *Dismiss alert* silences it). The existing "plugins are taking a short break" notice adds
  "Errors reported by: Forge." when logs show errors.
- **Credit:** the idea (per-plugin error state from Decky's logs) comes from Decky Task Manager. It has no licence,
  so no code was taken from it.

Code: `GamingMode.Services/DeckyPluginLogs.cs` (rules, masking), `MarkIIIHost.Batch.cs` (file reads).

## 3. 4 a.m. wind-down (B10)

Calm reminders before bedtime: by default at 3:30 and 3:50 for a 4:00 bedtime. "It's 3:50 — good moment to save."

- **Warnings only.** Core never closes a game, quits anything or sleeps the PC for this.
- **Only while a game runs.** With nothing to save, a reminder is noise, so it is skipped when no game runs (an idle
  PC, Big Picture's menus or the desktop). A reminder stays due for 10 minutes, so a game started at 3:52 still hears
  about 3:50; each reminder is given once a night. Two due at once: only the later one is said.
- **How it appears:** as a Steam toast, through Core's Decky plugin (the agent hands it over in the answer to the
  plugin's 30-second heartbeat). Core's own notice window is for Big Picture and is never shown over a game, so the
  Steam toast is the in-game route. Without Decky the reminder is dropped (and logged).
- **Stream mode and VR:** still notified, quietly (the toast plays no sound). The toast appears wherever Steam's
  overlay is, so it follows the stream.
- **Settings:** *Reminders to save before bed* (on), *Bedtime* (`04:00`), *Minutes before* (`30, 10`; 1 to 180, at
  most four).
- **Home Assistant hook:** `MarkIIIHost.SleepModeActive()` returns null today. When it can read Aincrad's
  `input_boolean.sleep_mode`, true will skip the reminders. No Home Assistant integration is part of this batch.

Code: `GamingMode.Services/WindDown.cs` (rules), `MarkIIIHost.Batch.cs` (`CheckWindDown`, the heartbeat answer),
`GamingModeDeckyPlugin/src/coreHealth.ts` (`heartbeatToast`), `index.tsx` (the toast).

## 4. Auto-Completed (B9)

When a Steam game reaches 100% of its achievements, Core's Decky plugin adds it to the Steam collection named
**Completed**.

- **Matching:** the user's collection whose name is "Completed", ignoring case and spaces around it.
- **Creating:** only with *Create the Completed collection if it's missing* on (off by default). Otherwise nothing is
  created and the agent log says the game is at 100% with no collection to add it to.
- **Never removes** a game from the collection, and skips games with no achievements and non-Steam shortcuts.
- **When:** a few seconds after Steam reports an achievement unlock (`SteamClient.GameSessions`), and once a day over
  the Steam games played in the last 14 days (the first sweep a minute after Big Picture starts).
- **Where the numbers come from:** Steam's achievement progress cache (`appAchievementProgressCache`); when it does
  not say 100%, `SteamClient.Apps.GetMyAchievementsForApp` (locked hidden achievements count against 100%).
- **Decky 3 and 4:** the stores (`collectionStore`, `appStore`, the progress cache) are taken from Steam's globals
  when they exist, otherwise found through the compat helpers in `steamModules.ts`. Anything missing means "not now":
  no error at the TV.
- **Setting:** *Add completed games to the Completed collection*, on by default.
- **Credit:** ideas from Completionist and Deck Progress Tracker (both BSD-3-Clause): the progress cache as the
  source of 100%, collectionStore for the collection. No code was copied.

Code: `GamingModeDeckyPlugin/src/autoCompleted.ts`, wired in `index.tsx`; settings and the log line through the
agent (`GET /core/completed`, `POST /core/completed/report`, `main.py` `core_completed_settings` /
`core_completed_report`).

## Tests

- `Source/CoreHealth.Tests` (`BatchOneTests.cs`): finding Ludusavi, reading its `--api` answer, once a night, the
  save backup row; log offsets and caps, what counts as an error, masking, the Decky row and its notice; wind-down
  times, grace, once a night, idle, quiet, the sleep-mode hook.
- `Source/MarkIII.Tests`: the new agent texts exist in all 12 languages with matching placeholders.
- `Source/LocalizationResource.Tests`: the *Before bed* card in all 12 languages.
- `GamingModeDeckyPlugin/tests/coreBatchOne.test.mjs`: the two Health rows in 12 languages, the heartbeat toast,
  Auto-Completed (matching, creating, never removing, 0 achievements, the daily sweep).

## Needs the PC

Nothing here can be checked on Linux against the real thing: Ludusavi's run and its timing, the log folder's real
names and formats, Steam's achievement notifications and collection saves on both Steam clients, and the toast over
a game. See the TV checklist in the batch's pull request or report.
