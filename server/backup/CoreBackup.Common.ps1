# Core backup: shared functions for Backup-Core.ps1, Install-CoreBackup.ps1 and Test-CoreRestore.ps1.
# Windows PowerShell 5.1 compatible (no ??, ?:, &&, ||, no PowerShell 7-only cmdlet parameters).
#
# The pure functions (config merge, command-line quoting, ROM plan, drive verdict, state update, status
# document) take plain values so Test-CoreBackupLogic.ps1 can exercise them without Windows, Docker or
# the Proton Drive CLI. Everything that touches the outside world goes through a $script: seam.
#
# Credentials: this code never asks for, stores, prints or logs a password or token. The Proton Drive
# CLI keeps its own session in Windows Credential Manager; the RomM database dump reads the database
# password from the database container's own environment, inside the container.

Set-StrictMode -Version 2.0

$script:CoreStatusSchema = 1

# ---------- configuration ----------

function Get-CoreBackupDefaults {
	[ordered]@{
		server_name  = ''
		status_folder = 'C:\CoreStatus'
		state_folder = 'C:\ProgramData\CoreBackup'
		share        = [ordered]@{ name = 'CoreStatus'; read_account = 'Everyone' }
		schedule     = [ordered]@{ time = '05:30'; max_hours = 6 }
		proton       = [ordered]@{ cli_path = 'proton-drive'; remote_root = '/my-files/CoreBackup'; file_conflict_strategy = 'skip' }
		pool         = [ordered]@{ total_bytes = $null; others_used_bytes = 0; min_free_fraction = 0.25 }
		romm         = [ordered]@{
			url = 'http://localhost:8080'; container = 'romm'; db_container = 'romm-db'
			library = 'D:\RomM\library'; assets = 'D:\RomM\assets'; config = 'D:\RomM\config'
			library_structure = 'auto'; keep_local_dumps = 14
		}
		drive        = [ordered]@{ letter = '' }
		roms         = [ordered]@{ enabled = $true; skip_order = @('ps2', 'ngc', 'gamecube', 'ps3', 'wii', 'psp', 'saturn', 'psx', 'ps1') }
		restore_check = [ordered]@{ every_days = 30 }
	}
}

# Overlays a parsed JSON object (PSCustomObject) on the defaults. Unknown keys are kept; a key whose
# default is a section stays a section.
function Merge-CoreBackupConfig {
	param($Defaults, $Overrides)
	$result = [ordered]@{}
	foreach ($key in $Defaults.Keys) { $result[$key] = $Defaults[$key] }
	if ($null -eq $Overrides) { return $result }
	foreach ($property in $Overrides.PSObject.Properties) {
		$name = $property.Name
		if ($name.StartsWith('_')) { continue } # "_comment" keys in the example file
		$value = $property.Value
		if ($result.Contains($name) -and $result[$name] -is [System.Collections.IDictionary] -and $value -is [System.Management.Automation.PSCustomObject]) {
			$result[$name] = Merge-CoreBackupConfig -Defaults $result[$name] -Overrides $value
		}
		else { $result[$name] = $value }
	}
	return $result
}

function Get-CoreBackupConfig {
	param([string]$Path)
	if (-not (Test-Path -LiteralPath $Path)) { throw "Config file not found: $Path (copy backup-config.example.json to backup-config.json and edit it)." }
	$parsed = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
	$config = Merge-CoreBackupConfig -Defaults (Get-CoreBackupDefaults) -Overrides $parsed
	if ([string]::IsNullOrWhiteSpace($config.server_name)) { $config.server_name = $env:COMPUTERNAME }
	if ([string]::IsNullOrWhiteSpace($config.drive.letter)) {
		$root = [System.IO.Path]::GetPathRoot([string]$config.romm.library)
		if ($root -match '^([A-Za-z]):') { $config.drive.letter = $Matches[1].ToUpperInvariant() }
	}
	Assert-CoreBackupConfig $config
	return $config
}

# Refuses settings that would let a run delete or overwrite cloud copies.
function Assert-CoreBackupConfig {
	param($Config)
	$strategy = [string]$Config.proton.file_conflict_strategy
	if (@('skip', 'rename') -notcontains $strategy) {
		throw "proton.file_conflict_strategy must be 'skip' or 'rename' (never 'replace' or 'create-new-revision'): got '$strategy'."
	}
	if (-not ([string]$Config.proton.remote_root).StartsWith('/my-files/')) {
		throw "proton.remote_root must be a folder under /my-files/, for example /my-files/CoreBackup."
	}
	$fraction = [double]$Config.pool.min_free_fraction
	if ($fraction -lt 0 -or $fraction -ge 1) { throw 'pool.min_free_fraction must be between 0 and 1 (0.25 keeps a quarter of the pool free).' }
	if ($Config.schedule.time -notmatch '^([01]?\d|2[0-3]):[0-5]\d$') { throw "schedule.time must be HH:mm, got '$($Config.schedule.time)'." }
}

# ---------- logging (never pass a secret to it; nothing in this code has one) ----------

$script:CoreLogPath = $null

function Write-CoreLog {
	param([string]$Message)
	$line = '{0} {1}' -f ([DateTimeOffset]::Now.ToString('yyyy-MM-dd HH:mm:ss zzz')), $Message
	if ($script:CoreLogPath) {
		try { [System.IO.File]::AppendAllText($script:CoreLogPath, $line + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false))) } catch { }
	}
	Write-Host $line
}

# ---------- running external programs ----------

# Quotes one argument for the Windows command line (CommandLineToArgvW / MSVCRT rules).
# .NET Framework's ProcessStartInfo has no ArgumentList, so PowerShell 5.1 needs this.
function ConvertTo-CommandLineArgument {
	param([string]$Value)
	if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
	$builder = New-Object System.Text.StringBuilder
	[void]$builder.Append('"')
	$backslashes = 0
	foreach ($char in $Value.ToCharArray()) {
		if ($char -eq '\') { $backslashes++; continue }
		if ($char -eq '"') { [void]$builder.Append('\' * ($backslashes * 2 + 1)); [void]$builder.Append('"') }
		else { if ($backslashes) { [void]$builder.Append('\' * $backslashes) }; [void]$builder.Append($char) }
		$backslashes = 0
	}
	if ($backslashes) { [void]$builder.Append('\' * ($backslashes * 2)) }
	[void]$builder.Append('"')
	return $builder.ToString()
}

function Join-CommandLine {
	param([string[]]$Arguments)
	return (($Arguments | ForEach-Object { ConvertTo-CommandLineArgument $_ }) -join ' ')
}

# Runs a program and returns @{ ExitCode; StdOut; StdErr; TimedOut }. The deadline (a DateTimeOffset or
# $null) kills the process tree when reached. Replaced by the tests.
$script:CoreProcessRunner = {
	param([string]$FilePath, [string[]]$Arguments, $Deadline, [hashtable]$Environment)
	$info = New-Object System.Diagnostics.ProcessStartInfo
	$info.FileName = $FilePath
	$info.Arguments = Join-CommandLine $Arguments
	$info.UseShellExecute = $false
	$info.RedirectStandardOutput = $true
	$info.RedirectStandardError = $true
	$info.CreateNoWindow = $true
	if ($Environment) { foreach ($key in $Environment.Keys) { $info.EnvironmentVariables[$key] = [string]$Environment[$key] } }
	$process = [System.Diagnostics.Process]::Start($info)
	$out = $process.StandardOutput.ReadToEndAsync()
	$err = $process.StandardError.ReadToEndAsync()
	$timedOut = $false
	while (-not $process.WaitForExit(1000)) {
		if ($Deadline -and [DateTimeOffset]::Now -ge $Deadline) {
			$timedOut = $true
			try { & taskkill.exe /PID $process.Id /T /F | Out-Null } catch { }
			[void]$process.WaitForExit(30000)
			break
		}
	}
	$process.WaitForExit()
	@{ ExitCode = $process.ExitCode; StdOut = $out.Result; StdErr = $err.Result; TimedOut = $timedOut }
}

# ---------- Proton Drive CLI ----------
# Commands used (Proton Drive CLI, ProtonDriveApps/sdk cli/, checked against source at 39933bb, 2026-11-19):
#   filesystem upload <localPath...> <parentPath> --file-conflict-strategy <skip|rename> --folder-conflict-strategy merge --skip-thumbnails --json
#   filesystem download <path...> <localFolder> --file-conflict-strategy rename --json
#   filesystem size <path> --json         -> {"size":n,"numberOfDescendants":n}
#   filesystem list <path> --json         (used only to test that a session exists)
# Exit codes: 0 ok, 1 error (also "You need to login first"), 2 usage error.

$script:ProtonEnvironment = @{ PROTON_DRIVE_LOG_LEVEL = 'WARNING' }

function Invoke-ProtonCli {
	param($Config, [string[]]$Arguments, $Deadline = $null)
	return & $script:CoreProcessRunner ([string]$Config.proton.cli_path) $Arguments $Deadline $script:ProtonEnvironment
}

# The CLI's own documentation shows "--conflict-strategy skip" (blog, June 2026); its source since
# v0.8.0 ("Clarify conflict strategies") has separate file and folder options. Read the installed
# version's help and use what it offers; refuse to upload if neither is there.
function Get-ProtonConflictArguments {
	param([string]$UploadHelp, [string]$FileStrategy)
	if ($UploadHelp -match '--file-conflict-strategy' -and $UploadHelp -match '--folder-conflict-strategy') {
		return @('--file-conflict-strategy', $FileStrategy, '--folder-conflict-strategy', 'merge')
	}
	if ($UploadHelp -match '--conflict-strategy') {
		if ($FileStrategy -ne 'skip') { return $null } # a single strategy would also rename folders
		return @('--conflict-strategy', 'skip')
	}
	return $null
}

function Test-ProtonSignedIn {
	param($Config)
	$result = Invoke-ProtonCli $Config @('filesystem', 'list', '/my-files', '--json')
	if ($result.ExitCode -eq 0) { return @{ SignedIn = $true; Message = 'signed in' } }
	$text = ($result.StdOut + "`n" + $result.StdErr)
	if ($text -match 'login first|log in|sign in') { return @{ SignedIn = $false; Message = 'not signed in' } }
	return @{ SignedIn = $false; Message = ('Proton Drive CLI failed (exit {0}).' -f $result.ExitCode) }
}

# The CLI prints one JSON summary line for a transfer with --json.
function ConvertFrom-ProtonTransferSummary {
	param([string]$StdOut)
	$lines = @($StdOut -split "`r?`n" | Where-Object { $_.TrimStart().StartsWith('{') })
	for ($i = $lines.Count - 1; $i -ge 0; $i--) {
		try {
			$parsed = $lines[$i] | ConvertFrom-Json
			if ($parsed.PSObject.Properties.Name -contains 'transferredItems') { return $parsed }
		}
		catch { }
	}
	return $null
}

function Get-ProtonFolderSize {
	param($Config, [string]$RemotePath)
	$result = Invoke-ProtonCli $Config @('filesystem', 'size', $RemotePath, '--json')
	if ($result.ExitCode -ne 0) { return $null }
	foreach ($line in ($result.StdOut -split "`r?`n")) {
		if (-not $line.TrimStart().StartsWith('{')) { continue }
		try { $parsed = $line | ConvertFrom-Json; if ($null -ne $parsed.size) { return [int64]$parsed.size } } catch { }
	}
	return $null
}

# The CLI expands a local path that contains * ? [ or { as a glob (cli/src/commands/fileSystem/localPath.ts).
# ROM names often contain [!] or (USA) [b], so only folders are ever passed, and only folders whose own
# path has none of these characters; the CLI reads their contents itself, without globbing.
function Test-GlobFreePath {
	param([string]$Path)
	return -not ($Path -match '[\*\?\[\{]')
}

# Last path segment, for Windows paths on any host (Split-Path splits on \ only on Windows).
function Get-LeafName {
	param([string]$Path)
	$trimmed = $Path.TrimEnd('\', '/')
	return $trimmed.Substring($trimmed.LastIndexOfAny([char[]]@('\', '/')) + 1)
}

function Join-RemotePath {
	param([string]$Parent, [string]$Child)
	return ($Parent.TrimEnd('/') + '/' + $Child.Trim('/'))
}

# Creates the remote folders by uploading an empty local copy of the tree with folders merged:
# the CLI has no "create if missing", and create-folder fails when the folder exists.
function New-ProtonFolderTree {
	param($Config, [string[]]$RemoteFolders, [string]$ScratchRoot, [string[]]$ConflictArguments)
	$root = [string]$Config.proton.remote_root
	$parent = $root.Substring(0, $root.TrimEnd('/').LastIndexOf('/'))
	$skeleton = Join-Path $ScratchRoot 'skeleton'
	if (Test-Path -LiteralPath $skeleton) { Remove-Item -LiteralPath $skeleton -Recurse -Force }
	$top = Join-Path $skeleton ($root.TrimEnd('/').Split('/')[-1])
	[void](New-Item -ItemType Directory -Path $top -Force)
	foreach ($folder in $RemoteFolders) {
		if (-not $folder.StartsWith($root.TrimEnd('/') + '/')) { continue }
		$relative = $folder.Substring($root.TrimEnd('/').Length + 1)
		[void](New-Item -ItemType Directory -Path (Join-Path $top ($relative -replace '/', [System.IO.Path]::DirectorySeparatorChar)) -Force)
	}
	$arguments = @('filesystem', 'upload', $top, $parent) + $ConflictArguments + @('--skip-thumbnails', '--json')
	$result = Invoke-ProtonCli $Config $arguments
	return ($result.ExitCode -eq 0)
}

# Uploads one local folder into a remote parent: the folder arrives as <parent>/<folder name>, folders
# are merged, files follow the configured conflict strategy (skip or rename; never replace).
function Send-ProtonFolder {
	param($Config, [string]$LocalFolder, [string]$RemoteParent, [string[]]$ConflictArguments, $Deadline = $null)
	if (-not (Test-GlobFreePath $LocalFolder)) {
		return @{ Ok = $false; Files = 0; Bytes = [int64]0; Skipped = 0; Failed = 0; TimedOut = $false
			Message = "The path $LocalFolder contains * ? [ or {, which the Proton Drive CLI treats as a pattern. Rename the folder." }
	}
	$arguments = @('filesystem', 'upload', $LocalFolder, $RemoteParent) + $ConflictArguments + @('--skip-thumbnails', '--json')
	$result = Invoke-ProtonCli $Config $arguments $Deadline
	$summary = ConvertFrom-ProtonTransferSummary $result.StdOut
	$files = 0; $bytes = [int64]0; $skipped = 0; $failed = 0
	if ($summary) { $files = [int]$summary.transferredItems; $bytes = [int64]$summary.transferredBytes; $skipped = [int]$summary.skippedItems; $failed = [int]$summary.failedItems }
	$ok = ($result.ExitCode -eq 0 -and -not $result.TimedOut -and $failed -eq 0)
	$message = 'ok'
	if ($result.TimedOut) { $message = 'stopped at the night''s time limit; continues next run' }
	elseif (-not $ok) {
		$detail = ''
		if ($summary -and $summary.failures) { $detail = (@($summary.failures | Select-Object -First 3 | ForEach-Object { $_.name + ': ' + $_.error }) -join '; ') }
		elseif ($result.StdErr) { $detail = ($result.StdErr -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 1) }
		$message = ('upload failed (exit {0}, {1} failed) {2}' -f $result.ExitCode, $failed, $detail).Trim()
	}
	return @{ Ok = $ok; Files = $files; Bytes = $bytes; Skipped = $skipped; Failed = $failed; TimedOut = [bool]$result.TimedOut; Message = $message }
}

# ---------- RomM layout ----------

# RomM library structure A: library\roms\<platform>, library\bios\<platform>.
# Structure B: library\<platform>\roms, library\<platform>\bios. (docs.romm.app, Folder Structure.)
function Get-RommLayout {
	param([string]$Library, [string]$Structure = 'auto')
	$layout = [ordered]@{ Structure = $null; Roms = [ordered]@{}; Bios = [ordered]@{} }
	if (-not (Test-Path -LiteralPath $Library -PathType Container)) { return $layout }
	$romsA = Join-Path $Library 'roms'
	$useA = ($Structure -eq 'A') -or ($Structure -eq 'auto' -and (Test-Path -LiteralPath $romsA -PathType Container))
	if ($useA) {
		$layout.Structure = 'A'
		foreach ($dir in @(Get-ChildItem -LiteralPath $romsA -Directory -ErrorAction SilentlyContinue | Sort-Object Name)) { $layout.Roms[$dir.Name.ToLowerInvariant()] = $dir.FullName }
		$biosA = Join-Path $Library 'bios'
		if (Test-Path -LiteralPath $biosA -PathType Container) { $layout.Bios['*'] = $biosA }
	}
	else {
		$layout.Structure = 'B'
		foreach ($dir in @(Get-ChildItem -LiteralPath $Library -Directory -ErrorAction SilentlyContinue | Sort-Object Name)) {
			$roms = Join-Path $dir.FullName 'roms'; $bios = Join-Path $dir.FullName 'bios'
			if (Test-Path -LiteralPath $roms -PathType Container) { $layout.Roms[$dir.Name.ToLowerInvariant()] = $roms }
			if (Test-Path -LiteralPath $bios -PathType Container) { $layout.Bios[$dir.Name.ToLowerInvariant()] = $bios }
		}
	}
	return $layout
}

# Where a local folder lands in the cloud. Structure A: roms\<p> -> <root>/roms/<p>, bios -> <root>/bios.
# Structure B: <p>\roms -> <root>/roms/<p>/roms, <p>\bios -> <root>/bios/<p>/bios.
function Get-RemoteParentFor {
	param([string]$Root, [string]$Category, [string]$Structure, [string]$Platform)
	switch ($Category) {
		'saves' { return (Join-RemotePath $Root 'saves') }
		'romm' { return (Join-RemotePath $Root 'romm') }
		'bios' { if ($Structure -eq 'B') { return (Join-RemotePath (Join-RemotePath $Root 'bios') $Platform) }; return $Root }
		'roms' { if ($Structure -eq 'B') { return (Join-RemotePath (Join-RemotePath $Root 'roms') $Platform) }; return (Join-RemotePath $Root 'roms') }
	}
	throw "Unknown category $Category"
}

function Get-FolderBytes {
	param([string]$Path)
	$sum = [int64]0
	foreach ($file in @(Get-ChildItem -LiteralPath $Path -File -Recurse -Force -ErrorAction SilentlyContinue)) { $sum += $file.Length }
	return $sum
}

# ---------- ROM plan (pure) ----------

# Decides which ROM platforms to upload tonight. $Platforms: objects with Name and Delta (bytes still to
# upload). Keeps at least MinFreeFraction of the pool free after the upload; when everything does not
# fit, platforms are dropped in SkipOrder first (large disc-based systems), then the largest remaining
# uploads. Unknown pool size uploads nothing.
function Get-RomPlan {
	param([object[]]$Platforms, $TotalBytes, [int64]$UsedBytes, [double]$MinFreeFraction, [string[]]$SkipOrder)
	$include = New-Object System.Collections.ArrayList
	foreach ($p in $Platforms) { [void]$include.Add($p) }
	$skipped = New-Object System.Collections.ArrayList
	if ($null -eq $TotalBytes -or [int64]$TotalBytes -le 0) {
		foreach ($p in $include) { [void]$skipped.Add($p.Name) }
		return @{ Include = @(); Skipped = @($skipped); FreeAfter = $null; Reason = 'pool size unknown: set pool.total_bytes' }
	}
	$total = [int64]$TotalBytes
	$floor = [int64][math]::Ceiling($total * $MinFreeFraction)
	$order = New-Object System.Collections.ArrayList
	foreach ($name in $SkipOrder) { $hit = @($include | Where-Object { $_.Name -eq $name.ToLowerInvariant() }); if ($hit.Count) { [void]$order.Add($hit[0]) } }
	foreach ($p in @($include | Where-Object { $order -notcontains $_ } | Sort-Object -Property @{ Expression = { [int64]$_.Delta } } -Descending)) { [void]$order.Add($p) }
	$pending = [int64]0; foreach ($p in $include) { $pending += [int64]$p.Delta }
	foreach ($candidate in $order) {
		if ($total - $UsedBytes - $pending -ge $floor) { break }
		if ([int64]$candidate.Delta -le 0) { continue } # nothing to upload: dropping it frees nothing
		[void]$include.Remove($candidate)
		[void]$skipped.Add($candidate.Name)
		$pending -= [int64]$candidate.Delta
	}
	$reason = 'fits'
	if ($skipped.Count) { $reason = 'pool would fall below the free-space floor' }
	if ($total - $UsedBytes - $pending -lt $floor) { $reason = 'pool is already below the free-space floor' }
	return @{ Include = @($include | ForEach-Object { $_ }); Skipped = @($skipped); FreeAfter = ($total - $UsedBytes - $pending); Reason = $reason }
}

# ---------- server drive (pure verdict + Windows probe) ----------

function Resolve-DriveHealth {
	param([string]$HealthStatus, [string[]]$OperationalStatus, $Reliability, $FreeBytes, $TotalBytes)
	$notes = New-Object System.Collections.ArrayList
	$health = 'unknown'
	switch -Regex ($HealthStatus) {
		'^Healthy$' { $health = 'healthy' }
		'^Warning$' { $health = 'warning'; [void]$notes.Add('Windows reports the disk health as Warning') }
		'^Unhealthy$' { $health = 'unhealthy'; [void]$notes.Add('Windows reports the disk as Unhealthy') }
	}
	$ops = @($OperationalStatus | Where-Object { $_ })
	if ($ops.Count -and @($ops | Where-Object { $_ -ne 'OK' -and $_ -ne 'Online' }).Count) {
		if ($health -eq 'healthy' -or $health -eq 'unknown') { $health = 'warning' }
		[void]$notes.Add('operational status: ' + ($ops -join ', '))
	}
	if ($Reliability) {
		$uncorrected = [int64]0
		foreach ($name in @('ReadErrorsUncorrected', 'WriteErrorsUncorrected')) {
			if ($Reliability.PSObject.Properties.Name -contains $name -and $null -ne $Reliability.$name) { $uncorrected += [int64]$Reliability.$name }
		}
		if ($uncorrected -gt 0) { if ($health -ne 'unhealthy') { $health = 'warning' }; [void]$notes.Add("$uncorrected uncorrected read/write errors") }
		if ($Reliability.PSObject.Properties.Name -contains 'Wear' -and $null -ne $Reliability.Wear -and [int]$Reliability.Wear -ge 90) {
			if ($health -ne 'unhealthy') { $health = 'warning' }; [void]$notes.Add("wear $($Reliability.Wear)%")
		}
		if ($Reliability.PSObject.Properties.Name -contains 'Temperature' -and $null -ne $Reliability.Temperature -and [int]$Reliability.Temperature -ge 60) {
			if ($health -ne 'unhealthy') { $health = 'warning' }; [void]$notes.Add("temperature $($Reliability.Temperature) C")
		}
	}
	if ($null -ne $FreeBytes -and $null -ne $TotalBytes -and [int64]$TotalBytes -gt 0 -and [int64]$FreeBytes / [double]$TotalBytes -lt 0.05) {
		if ($health -ne 'unhealthy') { $health = 'warning' }; [void]$notes.Add('less than 5% free')
	}
	$message = 'OK'
	if ($notes.Count) { $message = ($notes -join '; ') }
	elseif ($health -eq 'unknown') { $message = 'health not reported by Windows' }
	return @{ health = $health; message = $message }
}

$script:DriveProbe = {
	param([string]$Letter)
	$volume = Get-Volume -DriveLetter $Letter -ErrorAction Stop
	$disk = Get-Partition -DriveLetter $Letter -ErrorAction Stop | Get-Disk -ErrorAction Stop
	$physical = $null
	try { $physical = Get-PhysicalDisk -ErrorAction Stop | Where-Object { [string]$_.DeviceId -eq [string]$disk.Number } | Select-Object -First 1 } catch { }
	$reliability = $null
	if ($physical) { try { $reliability = $physical | Get-StorageReliabilityCounter -ErrorAction Stop } catch { } }
	$health = [string]$disk.HealthStatus; $ops = @([string]$disk.OperationalStatus)
	if ($physical) { $health = [string]$physical.HealthStatus; $ops = @($physical.OperationalStatus | ForEach-Object { [string]$_ }) }
	$label = [string]$volume.FileSystemLabel
	if (-not $label) { $label = [string]$disk.FriendlyName }
	@{ Label = $label; HealthStatus = $health; OperationalStatus = $ops; Reliability = $reliability; FreeBytes = [int64]$volume.SizeRemaining; TotalBytes = [int64]$volume.Size }
}

function Get-DriveStatus {
	param($Config)
	$letter = [string]$Config.drive.letter
	if (-not $letter) { return [ordered]@{ label = $null; health = 'unknown'; free_bytes = $null; total_bytes = $null; message = 'drive.letter is not set' } }
	try {
		$probe = & $script:DriveProbe $letter
		$verdict = Resolve-DriveHealth $probe.HealthStatus $probe.OperationalStatus $probe.Reliability $probe.FreeBytes $probe.TotalBytes
		return [ordered]@{ label = ('{0} ({1}:)' -f $probe.Label, $letter); health = $verdict.health; free_bytes = $probe.FreeBytes; total_bytes = $probe.TotalBytes; message = $verdict.message }
	}
	catch { return [ordered]@{ label = "${letter}:"; health = 'unknown'; free_bytes = $null; total_bytes = $null; message = ('drive not readable: ' + $_.Exception.Message) } }
}

# ---------- RomM reachability ----------

$script:HttpProbe = {
	param([string]$Url)
	try { $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 10 -MaximumRedirection 3; return @{ Ok = $true; Status = [int]$r.StatusCode } }
	catch {
		$status = $null
		if ($_.Exception.Response) { try { $status = [int]$_.Exception.Response.StatusCode } catch { } }
		return @{ Ok = $false; Status = $status; Error = $_.Exception.Message }
	}
}

$script:DockerState = {
	param([string]$Container)
	$docker = Get-Command docker -ErrorAction SilentlyContinue
	if (-not $docker) { return $null }
	$state = & $docker.Source inspect --format '{{.State.Status}}' $Container 2>$null
	if ($LASTEXITCODE -ne 0) { return 'missing' }
	return ([string]$state).Trim()
}

function Get-RommStatus {
	param($Config)
	$now = [DateTimeOffset]::Now.ToString('o')
	$http = & $script:HttpProbe ([string]$Config.romm.url)
	$container = $null
	if ($Config.romm.container) { $container = & $script:DockerState ([string]$Config.romm.container) }
	# A web server that answers (even 401/403 for a login page) is reachable.
	$reachable = $http.Ok -or ($null -ne $http.Status -and $http.Status -lt 500)
	$parts = New-Object System.Collections.ArrayList
	if ($reachable) { [void]$parts.Add("answers at $($Config.romm.url)") } else { [void]$parts.Add("no answer at $($Config.romm.url)") }
	if ($null -ne $container) {
		[void]$parts.Add("container $($Config.romm.container): $container")
		if ($container -ne 'running') { $reachable = $false }
	}
	return [ordered]@{ reachable = [bool]$reachable; checked_at = $now; message = ($parts -join '; ') }
}

# ---------- RomM database dump ----------

# Dumps RomM's MariaDB from inside its container to a gzip file, using the credentials the container
# was started with ($MARIADB_USER/$MARIADB_PASSWORD, or the MYSQL_* names of older images). The
# password never leaves the container: it is passed to the dump tool as MYSQL_PWD, not on a command
# line, and this script never reads it. --single-transaction gives a consistent InnoDB snapshot
# without locking RomM out.
$script:DumpCommand = 'U="${MARIADB_USER:-$MYSQL_USER}"; P="${MARIADB_PASSWORD:-$MYSQL_PASSWORD}"; D="${MARIADB_DATABASE:-${MYSQL_DATABASE:-romm}}"; T="$(command -v mariadb-dump || command -v mysqldump)"; [ -n "$T" ] || { echo "no dump tool in container" >&2; exit 3; }; MYSQL_PWD="$P" exec "$T" --single-transaction --quick --skip-lock-tables --default-character-set=utf8mb4 -u"$U" "$D"'

$script:DatabaseDumper = {
	param([string]$Container, [string]$Destination)
	$docker = Get-Command docker -ErrorAction SilentlyContinue
	if (-not $docker) { return @{ Ok = $false; Message = 'docker is not installed or not on PATH' } }
	$info = New-Object System.Diagnostics.ProcessStartInfo
	$info.FileName = $docker.Source
	$info.Arguments = Join-CommandLine @('exec', $Container, 'sh', '-c', $script:DumpCommand)
	$info.UseShellExecute = $false; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true; $info.CreateNoWindow = $true
	$process = [System.Diagnostics.Process]::Start($info)
	$errors = $process.StandardError.ReadToEndAsync()
	$tail = New-Object byte[] 512; $tailLength = 0
	$file = [System.IO.File]::Create($Destination)
	try {
		$gzip = New-Object System.IO.Compression.GZipStream($file, [System.IO.Compression.CompressionMode]::Compress)
		try {
			$buffer = New-Object byte[] 65536
			$stream = $process.StandardOutput.BaseStream
			while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
				$gzip.Write($buffer, 0, $read)
				if ($read -ge $tail.Length) { [Array]::Copy($buffer, $read - $tail.Length, $tail, 0, $tail.Length); $tailLength = $tail.Length }
				else {
					$keep = [math]::Min($tailLength, $tail.Length - $read)
					[Array]::Copy($tail, $tailLength - $keep, $tail, 0, $keep)
					[Array]::Copy($buffer, 0, $tail, $keep, $read); $tailLength = $keep + $read
				}
			}
		}
		finally { $gzip.Dispose() }
	}
	finally { $file.Dispose() }
	$process.WaitForExit()
	$end = [System.Text.Encoding]::UTF8.GetString($tail, 0, $tailLength)
	if ($process.ExitCode -ne 0) {
		$first = ($errors.Result -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 1)
		return @{ Ok = $false; Message = ('database dump failed (exit {0}): {1}' -f $process.ExitCode, $first) }
	}
	if ($end -notmatch 'Dump completed') { return @{ Ok = $false; Message = 'database dump looks incomplete (no "Dump completed" line)' } }
	return @{ Ok = $true; Message = 'ok' }
}

# ---------- state and status (pure) ----------

function New-CoreBackupState {
	[ordered]@{ consecutive_failures = 0; last_success_at = $null; last_result = 'never'; roms_uploaded_platforms = @(); restore_check = [ordered]@{ at = $null; result = 'never'; message = $null } }
}

function Read-CoreBackupState {
	param([string]$Path)
	$state = New-CoreBackupState
	if (-not (Test-Path -LiteralPath $Path)) { return $state }
	try {
		$parsed = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
		$merged = Merge-CoreBackupConfig -Defaults $state -Overrides $parsed
		return $merged
	}
	catch { return $state }
}

# A run that is not "ok" counts as a failure, so a category that fails every night (partial) still
# raises consecutive_failures and Core's alert.
function Update-CoreBackupState {
	param($State, [string]$Result, [string]$FinishedAt)
	$next = [ordered]@{}
	foreach ($key in $State.Keys) { $next[$key] = $State[$key] }
	$next.last_result = $Result
	if ($Result -eq 'ok') { $next.consecutive_failures = 0; $next.last_success_at = $FinishedAt }
	else { $next.consecutive_failures = [int]$State.consecutive_failures + 1 }
	return $next
}

# Overall result from the four categories: ok when all are ok; failed when saves (priority 1) failed
# or nothing succeeded; partial otherwise. Category values: $true, $false or $null (not attempted).
function Get-CoreBackupResult {
	param($Categories)
	$values = @($Categories.Keys | ForEach-Object { $Categories[$_].ok })
	if (@($values | Where-Object { $_ -eq $false }).Count -eq 0) { return 'ok' }
	if ($Categories['saves'].ok -ne $true) { return 'failed' }
	return 'partial'
}

function New-CategoryResult { [ordered]@{ ok = $false; files = 0; bytes = [int64]0 } }

# The status document read by Core (GamingModeAgent CoreHealth). Schema 1; keep it stable: add fields,
# never rename or remove them, and bump "schema" only for an incompatible change.
function New-CoreStatusDocument {
	param([string]$Server, $Backup, $Drive, $Romm)
	return [ordered]@{
		schema = $script:CoreStatusSchema
		written_at = [DateTimeOffset]::Now.ToString('o')
		server = $Server
		backup = $Backup
		drive = $Drive
		romm = $Romm
	}
}

function New-BackupSection {
	param([string]$Result, [string]$StartedAt, [string]$FinishedAt, [string]$Message, [int64]$UploadedBytes, $State, $Categories, [string[]]$SkippedPlatforms, $Pool)
	return [ordered]@{
		result = $Result; started_at = $StartedAt; finished_at = $FinishedAt; message = $Message
		uploaded_bytes = $UploadedBytes
		consecutive_failures = [int]$State.consecutive_failures
		last_success_at = $State.last_success_at
		categories = $Categories
		roms_skipped_platforms = @($SkippedPlatforms)
		pool = $Pool
		restore_check = $State.restore_check
	}
}

# Writes the status atomically: a temporary file in the same folder, then a rename over the old one, so
# a reader on the PC never sees half a file.
function Write-CoreStatus {
	param($Document, [string]$Folder)
	if (-not (Test-Path -LiteralPath $Folder)) { [void](New-Item -ItemType Directory -Path $Folder -Force) }
	$path = Join-Path $Folder 'core-status.json'
	$temporary = Join-Path $Folder ('core-status.json.' + [Guid]::NewGuid().ToString('N') + '.tmp')
	$json = ConvertTo-Json -InputObject $Document -Depth 8
	[System.IO.File]::WriteAllText($temporary, $json, (New-Object System.Text.UTF8Encoding($false)))
	if (Test-Path -LiteralPath $path) { [System.IO.File]::Replace($temporary, $path, [NullString]::Value) }
	else { [System.IO.File]::Move($temporary, $path) }
	return $path
}

function Save-JsonFile {
	param($Value, [string]$Path)
	$temporary = $Path + '.tmp'
	[System.IO.File]::WriteAllText($temporary, (ConvertTo-Json -InputObject $Value -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
	if (Test-Path -LiteralPath $Path) { [System.IO.File]::Replace($temporary, $Path, [NullString]::Value) } else { [System.IO.File]::Move($temporary, $Path) }
}

# ---------- restore check ----------

function Test-RestoreDue {
	param($State, [int]$EveryDays, [DateTimeOffset]$Now)
	$at = $State.restore_check.at
	if (-not $at) { return $true }
	try { return ($Now - [DateTimeOffset]::Parse([string]$at, [Globalization.CultureInfo]::InvariantCulture)).TotalDays -ge $EveryDays } catch { return $true }
}

function Get-FileSha256 {
	param([string]$Path)
	$sha = [System.Security.Cryptography.SHA256]::Create()
	try { $stream = [System.IO.File]::OpenRead($Path); try { return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() } finally { $stream.Dispose() } }
	finally { $sha.Dispose() }
}

# Remote path of a local file inside an uploaded folder: <remoteParent>/<folder name>/<relative path>.
function Get-RemoteFilePath {
	param([string]$LocalFolder, [string]$RemoteParent, [string]$LocalFile)
	$folder = $LocalFolder.TrimEnd('\', '/')
	$relative = $LocalFile.Substring($folder.Length).TrimStart('\', '/') -replace '\\', '/'
	return (Join-RemotePath (Join-RemotePath $RemoteParent (Get-LeafName $folder)) $relative)
}

# Picks a random file that should already be in the cloud: older than the last successful run and not
# touched for two days (a file changed after its upload keeps the old copy in the cloud, by design).
function Select-RestoreSample {
	param([string[]]$Folders, $Before, [System.Random]$Random)
	$pool = New-Object System.Collections.ArrayList
	foreach ($folder in $Folders) {
		if (-not $folder -or -not (Test-Path -LiteralPath $folder -PathType Container)) { continue }
		foreach ($file in @(Get-ChildItem -LiteralPath $folder -File -Recurse -ErrorAction SilentlyContinue)) {
			if ($Before -and $file.LastWriteTime -ge $Before) { continue }
			[void]$pool.Add(@{ Folder = $folder; File = $file.FullName })
		}
	}
	if ($pool.Count -eq 0) { return $null }
	return $pool[$Random.Next($pool.Count)]
}

function Invoke-CoreRestoreCheck {
	param($Config, $State, [System.Random]$Random = (New-Object System.Random))
	$root = [string]$Config.proton.remote_root
	$layout = Get-RommLayout ([string]$Config.romm.library) ([string]$Config.romm.library_structure)
	$before = (Get-Date).AddDays(-2)
	if ($State.last_success_at) { try { $last = [DateTimeOffset]::Parse([string]$State.last_success_at, [Globalization.CultureInfo]::InvariantCulture).LocalDateTime; if ($last -lt $before) { $before = $last } } catch { } }
	$staging = Join-Path ([string]$Config.state_folder) 'romm-dumps'
	$sources = [ordered]@{
		saves = @(@{ Folders = @([string]$Config.romm.assets); Parent = { param($f) Get-RemoteParentFor $root 'saves' $layout.Structure '' } })
		romm  = @(@{ Folders = @(Get-ChildItem -LiteralPath $staging -Directory -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }); Parent = { param($f) Get-RemoteParentFor $root 'romm' $layout.Structure '' } })
		bios  = @(@{ Folders = @($layout.Bios.Values); Parent = { param($f) $p = ''; foreach ($k in $layout.Bios.Keys) { if ($layout.Bios[$k] -eq $f) { $p = $k } }; Get-RemoteParentFor $root 'bios' $layout.Structure $p } })
		roms  = @(@{ Folders = @($layout.Roms.Keys | Where-Object { @($State.roms_uploaded_platforms) -contains $_ } | ForEach-Object { $layout.Roms[$_] }); Parent = { param($f) $p = ''; foreach ($k in $layout.Roms.Keys) { if ($layout.Roms[$k] -eq $f) { $p = $k } }; Get-RemoteParentFor $root 'roms' $layout.Structure $p } })
	}
	$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('CoreRestore-' + [Guid]::NewGuid().ToString('N'))
	[void](New-Item -ItemType Directory -Path $temp -Force)
	$lines = New-Object System.Collections.ArrayList
	$failed = $false
	try {
		foreach ($category in $sources.Keys) {
			$source = $sources[$category][0]
			$sample = Select-RestoreSample -Folders $source.Folders -Before $before -Random $Random
			if (-not $sample) { [void]$lines.Add("${category}: nothing to check yet"); continue }
			$remote = Get-RemoteFilePath $sample.Folder (& $source.Parent $sample.Folder) $sample.File
			$target = Join-Path $temp $category
			[void](New-Item -ItemType Directory -Path $target -Force)
			$result = Invoke-ProtonCli $Config @('filesystem', 'download', $remote, $target, '--file-conflict-strategy', 'rename', '--json')
			$downloaded = Join-Path $target (Split-Path -Leaf $sample.File)
			if ($result.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $downloaded)) {
				$failed = $true; [void]$lines.Add("${category}: download of $remote failed (exit $($result.ExitCode))"); continue
			}
			if ((Get-FileSha256 $downloaded) -eq (Get-FileSha256 $sample.File)) { [void]$lines.Add("${category}: $remote matches") }
			else { $failed = $true; [void]$lines.Add("${category}: $remote differs from the local file (SHA-256)") }
		}
	}
	finally { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
	$result = 'ok'; if ($failed) { $result = 'failed' }
	return [ordered]@{ at = [DateTimeOffset]::Now.ToString('o'); result = $result; message = ($lines -join '; ') }
}
