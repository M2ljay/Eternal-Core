<#
.SYNOPSIS
  One-time setup of Core's nightly server run (run in an elevated PowerShell on the server).

.DESCRIPTION
  - Checks that the Proton Drive CLI is installed and signed in. It never signs in for you: if there is
    no session it tells you to run "proton-drive auth login" yourself, in this same Windows account.
  - Creates the status folder and shares it READ-ONLY as \\<server>\CoreStatus (read for Everyone, or the
    account in share.read_account).
  - Registers the Task Scheduler job "Core nightly backup" at schedule.time (default 05:30).
  - Writes a first core-status.json (health only) so Core on the PC has something to read.

  The task runs as your Windows account, because the Proton Drive CLI keeps its session in your
  Credential Manager and Docker Desktop runs in your session. By default it runs only while you are
  signed in (most home servers stay signed in). -RunWhetherSignedIn opens the standard Windows
  credential prompt so Task Scheduler can store your password itself; this script never sees it
  outside that prompt's object, never writes it and never logs it.

.PARAMETER ConfigPath
  backup-config.json (default: next to this script). Created from backup-config.example.json if missing.

.PARAMETER RunWhetherSignedIn
  Register the task to run even when nobody is signed in (asks for your Windows password via the
  standard credential prompt; Task Scheduler stores it).

.PARAMETER SkipSignInCheck
  Register the task even if the Proton Drive CLI is not signed in yet.

.PARAMETER RunNow
  Start the nightly task once right after registering it.
#>
[CmdletBinding()]
param(
	[string]$ConfigPath = (Join-Path $PSScriptRoot 'backup-config.json'),
	[switch]$RunWhetherSignedIn,
	[switch]$SkipSignInCheck,
	[switch]$RunNow
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CoreBackup.Common.ps1')
$taskName = 'Core nightly backup'

function Say([string]$Text) { Write-Host $Text }
function Stop-Setup([string]$Text) { Write-Host ''; Write-Host $Text -ForegroundColor Yellow; exit 1 }

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
	Stop-Setup 'Run this in PowerShell opened with "Run as administrator" (the share and the task need it).'
}

if (-not (Test-Path -LiteralPath $ConfigPath)) {
	Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'backup-config.example.json') -Destination $ConfigPath
	Stop-Setup "Created $ConfigPath from the example. Edit the RomM paths, drive letter and pool size in it, then run this again."
}
$config = Get-CoreBackupConfig $ConfigPath
Say "Config: $ConfigPath (server name $($config.server_name))"

# 1. Proton Drive CLI.
$cli = Get-Command ([string]$config.proton.cli_path) -ErrorAction SilentlyContinue
if (-not $cli) {
	Stop-Setup ("The Proton Drive CLI was not found ('{0}'). Download it from https://proton.me/drive/download (Proton Drive CLI, Windows), put proton-drive.exe in a folder on PATH or set proton.cli_path in the config to its full path, then run this again." -f $config.proton.cli_path)
}
$config.proton.cli_path = $cli.Source
$version = Invoke-ProtonCli $config @('version')
Say ("Proton Drive CLI: {0} ({1})" -f $cli.Source, (($version.StdOut -split "`r?`n" | Where-Object { $_.Trim() }) -join ' / '))
$help = Invoke-ProtonCli $config @('filesystem', 'upload', '--help')
$conflict = Get-ProtonConflictArguments ($help.StdOut + "`n" + $help.StdErr) ([string]$config.proton.file_conflict_strategy)
if (-not $conflict) { Stop-Setup 'This Proton Drive CLI version offers no upload conflict strategy that the script knows. Nothing was set up. See README, "CLI flags".' }
Say ("Upload options this CLI accepts: {0}" -f ($conflict -join ' '))
if (-not $SkipSignInCheck) {
	$signIn = Test-ProtonSignedIn $config
	if (-not $signIn.SignedIn) {
		Stop-Setup ("Proton Drive is {0}. In a normal (not elevated is fine) PowerShell window, signed in as {1}, run:`n`n    proton-drive auth login`n`nSign in in the browser that opens (your password goes only to Proton), then run this setup again." -f $signIn.Message, $env:USERNAME)
	}
	Say 'Proton Drive: signed in.'
}

# 2. Folders and the read-only share.
$statusFolder = [string]$config.status_folder
foreach ($folder in @($statusFolder, [string]$config.state_folder)) {
	if (-not (Test-Path -LiteralPath $folder)) { [void](New-Item -ItemType Directory -Path $folder -Force); Say "Created $folder" }
}
$shareName = [string]$config.share.name
$reader = [string]$config.share.read_account
$share = Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue
if ($share -and $share.Path.TrimEnd('\') -ne $statusFolder.TrimEnd('\')) {
	Stop-Setup "A share named $shareName already exists for $($share.Path). Remove it or pick another share.name."
}
if (-not $share) {
	[void](New-SmbShare -Name $shareName -Path $statusFolder -ReadAccess $reader -Description 'Core server status (read-only)')
	Say "Shared $statusFolder as \\$($config.server_name)\$shareName (read: $reader)."
}
else {
	[void](Grant-SmbShareAccess -Name $shareName -AccountName $reader -AccessRight Read -Force)
	foreach ($entry in @(Get-SmbShareAccess -Name $shareName | Where-Object { $_.AccessRight -ne 'Read' -and $_.AccessControlType -eq 'Allow' })) {
		[void](Revoke-SmbShareAccess -Name $shareName -AccountName $entry.AccountName -Force)
		Say "Removed $($entry.AccessRight) access for $($entry.AccountName) on the share (it must stay read-only)."
	}
	Say "Share \\$($config.server_name)\$shareName already exists; read access for $reader confirmed."
}
# NTFS: the reader may read, never write. The account running the backup keeps full control.
& icacls.exe $statusFolder /grant ('{0}:(OI)(CI)RX' -f $reader) | Out-Null
& icacls.exe $statusFolder /grant ('{0}:(OI)(CI)F' -f "$env:USERDOMAIN\$env:USERNAME") | Out-Null

# 3. The nightly task.
$script = Join-Path $PSScriptRoot 'Backup-Core.ps1'
$arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -ConfigPath "{1}"' -f $script, (Resolve-Path -LiteralPath $ConfigPath).Path
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments -WorkingDirectory $PSScriptRoot
$time = [DateTime]::ParseExact([string]$config.schedule.time, 'H:mm', [Globalization.CultureInfo]::InvariantCulture)
$trigger = New-ScheduledTaskTrigger -Daily -At $time
$limit = New-TimeSpan -Hours ([double]$config.schedule.max_hours + 2)
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -WakeToRun -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
	-ExecutionTimeLimit $limit -MultipleInstances IgnoreNew
$user = "$env:USERDOMAIN\$env:USERNAME"
if ($RunWhetherSignedIn) {
	$credential = Get-Credential -UserName $user -Message 'Your Windows password, so Task Scheduler can run the backup while nobody is signed in. Task Scheduler stores it; this script does not.'
	if (-not $credential) { Stop-Setup 'No credential entered; the task was not registered.' }
	[void](Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -RunLevel Highest `
		-User $credential.UserName -Password $credential.GetNetworkCredential().Password -Force)
	$credential = $null
	Say "Task '$taskName' registered at $($config.schedule.time), runs whether or not you are signed in."
}
else {
	$taskPrincipal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
	[void](Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $taskPrincipal -Force)
	Say "Task '$taskName' registered at $($config.schedule.time), runs while $user is signed in."
}

# 4. First status file.
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -ConfigPath (Resolve-Path -LiteralPath $ConfigPath).Path -HealthOnly | Out-Null
if (Test-Path -LiteralPath (Join-Path $statusFolder 'core-status.json')) { Say "First status written: $(Join-Path $statusFolder 'core-status.json')" }

if ($RunNow) { Start-ScheduledTask -TaskName $taskName; Say "Started '$taskName'. Its log is in $(Join-Path $config.state_folder 'logs')." }
Say ''
Say "Done. On the PC, set Core > Gaming Mode > Core Health > Server to: $($config.server_name)"
