# Eternal Core

Eternal Core is a personal fork of [Playhub](https://github.com/LoZazaMastro/Playhub), the Windows couch-gaming app
(Decky Loader and Gaming Mode on Windows, Steam Big Picture). Upstream development stopped at v3.0.0, so this fork
continues it.

**Names.** The app is called **Core** wherever the user reads it; **Eternal** is the maker brand ("Core by Eternal",
like PlayStation by Sony). "Eternal Core" names this repository and the docs. Internal identifiers (install and data
folders, uninstall key, AppUserModelID, executables, plugin IDs) still say Playhub so updates keep working. Details,
the list of what deliberately still says Playhub, and the image inventory: [REBRAND.md](REBRAND.md).

- **Base:** Playhub v3.0.0, commit `2f60fb0` (the fork's `main` adds only README and screenshot commits).
- **Licence:** MIT, unchanged. The original copyright, © 2026 Andrea Sgarro (LoZazaMastro), is kept in `LICENSE`,
  in the source headers and in the app's About card, which reads "Core by Eternal. Based on Playhub by Andrea Sgarro
  (LoZazaMastro), MIT licence."
- **Technical notes:** [ARCHITECTURE-NOTES.md](ARCHITECTURE-NOTES.md) covers the update mechanism, every remote
  source that points at upstream, and the build system.

## Releases: the Mark scheme

Releases are named after Iron Man suits. Each Mark has an internal semantic version that sorts above upstream 3.0.0,
so the existing update comparison, and any installed Playhub 3.0, treat it as newer.

| Release  | Version | Git tag  |
| -------- | ------- | -------- |
| Mark I   | 3.1.0   | `v3.1.0` |
| Mark II  | 3.2.0   | `v3.2.0` |
| Mark III | 3.3.0   | `v3.3.0` |
| …        | 3.N.0   | `v3.N.0` |

A fix release inside a Mark bumps the patch number (`3.1.1` is still Mark I).

The single source of truth is `Source/Shared/EternalRelease.cs` (`Version`, `MarkName`, `UpdateRepository`).
`Source/EternalRelease.props` copies the version into the app, setup and agent builds. To cut the next Mark, change
those two constants and nothing else.

The About card and the setup window show the Mark and version, for example `Mark II (3.2.0)`. Windows "Installed apps"
shows `3.2.0`.

## How updates flow

1. A release on `M2ljay/Eternal-Core` that is published, not a draft and not a prerelease, has a numeric tag
   (`v3.1.0`) and an asset named `Playhub-Setup.exe`.
2. Core checks `releases/latest` of the fork every few minutes. Installs that still store upstream's repository in
   `settings.json` are redirected automatically.
3. The download is checked against GitHub's SHA-256 digest of the asset, its size and the `MZ` header. Then the
   installer runs with `--update` and upgrades in place, keeping settings.

**Bootstrap:** your current Playhub 3.0 still checks upstream, so it will never offer Mark I by itself. Install Mark I
once by hand (run `Playhub-Setup.exe` from the release). It upgrades in place. From then on, updates come from the
fork.

## How to build

### Recommended: GitHub Actions

`.github/workflows/eternal-release.yml` builds the installer on `windows-latest`:

1. On GitHub, open the fork's **Actions** tab and enable workflows (forks have them disabled by default).
2. Pushing `main` or a `mark-*` branch, or running the workflow manually, builds the installer and attaches
   `Playhub-Setup.exe` and `.sha256` to the run as an artifact.
3. Pushing a tag such as `v3.1.0` (it must equal `EternalRelease.Version`) also creates a **draft** release.
   Review it on GitHub and click *Publish*. Only then do installed copies see it.

```
git tag v3.1.0
git push origin v3.1.0
```

### On a Windows PC (not needed for normal use)

Requires the .NET 8 SDK, Node 22, PowerShell 7, and Git with `core.autocrlf=true` (the default in Git for Windows).

```powershell
git clone --branch mark-ii https://github.com/M2ljay/Eternal-Core
cd Eternal-Core\Source\GamingModeDeckyPlugin
npm install -g pnpm@9; pnpm install --frozen-lockfile
cd ..\EternalRelease.Tests; dotnet run -c Release
cd ..\..
pwsh -File Source\PlayhubSetup\build-update-preview.ps1
# -> Source\PlayhubSetup\Output\Playhub-Setup.exe (+ .sha256)
```

### Linux (checks only)

The app, setup and agent target Windows and need NuGet packages, so they only build on Windows or in CI. The
portable test projects run on Linux; see `CLAUDE.md` at the repo root.

## Mark II (3.2.0)

### Anti-cheat safe mode (upstream #25)

Some kernel anti-cheats (Ricochet for Call of Duty was the report) refuse to start a game while Gaming Mode is active.
Gaming Mode does not stop services or kill Explorer, but four of its Gaming-only features touch games or global input:

| Feature (config key)                    | What it does to the game or the system                                     |
| --------------------------------------- | -------------------------------------------------------------------------- |
| volume-key hook (`volume`)              | a global low-level keyboard hook (`WH_KEYBOARD_LL`) for the volume keys     |
| borderless/foreground handling (`window`) | restyles game windows to borderless, forces them to the foreground, and watches the game process to bring Big Picture back afterwards |
| cursor auto-hide (`cursor`)             | replaces the system cursors with a blank one                               |
| game power QoS (`qos`)                  | opens a handle on the game process to set its power throttling            |

**What it does.** While a protected game runs, the Gaming Mode agent pauses all four: it removes the keyboard hook,
leaves game windows alone, restores the system cursors and closes or refuses any handle on the game. When the game
exits (after about two seconds without any protection signal) they resume, and Big Picture is brought back to the
front if nothing else is open. It works in both modes; in Desktop Mode the features are already off.

**How a protected game is detected.** Passively: the agent never opens, injects into, enumerates the modules of, or
reads the memory of a game process. Every ~0.75 s it checks, in order:

1. Anti-cheat services reported as running by the Windows service control manager (read-only queries):
   `EasyAntiCheat`, `EasyAntiCheat_EOS`, `BEService`, `BEDaisy`, `vgc`, `vgk`, `FACEIT`, `FACEITService`,
   `EAAntiCheatService`, `atvi-randgrid_sr`, `atvi-randgrid_msstore` (Ricochet), `mhyprot2`, `mhyprot3`, `npggsvc`,
   `GameGuard`, `xhunter1`. Only services that start on demand count: a service set to start with Windows (boot,
   system or automatic, such as Riot's `vgk` driver) runs whether or not you play, so the log notes it once and it is
   not used.
2. Your **Always protected games** list, matched against running process names.
3. Anti-cheat files in the running game's folder: `EasyAntiCheat`, `EasyAntiCheat_EOS_Setup.exe`,
   `start_protected_game.exe`, `BattlEye`, `BEService*.exe`, `randgrid.sys` (Ricochet), `EAAntiCheat.*.exe`,
   `GameGuard`, `XIGNCODE`. The folder comes from Steam's running game (`RunningAppID` and its appmanifest), or from
   the image path of a process that owns a visible game-sized window (read by process ID with
   `NtQuerySystemInformation`, without opening the process). The agent lists the executable's folder and up to three
   parents, and stops at library folders such as `steamapps\common` so a neighbouring game never matches.
4. **Treat every game as protected** (off by default): any running Steam game or game-like window.

Settings (Core > Gaming Mode > *Protezione anti-cheat* / *Anti-cheat protection*):

- **Anti-cheat safe mode**: master switch, on by default.
- **Treat every game as protected**: off by default.
- **Always protected games**: one executable per line.

**How to add a game.** Find the game's executable (in Steam: *Manage > Browse local files*, or in Task Manager's
*Details* tab while it runs) and type its file name, for example `cod.exe`, on its own line under
*Always protected games*. The name is enough; a full path also works. Case and the `.exe` suffix do not matter.

**Which game triggered it.** The agent log, `%APPDATA%\GamingMode\agent.log`, has one line per change:

```
Anti-cheat safe mode engaged: anti-cheat service atvi-randgrid_sr (Ricochet (Call of Duty)) is running. Pausing: game power QoS, borderless/foreground handling, volume-key hook, cursor auto-hide.
Anti-cheat safe mode still engaged: cod.exe is on the "always protected" list.
Anti-cheat safe mode released: no protected game is detected any more (it was engaged because ...). Resuming: ...
```

**Limits.**

- Protection starts when the first signal appears. Steam sets `RunningAppID` as it launches a game, and on-demand
  anti-cheat services start with the game's launcher, so this is normally before the anti-cheat inspects the system,
  but it is a race measured in fractions of a second. For a game that still fails, add its executable to the list, or
  turn on *Treat every game as protected*.
- An anti-cheat driver that stays loaded after its game exits (mhyprot is known for this) keeps protection on until
  it unloads or Windows restarts. The log says which service it is.
- Other agent features that observe games are not paused: the live performance HUD (PresentMon, which uses ETW),
  per-game audio volume, Steam overlay brokers, Launch Curtain and the QAM focus guard. See
  [ARCHITECTURE-NOTES.md §4](ARCHITECTURE-NOTES.md#4-mark-ii-changes).

### Big Picture menu after a non-Steam game (upstream #22)

After a non-Steam game exited, Ctrl+1, Ctrl+2 and the Steam menu button stopped opening Big Picture's menus until a
Steam game was launched. Core's own Decky plugin kept treating the exited game as running. It now forgets the game
when Steam reports that it stopped, and the press reaches Big Picture again.

### Dashboard from the controller (upstream #10)

Core never reads controllers: Steam owns them, and an earlier attempt to read them directly broke Steam Input. The
Dashboard card in Core > Gaming Mode now has *Apri la Dashboard con il controller* (*Open the Dashboard with your
controller*): it shows the current keyboard shortcut (Ctrl+Alt+P unless changed), explains how to bind a Steam
Guide-button chord (for example Guide + Y) to that shortcut, and has a button that opens Steam's chord configurator
(`steam://controllerconfig/443510`). Valve documents `steam://controllerconfig/<app id>`, and 443510 is the app ID
Steam uses for the chord layout, but that this pair opens the chord editor is not yet verified on a PC. If Steam does
not open it, follow *Settings > Controller > Guide Button Chord Layout > Edit* by hand.

## Status of the Mark I branch (as built in a Linux sandbox, 2026-11-24)

- **Not built here:** the app, setup and agent. NuGet and Microsoft download hosts were blocked in that sandbox. No
  installer has been produced yet. The first Actions run is the first real build of these changes.
- **Ran on Linux:** 41 plain `net8.0` test projects. 35 pass, including the new `EternalRelease.Tests`. Five fail
  the same way before and after the fork changes, because they test Windows behaviour (case-insensitive paths, file
  locks, PE metadata, native fixtures): `ControlledQamFocusChecks`, `DeckyArtifactComparison.Tests`,
  `Playhub.BundledDecky.Tests`, `Playhub.ImportCatalog.Tests`, `Playhub.PluginUninstall.Tests`.
  `PlayhubUpdate.Tests` does not compile at upstream v3.0.0 (pre-existing).
- **Decky plugin:** builds on Linux. Its node tests show 1,273 of 1,327 passing, with 38 pre-existing failures.

## Open questions

1. **Remote content still served by upstream:** the Plugin Store catalog, the "On this day" feed and its images, and
   the update checks for upstream's plugins. All of them keep working while upstream stays online. The images are the
   only part with no offline fallback. Options and a recommendation for each are in
   [ARCHITECTURE-NOTES.md §2](ARCHITECTURE-NOTES.md#2-other-remote-sources-tied-to-upstream). Keep, mirror into the
   fork, or bundle?
2. **Update notice wording:** the "Core 3.2.0 available" dialog shows the version number, not the Mark name. Should
   it map 3.N to "Mark N"?
3. **Code signing:** upstream never signed the installer, so Windows SmartScreen warns. A certificate would remove that
   warning but costs money.
4. **Branding, Phase 2:** Phase 1 renamed every visible name to Core and replaced the logos, icons, splash and accent
   colours ([REBRAND.md](REBRAND.md); masters in [brand/](brand/)). The identifiers (AppUserModelID
   `LoZazaMastro.Playhub`, install and data folders, uninstall key, executables, plugin IDs) are unchanged. Renaming
   them needs the migration plan sketched there. Still open: the onboarding mascots, and where the Eternal maker logo
   appears (a boot sequence).
5. **Release tests:** upstream's `PlayhubUpdate.Tests` is broken at v3.0.0. Fix it, or rely on `EternalRelease.Tests`?
6. **Ricochet's exact trigger** is unknown. Once it is bisected on the PC
   ([ARCHITECTURE-NOTES.md §4](ARCHITECTURE-NOTES.md#4-mark-ii-changes)), should the pause list shrink to the
   features that matter, so volume keys and the cursor keep working in protected games?
7. **Controller chord inside Big Picture:** the Decky plugin could also listen for a chord while Big Picture itself
   is focused. Worth building as a follow-up?
