# Rebrand: Playhub to Core

Brand architecture, as with Sony and PlayStation: **Eternal** is the maker brand, **Core** is the product. The user
sees "Core", and "Core by Eternal" where a maker is named. "Eternal Core" is the name of this repository and of the
docs. Core is based on Playhub by Andrea Sgarro (LoZazaMastro), MIT licence; that credit, the copyright lines, the SPDX
headers and `LICENSE` stay.

## Phase 1 (branch `core-rebrand`): names the user sees

Text first (this section), then images, colours and the support card (sections below). No identifier that an
installed copy depends on changed.

| Area | What changed |
| ---- | ------------ |
| Single source | `Source/Shared/EternalRelease.cs`: `DisplayName = "Core"`, `MakerName = "Eternal"`. `Source/EternalRelease.props` reads `DisplayName` into `$(EternalDisplayName)` for the file metadata. |
| App | Window title, About card (`Core Mark II (3.2.0)`, then "Core by Eternal. Based on Playhub by Andrea Sgarro (LoZazaMastro), MIT licence. © 2026 Andrea Sgarro (LoZazaMastro)." in 12 languages), update dialog and its screen-reader names, every message, error and onboarding string that named the app. `Product` and `AssemblyTitle` of `Playhub.exe` are `Core` (Task Manager and file properties). |
| Localization | All 12 languages (`Assets/Localization/*.json` and the tables in `LocalizationService.cs`): Italian source keys and translations together. "Core" is a proper name and stays untranslated; Korean particles follow the vowel ending (Core를, Core가). |
| Installer | Window title and texts (`Core Setup`, "Install Core", …) in 12 languages; `Product`/`AssemblyTitle` `Core Setup`; Installed apps `DisplayName = Core`, `Publisher = Eternal`. |
| Shortcuts | Start menu and desktop shortcuts are `Core.lnk` (a shortcut's file name is the name Windows shows). An update renames the old `Playhub.lnk` only if it launches this install's `Playhub.exe` and `Core.lnk` does not exist yet, so arguments, hotkey and taskbar pins inside it are kept (`ShellIdentity.MoveOwnedShortcut`). Uninstall removes both names. Shortcut description: `Core`. |
| Taskbar | The window's relaunch display name (`PKEY_AppUserModel_RelaunchDisplayNameResource`) is `Core`. The AppUserModelID is unchanged. The app sends no Windows toast notifications, so no AUMID display name needs registering. |
| Big Picture (Decky plugin) | Toast titles, the Dashboard button and labels ("Core Dashboard"), Quick Access labels, onboarding, update notice ("Core 3.3.0 is available. Open Core on your PC to update."), Quick Settings title, Game Settings menu ("Core Game Settings"), controller profiles title, error texts, image alt texts. The committed copy in `Source/Playhub/Assets/GamingModeDeckyPlugin/gaming-mode` was rebuilt (`dist/index.js`, two Python files). |
| Agent | The Windows power-request reason (`powercfg /requests`): "Core: finishing a Steam download". |
| README | The fork note at the top of the root `README.md`. The upstream README below it is unchanged. |

Guards: `LocalizationResource.Tests` fails if any language still calls the app Playhub outside the list below, and
checks the About credit in every language. `EternalRelease.Tests` pins `DisplayName`, `MakerName` and the props
regex. `ShellIdentityChecks` (Windows only, not in CI) covers the shortcut rename.

## Text that still says Playhub, on purpose

| Text | Why |
| ---- | --- |
| About credit, `README.md`, licence and copyright lines | Credit to the upstream project and author. |
| "Playhub plugins", the store's "Playhub" source filter and category, the plugin names Playhub Artworks, Metadata, Notifications, Surround, Discord | The upstream plugin suite. Their names come from upstream's catalogue and `plugin.json` and are matched by name. |
| Log lines, thread names, exception messages that only reach logs | Not shown in the UI; renaming them would break log searches. |
| Decky `plugin.json` description | Only shown by a Decky store, which does not list this bundled plugin. |
| `ChainFreeEngine/Plugin` | Suspended plugin, not shipped. |

## Phase 2 (branch `core-names-phase2`): file and folder names

Rule chosen: rename what the user can see or click (screen text, file and folder names); keep hidden identifiers.
The names and the pure rules live in `Source/Shared/CoreNames.cs` (tests: `Source/CoreNames.Tests`, Linux and CI-free;
plugin: `tests/deckyPluginName.test.mjs`, `tests/test_core_names.py`).

| Item | Now | Migration rule |
| ---- | --- | -------------- |
| App executable | `Core.exe` (`Core.dll`, `Core.pri`; `AssemblyName` only, namespaces stay `Playhub`) | Setup stops a running `Playhub.exe` or `Core.exe` (a `Core` process only when it runs from this install folder), then deletes `Playhub.exe`, `.dll`, `.pri`, `.deps.json`, `.runtimeconfig.json` once their `Core.*` successor is installed. Start, desktop and startup shortcuts and taskbar pins that launch this install's `Playhub.exe` are pointed at `Core.exe` (arguments, hotkey and the pin kept; AppUserModelID unchanged), then `Playhub.lnk` is renamed `Core.lnk` as in phase 1. The release packager requires `Core.exe`, `Core.dll` and `Core.pri` (or `resources.pri`) and refuses Playhub-named copies. |
| Decky lifetime host | `Core-DeckyHost.exe` | The startup script (`homebrew\services\Playhub-StartDecky.ps1`) and `DeckyStartupGuard.RuntimeHostPath` look for `Core-DeckyHost.exe`, then `Playhub-DeckyHost.exe` (a runtime copied by an older version). The app refreshes the script before it moves the runtime pointer. |
| Store host | `Core.StoreHost.exe` (`StoreLibrary\Core.StoreHost.*`) | Setup's quiesce, the plugin and the app accept either name; old `StoreLibrary\Playhub.StoreHost.*` files are deleted after the update. The store manifest pins `Core.exe` as the app client; the store host still accepts `Playhub.exe` from an older manifest. |
| Uninstaller copy | `unins-core.exe` | Written by setup; `UninstallString` points at it; `unins-playhub.exe` is deleted. |
| Install folder | New installs: `%LOCALAPPDATA%\Programs\Core`. Existing installs: **stay where they are** | See "Not moved" below. Every lookup from outside the install (agent, plugin, support report, UWP session association, shell-free trial, Chain Free Engine) tries the folder registered in Installed apps, then `Programs\Core`, then `Programs\Playhub`, and `Core.exe` before `Playhub.exe`. Setup's folder picker adds a `Core` subfolder to a generic folder. |
| Data folders `%APPDATA%\Playhub`, `%LOCALAPPDATA%\Playhub` | `%APPDATA%\Core`, `%LOCALAPPDATA%\Core` | One-time copy, done by setup (app and store host stopped) or else by the app on its next start. The copy is built in `<parent>\.Core-migration`, which already holds the marker `.core-migrated.json`, then renamed, so `Core` is either absent or complete. Until the marker exists every component (app, setup, agent, store host, Decky host, session helpers, plugin Python, helper scripts) keeps using `Playhub`, so mixed versions never split their data. **Copied:** everything, caches included (imported games may point at a cached cover by path). **Moved:** `backups` (Steam artwork and Steam client backups, gigabytes, found by folder scan). **Skipped:** `downloads` (holds the running setup), `DeckyRuntime` (scratch), `DiagnosticsRequests`, `StoreLibrary\maintenance-v1.json` (per-update lease). A failed copy of anything outside `logs`/`diagnostics` aborts the attempt (retried next start; Playhub stays in use). A `Core` folder without the marker belongs to someone else: it is left alone and Playhub stays in use. The old folder stays as the backup (minus `backups`); uninstall with "remove data" deletes both. |
| Decky plugin name | `Core` (`plugin.json`, `definePlugin`; Decky serves it at `/plugins/Core/`) | Every match on Decky's plugin name accepts `Core` and `Playhub` and reloads under the name Decky has loaded. Decky's own `pluginOrder` and `hiddenPlugins` settings are renamed once, keeping Core where Playhub was. The folder stays `gaming-mode`, so Decky's per-plugin settings, data and logs are unaffected. |
| Controlled folder access help | "Add Core.exe" (12 languages) | `LocalizationResource.Tests` no longer allows `Playhub.exe`. |

### Not moved: existing install folders

Moving `Programs\Playhub` would break the Steam library: every shortcut Core created for an imported Xbox, Epic, GOG
or Amazon game stores the absolute path of `Playhub.GameSession.exe` (and Xbox ones `Playhub.XboxSession.exe`), the
store registry (`steam-library-v1.json`) proves ownership by that exact path, and the store host authorises its
clients by image path. Steam derives a shortcut's identity (artwork, collections) from the exe path, so rewriting
the shortcuts is a separate, PC-tested job (the plugin could do it with `SteamClient.Apps.SetShortcutExe`). A
directory junction from the old path was considered and rejected: process image paths reported through a junction
are inconsistent, and half the comparisons above would fail without a Windows test. So: new installs use
`Programs\Core`; existing ones are updated in place and only their files are renamed.

## Identifiers kept

Hidden, or renaming them would break something for no visible gain.

| Identifier | Where | Why kept |
| ---------- | ----- | -------- |
| `Playhub.GameSession.exe`, `Playhub.XboxSession.exe` | Steam shortcuts of imported games | Visible in Task Manager while a game runs, but Steam stores their absolute path in each shortcut (see above). Renaming needs the shortcut migration. |
| Existing install folder `Programs\Playhub` | Installed apps `InstallLocation` | See above. |
| `PlayhubSteamInputReader.exe`, `PlayhubWindowContentRead.exe`, `Playhub*.dll` natives | Private candidate builds only | Not in an ordinary release; native binaries pinned by hash. |
| `Playhub Setup.exe` | Build output of `PlayhubSetup` | Never shipped under that name: the release is `Core-Setup.exe`. |
| Release asset `Playhub-Setup.exe` | Mark I/II updaters' asset regex | Still published as a copy. |
| Uninstall key `HKCU\…\Uninstall\Playhub` | `Installer`, agent, plugin | Hidden; renaming orphans the Installed apps entry. Its values (`InstallLocation`, `DisplayIcon`, `UninstallString`) are rewritten on every install. |
| AppUserModelID `LoZazaMastro.Playhub` | `ShellIdentity.AppId` | Keeps taskbar pins and Start entries attached. |
| Update repository setting and its `LoZazaMastro/Playhub` mapping | `PlayhubUpdatePolicy` | 3.0 installs stop updating otherwise. |
| Namespaces `Playhub.*`, `PlayhubSetup`, XAML keys, resource names | Source | Hidden. |
| Decky plugin folder `gaming-mode`, author `LoZazaMastro`, QAM bridge key and layout entry `Playhub`, tab key `shortcut:Playhub` | Plugin, `GamingModeService` | Stored keys, never shown. |
| Other plugin IDs (Playhub Metadata, Playhub-Artworks, playhub-notifications, Playhub-Discord) | Catalogue, `ProtectedPluginForks.json` | Upstream plugin names. |
| Steam shortcut tag `Playhub`, draft shortcut name `Playhub - <store> - <id>` | `XboxSteamShortcut`, `storeLibraryRuntime.ts` | Persisted in Steam; recovery of half-created shortcuts. |
| Startup entries `Playhub Desktop Safety`, `Playhub Focus Rescue`, `Playhub Xbox Game Bar` | `MainWindow.IsPlayhubManagedStartupApp` | Existing entries must stay recognised. |
| Mutexes, pipes, events, header `X-Playhub-Shell-Token` | Agent, app, plugin | Mixed-version components must keep talking during an update. |
| Assembly metadata keys `Playhub*Sha256`, `PlayhubControllerDriverRequired` | csproj, `build-update-preview.ps1` | Hidden. |
| `playhub.*` storage keys, `.playhub-*` marker files, `Playhub-Report-*`, `PlayhubSteamArtwork-*`, `PlayhubSteamClient-*` backups, `Playhub-StartDecky.ps1`, `Playhub-Decky-startup.log` | App, plugin, agent, `homebrew\services` | Persisted; existing ones must still be found. |
| `Company` metadata `Andrea Sgarro (LoZazaMastro)` | csproj | Upstream credit. |
| Thread and log names | Agent, plugin | Only log searches. |

## Images (done)

Every Playhub logo, icon and splash image the app, setup, agent and Big Picture plugin show is now Core's. File names,
pixel sizes and formats are unchanged, so no reference moved. Masters: [brand/](brand/) (`PALETTE.md`); the rasters
were rendered from them by `brand/tools/render_brand.py` (rsvg-convert; "CORE" outlined from Inter Medium with
fontTools, so no viewer needs Inter). `brand/core-wordmark.svg` is the outlined wordmark master.

| File | Format and size | Now | Used by |
| ---- | --------------- | --- | ------- |
| `Source/Playhub/Assets/Brand/Playhub.ico`, `Source/PlayhubSetup/Assets/Playhub.ico`, `Source/GamingModeAgent/app.ico`, `Plugins/Gaming Mode/gaming-mode-win-x64/assets/logo.ico` | ICO 16, 24, 32, 48, 64, 128, 256 (was one 256 or 207 frame) | Core mark on the gunmetal tile; 16-32 px use a simplified ring (thicker, no ticks) | App, setup and agent exe icons, window/taskbar, Start and desktop shortcuts, `PlayhubLauncher`, Gaming Mode shortcuts |
| `Source/Playhub/Assets/Brand/Playhub Cube Icon.png` | PNG 512x512 | Tiled mark | Title-bar icon |
| `Source/Playhub/Assets/Brand/cube.png`, `Source/PlayhubSetup/Assets/cube.png` | PNG 1024x1024 | Tiled mark | Update dialog header; setup resource; required payload entry |
| `Source/Playhub/Assets/Brand/playhub-wordmark-white.png` | PNG 500x132 | Ring + "CORE", near-white `#F4F7FA` | Update dialog; required payload entry |
| `Source/Playhub/Assets/Brand/base-logo.png`, `Plugins/Gaming Mode/gaming-mode-win-x64/assets/base-logo.png`, `Source/PlayhubSetup/Assets/wordmark.png` | PNG 2257x595 | Ring + "CORE", chrome gradient | Gaming Mode boot/transition splash (default logo choice, now labelled "Core"); installer side panel |
| `Source/Playhub/Assets/Brand/PlayhubTag.png` | PNG 450x450 | Mark alone (drawn at 26 px on a gunmetal disc) | Store badge for the upstream suite's plugins |
| `Source/GamingModeDeckyPlugin/assets/playhub-logo-decky.png` (+ `.svg`, + the copy in `Assets/GamingModeDeckyPlugin/gaming-mode/assets`) | PNG 450x450, SVG | Mark alone | Decky plugin icon |
| `Source/GamingModeDeckyPlugin/assets/playhub-wordmark.png` | PNG 500x132 | Ring + "CORE", near-white | Quick Access header |
| `Source/GamingModeDeckyPlugin/assets/playhub-wordmark-large.png` | PNG 1600x371 | Ring + "CORE", chrome | Big Picture onboarding |
| `Source/GamingModeDeckyPlugin/assets/source-badges/playhub.png` | PNG 450x450 | Tiled mark | Big Picture store source badge for the upstream suite |

The committed bundle (`Assets/GamingModeDeckyPlugin/gaming-mode/dist`) was rebuilt: `index.js` and the three hashed
`playhub-*.png` outputs (old hashes removed). Release packaging pins no image by a literal hash:
`build-update-preview.ps1` compares `final-onboarding.png` and the bundle with their own source files, so nothing
needed re-pinning.

Not changed, on purpose:

| Image | Why |
| ----- | --- |
| `Source/Playhub/Assets/Welcome/Mascots/*.png` (17), `Assets/Extra/decky-repair-mascot.png` | Character illustrations, not logos; none shows the Playhub name or "p" mark. Two (`plugin-store-grid-v2`, the installer's unused `mascot.png`) hold a small yellow cube that echoes the old logo. Replacing the mascots is an art decision, not a palette swap. |
| `Source/PlayhubSetup/Assets/mascot.png`, `setup-agreement.png`, `setup-done.png`, `setup-language.png`, `setup-progress.png`, `setup-ready.png` | Not referenced by the installer (only `wordmark.png` and `cube.png` are resources); leftovers from upstream. |
| `Source/Playhub/Assets/Extra/css-theme-preview.png` | Preview of the CSS Loader "Playhub" theme, which is unchanged. |
| `docs/images/*` | Upstream README. |
| `Source/ChainFreeEngine/Plugin/assets/*` | Suspended plugin, not shipped. |
| Plugin covers, store and service logos, OEM splash logos (`assets/logos/`), "On this day" images, `DeckyStoreBadge.png` | Not app branding. |

The four Eternal plugin icons (`brand/icons/`) are not placed: this repository bundles no icon for The Collector,
Awesome Mix, Forge or Assemble.

## Colours (done)

A palette swap from Playhub's yellow `#FFCB0F` to Core's crimson `#D7263D` with gunmetal `#1A1D21` surfaces and
`#3A3F46` strokes. Contrast (WCAG 2.1): white on crimson 4.96:1; crimson as text on gunmetal is only 3.4:1, so text
uses lighter tints.

| Area | Change |
| ---- | ------ |
| App accent (`PlayhubSettings.AccentColor`, `AccentPalette`) | Default and first swatch `#D7263D`. A saved `#FFCB0F` (no longer offered) moves to crimson once, like the older `#4CC2FF`/`#FFB454` defaults. White text on the accent (`NeedsLightForeground`). Hover on a dark accent now darkens (`AccentHover`; lightening put white text at 4.05:1). Accent-coloured text (release-notes "Novità" and version) uses the Light2 tint `#E67887`, 5.96:1 on gunmetal (`AccentText`). Icons and progress keep the accent (3:1 for graphics). |
| Store suite badge (app) | Gunmetal metal disc instead of the gold one; label gradient `#D7263D` to `#7A1220` (white text). |
| Gaming Mode transition colour | Defaults `#D7263D` (app model, agent model, agent fallback). `GamingModeService.NormalizeConfig` moves a saved non-custom `#FFCB0F` to crimson once, so the agent's config follows. |
| Installer (`App.xaml`, `MainWindow.xaml`) | Accent `#D7263D`, hover `#C11F35`, pressed `#A51A2D`, all with white text (4.96, 5.97, 7.54:1). Surfaces `#1A1D21`, strokes `#3A3F46`. Accent bar and "done" disc crimson with a white tick. |
| Big Picture plugin | Control Center tab indicator crimson; the store's suite badge tint crimson. Digital Stores: `--sd-accent: #d7263d` for fills (focused tab with white text, progress), and `--sd-accent-line: #ff5a6e` for outlines, icons, caret and the active-tab underline on its grey surfaces (3.96 to 6.6:1, where crimson would be 2.4 to 3:1). |

Left as is:

- The app's surfaces: Mica, Acrylic or the WinUI dark theme supply them; tinting them gunmetal would be a redesign.
- The amber warning card (`BuildYellowWarning`) and the plugin's yellow "experimental" badge: warning semantics, not brand.
- "On this day" (`DailyHistory.tsx`): its sepia and gold editorial look.
- The agent's standalone settings window (`GamingMode/MainWindow.cs`, `AppBrushes.Yellow #fcba03` with ink text; shown
  only when the agent exe runs without arguments): swapping the fill to crimson would need its text inverted (ink on
  crimson is 3.8:1), which is a redesign.
- The installer window's translucent black backdrop (`#9E121316`).
- CSS Loader themes (`Assets/CssLoaderThemes/themes.zip`): third-party themes.

## Support card (removed)

The Support page and the usage-timed "Enjoying Playhub?" reminder linked to Andrea Sgarro's Ko-fi. Both are removed,
with their navigation item, `SupportUsageClock`, the saved `SupportReminderUsageSeconds` (an old value in
`settings.json` is ignored), `Assets/Support/Donation.png`, the `Playhub.SupportUsage.Tests` project, the UI-review
hooks and their strings in the code table and all 12 language files. `LocalizationResource.Tests` checks the strings
stay gone. The About credit, copyright lines and MIT licence are unchanged. The Big Picture plugin and the installer
had no support or donation entry.

## Maker logo (Eternal): placements still open

An Eternal logo and wordmark exist as masters (`brand/eternal-logo.svg`, `brand/eternal-wake.svg`). Since Mark III the
Gaming Mode boot sequence (agent `BootSequenceVisual.cs`) draws `eternal-wake.svg` and then `core-logo-wake.svg` natively
in WPF, following `brand/preview/index.html`, with `brand/sound/eternal-core-boot.ogg` (embedded as WAV). Other places
where a maker logo could appear:

- Gaming Mode boot and mode-switch splash (agent `SplashScreenService.cs` and `ModeTransitionVisual.cs`): Eternal,
  then the Core logo, before Big Picture.
- Desktop app start (`MainWindow` first frame) and the onboarding's first and last slides.
- About card in Settings, next to "Core by Eternal".
- Installer welcome and done pages, and the installer window's corner.
- Big Picture onboarding intro (`PlayhubOnboarding.tsx`) and the Dashboard header.
- Release pages and the README header.

Formats to plan for: a transparent PNG wordmark (at least 2257x595, like `base-logo.png`, for 4K splash), a square mark
(1024x1024 PNG and a multi-size ICO 16-256) and an SVG master.
