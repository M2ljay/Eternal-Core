<#
.SYNOPSIS
  Core's nightly server run: offsite backup to Proton Drive, server health checks, core-status.json.

.DESCRIPTION
  1. Checks the external drive and RomM, whatever happens next.
  2. Uploads, in priority order: (1) saves and states (RomM assets), (2) a RomM database dump and
     RomM's config, (3) BIOS files, (4) ROMs. 1-3 run every night. ROMs run only while at least
     pool.min_free_fraction (25%) of the Proton storage pool stays free after the upload; large
     disc-based platforms are skipped first and listed in the status.
  3. Once a month (restore_check.every_days) downloads one random file per category and compares
     its SHA-256 with the original.
  4. Writes core-status.json (schema 1) atomically into the CoreStatus share for Core on the PC.

  Upload-only: files that already exist in the cloud are skipped (or, with
  proton.file_conflict_strategy = "rename", uploaded beside them under a new name). Nothing in the
  cloud is ever deleted, trashed or overwritten. No password or token is read, stored or logged.

.PARAMETER ConfigPath
  backup-config.json (default: next to this script).

.PARAMETER HealthOnly
  Run the drive and RomM checks and rewrite core-status.json with the last backup result; no upload.
#>
[CmdletBinding()]
param(
	[string]$ConfigPath = (Join-Path $PSScriptRoot 'backup-config.json'),
	[switch]$HealthOnly,
	# Tests only (Test-CoreBackupLogic.ps1): a script dot-sourced after the shared functions that
	# replaces the outside-world seams with fakes.
	[string]$SeamsScript
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CoreBackup.Common.ps1')
if ($SeamsScript) { . $SeamsScript }

$config = Get-CoreBackupConfig $ConfigPath
$stateFolder = [string]$config.state_folder
$logFolder = Join-Path $stateFolder 'logs'
foreach ($folder in @($stateFolder, $logFolder)) { if (-not (Test-Path -LiteralPath $folder)) { [void](New-Item -ItemType Directory -Path $folder -Force) } }
$script:CoreLogPath = Join-Path $logFolder ('backup-{0}.log' -f (Get-Date -Format 'yyyyMMdd'))
Get-ChildItem -LiteralPath $logFolder -Filter 'backup-*.log' -ErrorAction SilentlyContinue |
	Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-60) } | Remove-Item -Force -ErrorAction SilentlyContinue

$mutex = New-Object System.Threading.Mutex($false, 'Global\CoreBackupNightly')
if (-not $mutex.WaitOne(0)) { Write-CoreLog 'Another Core backup run is in progress; this one exits.'; exit 0 }

$statePath = Join-Path $stateFolder 'backup-state.json'
$state = Read-CoreBackupState $statePath
$started = [DateTimeOffset]::Now
$deadline = $started.AddHours([double]$config.schedule.max_hours)
$root = ([string]$config.proton.remote_root).TrimEnd('/')
$categories = [ordered]@{ saves = (New-CategoryResult); romm = (New-CategoryResult); bios = (New-CategoryResult); roms = (New-CategoryResult) }
$notes = New-Object System.Collections.ArrayList
$skippedPlatforms = @()
$pool = $null
$fatal = $null
$drive = $null; $romm = $null

function Add-Note([string]$Text) { [void]$notes.Add($Text); Write-CoreLog $Text }

function Add-Upload($Category, $Upload) {
	$Category.files = [int]$Category.files + [int]$Upload.Files
	$Category.bytes = [int64]$Category.bytes + [int64]$Upload.Bytes
}

try {
	Write-CoreLog "Core server run started on $($config.server_name)$(if ($HealthOnly) { ' (health only)' })."
	$drive = Get-DriveStatus $config
	Write-CoreLog "Drive $($drive.label): $($drive.health) - $($drive.message)"
	$romm = Get-RommStatus $config
	Write-CoreLog "RomM: reachable=$($romm.reachable) - $($romm.message)"

	if ($HealthOnly) {
		$backup = $state.last_backup
		if (-not $backup) { $backup = New-BackupSection 'never' $null $null 'no backup has run yet' 0 $state $categories @() $null; $backup.result = 'never' }
		$path = Write-CoreStatus (New-CoreStatusDocument $config.server_name $backup $drive $romm) ([string]$config.status_folder)
		Write-CoreLog "Status written: $path"
		exit 0
	}

	$signIn = Test-ProtonSignedIn $config
	if (-not $signIn.SignedIn) { throw "Proton Drive: $($signIn.Message). Sign in on the server yourself with: proton-drive auth login" }
	$help = Invoke-ProtonCli $config @('filesystem', 'upload', '--help')
	$conflict = Get-ProtonConflictArguments ($help.StdOut + "`n" + $help.StdErr) ([string]$config.proton.file_conflict_strategy)
	if (-not $conflict) { throw 'Proton Drive CLI: its upload command offers no conflict strategy this script recognises; nothing was uploaded (see README, "CLI flags").' }

	$layout = Get-RommLayout ([string]$config.romm.library) ([string]$config.romm.library_structure)
	$folders = @("$root/saves", "$root/romm", "$root/bios", "$root/roms")
	if ($layout.Structure -eq 'B') {
		foreach ($p in $layout.Bios.Keys) { $folders += "$root/bios/$p" }
		foreach ($p in $layout.Roms.Keys) { $folders += "$root/roms/$p" }
	}
	if (-not (New-ProtonFolderTree $config $folders $stateFolder $conflict)) { throw "Proton Drive: could not create the backup folders under $root." }

	# (1) Saves and states: RomM's assets folder (users/<id>/saves|states/<platform>/<rom>/...).
	$assets = [string]$config.romm.assets
	if (Test-Path -LiteralPath $assets -PathType Container) {
		$upload = Send-ProtonFolder $config $assets (Get-RemoteParentFor $root 'saves' $layout.Structure '') $conflict
		Add-Upload $categories.saves $upload
		$categories.saves.ok = [bool]$upload.Ok
		if (-not $upload.Ok) { Add-Note "Saves: $($upload.Message)" } else { Write-CoreLog "Saves: $($upload.Files) uploaded, $($upload.Skipped) already there." }
	}
	else { Add-Note "Saves: assets folder not found ($assets)." }

	# (2) RomM database dump and config, in a dated folder so every night's copy is kept.
	$stamp = $started.ToString('yyyyMMdd-HHmmss')
	$dumps = Join-Path $stateFolder 'romm-dumps'
	$tonight = Join-Path $dumps "romm-$stamp"
	[void](New-Item -ItemType Directory -Path $tonight -Force)
	$dump = & $script:DatabaseDumper ([string]$config.romm.db_container) (Join-Path $tonight 'romm-db.sql.gz')
	if (-not $dump.Ok) { Add-Note "RomM database: $($dump.Message)"; Remove-Item -LiteralPath (Join-Path $tonight 'romm-db.sql.gz') -Force -ErrorAction SilentlyContinue }
	$configCopied = $false
	$rommConfig = [string]$config.romm.config
	if (Test-Path -LiteralPath $rommConfig -PathType Container) {
		Copy-Item -LiteralPath $rommConfig -Destination (Join-Path $tonight 'config') -Recurse -Force
		$configCopied = $true
	}
	else { Add-Note "RomM config: folder not found ($rommConfig)." }
	$upload = Send-ProtonFolder $config $tonight (Get-RemoteParentFor $root 'romm' $layout.Structure '') $conflict
	Add-Upload $categories.romm $upload
	$categories.romm.ok = [bool]($dump.Ok -and $configCopied -and $upload.Ok)
	if (-not $upload.Ok) { Add-Note "RomM dump upload: $($upload.Message)" }
	$keep = [int]$config.romm.keep_local_dumps
	@(Get-ChildItem -LiteralPath $dumps -Directory -Filter 'romm-*' | Sort-Object Name -Descending | Select-Object -Skip $keep) |
		ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

	# (3) BIOS.
	if ($null -eq $layout.Structure) { Add-Note "BIOS and ROMs: RomM library not found ($($config.romm.library))." }
	elseif ($layout.Bios.Count -eq 0) { $categories.bios.ok = $true; Write-CoreLog 'BIOS: no bios folder in the library.' }
	else {
		$allOk = $true
		foreach ($p in $layout.Bios.Keys) {
			$upload = Send-ProtonFolder $config $layout.Bios[$p] (Get-RemoteParentFor $root 'bios' $layout.Structure $p) $conflict
			Add-Upload $categories.bios $upload
			if (-not $upload.Ok) { $allOk = $false; Add-Note "BIOS ($p): $($upload.Message)" }
		}
		$categories.bios.ok = $allOk
	}

	# (4) ROMs, within the pool budget and the night's time limit.
	if ($null -eq $layout.Structure) { }
	elseif (-not [bool]$config.roms.enabled) { $categories.roms.ok = $true; Write-CoreLog 'ROMs: turned off in backup-config.json.' }
	else {
		$mine = Get-ProtonFolderSize $config '/my-files'
		$total = $config.pool.total_bytes
		$used = $null
		if ($null -ne $mine) { $used = [int64]$mine + [int64]$config.pool.others_used_bytes }
		if ($null -ne $total -and $null -ne $used) { $pool = [ordered]@{ used_bytes = $used; total_bytes = [int64]$total } }
		$platforms = @()
		foreach ($p in $layout.Roms.Keys) {
			$local = Get-FolderBytes $layout.Roms[$p]
			$remote = Get-ProtonFolderSize $config (Join-RemotePath (Get-RemoteParentFor $root 'roms' $layout.Structure $p) (Get-LeafName $layout.Roms[$p]))
			if ($null -eq $remote) { $remote = 0 }
			$platforms += New-Object psobject -Property @{ Name = $p; Delta = [int64][math]::Max(0, $local - $remote) }
		}
		if ($null -eq $used) {
			$plan = @{ Include = @(); Skipped = @($platforms | ForEach-Object { $_.Name }); Reason = 'could not read the Drive usage' }
		}
		else {
			$plan = Get-RomPlan -Platforms $platforms -TotalBytes $total -UsedBytes $used -MinFreeFraction ([double]$config.pool.min_free_fraction) -SkipOrder @($config.roms.skip_order)
		}
		$skippedPlatforms = @($plan.Skipped)
		if ($skippedPlatforms.Count) { Add-Note ("ROMs skipped ({0}): {1}" -f $plan.Reason, ($skippedPlatforms -join ', ')) }
		$allOk = $true; $deferred = @()
		$uploadedPlatforms = New-Object System.Collections.ArrayList
		foreach ($name in @($state.roms_uploaded_platforms)) { if ($name) { [void]$uploadedPlatforms.Add([string]$name) } }
		foreach ($platform in @($plan.Include | Sort-Object -Property @{ Expression = { [int64]$_.Delta } })) {
			if ([DateTimeOffset]::Now -ge $deadline) { $deferred += $platform.Name; continue }
			$upload = Send-ProtonFolder $config $layout.Roms[$platform.Name] (Get-RemoteParentFor $root 'roms' $layout.Structure $platform.Name) $conflict $deadline
			Add-Upload $categories.roms $upload
			if ($upload.TimedOut) { $deferred += $platform.Name }
			elseif (-not $upload.Ok) { $allOk = $false; Add-Note "ROMs ($($platform.Name)): $($upload.Message)" }
			elseif (-not $uploadedPlatforms.Contains($platform.Name)) { [void]$uploadedPlatforms.Add($platform.Name) }
		}
		$state.roms_uploaded_platforms = @($uploadedPlatforms)
		if ($deferred.Count) { Add-Note ("ROMs continue next night ({0} h limit): {1}" -f $config.schedule.max_hours, ($deferred -join ', ')) }
		$categories.roms.ok = $allOk
	}

	# Monthly restore check.
	if (Test-RestoreDue $state ([int]$config.restore_check.every_days) ([DateTimeOffset]::Now)) {
		Write-CoreLog 'Restore check: downloading one random file per category.'
		$state.restore_check = Invoke-CoreRestoreCheck $config $state
		Write-CoreLog "Restore check: $($state.restore_check.result) - $($state.restore_check.message)"
	}
}
catch {
	$fatal = $_.Exception.Message
	Add-Note $fatal
}
finally {
	if (-not $HealthOnly) {
		$finished = [DateTimeOffset]::Now
		$result = Get-CoreBackupResult $categories
		if ($fatal) { $result = 'failed' }
		$state = Update-CoreBackupState $state $result $finished.ToString('o')
		$uploadedBytes = [int64]0; foreach ($key in $categories.Keys) { $uploadedBytes += [int64]$categories[$key].bytes }
		$message = 'All categories backed up.'
		if ($notes.Count) { $message = ($notes -join ' | ') }
		$backup = New-BackupSection $result $started.ToString('o') $finished.ToString('o') $message $uploadedBytes $state $categories $skippedPlatforms $pool
		$state.last_backup = $backup
		try { Save-JsonFile $state $statePath } catch { Write-CoreLog "State could not be saved: $($_.Exception.Message)" }
		if (-not $drive) { $drive = [ordered]@{ label = $null; health = 'unknown'; free_bytes = $null; total_bytes = $null; message = 'not checked' } }
		if (-not $romm) { $romm = [ordered]@{ reachable = $false; checked_at = [DateTimeOffset]::Now.ToString('o'); message = 'not checked' } }
		try {
			$path = Write-CoreStatus (New-CoreStatusDocument $config.server_name $backup $drive $romm) ([string]$config.status_folder)
			Write-CoreLog "Run finished: $result. Status written: $path"
		}
		catch { Write-CoreLog "Status could not be written: $($_.Exception.Message)" }
	}
	$mutex.ReleaseMutex()
}
if ($fatal) { exit 1 }
exit 0
