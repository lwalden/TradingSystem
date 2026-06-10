# Paper-Validation Launch Runbook

Operating guide for the 12-week SANDBOX paper-validation run (ADR-010, ADR-030, PDR-004).
This document is the system of record for the validation start date (Run Log, bottom of this
file) and the first place to look when something does not behave as expected.

Audience: the operator (single dev box). Written S5-004, 2026-06-10.

---

## 1. Run model (ADR-031)

The Azure Functions isolated worker runs **locally on the dev box**, not in Azure:

- TWS (paper, API port **7497**) and claude-gateway (**localhost:3131**) are both
  **loopback-only** (ADR-029). The worker must share the host with them — an Azure-hosted
  worker cannot reach either. Full rationale and alternatives: ADR-031 in `DECISIONS.md`.
- Start the worker:
  ```powershell
  cd D:\Source\TradingSystem\src\TradingSystem.Functions
  func start          # or: dotnet run
  ```
- **Machine-on requirement:** the dev box and the worker must be up across both timer
  firings (see section 3). Sleep/hibernate counts as down.
- **Missed runs do not catch up.** If the worker was not running when a timer was due,
  that run is simply skipped — a gap day shows up as a missing entry in
  `data/snapshots.json` and no Discord report for that date. Gap days are tolerated by
  the ≥12-week window (ADR-031); triage is in section 5, not panic.

## 2. Daily preflight (before the pre-market timer)

Run this check each trading morning **before 5:00 AM PT in winter (PST) / 6:00 AM PT in summer
(PDT)** — the winter deadline is the earlier one (see section 3):

1. **TWS paper** is running and logged in to the paper account; API enabled with
   socket port **7497** and trusted IP **127.0.0.1** (Configure → API → Settings).
2. **claude-gateway** is up:
   ```powershell
   curl.exe http://localhost:3131/health
   ```
   (In PowerShell, bare `curl` aliases `Invoke-WebRequest` — use `curl.exe` or
   `Invoke-RestMethod http://localhost:3131/health`.) 200 = up. If it is down, the system degrades to deterministic rule-based regime
   classification (ADR-029/ADR-030) — **this is expected behavior, not an incident**.
   Restart the gateway (`D:\Source\claude-gateway`) when convenient.
3. **Functions worker** started (`func start` per section 1).
4. **Startup-log smoke check:** the worker banner lists both timer functions —
   `DailyOrchestrator_PreMarket` and `DailyOrchestrator_EndOfDay`. If either is missing,
   the build is broken or the wrong project started; do not assume the day will run.
5. **Host boot smoke:** after `func start`, the function-list banner must appear with **no
   `TypeLoadException`** (or repeated "worker process" restarts) in the startup output.
   The banner proves the isolated worker process actually loaded its DI graph — a
   package-version mismatch (S5-004r) can pass the full unit suite yet crash the worker
   at bootstrap, because tests never load the Functions host.

## 3. Timer schedule

The NCRONTAB expressions are **UTC-fixed**; PT wall-clock times therefore shift by one hour
across DST transitions. Document only — do not "fix" the crons.

| Function | Cron (UTC) | UTC | PT (PDT, summer) | PT (PST, winter) |
|---|---|---|---|---|
| `DailyOrchestrator_PreMarket` | `0 0 13 * * 1-5` | 13:00 | 6:00 AM | 5:00 AM |
| `DailyOrchestrator_EndOfDay` | `0 30 20 * * 1-5` | 20:30 | 1:30 PM | 12:30 PM |

**Which column applies:** the planned validation window (2026-06-11 → 2026-09-03) falls
entirely in **PDT (summer)**; the PST (winter) column takes over only if the run extends past
US DST end on 2026-11-01.

Weekdays only (`1-5`). The orchestrator's own calendar/no-trade-window logic decides what
actually happens inside a run; the timer just fires.

## 4. What each Discord message means

All messages arrive on the same webhook channel. Titles below are exact. Embed border color
tells you the class at a glance: **red border = risk-stop alert, orange border = operational
failure, unbordered/neutral = normal digest.**

| Message (exact title) | Meaning | Operator action |
|---|---|---|
| `Daily Report — <date>` (e.g. `Daily Report — Wed Jun 11, 2026`) | EOD digest: trades, realized/unrealized P&L, positions, market regime, ADR-023 cost breakout | None — read it. Its *absence* on a trading day is a triage trigger (section 5) |
| `Sleeve Readiness (paper-validation gate)` (embed appended to the Friday daily report) | PDR-004 gate progress per sleeve (hit rate, profit factor, drawdown, weeks observed) | None — track gate progress week over week |
| `Broker Connect Failure — Pre-Market` | Worker could not connect to TWS; options sleeve skipped for the day | Check TWS is running/logged in, port 7497, trusted IP; fix before the EOD timer |
| `Broker Connect Failure — End of Day` | Worker could not connect to TWS at EOD; **no snapshot written** for the day | Fix TWS; the day is a snapshot gap (tolerated, but log it in the Run Log) |
| `Orchestration Run Failure — Pre-Market` | Unhandled exception in the pre-market run (exception type in the message) | Check worker console / App Insights for the failure; rerun is not automatic |
| `Orchestration Run Failure — End of Day` | Unhandled exception in the EOD run; snapshot may not have been written | Same as above; verify whether `data/snapshots.json` has today's entry |
| `Daily Risk Stop Triggered` | Daily P&L breached the daily stop threshold (alert-only in paper — nothing is halted) | Note it; verify the EOD snapshot carries the flag. No parameter changes |
| `Weekly Risk Stop Triggered` | Weekly P&L breached the weekly stop threshold (alert-only in paper) | Same as daily stop |
| `Drawdown Halt Triggered` | Drawdown breached the halt threshold (alert-only in paper) | Same as daily stop |

### Silence cases (no message is also a signal)

- **`Discord:Enabled=false`** — every send is skipped by design. The worker logs a one-time
  Information notice at startup ("Discord risk alerts are disabled (Enabled=false)…") and
  per-call Debug skips. For the validation run this flag must be `true`.
- **Dropped alert (`AlertDropped=True` in the rendered log)** — Discord was reachable in principle but delivery
  terminally failed (non-429 4xx/5xx, retry budget exhausted, timeout, or transport error).
  The alert is gone and will NOT be retried; the only record is the log signature described
  in section 5.
- **Dead-man gap (worker not running)** — no worker means no runs, no alerts, and no
  "I'm down" message. Nothing can tell you from Discord. Section 5, first entry.

## 5. Failure triage

Check entries top-down; the first one is the one Discord can never tell you about.

| Symptom | Likely cause | Action |
|---|---|---|
| **Nothing at all** — no report, no alert, on a trading day | Worker (or the whole box) was not running — the dead-man case | **Check this first each morning.** Confirm the worker process is up and the startup banner shows both timers. Distinguish crash from never-started: startup banner present in the console = the worker ran and then exited (check the exit reason in the console tail / App Insights); no banner at all = it was never started. If the box slept through a timer, that run is gone (no catch-up); log the gap day in the Run Log |
| **Worker IS running, timers fired (visible in log), but no Discord message arrived** | Discord unreachable (or webhook broken) at the moment an ops/risk alert was sent — the alert was dropped (dead-man gap on the alert path) | The ONLY signal is the local worker log / App Insights: the signature is a `LogError` line from `DiscordRiskAlertService` matching `"… alert NOT delivered and will not be retried — … AlertDropped=True …"` (rendered console form — capital `T`); structured query: `traces \| where customDimensions.AlertDropped == "true"`. Operator action: treat the logged title as the alert you never received — triage its underlying failure first, then fix Discord delivery (webhook URL valid? Discord up?), and log the incident in the Run Log |
| No daily report by ~15 min after the EOD timer, but no orange alert either | Report send failed silently is *not* possible without a log — check worker log for report-path warnings; also re-check the dead-man cases above | Worker log around 20:30 UTC (12:30 PM PT winter / 1:30 PM PT summer); `data/snapshots.json` tells you whether the EOD run itself happened |
| `Broker Connect Failure — …` alert | TWS not running, not logged in, API disabled, or port/client-id mismatch | Match TWS API settings against `IBKR:Host/Port/ClientId` in `local.settings.json`; restart TWS; pre-market failure skips the options sleeve, EOD failure skips the snapshot |
| `Broker Connect Failure — Pre-Market` on a **Monday morning** | TWS weekly auto-restart (Sunday) left it at the restart prompt or logged out — the restart can also reset API settings | Accept the restart prompt, re-login to the **paper** account, then re-verify API settings: socket port **7497** and trusted IP **127.0.0.1**. If the pre-market timer already passed while TWS was down, that run is gone — log a gap day in the Run Log |
| `Orchestration Run Failure — …` alert | Unhandled exception (type name is in the alert) | Worker console / App Insights failure traces for the runId in the alert |
| Gateway down (health check fails, or log line `Direct API fallback disabled; gateway miss → deterministic rules`) | claude-gateway process not running or CLI session expired | **Expected degrade, not an incident:** regime classification falls back to deterministic rules with zero metered spend. Restart the gateway; check `GET /health/cli` for credential status. Do NOT enable the metered fallback as a remediation |
| Snapshot missing for a day (`data/snapshots.json`) | Any of the above on the EOD path | Identify which from logs/alerts; record the gap day in the Run Log |

## 6. Locked posture (ops policy)

Restating the repo trading rules as standing ops policy for the entire validation window:

- **SANDBOX only.** SANDBOX→LIVE requires explicit human approval — it is not an ops action
  and nothing in this runbook authorizes it.
- **`Claude:DirectApiFallbackEnabled=false` stays false.** Zero metered API spend; the
  gateway-or-rules degrade (ADR-029/ADR-030) is the designed behavior. No triage path in
  this document ends with "turn the fallback on".
- **Stops are alert-only in paper.** Risk-stop alerts (red) record threshold breaches; they
  do not halt anything, and no halt behavior is to be added mid-run.
- **No risk-parameter or sleeve-allocation changes** during the run without explicit human
  approval — changing them invalidates the validation window.
- All trading logic is deterministic; AI (gateway) is analysis-only.

## 7. Day-0 checklist (executed once, at sprint close)

- [x] (a) Retrieve the Discord webhook URL from the Bitwarden **"ClimbOn Co"** org vault into
  the gitignored `src/TradingSystem.Functions/local.settings.json` (`.gitignore:16`).
  The URL must NEVER be committed, logged, or echoed — write it into the file with an
  editor/file tool, never via shell `echo`/string interpolation or a shell variable
  (it would land in shell history). Set `Discord:Enabled=true`,
  `Reporting:WeeklyScorecardDay=Friday`. Also retrieve `Claude:GatewayApiKey` from the same
  vault into the same file; leave `Claude:ApiKey` as its placeholder — the metered key is
  not needed under the default `DirectApiFallbackEnabled=false` posture.
- [x] (b) Send a test operational alert through the production `DiscordRiskAlertService`
  path (S5-003 / `DiscordWebhookGuard`) and confirm it renders in the Discord channel.
- [ ] (c) Start gateway + worker; confirm gateway preflight (regime call resolves
  gateway-or-rules and logs its source) — first scheduled run covers this.
- [x] (d) Record the validation start date in the Run Log below.
- [ ] (e) Fill KD-001's resolution date in `DECISIONS.md` — wording owned by S5-006, date
  filled at sprint close (not this document's job).

> Day-0 steps (a), (b), (d) executed 2026-06-10 during S5-004 — test alert delivered
> (HTTP 204). Update Run Log dates if the run start slips past 2026-06-11.

### Day-1 / Week-1 checks

- Day 1: after the first scheduled EOD run, `data/snapshots.json` gains an enriched entry
  for the day (trades, commissions, realized P&L, SPY/VIX close, regime) and the daily
  report arrives on Discord.
- First Friday: the daily report carries the `Sleeve Readiness (paper-validation gate)`
  embed; non-Friday reports carry only the core digest.

## 8. Run Log (system of record for the validation window)

The first row anchors the validation start date (locked decision: the clock starts at sprint
close). Target ≥12 weeks of observed paper trading per ADR-010/PDR-004 before any gate
evaluation.

| Date | Event | Notes |
|---|---|---|
| 2026-06-10 | Day-0 configuration completed | Webhook provisioned from Bitwarden "ClimbOn Co" into gitignored `local.settings.json`; test alert delivered through the production alert path. **Validation run starts the next trading session: 2026-06-11.** Earliest 12-week gate evaluation: on/after 2026-09-03 (12 calendar weeks; verify against trading-day count if PDR-004 means observed trading weeks) |
| | | |
