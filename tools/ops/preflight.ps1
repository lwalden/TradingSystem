<#
.SYNOPSIS
    Daily preflight checks for the paper-validation run (automates runbook section 2).

.DESCRIPTION
    Read-only probes with bounded timeouts — safe to run while the live host is up;
    touches no trading state and reads no secrets at all.

    Checks (docs/paper-validation-runbook.md section 2):
      (a) TWS paper API port reachable (TcpClient, 3s timeout — not Test-NetConnection,
          which can stall)                                                  -> PASS/FAIL
      (b) claude-gateway /health (5s timeout). Gateway-down is the DESIGNED
          rules-fallback degrade (ADR-029/ADR-030)                          -> PASS/WARN
      (c) Functions worker admin endpoint lists all 4 functions:
          DailyOrchestrator_PreMarket, DailyOrchestrator_EndOfDay,
          IncomeSleeve_MonthlyReinvest, IncomeSleeve_QuarterlyAudit         -> PASS/FAIL
      (d) snapshots data directory exists                                   -> PASS/FAIL

    Exit code: 0 = no FAIL (WARNs allowed), 1 = at least one FAIL.

.NOTES
    Never auto-run by CI, tests, hooks, or agent pipelines — operator tooling only.
    Requires PowerShell 7 (pwsh).
#>
[CmdletBinding()]
param(
    [string]$TwsHost = '127.0.0.1',
    [int]$TwsPort = 7497,
    [string]$GatewayHealthUrl = 'http://localhost:3131/health',
    [string]$WorkerAdminUrl = 'http://127.0.0.1:7071/admin/functions',
    [string]$DataDirectory = 'D:\Source\TradingSystem\src\TradingSystem.Functions\data'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedFunctions = @(
    'DailyOrchestrator_PreMarket',
    'DailyOrchestrator_EndOfDay',
    'IncomeSleeve_MonthlyReinvest',
    'IncomeSleeve_QuarterlyAudit'
)

$results = [System.Collections.Generic.List[object]]::new()
function Add-Result([string]$Check, [string]$Status, [string]$Detail) {
    $results.Add([pscustomobject]@{ Check = $Check; Status = $Status; Detail = $Detail })
}

# --- (a) TWS API port: TcpClient connect with a 3s timeout ---
$client = [System.Net.Sockets.TcpClient]::new()
try {
    $connectTask = $client.ConnectAsync($TwsHost, $TwsPort)
    if ($connectTask.Wait(3000) -and $client.Connected) {
        Add-Result 'TWS API port' 'PASS' "TCP connect to ${TwsHost}:${TwsPort} succeeded"
    }
    else {
        Add-Result 'TWS API port' 'FAIL' "No TCP connect to ${TwsHost}:${TwsPort} within 3s — TWS down, not logged in, or API disabled (runbook section 2 step 1)"
    }
}
catch {
    # ConnectAsync faults (e.g. connection refused) surface here via Task.Wait — same verdict as a timeout.
    Add-Result 'TWS API port' 'FAIL' "No TCP connect to ${TwsHost}:${TwsPort} (refused or unreachable) — TWS down, not logged in, or API disabled (runbook section 2 step 1)"
}
finally {
    $client.Dispose()
}

# --- (b) claude-gateway health: WARN, not FAIL (designed degrade per ADR-029/ADR-030) ---
try {
    Invoke-RestMethod -Uri $GatewayHealthUrl -TimeoutSec 5 | Out-Null
    Add-Result 'claude-gateway' 'PASS' 'Health endpoint responded'
}
catch {
    Add-Result 'claude-gateway' 'WARN' 'Gateway down — system degrades to deterministic rule-based regime classification (ADR-029/ADR-030): expected behavior, not an incident. Restart the gateway when convenient'
}

# --- (c) Functions worker: admin endpoint must list all 4 functions ---
try {
    $functions = Invoke-RestMethod -Uri $WorkerAdminUrl -TimeoutSec 5
    $names = @($functions | ForEach-Object { $_.name })
    $missing = @($expectedFunctions | Where-Object { $_ -notin $names })
    if ($missing.Count -eq 0) {
        Add-Result 'Functions worker' 'PASS' "All 4 functions registered ($($names.Count) total)"
    }
    else {
        Add-Result 'Functions worker' 'FAIL' "Worker is up but missing function(s): $($missing -join ', ') — broken build or wrong project started (runbook section 2 step 5)"
    }
}
catch {
    Add-Result 'Functions worker' 'FAIL' 'Admin endpoint unreachable — worker not running or still booting (runbook section 2 steps 3-4)'
}

# --- (d) snapshots data directory ---
if (Test-Path -LiteralPath $DataDirectory -PathType Container) {
    Add-Result 'Data directory' 'PASS' 'Snapshots data directory exists'
}
else {
    Add-Result 'Data directory' 'FAIL' "Data directory not found at the expected worker cwd location: $DataDirectory"
}

# --- Summary ---
$results | Format-Table -AutoSize -Wrap | Out-String | Write-Host

$failCount = @($results | Where-Object Status -eq 'FAIL').Count
if ($failCount -gt 0) {
    Write-Host "Preflight: $failCount FAIL check(s) — do not assume the day will run."
    exit 1
}
Write-Host 'Preflight: all checks passed (WARNs, if any, are designed degrades).'
exit 0
