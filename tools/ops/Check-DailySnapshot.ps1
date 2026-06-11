<#
.SYNOPSIS
    Dead-man gap-day monitor: verifies today's DailySnapshot entry exists (S6-004).

.DESCRIPTION
    Observability only — reads a JSON file and (on a gap) posts one Discord embed.
    It never touches TWS, the worker, or any trading state, and is safe to run while
    the live host is up.

    Snapshot location of record: JsonSnapshotRepository writes
    Path.Combine(LocalStorage:DataDirectory, "snapshots.json") with DataDirectory="data"
    (relative), resolved against the worker's working directory. The
    TradingSystem-FuncHost scheduled task action is
    `cmd /c cd /d D:\Source\TradingSystem\src\TradingSystem.Functions && func start ...`,
    so the worker cwd — verified against the registered task on 2026-06-10 — is
    D:\Source\TradingSystem\src\TradingSystem.Functions and the file of record is
    D:\Source\TradingSystem\src\TradingSystem.Functions\data\snapshots.json (the
    -SnapshotPath default). JSON is camelCase with ISO-8601 dates ("date" property).

    Trading-day logic is weekday-only (Mon-Fri). Market-holiday false positives
    (~9/year) are ACCEPTED and benign: "no snapshot today" on a holiday is expected —
    the operator ignores the alert (see docs/paper-validation-runbook.md section 5).
    No holiday calendar, no Polygon call from PowerShell — by design (spec D11).

    Secrets: the Discord webhook URL is read from the gitignored local.settings.json
    and flows file -> variable -> Invoke-RestMethod -Uri ONLY. It is never written to
    console, logs, errors, or transcript, and never interpolated into any string.

.PARAMETER Date
    The trading date to check (default: today). Override for fixture-driven testing
    and for controlled drills against past dates.

.PARAMETER SnapshotPath
    Path to snapshots.json (default: the live worker's file of record, see above).

.PARAMETER SettingsPath
    Path to the gitignored local.settings.json holding Discord:Enabled /
    Discord:WebhookUrl (default: the live worker's settings file).

.PARAMETER WhatIf
    Evaluate and print the verdict, but never POST. Exits with the code the real run
    would produce assuming delivery success (so fixtures are deterministic).

.NOTES
    Exit codes (diagnosable from scheduled-task history):
      0 = snapshot present (or weekend — nothing expected)
      1 = snapshot missing, alert skipped (Discord disabled, settings/webhook absent)
      2 = snapshot missing, alert delivery FAILED
      3 = snapshot missing, alert delivered (or would be, under -WhatIf)

    Never auto-registered or run by CI, tests, hooks, or agent pipelines.
    Registration is an explicit operator action via Register-GapDayMonitorTask.ps1.
    Requires PowerShell 7 (pwsh).
#>
[CmdletBinding()]
param(
    [DateTime]$Date = (Get-Date),
    [string]$SnapshotPath = 'D:\Source\TradingSystem\src\TradingSystem.Functions\data\snapshots.json',
    [string]$SettingsPath = 'D:\Source\TradingSystem\src\TradingSystem.Functions\local.settings.json',
    [switch]$WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$dateLabel = $Date.ToString('yyyy-MM-dd')

# --- 1. Trading-day check (weekday-only; holiday false positives accepted, see header) ---
if ($Date.DayOfWeek -in @([DayOfWeek]::Saturday, [DayOfWeek]::Sunday)) {
    exit 0
}

# --- 2. Look for a snapshot entry whose date part equals -Date ---
$found = $false
if (Test-Path -LiteralPath $SnapshotPath) {
    try {
        $entries = Get-Content -LiteralPath $SnapshotPath -Raw | ConvertFrom-Json
        foreach ($entry in @($entries)) {
            if ($null -eq $entry -or -not ($entry.PSObject.Properties.Name -contains 'date')) { continue }
            $entryDate = if ($entry.date -is [DateTime]) {
                $entry.date
            } else {
                [DateTime]::Parse([string]$entry.date,
                    [System.Globalization.CultureInfo]::InvariantCulture,
                    [System.Globalization.DateTimeStyles]::RoundtripKind)
            }
            if ($entryDate.Date -eq $Date.Date) { $found = $true; break }
        }
    }
    catch {
        # Unreadable/malformed file counts as missing — still a useful dead-man signal.
        Write-Warning "Could not parse snapshot file ($($_.Exception.GetType().Name)); treating as missing."
    }
}

if ($found) {
    Write-Host "OK: DailySnapshot entry present for $dateLabel."
    exit 0
}

Write-Warning "GAP: no DailySnapshot entry for $dateLabel (weekday). If today is a market holiday this is a benign false positive — see runbook section 5."

# --- 3. Read Discord settings from the gitignored local.settings.json ---
$discordEnabled = $false
$webhookUrl = $null   # flows file -> this variable -> -Uri only; NEVER logged or interpolated
if (Test-Path -LiteralPath $SettingsPath) {
    try {
        $cfg = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
        $discordEnabled = ([string]$cfg.Values.'Discord:Enabled') -eq 'true'
        $webhookUrl = [string]$cfg.Values.'Discord:WebhookUrl'
    }
    catch {
        Write-Warning "Could not parse settings file ($($_.Exception.GetType().Name))."
    }
}
else {
    Write-Warning 'Settings file not found.'
}

if (-not $discordEnabled -or [string]::IsNullOrWhiteSpace($webhookUrl) -or $webhookUrl -eq 'YOUR_DISCORD_WEBHOOK_URL') {
    Write-Warning 'Alert skipped: Discord disabled or webhook not configured. Snapshot is still MISSING — investigate per runbook section 5.'
    exit 1
}

# --- 4. Alert (orange embed; date + runbook pointer only — no paths, no config values) ---
$title = "Dead-Man Alert — No EOD Snapshot for $dateLabel"
if ($WhatIf) {
    Write-Host "WhatIf: would POST '$title' to the configured Discord webhook (not sent)."
    exit 3
}

$payload = @{
    username = 'TradingSystem Ops'
    embeds   = @(
        @{
            title       = $title
            description = "No DailySnapshot entry was written for $dateLabel. The worker may be down (dead-man case) — triage per docs/paper-validation-runbook.md section 5, first row. If $dateLabel was a market holiday, this is an expected false positive: ignore."
            color       = 15105570   # orange — operational failure class
        }
    )
} | ConvertTo-Json -Depth 5

try {
    Invoke-RestMethod -Uri $webhookUrl -Method Post -ContentType 'application/json' `
        -Body $payload -TimeoutSec 10 | Out-Null
    Write-Host 'Alert delivered.'
    exit 3
}
catch {
    # Exception text could echo the request URI — report the type name only.
    Write-Error -ErrorAction Continue "Alert delivery FAILED ($($_.Exception.GetType().Name)). Snapshot is still MISSING for $dateLabel."
    exit 2
}
