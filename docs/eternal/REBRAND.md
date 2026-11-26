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
| CSS Loader "Playhub" profile and "Playhub (Big Image Mode)" | The theme is installed into CSS Loader under that name (`Playhub.profile`); the text matches what CSS Loader shows. |
| "Playhub.exe" in the Controlled folder access help | The file the user has to pick is still called `Playhub.exe`. |
| Log lines, thread names, exception messages that only reach logs | Not shown in the UI; renaming them would break log searches. |
| Decky `plugin.json` description | Only shown by a Decky store, which does not list this bundled plugin. |
| `ChainFreeEngine/Plugin` | Suspended plugin, not shipped. |

## Identifiers kept (Phase 2 candidates)

Each one is read by an installed copy, by Windows or by another component. Renaming it needs a migration.

| Identifier | Where | What breaks if renamed alone |
| ---------- | ----- | ---------------------------- |
| Install folder `%LOCALAPPDATA%\Programs\Playhub` | `Installer.DefaultInstallDir`, agent fallbacks (`ChainFreeEngineService`, `UwpSteamSessionAssociation`, `ShellFreeOverlayTrialPolicy`), `support_report.py` | Updates install a second copy; the agent and plugin cannot find the app. |
| Uninstall key `HKCU\…\Uninstall\Playhub` | `Installer.UninstallKey`, agent, `support_report.py` | A second Installed apps entry; the old one is orphaned. |
| Data folders `%APPDATA%\Playhub`, `%LOCALAPPDATA%\Playhub` (settings, logs, caches, `CurtainDisplay`, `HudDisplay`, `StoreLibrary`) | `AppPaths`, agent, setup | Settings, library state and logs are lost. |
| AppUserModelID `LoZazaMastro.Playhub` | `ShellIdentity.AppId` | Taskbar pins and Start entries split from the running app. |
| Executables and assemblies: `Playhub.exe`, `Playhub Setup.exe`, `Playhub.GameSession.exe`, `Playhub.XboxSession.exe`, `Playhub.StoreHost.exe`, `Playhub-DeckyHost.exe`, `Playhub*.dll` | csproj files, process-name checks (`Installer.StopRunningPlayhub`, anti-cheat exclusion list), release script pins | Process detection, shortcut ownership (`ShellIdentity.IsMainTarget` requires `Playhub.exe`), payload hash pins. |
| Release asset `Playhub-Setup.exe` | `PlayhubUpdateService` asset regex, workflow | Installed copies stop finding updates. |
| Update repository setting and its `LoZazaMastro/Playhub` mapping | `PlayhubUpdatePolicy`, `EternalRelease.UpstreamUpdateRepository` | 3.0 installs stop updating. |
| Namespaces `Playhub.*`, `PlayhubSetup`, XAML style keys `Playhub*Style` | Source | Nothing for users; pure churn. |
| Decky plugin name `Playhub`, author `LoZazaMastro`, folder `gaming-mode`, QAM tab `shortcut:Playhub` | `plugin.json`, `GamingModeService` (author check), `BundledPlayhubMaintenance`, `qam.ts`, `QamSettings.tsx`, `deckyHost*.ts`, `controlBackend.ts` | Decky reloads, the desktop app's detection and the saved QAM layout. |
| Other plugin IDs: Playhub Metadata, Playhub-Artworks, playhub-notifications, Playhub-Discord | Catalogue, `ProtectedPluginForks.json` | Upstream updates and fork protection. |
| CSS Loader profile `Playhub`, `Playhub (Big Image Mode)` | `ExtraService`, `Assets/CssLoaderThemes` | The user's enabled theme disappears. |
| Steam shortcut tag `Playhub`, draft shortcut name `Playhub - <store> - <id>`, screensaver id `Playhub Circles` | `XboxSteamShortcut`, `storeLibraryRuntime.ts`, `circlesScreensaver.ts` | Steam collections and recovery of half-created shortcuts; the selected screensaver. |
| Startup entries `Playhub Desktop Safety`, `Playhub Focus Rescue`, `Playhub Xbox Game Bar` | `MainWindow.IsPlayhubManagedStartupApp`, Gaming Mode helpers | Existing entries are no longer recognised as owned. |
| Mutexes, pipes, events: `Global\Playhub.Decky.Start`, `Local\Playhub.DeckyHost.*`, `Playhub.XboxCompanion.*`, `Playhub.Win32Session.*`, `Global\Playhub.ControllerDriverDependency.1`, `Local\PlayhubGamingModeJsonStore`, header `X-Playhub-Shell-Token` | Agent, app, plugin | Mixed-version components stop talking during an update. |
| Assembly metadata keys `Playhub*Sha256`, `PlayhubControllerDriverRequired` | csproj, `build-update-preview.ps1` | The release build refuses the payload. |
| Persisted keys and file names: `playhub.*` / `playhub-*` storage keys and element IDs in the plugin, `Playhub-Report-*`, `PlayhubSteamArtwork-*` backups, `Playhub-StartDecky.ps1` | App, plugin | Saved preferences and existing backups are not found. |
| `Company` file metadata `Andrea Sgarro (LoZazaMastro)`, AppUserModelID owner | csproj | Kept as upstream credit; Phase 2 decides. |
| Thread and log names (`Playhub Dashboard …`, `[Playhub]`) | Agent, plugin | Only log searches. |

A Phase 2 migration would, per item: write the new name, keep reading the old one, move user data once with a marker
file, and keep the old update and asset names working for at least one release.

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
