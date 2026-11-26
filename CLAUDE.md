# Eternal Core (Playhub fork)

Fork of Playhub v3.0.0 (`2f60fb0`). Fork docs: `docs/eternal/README.md`, `docs/eternal/ARCHITECTURE-NOTES.md`.

## Build and test

- No `.sln`. ~130 independent projects under `Source/`. Most `*.Tests` and `*Checks` are console exes: run them with
  `dotnet run -c Release` from the project folder (exit code 0 means pass).
- App (`Source/Playhub`, WinUI 3), setup (`Source/PlayhubSetup`, WPF) and agent (`Source/GamingModeAgent`) build only
  on Windows or in CI (NuGet: WindowsAppSDK, Windows ref packs).
- Installer: `pwsh -File Source/PlayhubSetup/build-update-preview.ps1` produces `Source/PlayhubSetup/Output/Core-Setup.exe`
  (plus a `Playhub-Setup.exe` copy: Mark I/II updaters only look for that name, so releases carry both).
  CI: `.github/workflows/eternal-release.yml`.
- Linux, offline: plain `net8.0` projects run with `dotnet run -c Release -p:NuGetAudit=false`. The fork's own gate is
  `Source/EternalRelease.Tests`.
- Decky plugin (`Source/GamingModeDeckyPlugin`): `pnpm install --frozen-lockfile`, `tsc --noEmit`, `rollup -c`,
  `node --test tests/*.test.mjs`. Needs `catalog/` and `Source/Playhub/Assets/PluginImages` next to it.

## Traps

- **Version lives in one place:** `Source/Shared/EternalRelease.cs`. `Source/EternalRelease.props` regex-reads
  `const string Version = "x.y.z";`, so keep that line's exact form. Never put `<Version>` back in a csproj, or a
  literal in `Installer.cs` or the agent's `AssemblyInfo.cs`.
- Mark N = `3.N.x`. Tags must be numeric (`v3.1.0`): `PlayhubUpdateService.ParseVersion` ignores anything else, and
  the workflow refuses a tag that differs from `EternalRelease.Version`.
- The update repository is a persisted setting. 3.0 installs store `LoZazaMastro/Playhub`, and
  `PlayhubUpdatePolicy.Repository` maps it to the fork. Don't "simplify" that mapping away.
- Two update checkers exist: the app (`PlayhubUpdatePolicy`) and the Gaming Mode agent (`PlayhubUpdateNotice.cs`).
  Change both together.
- The agent's file version gates startup payload sync (`GamingModePayloadSync`): never lower it.
- Release packaging pins SHA-256 of bundled text files in **CRLF** form. Check out with `core.autocrlf=true`.
- `.cs`, `.csproj` and `.xaml` are `eol=crlf`; several files carry a UTF-8 BOM. Preserve both when editing.
- `PlayhubUpdate.Tests` does not compile at v3.0.0 (pre-existing). Five Linux test failures are Windows-only
  behaviour: `ControlledQamFocusChecks`, `DeckyArtifactComparison`, `BundledDecky`, `ImportCatalog`,
  `PluginUninstall`.
- The store must never update or overwrite the user's own plugin forks. They are listed in
  `Source/Shared/ProtectedPluginForks.json`, which both the app (embedded resource) and the Decky plugin read. Any
  project that links `DeckyPluginService.cs` or `PluginCatalogService.cs` must also link
  `Shared/PluginForkProtection.cs` and embed that JSON. See ARCHITECTURE-NOTES §5.
- Plugin catalog, "On this day" feed and images, and upstream plugin update checks still point at LoZazaMastro on
  purpose. See ARCHITECTURE-NOTES §2 before changing them. The image URL allowlist in
  `quick_settings/history_remote.py` is pinned to the upstream repository.

## Conventions

- Keep the original copyright, the SPDX headers and the "Andrea Sgarro (LoZazaMastro)" credits. The user-visible
  name is **Core** and the maker brand is **Eternal** ("Core by Eternal"), both from `Source/Shared/EternalRelease.cs`
  (`DisplayName`, `MakerName`); "Eternal Core" only in About/credits and docs. Identifiers (install and data folders,
  uninstall key, AppUserModelID `LoZazaMastro.Playhub`, exe/assembly names, namespaces, plugin names and IDs, pipes,
  persisted keys) stay "Playhub" until a Phase 2 migration. Deliberate exceptions and the asset inventory:
  `docs/eternal/REBRAND.md`. New UI text says Core; `LocalizationResource.Tests` fails on a stray product "Playhub".
- Work on branches. `main` mirrors upstream v3.0.0 plus README commits.
- Releases are drafts created by CI from a tag. Publishing the draft is what ships an update to installed copies.
