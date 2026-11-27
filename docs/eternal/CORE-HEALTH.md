# Core Health

Quick Access > Core has a **Health** section: one line for the whole living-room setup ("All good" or "Needs
attention: 2"), then a row for each part. Press **A** on a row to show its details. Healthy rows are steel, information
and pending things amber, problems crimson. The rows marked *console guards* keep the PC a console (nothing asks for a
keyboard, mouse or password, nothing quietly breaks the TV picture); what they read, their buttons and their limits are
in [CONSOLE-GUARDS.md](CONSOLE-GUARDS.md). A row that needs a fix shows its button under the list; an open row also
shows its other actions (for example **Save current display as normal**).

| Row | From | Needs attention when |
| --- | ---- | -------------------- |
| Server backup | the home server's `core-status.json` | the last run failed or partly failed (its details show the server's reason, for example a failed upload), the status file is missing, unreadable or older than 36 hours, or the server cannot be reached |
| Restore check | same | the monthly check failed, or the last one is over 45 days old |
| Server drive | same | Windows on the server reports the library drive as Warning or Unhealthy (or under 5% free) |
| RomM | same | RomM did not answer the server's own check |
| Windows restart | this PC | (information only: "Pending" when Windows wants a restart) |
| Update night | this PC | updates are waiting and no 5:00 restart is scheduled (schedule it from the card above) |
| Save backup | Ludusavi before update night | the last run failed or was stopped after 10 minutes (no notice: the update went ahead). Amber when Ludusavi is not installed. See [BATCH-1.md](BATCH-1.md) |
| TV | Core's TV control | TV control is on but the TV is not paired or does not answer (checked with a plain connection, never woken). While Stream mode is on it says *Left alone (Stream mode)* and is not checked at all |
| Streaming | Stream mode | (information only: "Idle", "Streaming to <device>" or "TV kept out by you"; details show the source and Apollo's virtual display. See [CORE-STREAM-MODE.md](CORE-STREAM-MODE.md)) |
| Sleep after streaming | batch 2 | never (information only: *Off*, *On · after 10 min* or *Sleeping at 01:24*). See [BATCH-2.md](BATCH-2.md) |
| Wake-on-LAN | batch 2 | never (amber when something stops a handheld from waking the PC: Wi-Fi only, *Wake on Magic Packet* off, power management, fast startup). See [BATCH-2.md](BATCH-2.md) |
| Home Assistant | batch 2 | never (information only: off, connected, connecting, login refused, broker not reachable). See [BATCH-2.md](BATCH-2.md) |
| Microphone | this PC | "Partly muted" (one default microphone muted, the other live; in crimson). Otherwise information only: "Muted" in crimson, "Live" or "No microphone", followed by the calls mic's name or "calls mic not connected" when one is chosen. A calls mic that is not connected is never a problem (it may be on another PC): no attention, no notice. No battery level: Windows reports none for a USB audio receiver. See [CORE-VOICE.md](CORE-VOICE.md) |
| Steam update | Steam updates | (only while an automatic Steam update is postponed; amber after 21 days past the soak. See [STEAM-UPDATES.md](STEAM-UPDATES.md)) |
| Decky | Core's heartbeat and Decky's plugin logs | plugins did not load, or a plugin's log reported errors in the last 6 hours ("Forge is reporting errors", with one masked line). See [BATCH-1.md](BATCH-1.md) |
| Display | console guards | the display differs from the one saved as normal (resolution, refresh rate, HDR, main display, Windows' variable refresh switch). **Restore display settings** |
| Sound output | console guards | the default playback device is not the one saved as normal. **Switch sound back** |
| Controllers | console guards | never (information only; an expected controller that is not seen is amber) |
| Steam sign-in | console guards | Steam runs with nobody signed in (a sign-in or Steam Guard screen), or *Remember me* is off |
| Automatic sign-in | console guards | Windows' automatic sign-in is off, runs out (`AutoLogonCount`) or is blocked by *Windows Hello only* |
| BitLocker | console guards | no recovery-key protector, a staged firmware update while BitLocker is on, or suspended for no known reason. **Suspend BitLocker for one restart** (with the desktop's guard installed) |
| Windows setup screens | console guards | never (amber while on). **Turn off setup screens** |
| Update policy | console guards | never (amber until Quiet Windows applies it; says when the PC runs Windows Home) |
| Launcher updates | console guards | never (information: launcher or anti-cheat updates that will ask for administrator rights) |
| Launcher pop-ups | console guards | never (how many launcher windows Core pushed behind Big Picture today) |
| After waking | console guards | Big Picture was not visible, the TV was not on Core's input, or the Ethernet cable is unplugged |
| PC disk space | this PC | under 10% or under 20 GB free on the Windows drive |
| Core update | Core's last update check | (information only: "Core 3.4.0 available") |

## Notices

With **Health alerts** on (the default), Core shows one short notice over Big Picture when:

- the server backup has failed two nights in a row or more,
- the server's drive reports Warning or Unhealthy,
- the server's status is older than a day and a half,
- the display or the default sound output is no longer the one saved as normal,
- Windows' automatic sign-in is off,
- a firmware update is staged while BitLocker is on and the BitLocker guard is not installed,
- the Ethernet cable is unplugged.
- a Decky plugin's log reports errors ("Decky: Forge is reporting errors"), again when the set of plugins changes.

Several at once share one notice ("A few things need attention").

At most once a day per problem, never during a game, and not while Core streams to another device (the notice waits
until the stream ends). The notice is Core's own, like the plugin notice, so
Quiet Windows' *Hide Windows notifications* does not hide it. **Dismiss alert** (under the Health rows) silences a
problem until it changes: a new failure streak, a drive going from Warning to Unhealthy, or a new stale file alerts
again. A problem that clears and comes back alerts again too.

## Setup

### At the server (once)

The server is the Ubuntu box running Aincrad (M2ljay/Aincrad). Its nightly `scripts/backup.sh` (RomM database,
saves and config, Home Assistant; Proton Drive off-site) also writes `core-status.json` into Home Assistant's `www/`
folder. Follow Aincrad's `docs/backups.md`, then restart Home Assistant once after the first run so it serves that
folder. Check from any browser: `http://serverv3:8123/local/core-status.json` shows the file.

### On the gaming PC (once)

1. Core > Gaming Mode > **Core Health**: type the server's name in **Home server** (`serverv3`; an IP address, a
   `name:port` or a full `http(s)://` address also works). Leave **Health alerts** on.
2. Press **Check now**. "All good" or "Things to check: N" means the agent could read the status.
3. If the server row says **Server not reachable** although the server is on, open the address above in a browser
   on the PC. No login is involved.

### At the TV

1. Open Quick Access > **Core**. Scroll to **Health** with the D-pad.
2. Check the top line, then move through the rows; press **A** on any crimson row for details.
3. A row with an alert has **Dismiss alert: <row>** below the list.

## How it works

- Server: Aincrad's `scripts/backup.sh` writes `core-status.json` (schema 1: backup result, last success, failures in
  a row, `/mnt/storage` space, RomM answering) atomically every night. It has no restore-check section, so that row
  reads *never* until one is added.
- Agent: `GET /health` and `POST /health/dismiss?id=` on 127.0.0.1:47991 (`MarkIIIHost.Health.cs`). The status file is
  read at most once a minute with a 4-second time box, so a sleeping server never stalls Quick Access; the TV probe is
  a TCP connect to webOS ports 3001/3000 (no Wake-on-LAN, no command); Core's version comes from the agent's cached
  update check (at most one GitHub request per 30 minutes, the same check Big Picture's update notice uses). The
  notice check runs every five minutes. Alert memory: `%APPDATA%\GamingMode\core-health-alerts.json`.
- Rules: `CoreHealth.cs` (pure; tests in `Source/CoreHealth.Tests`, which also read a status file shaped exactly as
  Aincrad writes it). The console guards' rows come from `ConsoleGuards.cs` (tests in
  `Source/CoreHealth.Tests/ConsoleGuardTests.cs`); their buttons go to `POST /health/action?id=`
  (`MarkIIIHost.Console.cs`). Quick Access: `GamingModeDeckyPlugin/src/HealthSection.tsx` and `healthReport.ts` (texts in 12
  languages; `tests/coreHealthSection.test.mjs`).
- Settings, in `%APPDATA%\GamingMode\config.json` (`gaming` section): `coreHealthServer`, `coreHealthAlertsEnabled`.

**Not included yet:** update checks for your own plugins (Awesome Mix, The Collector). Core does not read their
`update.json` today, and adding a new network check to every Quick Access open was out of scope; each plugin still
updates itself.
