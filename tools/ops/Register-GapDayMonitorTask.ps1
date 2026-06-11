<#
.SYNOPSIS
    Registers the TradingSystem-GapDayMonitor scheduled task (one-time MANUAL operator step).

.DESCRIPTION
    Registers a Windows scheduled task that runs Check-DailySnapshot.ps1 on weekdays at
    2:30 PM local time — at least 45 minutes after the EOD timer in BOTH DST regimes
    (EOD = 1:30 PM PDT / 12:30 PM PST), so the snapshot has had time to land.

    Idempotent: re-running unregisters the existing task and registers it again
    (useful after moving the repo or changing the schedule).

    Quoting note: the -File path is wrapped in literal double quotes inside the
    argument string (the S5 TradingSystem-Azurite cmd-quoting bug is the cautionary
    precedent). Verify after registration with:
        schtasks /query /tn TradingSystem-GapDayMonitor /v /fo LIST

.PARAMETER TaskName
    Scheduled task name (default: TradingSystem-GapDayMonitor — repo task convention).

.PARAMETER At
    Local wall-clock run time (default 2:30 PM). Do not move earlier than 2:15 PM:
    the EOD timer fires at 1:30 PM local in PDT (12:30 PM in PST), and the monitor
    must stay >=45 minutes behind it in both DST regimes.

.PARAMETER ScriptPath
    Absolute path to Check-DailySnapshot.ps1 (default: sibling of this script).

.NOTES
    NEVER invoked by CI, tests, hooks, or any agent pipeline — registration is a
    human/runbook action (docs/paper-validation-runbook.md, gap-day monitor subsection).
    Requires PowerShell 7 (pwsh) and rights to register scheduled tasks.
#>
[CmdletBinding()]
param(
    [string]$TaskName = 'TradingSystem-GapDayMonitor',
    [string]$At = '14:30',
    [string]$ScriptPath = (Join-Path $PSScriptRoot 'Check-DailySnapshot.ps1')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptPath = (Resolve-Path -LiteralPath $ScriptPath).Path
$repoRoot = Split-Path (Split-Path (Split-Path $ScriptPath))   # tools/ops/.. /.. = repo root
$pwshPath = (Get-Command pwsh).Source

# Literal quotes around the -File path — the Azurite task's quoting bug is the precedent.
$arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$ScriptPath`""

$action = New-ScheduledTaskAction -Execute $pwshPath -Argument $arguments -WorkingDirectory $repoRoot
$trigger = New-ScheduledTaskTrigger -Weekly -At $At -DaysOfWeek Monday, Tuesday, Wednesday, Thursday, Friday
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 5)

$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Task '$TaskName' already exists — unregistering for idempotent re-register."
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -Description 'TradingSystem dead-man gap-day monitor: checks for today''s DailySnapshot entry, weekdays 2:30 PM local (S6-004). Exit codes: 0 present/weekend, 1 missing+skipped, 2 missing+alert-failed, 3 missing+alert-delivered.' | Out-Null

Write-Host "Registered scheduled task '$TaskName':"
Get-ScheduledTask -TaskName $TaskName | Select-Object -ExpandProperty Actions |
    Format-List Execute, Arguments, WorkingDirectory
Get-ScheduledTask -TaskName $TaskName | Select-Object -ExpandProperty Triggers |
    Format-List StartBoundary, DaysOfWeek
Write-Host "Verify quoting with: schtasks /query /tn $TaskName /v /fo LIST"
