<#
.SYNOPSIS
  Restore check: downloads one random file from each backup category and compares its SHA-256 with the
  original on the server.

.DESCRIPTION
  Backup-Core.ps1 runs the same check once a month (restore_check.every_days). Run this script to do it
  now. Files are downloaded to a new temporary folder that is deleted afterwards; nothing local or in
  the cloud is changed. Only files older than the last successful backup (and at least two days old)
  are picked, because a file changed after its upload keeps its first copy in the cloud.
  The result is stored for the next core-status.json, which this script also rewrites.
#>
[CmdletBinding()]
param([string]$ConfigPath = (Join-Path $PSScriptRoot 'backup-config.json'))

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CoreBackup.Common.ps1')

$config = Get-CoreBackupConfig $ConfigPath
$logFolder = Join-Path ([string]$config.state_folder) 'logs'
if (-not (Test-Path -LiteralPath $logFolder)) { [void](New-Item -ItemType Directory -Path $logFolder -Force) }
$script:CoreLogPath = Join-Path $logFolder ('restore-{0}.log' -f (Get-Date -Format 'yyyyMMdd'))
$statePath = Join-Path ([string]$config.state_folder) 'backup-state.json'
$state = Read-CoreBackupState $statePath

$signIn = Test-ProtonSignedIn $config
if (-not $signIn.SignedIn) { Write-CoreLog "Proton Drive: $($signIn.Message). Run proton-drive auth login yourself first."; exit 1 }

$check = Invoke-CoreRestoreCheck $config $state
Write-CoreLog "Restore check: $($check.result) - $($check.message)"
$state.restore_check = $check
if ($state.last_backup) { $state.last_backup.restore_check = $check }
Save-JsonFile $state $statePath

# Refresh the status file so Core sees the result now, not after the next night.
& (Join-Path $PSScriptRoot 'Backup-Core.ps1') -ConfigPath $ConfigPath -HealthOnly | Out-Null
if ($check.result -eq 'ok') { exit 0 }
exit 1
