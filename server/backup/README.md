# Core server backup

Scripts for the home server (the Windows PC that runs RomM). Every night they back up the game library to Proton
Drive, check the server's health, and write a small status file that Core on the gaming PC shows in Quick Access >
Core > Health ([CORE-HEALTH.md](../../docs/eternal/CORE-HEALTH.md)).

| File | What it is |
| ---- | ---------- |
| `Install-CoreBackup.ps1` | One-time setup: checks the Proton Drive CLI and its sign-in, shares the status folder read-only, registers the nightly task |
| `Backup-Core.ps1` | The nightly run (Task Scheduler starts it). `-HealthOnly` rewrites the status file without uploading |
| `Test-CoreRestore.ps1` | Restore check on demand (the nightly run does it once a month by itself) |
| `CoreBackup.Common.ps1` | Shared functions |
| `backup-config.example.json` | Settings with their defaults; copy to `backup-config.json` |
| `tests/Test-CoreBackupLogic.ps1` | Logic tests with fakes (no upload, no Docker, no Windows needed) |

Windows PowerShell 5.1 (built into Windows) is enough; PowerShell 7 works too.

## What a night does

1. **Health, always first:** the drive that holds the library (Windows health status, operational status, the
   reliability counters where Windows exposes them, free space), and RomM (HTTP to `romm.url`, and the container's
   state when Docker is installed).
2. **Backup, upload only**, in priority order, into `proton.remote_root` (default `/my-files/CoreBackup`):

   | # | Category | Local source | In Proton Drive |
   | - | -------- | ------------ | --------------- |
   | 1 | Saves and states | `romm.assets` (RomM keeps them under `users/<id>/saves/<platform>/<rom id>/<emulator>/` and `.../states/...`) | `saves/<assets folder name>/...` |
   | 2 | RomM database and config | A dump of RomM's MariaDB plus a copy of `romm.config`, in a dated folder | `romm/romm-<yyyyMMdd-HHmmss>/romm-db.sql.gz`, `.../config/` |
   | 3 | BIOS | `library\bios` (RomM structure A) or `library\<platform>\bios` (structure B) | `bios/...` or `bios/<platform>/bios/...` |
   | 4 | ROMs | `library\roms\<platform>` (A) or `library\<platform>\roms` (B) | `roms/<platform>/...` or `roms/<platform>/roms/...` |

   Categories 1 to 3 run every night. ROMs run only while at least `pool.min_free_fraction` (25%) of the storage
   pool stays free **after** the upload. When not everything fits, platforms are dropped in `roms.skip_order` (ps2,
   GameCube, ps3, then wii, psp, saturn, ps1), then the largest remaining uploads. Skipped platforms are listed in the
   status and in Core. If the pool size is unknown (`pool.total_bytes` is `null`), no ROMs are uploaded.
   A night stops starting new ROM uploads after `schedule.max_hours` (6) and stops a running one at that limit; the
   rest continues the next night (the first full upload of a large library takes several nights).
3. **Restore check, monthly** (`restore_check.every_days`, 30): one random file per category is downloaded to a new
   temporary folder and its SHA-256 compared with the original. Only files older than the last successful run and at
   least two days old are picked (see "Changed files" below). The temporary folder is deleted afterwards.
4. **Status:** `core-status.json` is written into `status_folder` (shared as `\\<server>\CoreStatus`) through a
   temporary file and a rename, so Core never reads half a file.

### Never deleted, never overwritten

The scripts call only `filesystem upload` (and `download`, `size` and `list` for the checks). Existing cloud files
are skipped (`--file-conflict-strategy skip`); folders are merged. `proton.file_conflict_strategy` accepts only `skip`
or `rename`; the scripts refuse `replace` (which trashes the cloud copy) and `create-new-revision`. Locally, the only
things removed are the scripts' own: RomM dump folders beyond `romm.keep_local_dumps` in `state_folder\romm-dumps`,
logs older than 60 days, and the restore check's temporary folder.

### Changed files (read this once)

"Skip existing" means a file that already exists in the cloud under the same name is not uploaded again, even if it
changed locally. For RomM this matters little: RomM stores each uploaded save version as its own file. For anything
you edit in place (a save written by an emulator into the assets folder by hand, `config.yml`), the cloud keeps the
first copy. The RomM config is copied into a new dated folder every night, so it is always current. If you want
changed files kept as well, set `proton.file_conflict_strategy` to `rename`: the changed file is uploaded beside the
old one under a new name, and nothing is overwritten.

### Credentials

No script asks for, stores, prints or logs a password or token.

- **Proton:** you sign in yourself with `proton-drive auth login` (a browser opens; your password goes only to
  Proton). The CLI keeps the session in Windows Credential Manager for your Windows account.
- **RomM database:** the dump runs inside the database container and uses the credentials that container was started
  with (`MARIADB_USER` / `MARIADB_PASSWORD`, or the older `MYSQL_*` names), passed to the dump tool as `MYSQL_PWD`.
  The script never reads them.
- **Task Scheduler:** by default the task runs while you are signed in. With `-RunWhetherSignedIn` the standard
  Windows credential prompt asks for your Windows password and Task Scheduler stores it; the script does not.

## Setup

1. **Proton Drive CLI.** Download it from [proton.me/drive/download](https://proton.me/drive/download) (Proton Drive
   CLI for Windows). Put `proton-drive.exe` in a folder on `PATH` (or set `proton.cli_path` to its full path). Then,
   in a normal PowerShell window, signed in to Windows as the account that will run the backup:

   ```powershell
   proton-drive auth login
   ```

   Sign in in the browser. `proton-drive filesystem list /my-files` should then list your Drive.
2. **Config.** Copy `backup-config.example.json` to `backup-config.json` and set at least: `romm.library`,
   `romm.assets`, `romm.config` (the host folders your RomM compose file mounts as `/romm/library`, `/romm/assets`,
   `/romm/config`), `romm.url`, the container names if yours differ, and `pool.total_bytes` (see below).
3. **Install.** In PowerShell opened with *Run as administrator*, in this folder:

   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\Install-CoreBackup.ps1 -RunNow
   ```

   It stops with a clear message if the CLI is missing or not signed in. Add `-RunWhetherSignedIn` if the server is
   often signed out.
4. **Check.** The log is in `state_folder\logs\backup-<date>.log`; the status in `status_folder\core-status.json`.
   Run a restore check at any time with `.\Test-CoreRestore.ps1`.

### The storage pool

No Proton Drive CLI command reports the account's quota (checked in the CLI source, see below), so the pool is
configured:

- `pool.total_bytes`: the pool size from the Proton web dashboard (Proton Duo: the storage shared with your partner).
  `1000000000000` for 1 TB, `2000000000000` for 2 TB. `null` = unknown: no ROMs are uploaded.
- `pool.others_used_bytes`: an estimate of what your partner and anything outside My files use (from the dashboard).
- Your own usage is measured every night with `proton-drive filesystem size /my-files` (it counts trashed files too,
  as the quota does). Used = that + `others_used_bytes`.

Re-check the two numbers when the plan or your partner's usage changes; the ROM budget is only as good as they are.

## Settings (`backup-config.json`)

| Key | Default | Meaning |
| --- | ------- | ------- |
| `server_name` | computer name | Shown in Core; also what you type in Core's *Server* field |
| `status_folder` | `C:\CoreStatus` | Where `core-status.json` is written; shared as `share.name` |
| `state_folder` | `C:\ProgramData\CoreBackup` | Logs, `backup-state.json`, RomM dumps, scratch folders |
| `share.name` | `CoreStatus` | SMB share name (read-only) |
| `share.read_account` | `Everyone` | Who may read the share (a local account such as `corestatus` is tighter) |
| `schedule.time` | `05:30` | Daily start (the household goes to bed around 4) |
| `schedule.max_hours` | `6` | ROM uploads stop at this limit; the task's own limit is 2 h longer |
| `proton.cli_path` | `proton-drive` | Command name or full path of the CLI |
| `proton.remote_root` | `/my-files/CoreBackup` | Backup folder in Drive (must be under `/my-files/`) |
| `proton.file_conflict_strategy` | `skip` | `skip` or `rename` (never `replace`) |
| `pool.total_bytes` | `null` | Pool size in bytes; `null` = unknown, no ROMs |
| `pool.others_used_bytes` | `0` | Estimated usage outside your My files |
| `pool.min_free_fraction` | `0.25` | Share of the pool that must stay free after ROM uploads |
| `romm.url` | `http://localhost:8080` | RomM's local address (the compose example maps host port 80 or 8080) |
| `romm.container` | `romm` | RomM's container (state check); empty to skip |
| `romm.db_container` | `romm-db` | MariaDB container for the dump |
| `romm.library` / `assets` / `config` | `D:\RomM\...` | Host folders mounted as `/romm/library`, `/romm/assets`, `/romm/config` |
| `romm.library_structure` | `auto` | `A` (`roms/<platform>`), `B` (`<platform>/roms`) or `auto` |
| `romm.keep_local_dumps` | `14` | Dated dump folders kept on the server |
| `drive.letter` | from `romm.library` | Drive whose health is checked |
| `roms.enabled` | `true` | `false` uploads no ROMs at all |
| `roms.skip_order` | `ps2, ngc, gamecube, ps3, wii, psp, saturn, psx, ps1` | Dropped first when the pool is tight (RomM folder slugs) |
| `restore_check.every_days` | `30` | Days between automatic restore checks |

Folder paths that contain `*`, `?`, `[` or `{` cannot be uploaded: the CLI reads such a path as a pattern (see below).
Platform folders never contain them; rename a parent folder that does.

## The status file (`core-status.json`, schema 1)

```json
{"schema":1,"written_at":"2026-11-26T05:52:11-05:00","server":"V3",
 "backup":{"result":"ok","started_at":"…","finished_at":"…","message":"…","uploaded_bytes":21480128512,
   "consecutive_failures":0,"last_success_at":"…",
   "categories":{"saves":{"ok":true,"files":12,"bytes":1048576},"romm":{…},"bios":{…},"roms":{…}},
   "roms_skipped_platforms":["ps2","ngc"],"pool":{"used_bytes":1310000000000,"total_bytes":2000000000000},
   "restore_check":{"at":"…","result":"ok","message":"…"}},
 "drive":{"label":"Games (E:)","health":"healthy","free_bytes":812000000000,"total_bytes":4000000000000,"message":"OK"},
 "romm":{"reachable":true,"checked_at":"…","message":"…"}}
```

- `result`: `ok` (every category ok), `partial` (saves ok, something else failed), `failed` (saves failed, or the run
  could not start, for example no Proton session), `never`.
- `consecutive_failures` counts runs that were not `ok`; Core alerts from 2.
- `files` / `bytes` per category: uploaded in this run (already-present files are not counted).
- `pool` is `null` when the pool size or usage is unknown. `drive.health`: `healthy`, `warning`, `unhealthy`,
  `unknown`. `restore_check.result`: `ok`, `failed`, `never`.
- Stable: fields are only ever added. `schema` changes only for an incompatible change. A full example is
  `Source/CoreHealth.Tests/core-status.sample.json`, written by these scripts and read by Core's tests.

## The Proton Drive CLI: what this relies on

Sources (read 2026-11-26):

- Proton blog, [Introducing Proton Drive CLI](https://proton.me/blog/proton-drive-cli) (9 June 2026): Windows, macOS
  and Linux builds; browser sign-in with `proton-drive auth login`; sessions in Windows Credential Manager; the example
  `proton-drive filesystem upload ./reports/* /my-files/Reports --conflict-strategy skip`.
- Proton support, [Using Proton Drive CLI](https://proton.me/support/drive-cli): `auth login`, `filesystem list`,
  `upload`, `download`, `--json`, command abbreviations.
- Source: [ProtonDriveApps/sdk](https://github.com/ProtonDriveApps/sdk), `cli/` at commit `39933bb` (2026-11-19,
  CHANGELOG up to `cli/v0.9.0`). The CLI now lives in `cli/`; `js/cli/README.md` only points there.
  - `cli/src/commands/fileSystem/commandFileSystemUpload.ts`: `filesystem upload <localPath...> <parentPath>` with
    `--file-conflict-strategy`/`-f` (`create-new-revision`, `rename`, `replace`, `skip`),
    `--folder-conflict-strategy`/`-d` (`merge`, `rename`, `replace`, `skip`), `--skip-thumbnails`/`-t`. Files with
    identical content are skipped automatically. With `--json` there is no interactive prompt (an unresolved conflict
    is an error, never a guess).
  - `transferSummary.ts`: with `--json` a transfer prints one line
    `{"transferredItems","transferredBytes","skippedItems","failedItems","failures":[{name,nodeUid,error}]}`.
  - `commandFileSystemDownload.ts`: `filesystem download <path...> <localFolder>`, file strategies `rename`, `remove`,
    `skip`. `commandFileSystemSize.ts`: `filesystem size <path>` prints `{"size","numberOfDescendants"}` (active and
    trashed descendants).
  - `cli/run.ts`, `proton-drive.ts`: exit code 0 success, 1 error (including "You need to login first"), 2 usage error.
  - `localPath.ts`: a local path containing `* ? [ {` is expanded as a glob. That is why only folders are passed.
  - `credentials/`: stores `keychain` (default; Credential Manager on Windows), `pass`, `unsafe_file`
    (`PROTON_DRIVE_CREDENTIALS_STORE`); `PROTON_DRIVE_LOG_LEVEL` (the scripts set `WARNING`).
  - No command reports the storage quota (`storageQuotaUpsell.ts` only reacts to an out-of-space error).

**Not confirmed** (no Windows build was run while writing this):

- The conflict option's name in the version you install. The blog shows `--conflict-strategy skip`; the source since
  v0.8.0 ("Clarify conflict strategies") has the two options above. The scripts read `filesystem upload --help` and
  use whichever the installed CLI offers; with neither they upload nothing and say so.
- The Windows file name and install location (`proton-drive.exe` is assumed; set `proton.cli_path` if it differs).
- That a scheduled task reaches the Credential Manager session: it should for a task that runs as your account with
  *Interactive* (default) or *Password* logon, and would not with *S4U*, which the installer never uses.
- What a ROM upload stopped at the time limit leaves in Drive (presumably an unfinished upload that the next night's
  run uploads again). The status counts such a night as ok and says the ROMs continue.

If the CLI changes its options, adjust `Get-ProtonConflictArguments` in `CoreBackup.Common.ps1` and its tests.

## RomM: what this relies on

- [RomM quick start (4.9.2)](https://docs.romm.app/4.9.2/getting-started/quick-start/): containers `romm` and
  `romm-db` (`mariadb`), mounts `/romm/library`, `/romm/assets` ("uploaded saves, states, etc."), `/romm/config`
  (`config.yml`), `/romm/resources` (metadata images from IGDB etc.; not backed up, RomM fetches them again), database
  variables `MARIADB_DATABASE`, `MARIADB_USER`, `MARIADB_PASSWORD`.
- [Folder structure (4.9.2)](https://docs.romm.app/4.9.2/getting-started/folder-structure/): structure A
  `roms/{platform}`, `bios/{platform}`; structure B `{platform}/roms`, `{platform}/bios`.
- RomM source, `backend/handler/filesystem/assets_handler.py`: saves at `users/<id>/saves/<platform>/<rom id>/<emulator>`,
  states at `users/<id>/states/...`.
- The dump uses `mariadb-dump` (or `mysqldump` in older images) with `--single-transaction`, a consistent snapshot that
  does not lock RomM out, and checks the dump's closing `-- Dump completed` line.

## Tests

```powershell
powershell -File tests\Test-CoreBackupLogic.ps1   # or: pwsh -File tests/Test-CoreBackupLogic.ps1
```

They cover the config guard (no `replace`), command-line quoting, CLI option detection and its JSON summary, the ROM
plan (25% floor, skip order, unknown pool), the drive verdict, state and result rules, the status file (schema keys,
atomic write, no BOM), both RomM structures, and two whole nights against a fake CLI (uploads always skip and merge,
no destructive command, ps2 skipped, restore check; then a signed-out night). They also refuse PowerShell 7-only
syntax so the scripts keep running on Windows PowerShell 5.1.
