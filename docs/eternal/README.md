# Eternal Core

Eternal Core is a personal fork of [Playhub](https://github.com/LoZazaMastro/Playhub), the Windows couch-gaming app
(Decky Loader and Gaming Mode on Windows, Steam Big Picture). Upstream development stopped at v3.0.0, so this fork
continues it.

- **Base:** Playhub v3.0.0, commit `2f60fb0` (the fork's `main` adds only README and screenshot commits).
- **Licence:** MIT, unchanged. The original copyright, © 2026 Andrea Sgarro (LoZazaMastro), is kept in `LICENSE`,
  in the source headers and in the app's About card. The app is still called Playhub; only the version display
  changed.
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

The About card and the setup window show `Mark I (3.1.0)`. Windows "Installed apps" shows `3.1.0`.

## How updates flow

1. A release on `M2ljay/Eternal-Core` that is published, not a draft and not a prerelease, has a numeric tag
   (`v3.1.0`) and an asset named `Playhub-Setup.exe`.
2. Playhub checks `releases/latest` of the fork every few minutes. Installs that still store upstream's repository in
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
git clone --branch mark-i https://github.com/M2ljay/Eternal-Core
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
2. **Update notice wording:** the "Playhub 3.2.0 available" dialog shows the version number, not the Mark name. Should
   it map 3.N to "Mark N"?
3. **Code signing:** upstream never signed the installer, so Windows SmartScreen warns. A certificate would remove that
   warning but costs money.
4. **Branding:** the app name, AppUserModelID (`LoZazaMastro.Playhub`), publisher string and install folder are
   unchanged on purpose. Renaming them later needs a migration plan (shortcuts, uninstall key, plugin author check).
5. **Release tests:** upstream's `PlayhubUpdate.Tests` is broken at v3.0.0. Fix it, or rely on `EternalRelease.Tests`?
