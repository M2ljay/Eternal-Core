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
| Release asset `Playhub-Setup.exe` | Mark I/II updaters' asset regex | 3.3.0 ships `Core-Setup.exe` and keeps this name as a copy, so Mark I/II still update. |
| Update repository setting and its `LoZazaMastro/Playhub` mapping | `PlayhubUpdatePolicy`, `EternalRelease.UpstreamUpdateRepository` | 3.0 installs stop updating. |
| Namespaces `Playhub.*`, `PlayhubSetup`, XAML style keys `Playhub*Style` | Source | Nothing for users; pure churn. |
| Decky plugin name `Playhub`, author `LoZazaMastro`, folder `gaming-mode`, QAM tab `shortcut:Playhub` | `plugin.json`, `GamingModeService` (author check), `BundledPlayhubMaintenance`, `qam.ts`, `QamSettings.tsx`, `deckyHost*.ts`, `controlBackend.ts` | Decky reloads, the desktop app's detection and the saved QAM layout. |
| Other plugin IDs: Playhub Metadata, Playhub-Artworks, playhub-notifications, Playhub-Discord | Catalogue, `ProtectedPluginForks.json` | Upstream updates and fork protection. |
| Steam shortcut tag `Playhub`, draft shortcut name `Playhub - <store> - <id>` | `XboxSteamShortcut`, `storeLibraryRuntime.ts`, `circlesScreensaver.ts` | Steam collections and recovery of half-created shortcuts; the selected screensaver. |
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
| Gaming Mode transition colour | Defaults `#D7263D` (app model, agent model, agent fallback). `GamingModeService.NormalizeConfig` moves a saved non-custom `#FFCB0F` to crimson once, so the agent's config follows. Since `core-transitions` the mode-switch screens no longer use it (they are drawn in the brand palette, see below); it still tints the plain startup splash. |
| Installer (`App.xaml`, `MainWindow.xaml`) | Accent `#D7263D`, hover `#C11F35`, pressed `#A51A2D`, all with white text (4.96, 5.97, 7.54:1). Surfaces `#1A1D21`, strokes `#3A3F46`. Accent bar and "done" disc crimson with a white tick. |
| Big Picture plugin | One token module, `Source/GamingModeDeckyPlugin/src/corePalette.ts` (`CORE`, `CORE_STATUS`, `coreAlpha`), for every screen: black backgrounds, gunmetal `#1A1D21` surfaces with `#2A2F36` for raised/focused ones, steel `#C9D1D9` and hint `#9AA4AE` text, crimson `#D7263D` for selected/focused/active (rings, underlines, left bars, toggles, sliders, progress) and for errors, `#EA5A68` for crimson as text (5:1 on gunmetal), muted green `#6FA37F` for OK and amber `#C98B4A` for pending/warning/update (black text on their fills). Covered: Quick Access (Core tabs, Health, Voice, Stream mode, update night, device settings, SDL3 hint, Lossless profiles), the Dashboard (Playhub's white-fill focus and glows become gunmetal and a crimson ring), Digital Stores (`--sd-*` variables), the Plugin Store, store downloads, On this day and Home news. Steam blue (Lossless, downloads) and the green toggles are gone. `tests/corePalette.test.mjs` checks the values, the contrast and that the restyled screens hold no colour literals. |

Left as is:

- The app's surfaces: Mica, Acrylic or the WinUI dark theme supply them; tinting them gunmetal would be a redesign.
- The app's amber warning card (`BuildYellowWarning`): warning semantics, not brand.
- The Big Picture plugin's monochrome onboarding illustrations (white line art at varying opacity), the onboarding
  overlay's dark glass over Steam, the HUD's user-chosen text colours and the Ayaneo RGB preview: not palette roles.
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

- Mode-switch transitions: Core only, no Eternal, by design (branch `core-transitions`): `ModeTransitionVisual.cs` draws the boot's stars and Core's
  mark natively, powering up into Gaming Mode and down to Desktop, with "CORE" and a steel line; timing and the
  reduced-motion rule in `ModeTransitionTimeline.cs` (tested in `MarkIII.Tests/TransitionTests.cs`), the shared
  drawing in `CoreMarkDrawing.cs`. Playhub's "Switching to …" title and crimson circles are gone from them.
- Gaming Mode plain startup splash (agent `SplashScreenService.cs`, shown when the boot sequence is off): Eternal,
  then the Core logo, before Big Picture.
- Desktop app start (`MainWindow` first frame) and the onboarding's first and last slides.
- About card in Settings, next to "Core by Eternal".
- Installer welcome and done pages, and the installer window's corner.
- Big Picture onboarding intro (`PlayhubOnboarding.tsx`) and the Dashboard header.
- Release pages and the README header.

Formats to plan for: a transparent PNG wordmark (at least 2257x595, like `base-logo.png`, for 4K splash), a square mark
(1024x1024 PNG and a multi-size ICO 16-256) and an SVG master.

## Plugin logos and Library icons (Core UI 3.3.3)

Original Core artwork, made for Core and fine to commit:

| File | What | Where it shows |
| ---- | ---- | -------------- |
| `docs/eternal/brand/plugin-logos/{assemble,awesome-mix,forge,the-collector}.svg` (+ `-256.png`) | Core's tiles for the user's Eternal plugin forks: Assemble (Discord), Awesome Mix, Forge (Nexus Mods), The Collector (RomM-Dock) | Quick Access tab rail (a single-colour steel glyph derived from each), the Steam menu's Awesome Mix entry, covers on Core's store page; inlined in `Source/GamingModeDeckyPlugin/src/coreArtwork.ts` (CORE-UI.md, "Plugin logos") |
| `docs/eternal/brand/core-icons/original-*.svg` | The original drawings of the Core steel icon set: All games, Installed, VR headset, Completed trophy, PS3, Xbox and Xbox 360 pads (plain buttons, no symbols or emblems), generic pad | Fallback Library tab and collection icons; inlined in `coreArtwork.ts` |

The console icons the user prefers are derived from a personal-use icon pack and are **not** in the repository; Core
loads them at run time from `%APPDATA%\GamingMode\icons\` (CORE-UI.md, "Your own icons").
