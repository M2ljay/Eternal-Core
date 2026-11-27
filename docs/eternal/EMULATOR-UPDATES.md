# Emulator updates

How Core updates the emulators EmuDeck for Windows installs, with the same safety net as Steam's updates
([STEAM-UPDATES.md](STEAM-UPDATES.md)). Rules (pure, tested on Linux in `Source/CoreHealth.Tests/EmulatorTests.cs`):
`GamingModeAgent/GamingMode.Services/EmulatorUpdates.cs`; Windows half: `Emulators.Windows.cs` (`EmulatorUpdater`);
wiring: `MarkIIIHost.Emulators.cs`.

## Sources: the official GitHub projects only

The same projects and asset filters EmuDeck's own `<Emu>_install` functions use (EmuDeck's source,
`functions\EmuScripts\emuDeck*.ps1`), read from `https://api.github.com/repos/<project>/releases/latest` (the newest
release that is neither a draft nor a prerelease) at most every 6 hours per emulator. Downloads only from
`https://github.com/<project>/releases/download/`.

| Emulator | Project | Asset | Notes |
| -------- | ------- | ----- | ----- |
| PCSX2 | `PCSX2/pcsx2` | `windows*.7z` (no symbols) | `pcsx2-qt.exe` renamed `pcsx2-qtx64.exe`, as EmuDeck does |
| DuckStation | `stenzek/duckstation` | `windows-x64*.zip` (no symbols) | |
| Azahar | `azahar-emu/azahar` | `windows-msvc*.zip` | |
| RPCS3 | `RPCS3/rpcs3-binaries-win` | `*.7z` | |
| Xenia Canary | `xenia-canary/xenia-canary` | `windows*.7z` | |
| shadPS4 | `shadps4-emu/shadps4-qtlauncher` | `win64*.zip` | |
| Cemu | `cemu-project/Cemu` | `windows-x64*.zip` | |
| xemu | `xemu-project/xemu` | `windows-x86_64*.zip` (no pdb/dbg) | |

Not offered by Core (EmuDeck takes them from elsewhere; use EmuDeck's **Manage Emulators**): RetroArch
(buildbot.libretro.com), Dolphin (dolphin-emu.org), PPSSPP (ppsspp.org), Ryujinx (git.ryujinx.app). The Health row lists
them as *Updated by EmuDeck*.

**Why not EmuDeck's own updater:** EmuDeck asks before each update in its launcher script (`isLatestVersionGH`) and
installs with `<Emu>_install`, which is not built to run unattended (winget prompts, progress messages, resets of its
backend with git) and has no backup or rollback. Core reuses its sources, folder layout and renames instead.

The first release Core sees for an emulator it never updated is taken as the one installed (Core cannot tell
EmuDeck's build from a tag), so nothing updates on day one.

## Verification

- Every download is checked against **GitHub's own SHA-256 digest** of the asset (the `digest` field) and its size.
- If the project publishes its own checksum beside the asset (`<asset>.sha256`, `SHA256SUMS`, `checksums.txt`...), the
  download must match it too; such a version updates without asking.
- If the project publishes no hash of its own, GitHub's digest proves only that the bytes are the ones uploaded, not who
  uploaded them, so **the user confirms that version once on the desktop**: Core > Gaming Mode > Stays a console >
  *Emulator updates* > **Check confirmations** > **Confirm <emulator> <version>** (the agent's
  `POST /emulators/update/confirm`). Quick Access says *confirm it once on the desktop* and why. No confirmation, no
  update (also not with *Update now*). No signature is checked: none of these projects signs its release archives in a
  form Windows can verify without extra tools.

## The policy

1. **Soak:** each new version waits **10 days** (`gaming.emulatorUpdateSoakDays`, 1 to 60) from the day Core first saw
   it; a newer version restarts its own wait.
2. **When:** on an update night (05:00 to 07:00) while idle: no game, no stream, the emulator not running.
3. **Safety net** (`RunEmulatorUpdateAsync`):
   1. download to `%TEMP%\Core-emulator-update\`, verify (above);
   2. unpack with EmuDeck's own 7-Zip (`%APPDATA%\EmuDeck\backend\wintools\7z.exe`) or Windows' `tar.exe` (libarchive:
      `.zip` always, `.7z` on current Windows 11; *inferred* for older builds, checklist);
   3. **back up the program files** (everything in the emulator's folder outside its user-data paths) to
      `%APPDATA%\GamingMode\emulator-backups\<emulator>\update-<time>\`; no backup, no update;
   4. copy the new files over the folder (a single top folder in the archive is stripped). **User data is never touched:**
      archive entries under the emulator's data paths are skipped (PCSX2 `inis`, `memcards`, `sstates`, `bios`...;
      DuckStation `settings.ini`, `memcards`, `savestates`...; Azahar and shadPS4 `user`; RPCS3 `config`, `dev_hdd0`,
      `dev_flash`...; Xenia `content`, its config; Cemu `mlc01`, `settings.xml`, `keys.txt`, `controllerProfiles`,
      `gameProfiles`, `graphicPacks`; xemu `xemu.toml`, `eeprom.bin`, `xbox_hdd.qcow2`), and no file is ever deleted;
   5. **smoke test:** the exe is there and is a PE file, starts (minimised) and either keeps running for 6 s or exits
      cleanly; then it is closed (WM_CLOSE, then ended after 5 s);
   6. **on failure:** the backed-up program files are copied back, files the update added are removed, a Core notice
      says *The <emulator> update failed its check and was undone*, and that version is **not retried** until a newer one
      appears.
4. **After an update** the shared hotkeys are checked and re-applied once ([CONSOLE-GUARDS.md](CONSOLE-GUARDS.md#the-shared-emulator-hotkeys-335)),
   since an update can reset an emulator's config.
5. **Update now:** Quick Access (*Update <emulator> now* on the row when offered) skips the soak and the no-retry rule,
   never the confirmation, the running check or the safety net.

## State and endpoints

- `%APPDATA%\GamingMode\emulator-updates.json`: per emulator the installed tag, the pending version (asset, URLs,
  GitHub digest, verification, first seen), confirmations, the last result.
- `GET /emulators/updates` (the desktop's confirmations), `POST /emulators/update/confirm?id=&version=`,
  `POST /health/action?id=emu-update-<emulator>`; Health row `emulator-updates`.
- Switch: `gaming.emulatorUpdatesEnabled` (on).

## Checks on the PC

- The agent log shows `Emulator updates: <emulator> <version> is out; it soaks 10 days (GitHubOnly|Project)` within 6 hours.
- Confirm a version on the desktop, then use *Update now* from Quick Access with the emulator closed: the backup folder
  appears, the emulator starts minimised for a few seconds, and the row says *Up to date*.
- Whether `tar.exe` unpacks `.7z` on this Windows build if EmuDeck's 7-Zip is missing.
