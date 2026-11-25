# Rebrand: Playhub to Core

Brand architecture, as with Sony and PlayStation: **Eternal** is the maker brand, **Core** is the product. The user
sees "Core", and "Core by Eternal" where a maker is named. "Eternal Core" is the name of this repository and of the
docs. Core is based on Playhub by Andrea Sgarro (LoZazaMastro), MIT licence; that credit, the copyright lines, the SPDX
headers and `LICENSE` stay.

## Phase 1 (branch `core-rebrand`): names the user sees

Text only. No image changed, and no identifier that an installed copy depends on.

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
| Support card and donation reminder ("Enjoying Playhub?", "Playhub is free and open source…", "Thank you for being part of Playhub") | It links to Andrea Sgarro's Ko-fi: the donation supports Playhub's author, not Core. Rewording it is a separate decision. |
| "Playhub plugins", the store's "Playhub" source filter and category, the plugin names Playhub Artworks, Metadata, Notifications, Surround, Discord | The upstream plugin suite. Their names come from upstream's catalogue and `plugin.json` and are matched by name. |
| CSS Loader "Playhub" profile and "Playhub (Big Image Mode)" | The theme is installed into CSS Loader under that name (`Playhub.profile`); the text matches what CSS Loader shows. |
| "Playhub.exe" in the Controlled folder access help | The file the user has to pick is still called `Playhub.exe`. |
| Splash logo option "Playhub" (app and agent `LogoChoice`) | It names the Playhub logo image, which Phase 1 does not change. |
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

## Image assets (unchanged in Phase 1)

Pixel sizes read from the files. "Used by" lists the code that references each file.

| File | Format and size | Used by |
| ---- | --------------- | ------- |
| `Source/Playhub/Assets/Brand/Playhub.ico` | ICO, 256x256 | App exe icon (`ApplicationIcon` in `Playhub.csproj`), window and taskbar icon (`MainWindow.xaml.cs`, `ShellIdentity`), Start menu and desktop shortcuts (`Installer.cs`), `PlayhubLauncher` |
| `Source/PlayhubSetup/Assets/Playhub.ico` | ICO, 256x256 | Installer exe icon (`PlayhubSetup.csproj`) |
| `Source/GamingModeAgent/app.ico` | ICO, 207x207 | Gaming Mode agent exe icon (`GamingMode.csproj`) |
| `Source/Playhub/Plugins/Gaming Mode/gaming-mode-win-x64/assets/logo.ico` | ICO, 207x207 | Gaming Mode shortcuts (`install.ps1`) |
| `Source/Playhub/Assets/Brand/Playhub Cube Icon.png` | PNG, 512x512 | Small icon (22x22) in the app's title bar (`MainWindow.xaml.cs`) |
| `Source/Playhub/Assets/Brand/cube.png` | PNG, 1024x1024 | Update dialog header (`MainWindow.UpdateDialog.cs`); required payload entry (`build-update-preview.ps1`) |
| `Source/PlayhubSetup/Assets/cube.png` | PNG, 1024x1024 | Installer window (`PlayhubSetup.csproj`) |
| `Source/Playhub/Assets/Brand/playhub-wordmark-white.png` | PNG, 500x132 | Update dialog wordmark; required payload entry |
| `Source/Playhub/Assets/Brand/PlayhubTag.png` | PNG, 450x450 | Plugin Store badge for the upstream suite's plugins (`MainWindow.xaml.cs`) |
| `Source/Playhub/Assets/Brand/base-logo.png` and `Plugins/Gaming Mode/gaming-mode-win-x64/assets/base-logo.png` | PNG, 2257x595 | Gaming Mode boot/transition splash, default "Playhub" logo choice (`SplashScreenService.cs`, agent `MainWindow.cs`, app splash picker) |
| `Source/PlayhubSetup/Assets/mascot.png` | PNG, 783x1241 | Installer and Decky repair page mascot |
| `Source/PlayhubSetup/Assets/setup-agreement.png`, `setup-done.png`, `setup-language.png` | PNG, 1254x1254 | Installer pages (XAML) |
| `Source/PlayhubSetup/Assets/setup-progress.png` | PNG, 1254x1146 | Installer progress page (XAML) |
| `Source/GamingModeDeckyPlugin/assets/playhub-logo-decky.png` (+ `.svg` source) | PNG, 450x450 | Decky plugin icon (`plugin.json`), copied to the app's assets by `build-plugin.bat` |
| `Source/GamingModeDeckyPlugin/assets/playhub-wordmark.png` | PNG, 500x132 | Quick Access header (`index.tsx`) |
| `Source/GamingModeDeckyPlugin/assets/playhub-wordmark-large.png` | PNG, 1600x371 | Big Picture onboarding (`PlayhubOnboarding.tsx`) |
| `Source/GamingModeDeckyPlugin/assets/source-badges/playhub.png` | PNG, 450x450 | Plugin Store source badge for the upstream suite (`pluginStoreAssets.ts`) |
| `Source/Playhub/Assets/Welcome/Mascots/*.png` (17 files, e.g. `final-onboarding.png` 1619x971, `card-playhub-update-v2.png` 1254x1254) | PNG | Desktop onboarding and page headers; `final-onboarding.png` is hash-checked by `build-update-preview.ps1` |
| `Source/Playhub/Assets/Extra/css-theme-preview.png` | PNG, 2400x1349 | CSS Loader profile preview (shows the Playhub theme) |
| `Source/Playhub/Assets/Support/Donation.png` | PNG, 1350x760 | Support card (upstream author) |
| `docs/images/readme-logo-card.png` 550x190, `logo.png` 760x300, `hero.png` 1255x485, `readme-*.png` | PNG | Root `README.md` (upstream README) |

Not app branding, left as is: plugin covers (`PluginCovers/`, `plugin-covers/`, the hashed `dist/assets/Playhub *.jpg`),
store and service logos, OEM splash logos (`assets/logos/`), the "On this day" images. There are no Windows tile images
(the app is unpackaged, no `Assets/*Tile*.png`) and no installer banner bitmaps (the installer is a WPF window).

The committed `Source/Playhub/Assets/GamingModeDeckyPlugin/gaming-mode/dist/assets/playhub-*.png` are build outputs of
the Decky plugin images above and change with them.

## Maker logo (Eternal), needed later

A separate Eternal logo and wordmark is needed for a boot sequence: the Eternal logo first, then Core, like a console
start-up. Nothing exists yet. Places where a maker logo could appear:

- Gaming Mode boot and mode-switch splash (agent `SplashScreenService.cs` and `ModeTransitionVisual.cs`): Eternal,
  then the Core logo, before Big Picture.
- Desktop app start (`MainWindow` first frame) and the onboarding's first and last slides.
- About card in Settings, next to "Core by Eternal".
- Installer welcome and done pages, and the installer window's corner.
- Big Picture onboarding intro (`PlayhubOnboarding.tsx`) and the Dashboard header.
- Release pages and the README header.

Formats to plan for: a transparent PNG wordmark (at least 2257x595, like `base-logo.png`, for 4K splash), a square mark
(1024x1024 PNG and a multi-size ICO 16-256) and an SVG master.
