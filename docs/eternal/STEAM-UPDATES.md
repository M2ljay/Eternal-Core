# Steam updates

How Core decides when the Steam client may update, and what it does when an update goes wrong. The policy is pure
and tested in `Source/GamingModeAgent/GamingMode.Services/SteamUpdateHold.cs` (tests: `Source/MarkIII.Tests`); the
Windows wiring is in `MarkIIIHost.cs` (agent). Why it exists: [DECKY-4.md](DECKY-4.md). A Steam client that loads
its UI differently from what the installed Decky expects leaves every Big Picture plugin dead, so Steam updates are
treated as a change that has to be checked first.

## The block

The block is Playhub's existing one: `steam.cfg` with `BootStrapperInhibitAll=enable` in the Steam folder stops the
client from updating itself when it starts. The desktop app's "Hold Steam updates" switch writes or removes it.
With the switch off, Steam updates itself as usual and none of this applies.

Valve does not document `BootStrapperInhibitAll`, and it is **not confirmed for the 64-bit client**. Nothing here
relies on it. The agent watches the installed client version, and an update that happens anyway gets the same
smoke test and rollback (rule 5).

## The policy

### 1. Soak per version

- Core reads the pending client version from Valve's published manifest for this PC's channel
  (`https://media.steampowered.com/client/steam_client_win64`, or `steam_client_<beta>_win64` when
  `Steam\package\beta` names a beta) at most every 6 hours (1 hour after a failed read). The version is the same
  build number as in `Steam\package\steam_client_win64.manifest`. If the read fails, nothing is assumed. The URL
  still has to be confirmed on the PC; see "Checks on the PC".
- The first time Core sees a pending version, **that update night is day one**. An update night is named by the
  morning it ends on: anything before 07:00 belongs to this morning's window (05:00–07:00, `UpdateNight.cs`),
  anything later belongs to the next one.
- Steam may update only once that version has waited the soak (`SteamUpdateSoakDays` in the Gaming Mode config,
  **10 by default**, 1 to 60; the desktop app offers 3, 5, 7, 10, 14, 21 and 30). With the default, a version first
  seen on day one may install on the night of day eleven.
- The update runs on an update night, idle only: Gaming Mode, Steam running, no game and no anti-cheat game for 10
  minutes. The agent decides once per night, and keeps trying until 07:00 if the PC was not idle.
- A newer pending version restarts its own 10 days and drops any postponement of the older one.

### 2. Gate before updating

`SteamReleaseGate.Evaluate` decides with:

- the known-good file shipped inside the agent, `Source/Shared/SteamCompatibility.json`:
  - `bundledDecky`: the Decky version Core ships (it must equal `BundledDeckyWindowsRelease.BuildVersion`;
    `MarkIII.Tests` checks this);
  - `decky`: the Steam client kind (`webpack` or `modules`) each Decky version supports, by exact version (`"3.3.1"`)
    or by major (`"4"`). `MarkIII.Tests` checks each entry against the built-in rule;
  - `steamClients`: ranges of Steam client versions and their kind. A version that no range covers is "can't tell".
    The shipped range covers every client built before 1 November 2026, well before the first ES-module beta (18
    November 2026). **Extend a range only after checking that client on a real PC.**
- the installed Decky (`PluginLoader.exe` product version; unreadable means the bundled one is assumed);
- the client-kind detection: `typeof window.webpackChunksteamui` in Steam's SharedJSContext over the CEF debug port
  (`SteamClientKindProbe`).

The answer, using the one compatibility rule `DeckySteamPairing.Judge` (shared with the Decky install guard):

| Reason code | Result | When |
| --- | --- | --- |
| `ok` | pass | The pending client is the kind the installed Decky supports. |
| `no-decky` | pass | No Decky is installed, so there is nothing to break. |
| `pending-unsupported` | known incompatible | The file says the pending client is the other kind. |
| `pending-unknown` | can't tell | No pending version known, or no range covers it. |
| `decky-unknown` | can't tell | The file does not know the installed Decky. |
| `file-disagrees` | can't tell | The running client is not the kind the file lists for the installed version, so the file is not trusted. |
| `no-compatibility-file` | can't tell | The embedded file is missing or broken. |

If the gate says no or can't tell, the update is **postponed** and the gate is asked again every update night.
Core Health shows a row **"Steam update: Postponed: \<reason\>"**. It is information at first, and turns amber
(attention) once the postponement is 21 days past the end of the soak. The desktop app's Steam card and Quick
Access show the same reason.

### 3. Safety net

`SteamUpdateJob.RunAsync`, the same for every update Core makes:

1. Note what works now: if plugins worked before (Decky installed, "start Decky" on, Core's plugin has sent a
   heartbeat), the plugins Decky reports loaded (`window.DeckyPluginLoader.plugins`) are the ones expected back. A
   plugin layer that was already broken is not blamed on the update.
2. Close Steam, then **back up** the client (`SteamClientBackup`, the same backup the app makes; the two newest of
   Core's own are kept). No backup, no update.
3. Remove `steam.cfg`, start Steam so it updates, and run the **smoke test**, time-boxed to 5 minutes:
   - Steam is running and Big Picture is up (Big Picture counts only in Gaming Mode);
   - Decky's loader answers: port 1337 has a listener, the same check Core's Decky installer and Core Repair's
     "Update Decky" use;
   - every expected plugin is loaded, as Decky reports it;
   - Core's own plugin answers (a heartbeat since the update started).
4. Put `steam.cfg` back. If a plugin check failed, restart Decky once and check again for 2 minutes.
5. **On failure**: stop Steam, put the hold back first, restore the backup (`RestoreNewestBackupAsync`), start Steam
   and raise a Core notice naming what failed (`notice.steamRolledBack.bigPicture`, `.deckyLoader`, `.plugins`,
   `.corePlugin`, or `notice.steamRollbackFailed` when the restore failed; Core Repair then offers the rollback
   first).
6. **After a failure, no automatic retry** until one of these changes: `SteamCompatibility.json` (which names the
   bundled Decky), the installed Decky, or the pending Steam version. The fingerprint of the three is stored with the
   failure (`SteamReleaseGate.Fingerprint`). Core Health shows "Postponed: the last try failed its check".

### 4. Manual "Update Steam now"

The desktop app (Gaming Mode page, Steam updates card), Quick Access (Core tab, "Steam updates") and Core Repair
all call the agent (`POST /steam/update/now?confirm=N`, or `/core/repair/run?action=update-steam&confirm=N`), which
asks the gate first:

- pass: the update starts at once;
- can't tell: one confirmation (`confirm-cant-tell`), then it starts;
- known incompatible: two confirmations (`confirm-incompatible`, then `confirm-incompatible-again`).

Each confirmation is another press of the same button, within two minutes in the desktop app. The update uses the
same backup, smoke test and rollback. A manual update ignores the soak and the "no retry" rule.

### 5. Unexpected update

If the installed version changes while `steam.cfg` is present and Core is not updating Steam itself, the agent
runs the same smoke test (`SteamUpdateJob.VerifyAsync`) against the plugins it last saw loaded (read every 15
minutes while plugins work). On failure it rolls back to the newest backup, if there is one. Without a backup it
reports the failure and leaves Steam running.

## State and endpoints

- State: `%APPDATA%\GamingMode\steam-update-hold.json` (`SteamHoldState`: installed version, pending version and
  its day one, postponement, the last night's decision, the last failure and its fingerprint). A file from the old
  30-day hold loads as a fresh state.
- `GET /steam/update/state` (also `/core/repair/state`): `holdPhase` (`Off`, `Current`, `Soaking`, `Due`,
  `Postponed`, `WaitingForChange`), `daysLeft`, `soakDays`, `soakEnds`, `pendingVersion`, `postponedReason`,
  `failedCheck`, `attention`, and the job's progress.
- `POST /steam/update/now?confirm=N`: `{ok:true}` (202), `{ok:false, confirm, reason}`, or `{ok:false, busy:true}`
  (409).
- Core Health: the `steam-update` row (`GET /health`).

## Checks on the PC

- Valve's manifest URL answers with a `"version"` for the stable channel (agent log: "Steam ... is pending", or
  "pending version unknown this round"). If it does not, the gate says "can't tell" and only manual updates happen.
- Whether `BootStrapperInhibitAll` holds on the 64-bit client. If the log shows "Steam updated from ... although its
  updates were blocked", it does not, and rule 5 is what protects the PC.
- `window.DeckyPluginLoader.plugins` lists plugin names under Decky 3.3.1 (agent log: "smoke test expects: ...").
