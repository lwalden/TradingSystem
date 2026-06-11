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
- **Machine-on requirement:** the dev box and the worker must be up across the day's timer
  firings (all 4 timers — see section 3). Sleep/hibernate counts as down.
- **Missed runs do not catch up.** If the worker was not running when a timer was due,
  that run is simply skipped — a gap day shows up as a missing entry in
  `data/snapshots.json` and no Discord report for that date. Gap days are tolerated by
  the ≥12-week window (ADR-031); triage is in section 5, not panic.

## 2. Daily preflight (before the pre-market timer)

Run this check each trading morning **before 5:00 AM PT in winter (PST) / 6:00 AM PT in summer
(PDT)** — the winter deadline is the earlier one (see section 3) — or run
`tools/ops/preflight.ps1` (S6-004), which scripts the same checks with bounded timeouts:
TWS port probe (step 1), gateway health (step 2 — gateway-down is a WARN, not a FAIL),
worker admin-endpoint 4-function list (steps 3–5), and the snapshots data directory:

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
4. **Host boot smoke:** after `func start`, the function-list banner must appear with **no
   `TypeLoadException`** (or repeated "worker process" restarts) in the startup output.
   The banner proves the isolated worker process actually loaded its DI graph — a
   package-version mismatch (S5-004r) can pass the full unit suite yet crash the worker
   at bootstrap, because tests never load the Functions host. Run this check first: the
   function list in step 5 is meaningless until the worker has provably booted.
5. **Startup-log smoke check:** the worker banner lists **all 4 functions** —
   `DailyOrchestrator_PreMarket`, `DailyOrchestrator_EndOfDay`,
   `IncomeSleeve_MonthlyReinvest`, and `IncomeSleeve_QuarterlyAudit`. If any is missing,
   the build is broken or the wrong project started; do not assume the day will run.
   `tools/ci/host-boot-smoke.sh` (S6-002) is the scripted equivalent of steps 4–5 — it
   boots the worker and asserts the same four names.

## 3. Timer schedule

The NCRONTAB expressions are **UTC-fixed**; PT wall-clock times therefore shift by one hour
across DST transitions. Document only — do not "fix" the crons.

| Function | Cron (UTC) | UTC | PT (PDT, summer) | PT (PST, winter) |
|---|---|---|---|---|
| `DailyOrchestrator_PreMarket` | `0 0 13 * * 1-5` | 13:00 | 6:00 AM | 5:00 AM |
| `DailyOrchestrator_EndOfDay` | `0 30 20 * * 1-5` | 20:30 | 1:30 PM | 12:30 PM |
| `IncomeSleeve_MonthlyReinvest` | `0 30 13 1-7 * 1-5` | 13:30 | 6:30 AM | 5:30 AM |
| `IncomeSleeve_QuarterlyAudit` | `0 0 14 1-7 1,4,7,10 1-5` | 14:00 | 7:00 AM | 6:00 AM |

**Which column applies:** the planned validation window (2026-06-11 → 2026-09-03) falls
entirely in **PDT (summer)**; the PST (winter) column takes over only if the run extends past
US DST end on 2026-11-01.

Weekdays only (`1-5`). The orchestrator's own calendar/no-trade-window logic decides what
actually happens inside a run; the timer just fires.

**Income timer footnote:** the two income crons restrict day-of-month (`1-7`) AND
day-of-week (`1-5`), so each can fire on multiple days — potentially every weekday — of the
first week of its month(s); the audit additionally fires only in Jan/Apr/Jul/Oct (`1,4,7,10`).
That is expected: the **in-code gate** decides what actually runs — the first trading
weekday of the month for the reinvest (every other firing logs an Information skip and
returns), and the not-implemented skip for the audit (see the expected-behavior subsections
below). Do not "fix" the crons.

### Expected behavior: 2026-07-01 reinvest firing (S6-001 posture)

The first `IncomeSleeve_MonthlyReinvest` run of the validation window lands **Wed 2026-07-01**
(the first trading weekday of July) at 6:30 AM PDT. The default posture is
**recommendation-only** (`IncomeSleeve:OrderPlacementEnabled=false` — S6-001 locked
decision): the run computes the reinvestment plan and reports it; it places nothing.

- A neutral (unbordered) Discord message titled `Income Reinvest Plan — Wed Jul 1, 2026`
  arrives, with a Posture field reading
  `Recommendation-only; IncomeSleeve:OrderPlacementEnabled=false. NO orders were placed.`
- **NO orders appear in TWS paper.** Orders on this timer exist only if the owner explicitly
  flipped the flag to `true` beforehand.
- An empty sleeve legitimately yields `No buys proposed.` — that message arriving at all is
  proof the timer ran, not a failure.
- Other weekday firings in the Jul 1–7 window log an Information skip in the worker log
  (`Monthly reinvest skipped — Not the first trading weekday of the month …`) and send
  nothing — expected (worker console or `%LOCALAPPDATA%\TradingSystem\logs` — section 5).
- If the gate day is a market holiday, the run degrades like any closed-market day — the
  recommendation-only output is benign (accepted edge; no holiday calendar on this timer).
- The income sleeve's **PDR-004 ≥12-week clock starts at its first actual trade**, not at
  plan generation — recommendation-only months do not advance it.

**Before flipping `IncomeSleeve:OrderPlacementEnabled` to `true`** (an owner action, never an
ops/triage step), confirm all of:

1. the prior recommendation-only `Income Reinvest Plan — <date>` Discord report arrived and
   its proposed tickers match known income-sleeve holdings, quantities are non-zero, and
   estimated cost is within the available-cash estimate shown in the message;
2. TWS is running in **paper** mode;
3. the IB **LIVE** account is NOT the active TWS session.

### Expected behavior: Jul 1–7 quarterly-audit window (S6-007)

`IncomeSleeve_QuarterlyAudit` is **not implemented** (deferred per backlog, S7+). On each of
its firing day(s) in the Jul 1–7 window, the worker console/log shows one Warning line:

```
IncomeSleeve_QuarterlyAudit is not implemented — skipped (deferred per backlog, S7+). RunId: <id>
```

**No Discord message is sent — silence here is expected**, not a dropped alert. Do not
triage it.

## 4. What each Discord message means

All messages arrive on the same webhook channel. Titles below are exact. Embed border color
tells you the class at a glance: **red border = risk-stop alert, orange border = operational
failure, unbordered/neutral = normal digest.**

| Message (exact title) | Meaning | Operator action |
|---|---|---|
| `Daily Report — <date>` (e.g. `Daily Report — Wed Jun 11, 2026`) | EOD digest: trades, realized/unrealized P&L, positions, market regime, ADR-023 cost breakout | None — read it. Its *absence* on a trading day is a triage trigger (section 5) |
| `Sleeve Readiness (paper-validation gate)` (embed appended to the Friday daily report) | PDR-004 gate progress per sleeve (hit rate, profit factor, drawdown, weeks observed) | None — track gate progress week over week |
| `Income Reinvest Plan — <date>` (e.g. `Income Reinvest Plan — Wed Jul 1, 2026`) | Monthly income reinvest digest (neutral): posture line, available-cash estimate, sleeve value, category drift, proposed buys (or `No buys proposed.`) | Verify **NO orders** landed in TWS paper unless `IncomeSleeve:OrderPlacementEnabled=true` was explicitly set (section 3 expected-behavior); note the firing in the Run Log |
| `Broker Connect Failure — Pre-Market` | Worker could not connect to TWS; options sleeve skipped for the day | Check TWS is running/logged in, port 7497, trusted IP; fix before the EOD timer |
| `Broker Connect Failure — End of Day` | Worker could not connect to TWS at EOD; **no snapshot written** for the day | Fix TWS; the day is a snapshot gap (tolerated, but log it in the Run Log) |
| `Broker Connect Failure — Monthly Reinvest` | Worker could not connect to TWS on the reinvest gate day; no plan generated, no report sent | Fix TWS as above. No catch-up run: the next reinvest evaluation is next month's gate day — drift-based plans self-correct |
| `Orchestration Run Failure — Pre-Market` | Unhandled exception in the pre-market run (exception type in the message) | Check the worker console / local log files (section 5) for the failure; rerun is not automatic |
| `Orchestration Run Failure — End of Day` | Unhandled exception in the EOD run; snapshot may not have been written | Same as above; verify whether `data/snapshots.json` has today's entry |
| `Orchestration Run Failure — Monthly Reinvest` | Unhandled exception in the reinvest run (exception type in the message); plan/report may not have been produced | Check the worker console / local log files (section 5) for the runId in the alert |
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
- **`IncomeSleeve_QuarterlyAudit` not implemented (S7+)** — fires on weekdays in the
  Jul 1–7 window; one Warning log line, no Discord message; see section 3 for the exact
  log line.

## 5. Failure triage

Check entries top-down; the first one is the one Discord can never tell you about.

**Where the logs are:** every triage row below points at the **local sinks** — the worker
console, and the log files under `%LOCALAPPDATA%\TradingSystem\logs` when the worker runs as
the `TradingSystem-FuncHost` scheduled task (that task's output location). (App Insights is
intentionally disabled for this run — `APPLICATIONINSIGHTS_CONNECTION_STRING` is unset,
KD-005/KD-006 — so its absence is by design, not a gap to fix.)

| Symptom | Likely cause | Action |
|---|---|---|
| **Nothing at all** — no report, no alert, on a trading day | Worker (or the whole box) was not running — the dead-man case | **Check this first each morning.** Confirm the worker process is up and the startup banner shows all 4 functions (section 2 step 5). Distinguish crash from never-started: startup banner present in the console = the worker ran and then exited (check the exit reason in the console tail / `%LOCALAPPDATA%\TradingSystem\logs`); no banner at all = it was never started. If the box slept through a timer, that run is gone (no catch-up); log the gap day in the Run Log. The `TradingSystem-GapDayMonitor` task (2:30 PM weekdays — see the gap-day monitor subsection below) automates this check and alerts when the day's snapshot is missing |
| `Dead-Man Alert — No EOD Snapshot for <date>` (orange; sent by the **gap-day monitor task**, not the worker) | The 2:30 PM `TradingSystem-GapDayMonitor` task found no entry for today in `data/snapshots.json` — the EOD path never ran (worker down, box asleep, or EOD failure: the dead-man case above). **On a market holiday this is an expected false positive** — the monitor is weekday-only with no holiday calendar (~9/year, accepted by design); ignore it | Holiday → ignore. Otherwise triage the dead-man row above (worker up? banner? local logs), then record the gap day in the Run Log |
| **Worker IS running, timers fired (visible in log), but no Discord message arrived** | Discord unreachable (or webhook broken) at the moment an ops/risk alert was sent — the alert was dropped (dead-man gap on the alert path) | The ONLY signal is the local worker log (console, or the files under `%LOCALAPPDATA%\TradingSystem\logs`): the signature is a `LogError` line from `DiscordRiskAlertService` matching `"… alert NOT delivered and will not be retried — … AlertDropped=True …"` (rendered console form — capital `T`); search the log files for `AlertDropped=True`. Operator action: treat the logged title as the alert you never received — triage its underlying failure first, then fix Discord delivery (webhook URL valid? Discord up?), and log the incident in the Run Log |
| No daily report by ~15 min after the EOD timer, but no orange alert either | Report send failed silently is *not* possible without a log — check worker log for report-path warnings; also re-check the dead-man cases above | Worker log around 20:30 UTC (12:30 PM PT winter / 1:30 PM PT summer); `data/snapshots.json` tells you whether the EOD run itself happened |
| `Broker Connect Failure — …` alert | TWS not running, not logged in, API disabled, or port/client-id mismatch | Match TWS API settings against `IBKR:Host/Port/ClientId` in `local.settings.json`; restart TWS; pre-market failure skips the options sleeve, EOD failure skips the snapshot |
| `Broker Connect Failure — Pre-Market` on a **Monday morning** | TWS weekly auto-restart (Sunday) left it at the restart prompt or logged out — the restart can also reset API settings | Accept the restart prompt, re-login to the **paper** account, then re-verify API settings: socket port **7497** and trusted IP **127.0.0.1**. If the pre-market timer already passed while TWS was down, that run is gone — log a gap day in the Run Log |
| `Orchestration Run Failure — …` alert | Unhandled exception (type name is in the alert) | Worker console / `%LOCALAPPDATA%\TradingSystem\logs` — search for the runId in the alert |
| Gateway down (health check fails, or log line `Direct API fallback disabled; gateway miss → deterministic rules`) | claude-gateway process not running or CLI session expired | **Expected degrade, not an incident:** regime classification falls back to deterministic rules with zero metered spend. Restart the gateway; check `GET /health/cli` for credential status. Do NOT enable the metered fallback as a remediation |
| Snapshot missing for a day (`data/snapshots.json`) | Any of the above on the EOD path | Identify which from logs/alerts; record the gap day in the Run Log |

### Gap-day monitor (dead-man) — one-time manual registration

`tools/ops/Check-DailySnapshot.ps1` (S6-004) closes the dead-man blind spot in the first
triage row: a down worker can never tell you it is down, so an independent scheduled task
checks each weekday at **2:30 PM local** (≥45 min after the EOD timer in both DST regimes)
that today's entry exists in `data/snapshots.json`, and posts an orange
`Dead-Man Alert — No EOD Snapshot for <date>` embed when it does not. The webhook URL is
read from the gitignored `local.settings.json` (`Discord:WebhookUrl`) and is never logged.

**Registration is a one-time manual operator step** — it is never performed by CI, tests,
hooks, or agent pipelines:

```powershell
pwsh -NoProfile -File D:\Source\TradingSystem\tools\ops\Register-GapDayMonitorTask.ps1
```

Run this as the normal dev-box user — no elevation required; the task is registered to
run as the current user.

The script is idempotent (re-running replaces the task). Verify with
`schtasks /query /tn TradingSystem-GapDayMonitor /v /fo LIST`.

At the end of the 12-week run, decommission the task:
`Unregister-ScheduledTask -TaskName TradingSystem-GapDayMonitor -Confirm:$false`.

**Exit-code legend** (visible in Task Scheduler history — the code tells you what happened
without opening logs):

| Exit code | Meaning |
|---|---|
| 0 | Snapshot present (or weekend — nothing expected) |
| 1 | Snapshot MISSING; alert skipped (Discord disabled or webhook not configured) — still investigate |
| 2 | Snapshot MISSING; alert delivery FAILED (Discord unreachable) — investigate both |
| 3 | Snapshot MISSING; alert delivered — triage per the table above |

Optional controlled drill (renders a real alert without waiting for a genuine gap day):
`Check-DailySnapshot.ps1 -Date <past-weekday-with-no-snapshot>` — note it in the Run Log as
a drill. Use `-WhatIf` to evaluate without posting.

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
