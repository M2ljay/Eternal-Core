# Eternal Core: architecture notes

Research notes for the Eternal Core fork of Playhub. Unless stated otherwise, file:line references point at
upstream v3.0.0 (`2f60fb0`, identical source to the fork's `main` at `025cf97`), under `Source/`. Where the
`mark-i` branch changed a file, that is noted.

The app is **WinUI 3** (Windows App SDK 1.6, `net8.0-windows10.0.19041.0`, unpackaged, self-contained x64), not
WPF. The installer (`PlayhubSetup`) and the Gaming Mode agent are WPF.

## 1. Self-update

### Flow

1. **Trigger.** `MainWindow.xaml.cs:9062-9070` starts a 5-minute `DispatcherTimer` that runs a silent check (at most
   once every 2 minutes), and the Settings "check for updates" button calls `CheckPlayhubUpdatesAsync`
   (`MainWindow.xaml.cs:8974`).
2. **Which repository.** `MainWindow.xaml.cs:8986` and `:9074` call
   `PlayhubUpdateService.CheckAsync(PlayhubUpdatePolicy.Repository(_settings.PlayhubUpdateRepository), GetAppVersion(), PlayhubUpdatePolicy.ReleaseTag)`.
   - `Models/PlayhubSettings.cs:13`: `PlayhubUpdateRepository` defaults to `"LoZazaMastro/Playhub"` and **is persisted
     in the user's `settings.json`**. It is not shown anywhere in the UI. Every existing 3.0 install therefore has
     the upstream value stored, and changing only the default would not move those installs.
   - `Services/PlayhubUpdatePolicy.cs:11-12`: the release build passes the configured value through. The
     `PLAYHUB_UPDATE_PREVIEW` build (only made by `build-update-preview.ps1` tests; the release script asserts
     `IsPreview == false` in the shipped `Playhub.dll`) is pinned to `LoZazaMastro/Playhub` tag `v1.2.1`.
3. **Version source.** `MainWindow.xaml.cs:8968-8972` `GetAppVersion()` takes `Major.Minor.Build` of the assembly
   version, which comes from `<Version>3.0.0</Version>` in `Playhub/Playhub.csproj:17`.
4. **Metadata lookup.** `Services/PlayhubUpdateService.cs`
   - `:73-74`: `GET https://api.github.com/repos/{repo}/releases/latest` (anonymous, 8 s timeout).
   - `:116-149`: fallback when the API fails or is rate-limited: follow
     `https://github.com/{repo}/releases/latest` and read the tag from the redirect target (must stay on
     `github.com/{repo}/releases/tag/`).
   - `:151-175`: release notes from `https://github.com/{repo}/releases.atom` if the API body is empty.
   - `:184-206`: asset discovery fallback by scraping
     `https://github.com/{repo}/releases/expanded_assets/{tag}`.
   - "latest" means the newest non-draft, **non-prerelease** release.
5. **Asset selection.** `:179-182` accepts only names matching `^Playhub[ ._-]Setup(?:[ ._-]v?\d+(\.\d+){1,3})?\.exe$`
   (case-insensitive). `:318-348` prefers the exact name `Playhub Setup.exe`, then a name containing the version.
   The release build script produces `Playhub-Setup.exe`, which matches.
6. **Version comparison.** `:358-389` `ParseVersion`: strips a leading `v`, keeps the leading digits-and-dots
   (`3.1.0-beta` becomes `3.1.0`), pads to `System.Version`. Newer means `latest > current`. **A tag such as `mark-i`
   does not parse, so it is never offered as an update.** Tags must stay numeric (`v3.1.0`).
7. **Download and verification.** `:208-316`
   - The URL must be `https://github.com/...` (`:213-219`). Redirects to GitHub's object storage are followed.
   - Saved to `%LOCALAPPDATA%\…\Downloads\updates\<asset name>` via a `.part` file. Stalls are cut after 60 s.
   - Size: at least 64 KiB, equal to `Content-Length` and to the API `size` (`:283-287`).
   - **SHA-256:** compared with the GitHub API asset `digest` field (`sha256:<hex>`, computed by GitHub on upload)
     **only when the API path supplied one** (`:289-295`, `:350-356`). On the HTML fallback path there is no digest,
     and the hash check is skipped.
   - The file must start with `MZ` (`:298-304`).
   - **There is no Authenticode or other signature check, and no key held by the upstream author.** The installer is
     unsigned (see `PlayhubSetup/README_INSTALLER.md`, "Avviso SmartScreen"). Trust is "whatever GitHub serves for
     that repo's latest release". Moving the source to the fork changes no security property: the fork owner's
     GitHub account becomes the trust root, just as upstream's was. The `.sha256` file that the release script writes
     next to the installer is not read by the app.
8. **Install.** `MainWindow.xaml.cs:9030`: launches the downloaded installer with `--update` and closes Playhub.
   `PlayhubSetup/App.xaml.cs:19-21` handles `--update`. The setup installs per user into
   `%LOCALAPPDATA%\Programs\Playhub`, replacing the previous files in place, and rewrites
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Playhub` (`Installer.cs:353-367`) with
   `DisplayVersion = Installer.AppVersion`. A fork build installs over a Playhub 3.0 install with the same key and
   folder: it is an in-place upgrade, and settings and data are kept.

### Second checker: the Gaming Mode agent

`GamingModeAgent/GamingMode.Services/PlayhubUpdateNotice.cs:32-35` runs its own check for the Big Picture overlay
(`POST /playhub/update`, used by `GamingModeDeckyPlugin/main.py`). It hardcodes `"LoZazaMastro/Playhub"` and
compares against **its own** assembly version, `GamingModeAgent/Properties/AssemblyInfo.cs:8,9,14` (`3.0.0.0`,
`GenerateAssemblyInfo=False`). If the app version moved to 3.1.0 while the agent stayed at 3.0.0, Big Picture would
keep announcing an update that the desktop app considers installed.

### Version literals before the fork (four independent copies)

| Where                                        | Value     | Used for                                                                        |
| -------------------------------------------- | --------- | ------------------------------------------------------------------------------- |
| `Playhub/Playhub.csproj:17`                  | `3.0.0`   | App assembly version, `GetAppVersion()`, About card, update comparison          |
| `PlayhubSetup/PlayhubSetup.csproj:13`        | `3.0.0`   | Setup exe file version                                                          |
| `PlayhubSetup/Installer.cs:30`               | `3.0.0`   | Setup window "Version" label (`MainWindow.xaml.cs:112`), uninstall `DisplayVersion` |
| `GamingModeAgent/Properties/AssemblyInfo.cs` | `3.0.0.0` | Agent file version (startup payload sync, `GamingModePayloadSync.cs`), agent update notice |

The agent file version also feeds `Playhub/Services/GamingModePayloadSync.cs:14-24`: startup sync never replaces a
*newer* installed agent with an older bundled one. Raising it with the app version is therefore safe; lowering it
is not.

`GamingModeDeckyPlugin/package.json` and `plugin.json` also say `3.0.0`. That is the Decky plugin's own version
(its update is hash-based, `GamingModePayloadSync.NeedsPluginUpdate`), not the app version.

### Where the name and version are displayed

Upstream v3.0.0 (line numbers) and, after the arrow, the fork since the Phase 1 rebrand (`docs/eternal/REBRAND.md`):

- About card: `Playhub/MainWindow.xaml.cs:3836`: `"Playhub {GetAppVersion()} · © 2026 Andrea Sgarro (LoZazaMastro)"`
  → `"Core Mark II (3.2.0)"` plus the credit line "Core by Eternal. Based on Playhub by Andrea Sgarro (LoZazaMastro),
  MIT licence. © 2026 Andrea Sgarro (LoZazaMastro)." (localized).
- Update UI: `"Playhub {0} disponibile"` with the remote tag version (`MainWindow.xaml.cs:8997`,
  `MainWindow.UpdateDialog.cs`) → `"Core {0} disponibile"`.
- Setup window: `PlayhubSetup/MainWindow.xaml.cs:112` → title `Core Setup`.
- Windows "Installed apps": `DisplayName = "Playhub"`, `DisplayVersion`, `Publisher = "Andrea Sgarro (LoZazaMastro)"`
  → `DisplayName = "Core"` and `Publisher = "Eternal"` (`EternalRelease.DisplayName` / `MakerName`); the key path
  `Uninstall\Playhub` is unchanged.
- Diagnostics report: `Services/DiagnosticsService.cs:159` (full assembly version).

### What `mark-i` changed

- One source of truth: `Source/Shared/EternalRelease.cs` holds `Version` (`3.1.0`), `MarkName` (`Mark I`) and
  `UpdateRepository` (`M2ljay/Eternal-Core`). `Source/EternalRelease.props` reads `Version` out of that file with an
  MSBuild regex and sets `<Version>` for `Playhub.csproj` and `PlayhubSetup.csproj`. The agent's `AssemblyInfo.cs` and
  `Installer.AppVersion` use the constants directly.
- `PlayhubUpdatePolicy.Repository` maps an empty or legacy `LoZazaMastro/Playhub` setting to the fork, and leaves any
  other deliberately configured repository alone. The preview build is unchanged.
- The agent's update notice uses the same constants.
- Display: the About card and the setup window show `Mark I (3.1.0)`.
- Download and verification logic is untouched.

## 2. Other remote sources tied to upstream

"Upstream deletes" means the LoZazaMastro repository or file is removed or made private. "Changes" means upstream
keeps publishing.

| # | Source (file:line) | Fetches | If upstream deletes it | If upstream changes it | Recommendation |
| - | ------------------ | ------- | ---------------------- | ---------------------- | -------------- |
| 1 | `Playhub/Services/RemotePluginCatalogService.cs:18` `CatalogUrl` | `raw.githubusercontent.com/LoZazaMastro/Playhub/main/catalog/plugins.json` (Plugin Store catalog), every 6 h, retry 5 min (`:21-22`) | Falls back to the last good disk cache, then to the bundled copy of `catalog/plugins.json` (`Playhub.csproj` links it as `Assets/PluginCatalog/store-catalog.json`). The store keeps working, frozen at the bundled or cached revision. | Strictly validated (`:280-305`), rollback rejected (`:117-118`), but **upstream controls what the store lists**, including new install URLs for GitHub-hosted plugins. Upstream's new revisions keep flowing in. | **Keep pointing upstream while it is online** (free fixes to listings). Mirror into the fork (`catalog/` is already in the repo) when you want control, or if upstream goes quiet. Changing the URL is a one-line change plus the identical URL in the Decky plugin (#3). |
| 2 | `RemotePluginCatalogService.cs:288` | Validation rule: entries with `catalogSource: "playhub"` must have repository owner `LoZazaMastro` | n/a | n/a | Keep. If you ever publish your own plugins in a fork-hosted catalog, this rule must also accept `M2ljay`. |
| 3 | `GamingModeDeckyPlugin/src/pluginStoreCatalog.ts:2` (and the suspended `ChainFreeEngine/Plugin/src/pluginStoreCatalog.ts:2`) | Same `catalog/plugins.json`, fetched by the Big Picture plugin store | Big Picture store falls back to what it was built with or cached. | Same as #1. | Same as #1. Keep the two URLs in step. |
| 4 | `GamingModeDeckyPlugin/quick_settings/history_remote.py:13` | `raw.githubusercontent.com/LoZazaMastro/Playhub/main/catalog/on-this-day.json` ("Accade oggi" / On this day editorial), every 6 h | Last good disk cache (`editorial-catalog-v1.json`), else the built-in fallback table (`:99`). Text keeps working. | Upstream controls the daily editorial content (validated, rollback rejected). | **Keep upstream while online.** To mirror: change `:13` **and** the image-URL allowlist regex at `:112`, which is pinned to `LoZazaMastro/Playhub`, plus #5. |
| 5 | `GamingModeDeckyPlugin/src/historyImages.ts` (2,554 URLs), generated by `GamingModeDeckyPlugin/tools/build-history-images.py:59` | Images under `raw.githubusercontent.com/LoZazaMastro/Playhub/main/assets/on-this-day/v1/` | **Images break (blank) immediately**: there is no bundled copy and the browser cache is the only cache. | Upstream could replace or remove images. | This is the most fragile one. The fork already contains `assets/on-this-day` (1.2 GB) in its history, so **mirroring is only a URL swap**: regenerate `historyImages.ts` with the fork as base. Mirror if you rely on the feature. Bundling is impractical (1.2 GB). |
| 6 | `Playhub/Services/PluginCatalogService.cs:19` `Owner = "LoZazaMastro"`, `:919` | `api.github.com/users/LoZazaMastro/repos` (discovery of upstream's own plugin repos) | Silent: returns an empty list (`:933-936`), so upstream-suite discovery shows nothing new. | Upstream controls which of its repos appear. | Keep. Rate-limited, anonymous API. |
| 7 | `PluginCatalogService.cs:945`, `:1014` | `releases/latest` and `releases.atom` of each upstream plugin repo (Artwork, Metadata, Launch Curtain, Weather, Now Playing, Notifications, ThemeDeck-Windows, TrailerHero, and others; see `Playhub/Plugins/*/release-info.json` and the 14 `LoZazaMastro/*` entries in `catalog/plugins.json`) | Plugin update checks and installs for those plugins fail. The bundled installers (Metadata, Launch Curtain, Weather, CSS Loader zips in `Playhub/Plugins/`) still install offline. Already-installed plugins keep working. | Upstream plugin updates keep flowing in, which is probably what you want. | **Keep upstream.** Forking each plugin repo is a separate, larger decision. Your own Now Playing fork is a separate plugin. |
| 8 | `PlayhubSetup/build-update-preview.ps1:307` | Build-time assertion that a private Artwork candidate came from `LoZazaMastro/Playhub-Artworks` (only with `-Candidate`) | n/a, build-time only | n/a | Leave. |
| 9 | Ko-fi links (removed): upstream had a Support page and a usage-timed donation reminder linking to `ko-fi.com/lozazamastro` | n/a | n/a | n/a | Removed from Core with `SupportUsageClock` (a local usage timer, no network access); see `docs/eternal/REBRAND.md`. No telemetry or analytics endpoint exists in the code. |
| 10 | Upstream README badges and links (`README.md`) | Download and star badges for upstream | n/a | n/a | Left as is. A fork note was added at the top. |

### Not upstream (third-party infrastructure, unchanged)

- Decky Loader releases: `Services/DeckyWindowsReleaseClient.cs:21-22` (`SteamDeckHomebrew/decky-loader`). The
  official Decky 3.3.1 runtime is also bundled (`Assets/DeckyRuntime/Official-3.3.1`, hash-pinned).
- `Services/DeckyLatestArtifact.cs` is retry logic only (nightly.link run IDs); it holds no upstream URL.
- Official Decky plugin store: `RemotePluginCatalogService.Decky.cs:21-22` (`plugins.deckbrew.xyz`,
  `SteamDeckHomebrew/decky-plugin-database`).
- CSS Loader (`CssLoaderInstallService.cs:563`, `DeckThemes/SDH-CssLoader`), PawnIO (`PawnIoInstallService.cs:59`,
  compiled out), SteamGridDB, store CLIs (legendary, gogdl, bundled and hash-pinned).

### Identity constants (not URLs, intentionally unchanged)

- `Shared/ShellIdentity.cs:11` `AppId = "LoZazaMastro.Playhub"`: Windows AppUserModelID. Changing it would orphan
  taskbar pins and Start shortcuts and split notification identity. The Phase 1 rebrand kept it; only the relaunch
  display name (window property 4) and the shortcut file names became `Core`. Phase 2 list: `docs/eternal/REBRAND.md`.
- `Playhub/Services/GamingModeService.cs:597`: finds the bundled Gaming Mode Decky plugin by
  `author == "LoZazaMastro"` in its `plugin.json`. Changing either side alone breaks detection.
- `PlayhubSetup.csproj` `Company`, SPDX headers: copyright and credit. Not touched. `Installer.Publisher` (the
  Installed apps "Publisher" column, display only) is `Eternal` since the rebrand.

## 3. Build system

### Projects

There is **no `.sln`**. There are about 130 independent `.csproj` files under `Source/`, and most `*.Tests` and
`*Checks` projects are console executables (`dotnet run`), not xUnit/NUnit. The main graph:

- `Playhub/Playhub.csproj`: the app (WinUI 3, `Microsoft.WindowsAppSDK` 1.6.250205002). `BeforeTargets="PrepareForBuild"`
  hooks run `Restore;Rebuild;Publish` on `Playhub.GameSession`, `Playhub.DeckyHost`, `Playhub.XboxSession`
  (includes native pieces) and `Playhub.StoreHost`, then copy their single-file exes into the app output.
- `PlayhubSetup/PlayhubSetup.csproj`: the installer (WPF, single-file self-contained, assembly name `Playhub Setup`).
- `GamingModeAgent/GamingMode.csproj`: the Big Picture agent (WPF + ASP.NET Core minimal API).
- `GamingModeDeckyPlugin/`: the Decky plugin (TypeScript + Rollup, Python backend), built by `build-plugin.bat`.

### Installer

**A custom bootstrapper, not Inno Setup, WiX or MSI.** (`Installer/` and `OLD/` do not exist in any commit;
`git log --all -- Installer OLD` is empty.) `PlayhubSetup` is a WPF exe. The app is published self-contained, zipped,
and the zip is **appended** to the setup exe followed by an 8-byte little-endian length and the ASCII marker
`PLHB` (`Installer.cs` reads the trailer). Scripts in `Source/PlayhubSetup/`:

- `build-all.bat`: older one-shot script: checks, agent, Decky plugin, app publish, payload, stub, append →
  `Output\Playhub-Setup.exe`.
- `build-installer.bat` and `build-installer-fast.bat`: subsets of the above.
- `build-release-verified.ps1` → `build-update-preview.ps1` (PowerShell 7): **the real release path.** Runs the update
  and catalog tests, optionally a native UI review, publishes app, agent and stub, verifies dozens of pinned hashes and
  payload entries, writes `Output\Playhub-Setup.exe` and `Output\Playhub-Setup.exe.sha256`.

Needs Windows: `powershell`/`pwsh`, `Get-AuthenticodeSignature`, `robocopy`, `copy /b`, `build-plugin.bat` (Node,
`pnpm`/`npm`), WindowsAppSDK restore, `net8.0-windows` targeting.

### Release pipeline

None in the repository: no `.github/` ever existed in history. Upstream built on the author's PC and uploaded by
hand. The `mark-i` branch adds `.github/workflows/eternal-release.yml` (windows-latest), described in
`docs/eternal/README.md`.

### Linux sandbox findings (this session)

- The .NET 8 SDK installs from Ubuntu's archive (`apt install dotnet-sdk-8.0`, 8.0.134).
- **nuget.org, dot.net and the Microsoft download hosts are refused by this sandbox's egress proxy (HTTP 403).** So
  the app, setup, agent and every `net8.0-windows`, WindowsAppSDK or `FrameworkReference` project **cannot be
  restored or built here**. This is an environment limitation, not a code problem.
- The 40 plain `net8.0` projects with no package or framework references restore offline with
  `-p:NuGetAudit=false` (otherwise NuGet tries to download vulnerability data and fails). Results are in
  `docs/eternal/README.md`.
- The Decky plugin builds on Linux (`pnpm install --frozen-lockfile`, `tsc --noEmit`, `rollup -c`; the npm registry
  is reachable). Its `node --test tests/*.test.mjs` suite has 1,327 tests: 1,273 pass and 38 fail at v3.0.0. The
  failures are Windows Steam paths, `controller_porting/parent-integration.patch` (missing from the repo) and the
  Digital Stores UI suites.
- **Line-ending trap:** the release packager's SHA-256 pins on bundled text files (for example
  `Assets/DeckyRuntime/Official-3.3.1/PACKAGE-MANIFEST.json` and `Assets/SpatialAudio/Svcl/readme.txt`) match the
  **CRLF** form. The repository stores LF (`* text=auto`). A checkout without `core.autocrlf=true`, such as a Linux
  checkout or a CI checkout left at defaults, fails `build-update-preview.ps1`.
- `PlayhubUpdate.Tests` (the update-flow tests) **does not compile at upstream v3.0.0**: `Installer.cs` gained
  dependencies (`StoreLibraryUpdate`, `BundledPlayhubMaintenance`, `ShellIdentity`, `GamingModeAgentUpdate`, …) that
  the test project does not link. Upstream's release script runs it, so upstream either built it in a different
  state or the script was not re-run before 3.0.0.

## 4. Mark II changes

### Upstream #22: Home/QAM swallowed after a non-Steam game

`GamingModeDeckyPlugin/src/controlledQamIntegration.ts` replaces Big Picture's `OnHomeButtonPressed` and
`OnQuickAccessButtonPressed` and asks `steamOwnedLaunchDispatcher.ts` whether an owned (Playhub-wrapped) game should get
the press. A non-Steam `RunGame` recorded a launch hint that only the next `RunGame` cleared. After the game exited,
`activeCandidate()` stayed true, `signal()` returned `true` (swallow) and started an owned-route lookup for a game
that no longer existed; the original handler ran only if the agent proved Steam's UI was focused. Launching a native
Steam game cleared the hint, which matches the report exactly.

The hint deliberately outlives Steam's UI view of the game: Epic-protocol and other wrapped launches can leave
`MainRunningAppID` at 0 while the game runs, so "nothing running in the UI" is not proof of exit. The fix retires the
hint only on Steam's own `GameSessions.RegisterForAppLifetimeNotifications` stop event for the hinted shortcut, and
only after a start event for it was seen since the launch (a late stop from an earlier session cannot retire a new
hint). Every owned route requires Playhub's wrapper process to be alive, and Steam reports the shortcut stopped only
when that wrapper is gone, so no owned route can exist afterwards. With no hint and nothing running, the existing code
path hands the press to Steam's original handler. Tests: `tests/steamOwnedLaunchDispatcher.test.mjs` and
`tests/controlledQamIntegration.test.mjs` (the last block of each).

### Upstream #25: anti-cheat safe mode

Code: `GamingModeAgent/GamingMode.Services/AntiCheatProtection.cs` (catalogue, policy, folder probe, orchestrator;
pure, tested by `Source/AntiCheatProtection.Tests` on Linux and in CI) and `AntiCheatProtection.Windows.cs` (the
passive observer and the 750 ms loop). Wiring: `AgentHost.cs` creates the monitor before the boot mode is applied,
so an already-running protected game is seen before Gaming Mode's services start. Each paused service keeps a "wanted"
flag, so `ModeManager` can still start and stop it during a pause and the pause wins until release.

| Feature | Pause | Resume |
| ------- | ----- | ------ |
| `qos` | `GameExecutionQosController.StopAsync()` (the owner process restores the default and closes its handle); the endpoint and controller refuse new leases | nothing; the plugin renews leases as before |
| `window` | `GamingWindowFocusService` stops completely (no restyle, no forced foreground, no post-game watcher, which holds a process handle); its arm/QAM entry points refuse | restarts; brings Big Picture to the front once if only the desktop is |
| `volume` | `UnhookWindowsHookEx` (the volume watcher keeps running; it does not touch games) | the hook is reinstalled |
| `cursor` | the worker stops and `SPI_SETCURSORS` restores the cursors | restarts |

**The exact Ricochet trigger is still unverified.** Activision publishes no list of what Ricochet reacts to. The
reporter's evidence (it works after leaving Gaming Mode, in the same session) points at these four Gaming-only
features, but not at which one. The Ricochet service names (`atvi-randgrid_sr`, `atvi-randgrid_msstore`) and
`randgrid.sys` in the *Call of Duty HQ* folder come from third-party troubleshooting guides
(timesaver.gg, ugetfix.com), not from Activision.

**How to bisect on the PC.** The agent reads a config-only list, `antiCheatPausedFeatures`, in the `gaming` section
of `%APPDATA%\GamingMode\config.json` (Playhub keeps it when it rewrites the file). Empty or absent pauses all four.

1. Confirm the fix: Playhub's defaults (safe mode on), launch Call of Duty from Big Picture. Check `agent.log` for the
   "engaged" line and which signal matched.
2. Narrow it: set `"antiCheatPausedFeatures": ["volume"]`, wait a few seconds for the agent to reload it (the log
   prints the new settings), and launch the game again. Repeat with `["window"]`, `["cursor"]`, `["qos"]`.
   The feature(s) whose pause alone lets the game start are the trigger. If none does alone, try pairs.
3. Cross-check without the safe mode: turn it off and turn the matching feature off by hand where a toggle exists
   (*Nascondi il cursore*; *Finestre senza bordi* stops only the restyling, not the foreground handling). The volume
   hook and the QoS endpoint (which opens the game process before it looks at the profile) have no toggle, so step 2
   is the only clean test for them.
4. Report the result; the pause list can then shrink so the other features keep working in protected games.

If no subset helps, the trigger is outside these four. Candidates not paused today: Gaming Mode's Winlogon shell
replacement (`ShellTools.SetGamingShell`, test by switching to Gaming in-session from a Desktop boot), the live
performance HUD (PresentMon/ETW), per-game audio volume (`GameAudioVolumeService`), the native Steam overlay
brokers and Launch Curtain cover, the QAM focus guard, and `OverlayWindowTools.IsSteamForeground()`, which reads the
foreground process's identity (`NativeForegroundProcessIdentity`) and so may briefly query the game process.

### Upstream #10: Dashboard from the controller

Upstream's rule stays: the agent never reads controllers (`DashboardShortcutService.cs` explains the SDL/HIDAPI
conflict with Steam Input). The Gaming Mode page's Dashboard card has a guide to bind a Steam Guide-button chord to
the existing keyboard shortcut (`DashboardHotkey`, default `Ctrl+Alt+P`, read from the agent-only fields that the app
keeps in `GamingOptions.Extra`), and a button for `steam://controllerconfig/443510`. Valve's protocol page documents
`steam://controllerconfig/<id>`; 443510 is the chord-configuration app the plugin already reads
(`onboardingNative.tsx`). That the pair opens the chord editor is unverified on a PC; the steps cover it either way.

Follow-up, not built: the Decky plugin already receives `SteamClient.Input.RegisterForControllerInputMessages` while
Big Picture is foreground (`navigationHaptics.ts`). It could open the Dashboard on a chord there without touching the
controller itself, but only while Steam's UI is focused, and it must not consume input Steam Input also maps.

## 5. Plugin Store: the user's own forks

### The risk (Mark II as released)

The store has two front ends with independent matching, and neither knew about the user's forks.

- **Which catalogue.** The desktop app reads the bundled `catalog/plugins.json` (copied to
  `Assets/PluginCatalog/store-catalog.json`) merged with upstream's
  `raw.githubusercontent.com/LoZazaMastro/Playhub/main/catalog/plugins.json` (§2 #1; `RemotePluginCatalogService`,
  6 h refresh, last good copy cached). The Big Picture store fetches the same upstream URL and falls back to the copy
  bundled at build time (`pluginStoreData.ts`). On top of the catalogue, both look up `releases/latest` of each
  installed entry's repository (`PluginCatalogService.Releases.cs` `RefreshReleasesAsync`, `PluginStorePage.tsx`), so
  an upstream GitHub release raises the offered version before the catalogue does.
- **Desktop matching** (`PluginCatalogService.AppendUncataloguedInstalledPlugins`): a repository declared in
  `plugin.json`/`package.json` or in Playhub's `.playhub-release.json` marker is authoritative. Without one, the exact
  install folder (`installFolder`) wins, then a unique alias (name, folder, `aliases`). The match sets
  `HasUpdate = installed < offered` (`HasVersionUpdate`, SemVer: `0.33.0-win.17` is a prerelease of `0.33.0`).
- **Desktop install** (`DeckyPluginService.InstallOrUpdateAsync`): the destination is the matched installed folder;
  it is deleted, replaced with the upstream ZIP and stamped with an upstream marker. "Aggiorna tutto" runs this for
  every `HasUpdate` plugin; the app only notifies on its own (no silent plugin auto-update exists).
- **Big Picture matching** (`pluginStoreCatalog.ts` `findInstalledPlugin`): Decky's plugin name against the entry's
  name, folder and aliases; the update goes through Decky's `utilities/install_plugin`.

Result, verified with the real catalogue and the forks' real manifests:

| Fork (folder) | Matched to | Today |
| ------------- | ---------- | ----- |
| The Collector (`romm-dock`, `0.33.0-win.N`, no repository) | `Crimson3076/romm-dock` by folder | **Update offered now** on the desktop (0.33.0 > 0.33.0-win.17); any later upstream release on both front ends |
| Forge (`Nexus Mods`, `1.21.32-anygame.N`) | `RedRanger14/decky-nexus` by alias "Nexus Mods" | **Update to 1.21.40 offered now** on both front ends |
| Eternal-Assemble (`Discord`, manifests identical to upstream, 1.0.3) | `LoZazaMastro/Playhub-Discord` | Offered as soon as upstream publishes > 1.0.3 |
| Awesome Mix (declares `M2ljay/Eternal-Mix`) | nothing (own repository) | Safe; an old `Now Playing Plus` folder was also unmatched |

A fork deployed over an earlier store install of the upstream plugin can also keep the upstream
`.playhub-release.json`, whose repository and version then override the fork's own manifests.

### The fix

`Source/Shared/ProtectedPluginForks.json` lists the forks: own repository (and older names), the upstream
repositories they replace (`supersedes`), folders, Decky plugin names and optional version markers (`-win.`,
`-anygame`). A folder is a fork when its own `plugin.json`/`package.json` declare a fork repository, or when its
folder or plugin name is listed **and** its version carries the marker (an upstream RomM-Dock or decky-nexus install has
no marker and keeps its updates). The marker file is ignored for this decision.

- Desktop (`Source/Shared/PluginForkProtection.cs`, embedded resource `Playhub.ProtectedPluginForks.json`):
  `LoadCatalog` lists each installed fork as an installed entry with the fork's repository, its manifest version, no
  release ZIP and `ProtectedForkId` set; hides the catalogue entries it supersedes or whose install folder it occupies;
  skips release refreshes for it; `ApplyLatestRelease` never sets `HasUpdate` on it. `DeckyPluginService` refuses
  any install or update whose destination is a fork folder (checked again just before the delete), so "Aggiorna
  tutto", a stale view or a future code path cannot overwrite one. Uninstalling stays possible.
- Big Picture (`src/pluginStoreForks.ts`, same JSON): an installed fork, recognised by Decky name plus marker, hides
  the entries it replaces; `requestDeckyInstall` refuses such an entry before calling Decky.
- Eternal-Assemble has the same manifests as upstream Playhub-Discord, so **any** `Discord` plugin counts as the fork:
  upstream Discord updates are never offered. Declaring `M2ljay/Eternal-Assemble` in its `package.json` would make the
  rule exact; the folder rule can then be dropped.
- To add a fork, or after a rename (The Collector, Forge), edit the JSON only: both front ends and the tests read it.
  Tests fail if a `supersedes` repository disappears from `catalog/plugins.json`.

**No catalogue entries for the forks.** The catalogue validator (`RemotePluginCatalogService` `Validate`) requires a
`github.com/<repo>/releases/...` ZIP for every external entry. Eternal-Collector publishes `romm-dock.zip` and
`update.json` on a `release` branch, and the other forks have no releases, so the store cannot install them. Each fork
updates itself; the store only shows it as installed.

Tests: `Source/Playhub.CatalogIdentity.Tests` (`ForkProtectionTests.cs`; the upstream identity contracts there run
with an empty fork list), `Source/Playhub.BundledPlugin.Tests` (install guard), and
`GamingModeDeckyPlugin/tests/pluginStoreForks.test.mjs`. All three run in the release workflow.

## 6. Rebrand to Core (Phase 1)

The visible name comes from `Source/Shared/EternalRelease.cs` (`DisplayName = "Core"`, `MakerName = "Eternal"`).
`Source/EternalRelease.props` reads `DisplayName` with the same kind of regex as `Version` and sets `Product` and
`AssemblyTitle` of the app (`Core`) and the setup (`Core Setup`), so Task Manager and file properties follow it. The
localized UI text is keyed by its Italian source string, so a rename changes the key in code, in every
`Assets/Localization/*.json` and in `LocalizationService.cs` together; `LocalizationResource.Tests` fails on any
remaining product-name "Playhub" outside the deliberate list. The Decky plugin has no shared constant (its tests load
single source files, so a new import breaks their loaders); its strings say "Core" directly.

Shortcuts: Windows shows a `.lnk` file's name, so the setup writes `Core.lnk` and renames an existing owned
`Playhub.lnk` (`ShellIdentity.MoveOwnedShortcut`: only when it targets this install's `Playhub.exe` and the new name
is free). Everything an installed copy reads by name is unchanged; the full list, the reasons and the image inventory
are in `docs/eternal/REBRAND.md`.

## 7. Mark III

Code is in `GamingModeAgent/GamingMode.Services` unless noted. The pure halves are linked into
`Source/MarkIII.Tests` and run on Linux; the Windows halves (WPF, DPAPI, power events, XInput) compile only in CI.
`MarkIIIHost.cs` wires everything from `AgentHost.RunAsync`, beside Gaming Mode: every loop step, power event and
endpoint catches and logs its own failures, and no Mark III feature is on the path that starts Steam.

### Boot sequence

- Where: the existing cold-boot splash (`SplashScreenService.Show`, shown by `AgentHost` before any service exists and
  held by `SteamThemeStartupCover` until Big Picture is ready). `CreateBootWindow` replaces the logo with
  `BootSequencePlayer` (`BootSequenceVisual.cs`): a WPF `Storyboard` on a 1280x720 canvas in a `Viewbox`, reproducing
  `docs/eternal/brand/preview/index.html` (the timeline the boot sound is cut to). WPF was chosen because the splash,
  transitions and curtain already are WPF; the agent has no WebView2.
- Sound: `Assets/eternal-core-boot.wav` (the brand `.ogg` as 16-bit PCM; Windows has no built-in Ogg decoder) is an
  embedded resource, played from pinned memory with winmm `PlaySound(SND_MEMORY|SND_ASYNC)`.
- Time boxes: start waits for the TV gate at most 6 s (`MaxStartDelay`); the themed cover, once Steam is ready, waits
  for the sequence through `WaitForBootSequenceAsync`, never past 16 s after the splash appeared (`HardLimit`). The
  splash's own fail-safe deadline is unchanged.
- Skip: `BootSequenceInput` polls `XInputGetState` (xinput1_4) and `GetAsyncKeyState` every 40 ms only while the sequence
  plays; `BootSkipDetector` ignores anything already held at the start. This reads published state only and opens no
  device, unlike the SDL/HIDAPI drivers that upstream removed (`DashboardShortcutService.cs`).
- Wake: `MarkIIIHost.OnResume` uses a fresh `SplashScreenService` (the boot instance keeps the boot's foreground guard)
  and `ShowWakeSequence`, which closes itself after the sequence plus 0.6 s. `BootSequencePolicy.Decide` refuses on
  wake when a game runs, and always when anti-cheat safe mode is engaged.

### TV (LG webOS)

- `LgWebOsProtocol.cs` (pure): magic packet, MAC and input parsing, SSAP messages, replies, SSDP, and the off rule
  `ShouldTurnOff(foregroundAppId, coreInput)`: only `com.webos.app.hdmiN` for Core's input.
- `LgWebOsClient.cs`: UDP broadcast WoL (255.255.255.255 on ports 9 and 7, plus the /24 directed broadcast), SSDP for
  `urn:lge-com:service:webos-second-screen:1`, and a `ClientWebSocket` session: `wss://tv:3001` with the TV's self-signed
  certificate pinned by SHA-256 at pairing; `ws://tv:3000` only if 3001 does not answer and nothing is pinned.
- `TvControl.cs`: boot/wake (`WakeAndSwitchAsync`, retries the websocket until the budget ends), sleep/shutdown
  (`TurnOffIfShowingCoreAsync`, budget split per step so a rejected key is recognised in time), and the page's buttons.
- `TvControl.Windows.cs`: `DpapiTvPairingStore` (`tv-pairing.json`, CryptProtectData, current user) and `SendARP` for
  the MAC. The key never reaches a log line, an HTTP response or `config.json`; the test suite checks the log.
- Power events: `SystemEvents` subscribed from a pool thread (it then owns its window thread). Suspend: 1.8 s budget,
  synchronous. SessionEnding: 2.5 s. Resume is raised for `PBT_APMRESUMESUSPEND`, a wake with user presence, so an
  unattended wake timer does not turn the TV on.
- Endpoints (loopback only): `GET /tv/state`, `POST /tv/discover|pair|unpair|on|input|off`.

### Steam update hold

- Verification of `steam.cfg` on the current client: not possible from sources. Valve does not document
  `BootStrapperInhibitAll`; community sources predate the 64-bit client (December 2025). Treated as "probably works,
  verify on Core" (`Steam\logs\bootstrap_log.txt`). The design does not depend on it:
  `SteamUpdateHoldPolicy.Observe` detects a client version change while held (`package\steam_client_win64.manifest`),
  logs it and restarts the hold; the Decky health check then judges the new version.
- The app's toggle still writes or removes `steam.cfg` (`ExtraService`); its presence is the hold. The agent stores
  `heldSince`/`heldVersion` in `steam-update-hold.json`, notifies once at 30 days and runs the update job at an idle
  moment (Gaming Mode, Steam up, no game and no protected game for 10 minutes, counted from agent start too).
- `SteamUpdateJob` (pure orchestration over `ISteamUpdateHost`) and `SteamClientBackup.cs` (same folder, layout,
  marker and exclusions as `ExtraService`, checked by a test; keeps its own newest two backups). A hand-edited
  `steam.cfg` is put back as it was. `ModeManager.BeginSteamMaintenance` keeps the Desktop safety watchdog quiet while
  Steam is closed on purpose (it only acts when Explorer is also gone, i.e. the shell-free trial).
- Endpoints: `GET /steam/update/state`, `POST /steam/update/now`.

### Decky health and Core Repair

- Heartbeat: `GamingModeDeckyPlugin/src/coreHealth.ts` (frontend, every 30 s) calls `core_heartbeat` in `main.py`, which
  POSTs `/decky/heartbeat`. `DeckyHeartbeat.cs`: Big Picture up (`SteamFullscreenDetector.IsBigPictureUp`) for 90 s with
  no heartbeat, or heartbeats stale for 90 s, is `NotLoaded`; one notice per Steam process; none in Desktop Mode, when
  Decky is not installed or `deckyRequired` is off, during a game or a protected game, or after "Continue without
  plugins" for the same Steam version.
- Notice: `CoreNoticeOverlay.cs`, layered WPF window with NOACTIVATE|TRANSPARENT|TOOLWINDOW, black card, 10 s, re-checks
  "no game" every 500 ms and disappears if one starts. It reads no input (upstream's rule).
- Core Repair: `GamingMode.exe repair` (`CoreRepairWindow.cs`), handled in `Program.Main` before the agent mutex. It
  calls `POST /core/repair/run?action=` and polls `/core/repair/state`; jobs run in the agent one at a time, so closing
  the window never interrupts one. Actions that restart Steam close the window first (Steam counts it as a running
  game) and report through a notice. "Update Decky" runs `Playhub.exe decky-update <id>` (`CoreRepairDeckyCommand.cs`),
  i.e. the app's own verified installer, and waits for its receipt in `%LOCALAPPDATA%\Playhub\CoreRepair`.
- Tile: the plugin reads `/core/repair/shortcut` and adds the non-Steam shortcut once (adopting an existing "Core
  Repair"), 15 s after it loads. Decky must have worked once for the tile to exist; the desktop app's *Open Core Repair*
  button is the fallback.
- Core keeps working without Decky: `ModeManager.ApplyGamingMode` still launches Steam when Decky is missing or failed
  (unchanged; only a Steam launch failure aborts Gaming Mode).
