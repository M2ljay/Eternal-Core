# Decky 4 and the new Steam client

## Why this matters

- Stable Steam (2026) builds its UI with webpack. Decky Loader 3.x, which Core bundles
  (`Source/Playhub/Assets/DeckyRuntime/Official-3.3.1/`, `BundledDeckyWindowsRelease.cs`), finds React through
  `window.webpackChunksteamui`.
- The Steam beta of 18–21 November 2026 loads its UI as ES modules and renamed nearly all CSS classes. Decky 3.x
  does not load there.
- Decky 4.0.0-pre1 (pre-release only) loads on that beta, but not on today's stable Steam.

So the Decky generation must match the Steam client. There is one compatibility rule, `DeckySteamPairing.Judge`
(`Source/Shared/DeckySteamPairing.cs`): the client kind a Decky supports against the kind a Steam client is, with
"can't tell" when either is unknown. Which kind a Decky version supports comes from the shipped file
`Source/Shared/SteamCompatibility.json`, with `RequiredClient` (3.x webpack, 4.x and later ES modules) as the
built-in fallback.

To tell the clients apart, `SteamClientKindProbe` asks the running Steam over the CEF debug port Core already
enables: `GET http://127.0.0.1:8080/json`, the `SharedJSContext` page, one `Runtime.evaluate` of
`typeof window.webpackChunksteamui`. `"object"` means webpack and `"undefined"` means ES modules. Any failure gives
`Unknown` with a reason for the log, and it never throws.

## What Core does now (Decky 3.3.1 bundled, no Decky 4)

| Piece | Behaviour |
| ----- | --------- |
| Steam updates (agent, [STEAM-UPDATES.md](STEAM-UPDATES.md)) | Each pending Steam version soaks 10 days. Then the release gate checks it against `SteamCompatibility.json` and the probe. Anything the file does not know, including every client built after 1 November 2026, is "can't tell": the update is postponed and Core Health says so. A real update gets a backup, a smoke test and an automatic rollback. |
| Decky install guard (`DeckyInstallerService` → `DeckyWindowsReleaseClient.RequireSteamPairing`) | Covers Install, Update, a selected release and Core Repair's "Update Decky" (`CoreRepairDeckyCommand`). Before anything is downloaded or stopped, it refuses 4.x on webpack Steam and 3.x on ES-module Steam, and shows a translated reason. If the probe gives `Unknown`, the install goes ahead as before, with a `Diag` line saying so. |
| Core's Big Picture plugin (`Source/GamingModeDeckyPlugin`) | `src/decky.ts` hands every file a `DFL` whose `findModuleExport`, `findModuleByExport`, `findModule` and `findModuleChild` fall back to `findVar`, `findModuleScopeByVar`, `findScope` and module scopes when the running loader offers them (Decky 4), as Decky-Metadata v0.6.7 does (`src/steamModules.ts`, tested in `tests/steamModules.test.mjs`). `[class*="X"]` selectors in the stylesheets are rewritten to `:is([class*="X"], .hashed…)` from Steam's class map, through the optional `DFL.coreClassSelectors`. The Starfield screensaver's minified export names (`b3`, `rV`) also get lookups by what the objects do. Every lookup fails soft: if nothing is found, the feature stays off. The plugin stays built against `@decky/ui` 4, because version 5 alone hard-codes the class names of one Steam build (11255874) and breaks stable Steam. |

Not yet converted, and harmless if they miss: the `[class*=]` list in `onboardingAnchors.ts` (it only decides when
onboarding may show) and the top-bar clock selectors in `main.py`.

## The day Decky 4.0 ships as stable

### Release order

1. **Plugins first.** Release Core's Big Picture plugin and the user's forks (Eternal-Mix, Eternal-Collector,
   decky-nexus, Eternal-Assemble) with lookups that work on both loaders. Check each one on the beta client with
   Decky 4 and on stable with Decky 3.
2. **Core with Decky 4 bundled and a `SteamCompatibility.json` entry.** In the same release:
   - `bundledDecky: "4.0.0"` and an exact `"4.0.0": "modules"` entry;
   - a `steamClients` range with `"client": "modules"` that covers the ES-module client versions that were checked on
     a real PC (the build numbers from `Steam\package\steam_client_win64.manifest`), and a `webpack` range for every
     webpack client that was checked.
   The install guard keeps Decky 3 on webpack PCs (see "Keep both Decky packages").
3. **Then the gate opens.** On a PC with Decky 4 installed, a pending ES-module client is now `ok`. Each version
   still soaks 10 days, updates on an update night and gets the smoke test and rollback. A PC still on Decky 3
   keeps getting `pending-unsupported` until Decky 4 is installed (Core Repair's "Update Decky", or the agent, see
   below).

### Files, hashes and constants to change

- `Source/Playhub/Assets/DeckyRuntime/Official-4.0.0/`: the unmodified official release (`PluginLoader.exe`,
  `PluginLoader_noconsole.exe`, `LICENSE`, `NOTICE.txt`, `SOURCE.json`, `release-metadata.json`,
  `PACKAGE-MANIFEST.json`, `sources/Decky-v4.0.0-Source.zip`). Remember the CRLF rule for pinned text files and the
  LF exception for `LICENSE` in `.gitattributes` (copy that line for the new folder).
- `Source/Playhub/Services/BundledDeckyWindowsRelease.cs`: `UpstreamTag`, `BuildVersion`, `RelativeRoot`,
  `SourceName`, `SourceDigest`, `ConsoleDigest`, `BackgroundDigest`, the three sizes (`466_542`, `15_631_955`,
  `15_632_613`, each used twice), `UpstreamVersion` and the release date (`2026-10-27 04:26:19Z`). Decide what to do
  with the retired-pair digests. 3.3.1 becomes the webpack fallback, not the offline default.
- `Source/Shared/SteamCompatibility.json`: as in step 2 of the release order. `MarkIII.Tests` fails if
  `bundledDecky` differs from `BuildVersion`, or if a `decky` entry disagrees with `RequiredClient`.
- `Source/Playhub/Playhub.csproj`: the `Content Remove`/`Content Include` lines for
  `Assets\DeckyRuntime\Official-3.3.1` (lines 77–79).
- `Source/PlayhubSetup/build-update-preview.ps1`:
  - `Get-OfficialDeckyFiles`: the folder, the SHA-256 of `PACKAGE-MANIFEST.json`
    (`9a0cf680ce61631a2d5dc711e34d77dcfd179ac7a385874679d443067a4b4ec7` today), the `version`/`upstreamTag` check
    (`3.3.1`/`v3.3.1`) and the file count of 7;
  - the two `PluginLoader*.exe` paths around line 576;
  - `$officialDeckyRoot` and the `Assets/DeckyRuntime/Official-3.3.1/` prefix around line 753.
- Tests: `Playhub.BundledDecky.Tests` (`GetAsync("v3.3.1")`, sizes and digests), `Playhub.DeckyRepair.Tests` (tags
  in its fake release lists) and `MarkIII.Tests/DeckyTransitionTests.cs` (the shipped file's expectations: today it
  asserts the bundled Decky is for webpack Steam and that a client built after the cut-off is "can't tell").
- **Keep both Decky packages for one Mark.** A Core that bundles only Decky 4 cannot reinstall Decky on a PC whose
  Steam is still webpack. Offer 4.x when the probe says ES modules and 3.3.1 when it says webpack.
- Agent: after a Steam update moves the PC to the ES-module client, run "update-decky", so the swap from Decky 3 to 4
  does not wait for the user to open Core Repair. (With the gate, this only happens through a manual override.)
- Plugin: once every PC is on Decky 4, the webpack-only branches (the chunk read in `onboardingNative.tsx`, the
  `b3`/`rV` lookups) can go. Keep the fallbacks until then.

### Known gap: Install with a 4.x "latest" on webpack Steam

`DeckyWindowsReleaseClient.GetAsync(null)` takes GitHub's latest stable release. Once that is 4.0, Install or Update
on a webpack Steam is refused, with a reason, and nothing changes. That is safe but unhelpful. When Decky 4 is
bundled, choose the newest release that matches the probed client instead: `ListCompatibleAsync` already lists them
in order.

## If Steam stable updates first

This covers Valve shipping the ES-module client to stable before Core bundles Decky 4.

- **Blocked PCs:** the new version soaks, then the gate says "can't tell" (its build is after the cut-off) and the
  update is postponed every night. Core Health shows "Steam update: Postponed", amber after 21 days past the soak.
  Nothing breaks. If the maintainer confirms the version is ES modules and ships a file saying so, the reason becomes
  `pending-unsupported`.
- **"Update Steam now" anyway:** one confirmation for "can't tell", two for "known incompatible". The smoke test then
  sees Decky's plugins missing and rolls back to the backup. The notice names the failed check, and Core waits for a
  new compatibility file, Decky or Steam version before trying again by itself.
- **Steam updated by itself** (`steam.cfg` not honoured on the 64-bit client): the version watch runs the same smoke
  test and rolls back to the newest backup. Without a backup (Steam updated outside Core's job before any backup
  existed), Core reports it, Core Repair says there is no backup, and restoring an older client by hand is the only
  way back. Installing Decky 4.0-pre is refused while Steam is webpack and allowed on the new client; there it is the
  user's own choice, outside Core's supported path.
- **Hold switched off:** Steam updates itself as usual and the plugins break. The Decky health notice and Core Repair
  (Roll back Steam) are the way back.
- Release Core with Decky 4 and the new file entry as soon as 4.0 is stable.

## Checks on the PC (not runnable on Linux)

- Steam stable, Decky 3.3.1: Install/Update Decky works, and the `Diag` log shows `Steam client Webpack (typeof
  window.webpackChunksteamui = object)`.
- Steam closed: Install Decky works, and the `Diag` log shows that the client could not be checked.
- Selecting a 4.x release on stable is refused with the translated reason. This needs a stable 4.x tag; until then,
  check the message with a test build.
- Steam beta (ES modules): Install Decky 3.3.1 is refused with the second reason.
- Steam updates: the checks at the end of [STEAM-UPDATES.md](STEAM-UPDATES.md).
- Big Picture on stable with Decky 3: QAM Core tab, Control Center, Dashboard, Plugin Store, Starfield screensaver,
  onboarding glyphs and lossless profiles look and work as before. No visible change is expected.
