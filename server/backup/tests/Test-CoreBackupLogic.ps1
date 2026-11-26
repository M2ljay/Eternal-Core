# Core backup logic tests (no Pester). Run: powershell -File tests\Test-CoreBackupLogic.ps1
# (Windows PowerShell 5.1 or PowerShell 7 on any OS). Exit code 0 means every check passed.
# The outside world (Proton Drive CLI, Docker, disks, HTTP) is replaced by fakes; nothing is uploaded.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$backupDir = Split-Path -Parent $here
. (Join-Path $backupDir 'CoreBackup.Common.ps1')

$script:passed = 0
function Check([bool]$Condition, [string]$Name) {
	if (-not $Condition) { throw "FAIL $Name" }
	$script:passed++
	Write-Host "PASS $Name"
}

# ---------- Windows PowerShell 5.1 compatibility (syntax only PowerShell 7 understands) ----------
$banned = @('QuestionQuestion', 'QuestionQuestionEquals', 'QuestionDot', 'QuestionLBracket', 'AndAnd', 'OrOr', 'QuestionMark')
foreach ($file in @(Get-ChildItem -LiteralPath $backupDir -Filter '*.ps1') + @(Get-ChildItem -LiteralPath $here -Filter '*.ps1')) {
	$tokens = $null; $errors = $null
	[void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
	Check ($errors.Count -eq 0) "$($file.Name) parses"
	$bad = @($tokens | Where-Object { $banned -contains [string]$_.Kind })
	Check ($bad.Count -eq 0) "$($file.Name) uses no PowerShell 7-only operators ($(@($bad | ForEach-Object { $_.Text }) -join ' '))"
	if ($file.DirectoryName -ne $backupDir) { continue }
	$text = Get-Content -LiteralPath $file.FullName -Raw
	foreach ($pattern in @('-AsHashtable', '-Parallel', 'Join-String', '-AsByteStream', '$PSStyle', 'ArgumentList.Add')) {
		Check (-not $text.Contains($pattern)) "$($file.Name) avoids $pattern"
	}
}

# ---------- config ----------
$parsed = '{"_comment":"x","server_name":"V3","pool":{"total_bytes":2000000000000},"romm":{"library":"E:\\RomM\\library"}}' | ConvertFrom-Json
$merged = Merge-CoreBackupConfig (Get-CoreBackupDefaults) $parsed
Check ($merged.server_name -eq 'V3') 'config: top-level override'
Check ([int64]$merged.pool.total_bytes -eq 2000000000000 -and [double]$merged.pool.min_free_fraction -eq 0.25) 'config: section merge keeps defaults'
Check (-not $merged.Contains('_comment')) 'config: _comment keys ignored'
Check ($merged.schedule.time -eq '05:30') 'config: default schedule 05:30'
$bad = Merge-CoreBackupConfig (Get-CoreBackupDefaults) ('{"proton":{"file_conflict_strategy":"replace"}}' | ConvertFrom-Json)
$threw = $false; try { Assert-CoreBackupConfig $bad } catch { $threw = $true }
Check $threw 'config: replace strategy refused (never overwrite cloud copies)'
$bad = Merge-CoreBackupConfig (Get-CoreBackupDefaults) ('{"proton":{"file_conflict_strategy":"create-new-revision"}}' | ConvertFrom-Json)
$threw = $false; try { Assert-CoreBackupConfig $bad } catch { $threw = $true }
Check $threw 'config: create-new-revision refused'
$example = Get-Content -LiteralPath (Join-Path $backupDir 'backup-config.example.json') -Raw | ConvertFrom-Json
$exampleMerged = Merge-CoreBackupConfig (Get-CoreBackupDefaults) $example
Assert-CoreBackupConfig $exampleMerged
Check ($exampleMerged.romm.db_container -eq 'romm-db') 'config: example file is valid'

# ---------- command line ----------
Check ((ConvertTo-CommandLineArgument 'plain') -eq 'plain') 'argv: plain'
Check ((ConvertTo-CommandLineArgument 'D:\Rom M\x') -eq '"D:\Rom M\x"') 'argv: spaces quoted'
Check ((ConvertTo-CommandLineArgument 'D:\Rom M\') -eq '"D:\Rom M\\"') 'argv: trailing backslash doubled inside quotes'
Check ((ConvertTo-CommandLineArgument 'a"b') -eq '"a\"b"') 'argv: quote escaped'
Check ((ConvertTo-CommandLineArgument '') -eq '""') 'argv: empty'

# ---------- Proton CLI parsing ----------
$newHelp = "Options:`n  -f, --file-conflict-strategy <value>`n  -d, --folder-conflict-strategy <value>`n  -t, --skip-thumbnails"
Check ((Get-ProtonConflictArguments $newHelp 'skip') -join ' ' -eq '--file-conflict-strategy skip --folder-conflict-strategy merge') 'cli: split conflict options (source 0.8+)'
Check ((Get-ProtonConflictArguments $newHelp 'rename') -join ' ' -eq '--file-conflict-strategy rename --folder-conflict-strategy merge') 'cli: rename keeps folders merged'
Check ((Get-ProtonConflictMode $newHelp) -eq 'split') 'cli: split options detected'
Check ((Get-ProtonConflictMode '  --conflict-strategy <value>') -eq 'legacy') 'cli: single option (blog example) is the legacy CLI'
Check ($null -eq (Get-ProtonConflictArguments '  --conflict-strategy <value>' 'skip')) 'cli: legacy CLI gets no upload arguments, not even skip'
Check ($null -eq (Get-ProtonConflictArguments 'Usage: upload' 'skip')) 'cli: unknown help -> no upload'
Check ($script:ProtonTooOldMessage -match '^Proton CLI too old') 'cli: too-old message'
$defaultConfig = Get-CoreBackupDefaults
Check ((Get-CategoryFileStrategy $defaultConfig 'saves') -eq 'rename' -and (Get-CategoryFileStrategy $defaultConfig 'romm') -eq 'rename') 'strategy: saves and RomM dump keep every version (rename)'
Check ((Get-CategoryFileStrategy $defaultConfig 'roms') -eq 'skip' -and (Get-CategoryFileStrategy $defaultConfig 'bios') -eq 'skip') 'strategy: ROMs and BIOS skip existing files'
$listed = ConvertFrom-ProtonListNames "[`n{""name"":""a.srm"",""type"":""file""},`n{""name"":{""ok"":true,""value"":""a (1).srm""}},`n{""uid"":""x""}`n]`n"
Check (($listed -join '|') -eq 'a.srm|a (1).srm') 'list: names as strings and as result objects'
Check (@(ConvertFrom-ProtonListNames 'not json').Count -eq 0) 'list: garbage gives no names'
Check ((Select-NewestVersionName @('game.srm', 'game (1).srm', 'game (10).srm', 'game (2).srm', 'other (11).srm') 'game.srm') -eq 'game (10).srm') 'versions: newest numbered copy wins'
Check ((Select-NewestVersionName @('game.srm') 'game.srm') -eq 'game.srm') 'versions: no copy -> the file itself'
Check ((Select-NewestVersionName @('save', 'save (3)') 'save') -eq 'save (3)') 'versions: name without extension'
Check ((Select-NewestVersionName @('a.b (1).c', 'a.b.c (4)') 'a.b.c') -eq 'a.b (1).c') 'versions: extension is the last dot'
$summary = ConvertFrom-ProtonTransferSummary "noise`n{""transferredItems"":3,""transferredBytes"":1200,""skippedItems"":9,""failedItems"":0,""failures"":[]}`n"
Check ($summary.transferredItems -eq 3 -and $summary.skippedItems -eq 9) 'cli: JSON transfer summary'
Check (Test-GlobFreePath 'D:\RomM\library\roms\ps2') 'glob: plain folder allowed'
Check (-not (Test-GlobFreePath 'D:\Games [old]\roms')) 'glob: bracket folder refused'

# ---------- remote paths ----------
Check ((Get-RemoteParentFor '/my-files/CoreBackup' 'roms' 'A' 'ps2') -eq '/my-files/CoreBackup/roms') 'remote: A roms parent'
Check ((Get-RemoteParentFor '/my-files/CoreBackup' 'roms' 'B' 'ps2') -eq '/my-files/CoreBackup/roms/ps2') 'remote: B roms parent'
Check ((Get-RemoteParentFor '/my-files/CoreBackup' 'bios' 'A' '*') -eq '/my-files/CoreBackup') 'remote: A bios parent'
Check ((Get-RemoteFilePath 'D:\RomM\assets' '/my-files/CoreBackup/saves' 'D:\RomM\assets\users\abc\saves\gba\1\m.srm') -eq '/my-files/CoreBackup/saves/assets/users/abc/saves/gba/1/m.srm') 'remote: file path inside an uploaded folder'

# ---------- ROM plan ----------
$TB = [int64]1000000000000; $GB = [int64]1000000000
$platforms = @(
	(New-Object psobject -Property @{ Name = 'gba'; Delta = 20 * $GB }),
	(New-Object psobject -Property @{ Name = 'ps2'; Delta = 400 * $GB }),
	(New-Object psobject -Property @{ Name = 'ngc'; Delta = 200 * $GB }),
	(New-Object psobject -Property @{ Name = 'snes'; Delta = 2 * $GB }))
$plan = Get-RomPlan $platforms (2 * $TB) (300 * $GB) 0.25 @('ps2', 'ngc', 'ps3')
Check ($plan.Skipped.Count -eq 0 -and $plan.Include.Count -eq 4) 'plan: everything fits in 2 TB'
$plan = Get-RomPlan $platforms (1 * $TB) (300 * $GB) 0.25 @('ps2', 'ngc', 'ps3')
Check (($plan.Skipped -join ',') -eq 'ps2') 'plan: 1 TB pool drops ps2 first'
Check ($plan.FreeAfter -ge 250 * $GB) 'plan: at least 25% stays free'
$plan = Get-RomPlan $platforms (1 * $TB) (740 * $GB) 0.25 @('ps2', 'ngc', 'ps3')
Check (($plan.Skipped -join ',') -eq 'ps2,ngc,gba') 'plan: then ngc, then the largest remaining'
$plan = Get-RomPlan $platforms $null (300 * $GB) 0.25 @('ps2')
Check ($plan.Include.Count -eq 0 -and $plan.Skipped.Count -eq 4) 'plan: unknown pool uploads no ROMs'
$plan = Get-RomPlan $platforms (1 * $TB) (900 * $GB) 0.25 @('ps2')
Check ($plan.Include.Count -eq 0 -and $plan.Reason -match 'already below') 'plan: pool already over the floor'

# ---------- drive verdict ----------
Check ((Resolve-DriveHealth 'Healthy' @('OK') $null (500 * $GB) $TB).health -eq 'healthy') 'drive: healthy'
Check ((Resolve-DriveHealth 'Warning' @('OK') $null (500 * $GB) $TB).health -eq 'warning') 'drive: warning'
Check ((Resolve-DriveHealth 'Unhealthy' @('OK') $null (500 * $GB) $TB).health -eq 'unhealthy') 'drive: unhealthy'
$rel = New-Object psobject -Property @{ ReadErrorsUncorrected = 3; WriteErrorsUncorrected = 0; Wear = 0; Temperature = 35 }
$verdict = Resolve-DriveHealth 'Healthy' @('OK') $rel (500 * $GB) $TB
Check ($verdict.health -eq 'warning' -and $verdict.message -match 'uncorrected') 'drive: uncorrected errors warn'
Check ((Resolve-DriveHealth 'Healthy' @('OK') $null (10 * $GB) $TB).health -eq 'warning') 'drive: under 5% free warns'
Check ((Resolve-DriveHealth '' @() $null $null $null).health -eq 'unknown') 'drive: nothing reported is unknown'

# ---------- state and result ----------
$state = New-CoreBackupState
$cats = [ordered]@{ saves = @{ ok = $true }; romm = @{ ok = $true }; bios = @{ ok = $true }; roms = @{ ok = $true } }
Check ((Get-CoreBackupResult $cats) -eq 'ok') 'result: all ok'
$cats.roms = @{ ok = $false }
Check ((Get-CoreBackupResult $cats) -eq 'partial') 'result: roms failed is partial'
$cats.saves = @{ ok = $false }
Check ((Get-CoreBackupResult $cats) -eq 'failed') 'result: saves failed is failed'
$state = Update-CoreBackupState $state 'failed' '2026-11-26T05:31:00-05:00'
$state = Update-CoreBackupState $state 'partial' '2026-11-27T05:31:00-05:00'
Check ($state.consecutive_failures -eq 2 -and $null -eq $state.last_success_at) 'state: failures count up (partial counts)'
$state = Update-CoreBackupState $state 'ok' '2026-11-28T05:40:00-05:00'
Check ($state.consecutive_failures -eq 0 -and $state.last_success_at -eq '2026-11-28T05:40:00-05:00') 'state: success resets'
Check (Test-RestoreDue (New-CoreBackupState) 30 ([DateTimeOffset]::Now)) 'restore: never run is due'
$recent = New-CoreBackupState; $recent.restore_check.at = [DateTimeOffset]::Now.AddDays(-3).ToString('o')
Check (-not (Test-RestoreDue $recent 30 ([DateTimeOffset]::Now))) 'restore: 3 days ago is not due'

# ---------- status document ----------
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('core-backup-test-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tmp)
try {
	$categories = [ordered]@{ saves = (New-CategoryResult); romm = (New-CategoryResult); bios = (New-CategoryResult); roms = (New-CategoryResult) }
	$section = New-BackupSection 'ok' 'a' 'b' 'm' 5 $state $categories @('ps2') ([ordered]@{ used_bytes = 1; total_bytes = 2 }) ([ordered]@{ ok = $true; at = 'c'; name = 'canary-x.txt'; message = 'ok' })
	$doc = New-CoreStatusDocument 'V3' $section ([ordered]@{ label = 'Games (E:)'; health = 'healthy'; free_bytes = 1; total_bytes = 2; message = 'OK' }) ([ordered]@{ reachable = $true; checked_at = 'x'; message = 'ok' })
	$path = Write-CoreStatus $doc $tmp
	$path = Write-CoreStatus $doc $tmp # replaces an existing file
	$read = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
	Check ($read.schema -eq 1 -and $read.server -eq 'V3') 'status: schema 1 and server'
	foreach ($key in @('result', 'started_at', 'finished_at', 'message', 'uploaded_bytes', 'consecutive_failures', 'last_success_at', 'categories', 'roms_skipped_platforms', 'pool', 'restore_check', 'canary')) {
		Check ($read.backup.PSObject.Properties.Name -contains $key) "status: backup.$key present"
	}
	Check (@($read.backup.roms_skipped_platforms).Count -eq 1 -and $read.backup.roms_skipped_platforms[0] -eq 'ps2') 'status: skipped platforms is an array'
	Check ($read.backup.categories.saves.PSObject.Properties.Name -contains 'files') 'status: category shape'
	Check (@(Get-ChildItem -LiteralPath $tmp -Filter '*.tmp').Count -eq 0) 'status: no temporary file left'
	# Same shape as the sample Core's own tests read (Source/CoreHealth.Tests), when the repo has it.
	$sample = Join-Path $backupDir '..\..\Source\CoreHealth.Tests\core-status.sample.json'
	$sample = $sample -replace '\\', [string][System.IO.Path]::DirectorySeparatorChar
	if (Test-Path -LiteralPath $sample) {
		function Get-KeyPaths($Node, [string]$Prefix) {
			$out = @()
			if ($Node -is [System.Management.Automation.PSCustomObject]) {
				foreach ($p in $Node.PSObject.Properties) { $out += "$Prefix$($p.Name)"; $out += Get-KeyPaths $p.Value "$Prefix$($p.Name)." }
			}
			return $out
		}
		$mine = @(Get-KeyPaths $read '' | Sort-Object)
		$theirs = @(Get-KeyPaths (Get-Content -LiteralPath $sample -Raw | ConvertFrom-Json) '' | Sort-Object)
		Check (@(Compare-Object $mine $theirs).Count -eq 0) 'status: same keys as the sample Core reads'
	}
	$bytes = [System.IO.File]::ReadAllBytes($path)
	Check (-not ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB)) 'status: UTF-8 without BOM'

	# ---------- layout ----------
	$libA = Join-Path $tmp 'libA'
	foreach ($d in @('roms/ps2', 'roms/gba', 'bios/ps2')) { [void](New-Item -ItemType Directory -Path (Join-Path $libA $d) -Force) }
	$layout = Get-RommLayout $libA 'auto'
	Check ($layout.Structure -eq 'A' -and $layout.Roms.Count -eq 2 -and $layout.Bios.Contains('*')) 'layout: structure A'
	$libB = Join-Path $tmp 'libB'
	foreach ($d in @('gba/roms', 'ps2/roms', 'ps2/bios')) { [void](New-Item -ItemType Directory -Path (Join-Path $libB $d) -Force) }
	$layout = Get-RommLayout $libB 'auto'
	Check ($layout.Structure -eq 'B' -and $layout.Roms.Count -eq 2 -and $layout.Bios.Contains('ps2')) 'layout: structure B'

	# ---------- a whole night with fakes ----------
	$lib = Join-Path $tmp 'library'; $assets = Join-Path $tmp 'assets'; $rommConfig = Join-Path $tmp 'config'; $stateDir = Join-Path $tmp 'state'; $statusDir = Join-Path $tmp 'status'
	foreach ($d in @("$lib/roms/ps2", "$lib/roms/gba", "$lib/bios/ps2", "$assets/users/u1/saves/gba/1/mgba", $rommConfig)) { [void](New-Item -ItemType Directory -Path $d -Force) }
	[System.IO.File]::WriteAllBytes((Join-Path $lib 'roms/ps2/big.iso'), (New-Object byte[] 3000))
	[System.IO.File]::WriteAllBytes((Join-Path $lib 'roms/gba/small.gba'), (New-Object byte[] 100))
	[System.IO.File]::WriteAllText((Join-Path $assets 'users/u1/saves/gba/1/mgba/game.srm'), 'save')
	[System.IO.File]::WriteAllText((Join-Path $rommConfig 'config.yml'), 'exclude: {}')
	foreach ($f in @(Get-ChildItem -LiteralPath $lib, $assets, $rommConfig -Recurse -File)) { $f.LastWriteTime = (Get-Date).AddDays(-10) }
	$configFile = Join-Path $tmp 'backup-config.json'
	$cfg = [ordered]@{
		server_name = 'TESTSERVER'; status_folder = $statusDir; state_folder = $stateDir
		pool = [ordered]@{ total_bytes = 10000; others_used_bytes = 2000; min_free_fraction = 0.25 }
		romm = [ordered]@{ library = $lib; assets = $assets; config = $rommConfig; url = 'http://localhost:8080' }
		drive = [ordered]@{ letter = 'E' }; restore_check = [ordered]@{ every_days = 30 }
	}
	[System.IO.File]::WriteAllText($configFile, (ConvertTo-Json $cfg -Depth 5))
	$calls = Join-Path $tmp 'calls.txt'
	$uploads = Join-Path $tmp 'uploads.txt'
	$canaryNames = Join-Path $tmp 'canary-names.txt'
	$remoteNames = Join-Path $tmp 'remote-names.txt'
	$legacyFlag = Join-Path $tmp 'legacy.flag'
	$loseCanary = Join-Path $tmp 'lose-canary.flag'
	$seams = Join-Path $tmp 'seams.ps1'
	@"
`$script:CoreProcessRunner = {
	param([string]`$FilePath, [string[]]`$Arguments, `$Deadline, [hashtable]`$Environment)
	Add-Content -LiteralPath '$calls' -Value (`$Arguments -join ' | ')
	`$joined = `$Arguments -join ' '
	if (`$joined -eq 'filesystem upload --help') {
		if (Test-Path -LiteralPath '$legacyFlag') { return @{ ExitCode = 0; StdOut = '  --conflict-strategy <s>'; StdErr = ''; TimedOut = `$false } }
		return @{ ExitCode = 0; StdOut = '  -f, --file-conflict-strategy <s>  -d, --folder-conflict-strategy <s>'; StdErr = ''; TimedOut = `$false }
	}
	if (`$joined -like 'filesystem list */canary --json') {
		`$names = @(); if (Test-Path -LiteralPath '$canaryNames') { `$names = @(Get-Content -LiteralPath '$canaryNames') }
		if (Test-Path -LiteralPath '$loseCanary') { `$names = @() }
		return @{ ExitCode = 0; StdOut = ('[' + ((`$names | ForEach-Object { '{"name":"' + `$_ + '"}' }) -join ',') + ']'); StdErr = ''; TimedOut = `$false }
	}
	if (`$joined -like 'filesystem list */saves/*') {
		`$out = '[]'; if (Test-Path -LiteralPath '$remoteNames') { `$out = Get-Content -LiteralPath '$remoteNames' -Raw }
		return @{ ExitCode = 0; StdOut = `$out; StdErr = ''; TimedOut = `$false }
	}
	if (`$joined -like 'filesystem list*') { return @{ ExitCode = 0; StdOut = '[]'; StdErr = ''; TimedOut = `$false } }
	if (`$joined -eq 'filesystem size /my-files --json') { return @{ ExitCode = 0; StdOut = '{"size":3000,"numberOfDescendants":4}'; StdErr = ''; TimedOut = `$false } }
	if (`$joined -like 'filesystem size*') { return @{ ExitCode = 1; StdOut = ''; StdErr = 'not found'; TimedOut = `$false } }
	if (`$joined -like 'filesystem upload*') {
		`$local = `$Arguments[2]
		`$files = @(Get-ChildItem -LiteralPath `$local -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { `$_.Name } | Sort-Object)
		Add-Content -LiteralPath '$uploads' -Value ((Split-Path -Leaf `$local) + ' <- ' + `$local + ': ' + (`$files -join ','))
		if ((Split-Path -Leaf `$local) -eq 'canary') { foreach (`$f in `$files) { Add-Content -LiteralPath '$canaryNames' -Value `$f } }
		return @{ ExitCode = 0; StdOut = '{"transferredItems":1,"transferredBytes":10,"skippedItems":0,"failedItems":0,"failures":[]}'; StdErr = ''; TimedOut = `$false }
	}
	if (`$joined -like 'filesystem download*') {
		`$target = `$Arguments[3]; `$name = `$Arguments[2].Substring(`$Arguments[2].LastIndexOf('/') + 1)
		`$plain = `$name -replace ' \(\d+\)', ''
		`$source = Get-ChildItem -LiteralPath '$tmp' -Recurse -File -Filter `$plain | Where-Object { `$_.FullName -notlike '*CoreRestore*' } | Select-Object -First 1
		Copy-Item -LiteralPath `$source.FullName -Destination (Join-Path `$target `$name)
		return @{ ExitCode = 0; StdOut = '{"transferredItems":1}'; StdErr = ''; TimedOut = `$false }
	}
	return @{ ExitCode = 2; StdOut = ''; StdErr = 'unexpected'; TimedOut = `$false }
}
`$script:DatabaseDumper = { param(`$Container, `$Destination) [System.IO.File]::WriteAllText(`$Destination, '-- Dump completed'); @{ Ok = `$true; Message = 'ok' } }
`$script:DriveProbe = { param(`$Letter) @{ Label = 'Games'; HealthStatus = 'Healthy'; OperationalStatus = @('OK'); Reliability = `$null; FreeBytes = [int64]500; TotalBytes = [int64]1000 } }
`$script:HttpProbe = { param(`$Url) @{ Ok = `$true; Status = 200 } }
`$script:DockerState = { param(`$Container) 'running' }
"@ | Set-Content -LiteralPath $seams -Encoding UTF8
	$shell = (Get-Process -Id $PID).Path
	function Invoke-Night { & $shell -NoProfile -File (Join-Path $backupDir 'Backup-Core.ps1') -ConfigPath $configFile -SeamsScript $seams | Out-Null; return $LASTEXITCODE }
	function Read-Status { Get-Content -LiteralPath (Join-Path $statusDir 'core-status.json') -Raw | ConvertFrom-Json }
	Check ((Invoke-Night) -eq 0) 'night: Backup-Core exits 0'
	$status = Read-Status
	Check ($status.server -eq 'TESTSERVER' -and $status.schema -eq 1) 'night: status written'
	# Pool 10000, used 3000 + 2000, floor 2500: gba (100) fits, ps2 (3000) does not.
	Check (@($status.backup.roms_skipped_platforms) -contains 'ps2' -and @($status.backup.roms_skipped_platforms) -notcontains 'gba') 'night: ps2 skipped, gba uploaded'
	Check ($status.backup.result -eq 'ok') "night: result ok ($($status.backup.message))"
	Check ($status.backup.canary.ok -eq $true -and $status.backup.canary.name -like 'canary-*.txt') 'night: canary uploaded and listed'
	Check ($status.backup.pool.total_bytes -eq 10000 -and $status.backup.pool.used_bytes -eq 5000) 'night: pool reported'
	Check ($status.drive.health -eq 'healthy' -and $status.romm.reachable -eq $true) 'night: drive and RomM'
	$log = Get-Content -LiteralPath $calls
	$uploadLines = @($log | Where-Object { $_ -like 'filesystem | upload*' -and $_ -notlike '*--help*' })
	Check (@($uploadLines | Where-Object { $_ -notmatch 'file-conflict-strategy \| (skip|rename) \| --folder-conflict-strategy \| merge' }).Count -eq 0) 'night: every upload merges folders and never replaces files'
	Check (@($uploadLines | Where-Object { $_ -match '[\\/]assets \|' -and $_ -match 'file-conflict-strategy \| rename' }).Count -eq 1) 'night: saves keep every version (rename)'
	Check (@($uploadLines | Where-Object { $_ -match 'romm-dumps' -and $_ -match 'file-conflict-strategy \| rename' }).Count -eq 1) 'night: RomM dump keeps every version (rename)'
	Check (@($uploadLines | Where-Object { $_ -match 'roms.gba|bios' } | Where-Object { $_ -notmatch 'file-conflict-strategy \| skip' }).Count -eq 0) 'night: ROMs and BIOS skip existing files'
	Check (@($log | Where-Object { $_ -notlike 'filesystem | download*' -and $_ -match 'replace|create-new-revision|trash|delete|\bmove\b|filesystem \| rename' }).Count -eq 0) 'night: no destructive CLI command or strategy'
	Check (@($log | Where-Object { $_ -like '*upload*roms*ps2*' }).Count -eq 0) 'night: ps2 never uploaded'
	Check (@($log | Where-Object { $_ -like 'filesystem | download*' }).Count -eq 3) 'night: first run downloads saves, RomM and ROM samples (BIOS folder empty)'
	Check ($status.backup.restore_check.result -eq 'ok' -and $status.backup.restore_check.message -match 'bios: nothing to check') "night: restore check ok ($($status.backup.restore_check.message))"
	Check (Test-Path -LiteralPath (Join-Path $stateDir 'saves-manifest.json')) 'night: saves manifest written after a clean upload'
	Check (-not (Test-Path -LiteralPath (Join-Path $stateDir 'saves-staging'))) 'night: no staging folder left'

	# Night 2, nothing changed: no saves upload at all (a renamed copy would duplicate every night).
	Remove-Item -LiteralPath $uploads -Force
	Check ((Invoke-Night) -eq 0) 'unchanged: exits 0'
	Check (@(Get-Content -LiteralPath $uploads | Where-Object { $_ -like 'assets <-*' }).Count -eq 0) 'unchanged: saves not uploaded again'
	# Night 3: one save changed, one new: only those two are staged and uploaded.
	$saveDir = Join-Path $assets 'users/u1/saves/gba/1/mgba'
	[System.IO.File]::WriteAllText((Join-Path $saveDir 'game.srm'), 'save v2')
	[System.IO.File]::WriteAllText((Join-Path $saveDir 'other.srm'), 'new')
	[System.IO.File]::WriteAllText((Join-Path $saveDir 'same.srm'), 'x')
	(Get-Item -LiteralPath (Join-Path $saveDir 'same.srm')).LastWriteTime = (Get-Date).AddDays(-10)
	Remove-Item -LiteralPath $uploads -Force
	Check ((Invoke-Night) -eq 0) 'changed: exits 0'
	$savesUpload = @(Get-Content -LiteralPath $uploads | Where-Object { $_ -like 'assets <-*' })
	Check ($savesUpload.Count -eq 1 -and $savesUpload[0] -match 'saves-staging' -and $savesUpload[0] -match ': game.srm,other.srm,same.srm$') "changed: only new and changed saves staged ($savesUpload)"
	Remove-Item -LiteralPath $uploads -Force
	Check ((Invoke-Night) -eq 0) 'changed then unchanged: exits 0'
	Check (@(Get-Content -LiteralPath $uploads | Where-Object { $_ -like 'assets <-*' }).Count -eq 0) 'changed then unchanged: each change uploaded once'

	# Restore check against the newest renamed copy.
	'[{"name":"game.srm"},{"name":"game (1).srm"},{"name":"game (2).srm"}]' | Set-Content -LiteralPath $remoteNames
	$savedRunner = $script:CoreProcessRunner
	. $seams
	$restoreState = New-CoreBackupState
	$restoreConfig = Get-CoreBackupConfig $configFile
	$restoreConfig.romm.library = Join-Path $tmp 'nolibrary'
	(Get-Item -LiteralPath (Join-Path $saveDir 'game.srm')).LastWriteTime = (Get-Date).AddDays(-5)
	foreach ($f in @(Get-ChildItem -LiteralPath $saveDir -File)) { if ($f.Name -ne 'game.srm') { $f.LastWriteTime = (Get-Date) } }
	$check = Invoke-CoreRestoreCheck $restoreConfig $restoreState
	$script:CoreProcessRunner = $savedRunner
	Check ($check.message -match 'saves: \S*game \(2\)\.srm matches') "restore: renamed category compared with the newest copy ($($check.message))"

	# A missing canary fails the night even though every upload said ok.
	New-Item -ItemType File -Path $loseCanary -Force | Out-Null
	Check ((Invoke-Night) -eq 0) 'canary lost: run completes'
	$status = Read-Status
	Check ($status.backup.result -eq 'failed' -and $status.backup.canary.ok -eq $false -and $status.backup.message -match 'Canary: .*not listed') "canary lost: night failed with a message ($($status.backup.message))"
	Remove-Item -LiteralPath $loseCanary -Force

	# The legacy single-option CLI is refused: nothing is uploaded and Core is told why.
	New-Item -ItemType File -Path $legacyFlag -Force | Out-Null
	Remove-Item -LiteralPath $uploads -Force -ErrorAction SilentlyContinue
	Check ((Invoke-Night) -eq 1) 'old CLI: exit 1'
	$status = Read-Status
	Check ($status.backup.result -eq 'failed' -and $status.backup.message -match 'Proton CLI too old') "old CLI: failed, says Proton CLI too old ($($status.backup.message))"
	Check (-not (Test-Path -LiteralPath $uploads)) 'old CLI: nothing uploaded'
	Remove-Item -LiteralPath $legacyFlag -Force

	# A run without a Proton session fails, keeps counting and still reports health.
	(Get-Content -LiteralPath $seams -Raw).Replace('if ($joined -like ''filesystem list*'') { return @{ ExitCode = 0', 'if ($joined -like ''filesystem list*'') { return @{ ExitCode = 1; StdErr = ''You need to login first''; StdOut = ''''; TimedOut = $false } }; if ($false) { return @{ ExitCode = 0') | Set-Content -LiteralPath $seams -Encoding UTF8
	& $shell -NoProfile -File (Join-Path $backupDir 'Backup-Core.ps1') -ConfigPath $configFile -SeamsScript $seams | Out-Null
	Check ($LASTEXITCODE -eq 1) 'signed out: exit 1'
	$status = Get-Content -LiteralPath (Join-Path $statusDir 'core-status.json') -Raw | ConvertFrom-Json
	Check ($status.backup.result -eq 'failed' -and $status.backup.consecutive_failures -eq 3 -and $status.backup.message -match 'auth login') 'signed out: failed, counted, tells the user to sign in'
	Check ($null -ne $status.backup.last_success_at -and $status.drive.health -eq 'healthy') 'signed out: last success kept, health still checked'
	$text = Get-Content -LiteralPath (Join-Path $statusDir 'core-status.json') -Raw
	Check ($text -notmatch 'password|token|MYSQL_PWD') 'status: no credential words'
}
finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host "ALL PASS ($script:passed checks)"
exit 0
